using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // Milestone 2: the core loop works with tracked hands as well as controllers, and switching input mid-hold is safe.
    public class HandParityTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Stall.unity";
        XRInputModalityManager modality;
        XRGrabInteractable fruit, knife, bag;
        XRSimpleInteractable bell;
        CuttingBoard board;
        XRSocketInteractor tray;

        [UnitySetUp]
        public IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            modality = Object.FindAnyObjectByType<XRInputModalityManager>();
            board = Object.FindAnyObjectByType<CuttingBoard>();
            var knifeScript = Object.FindAnyObjectByType<Knife>();
            knife = knifeScript.GetComponent<XRGrabInteractable>();
            var dispensers = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None);
            fruit = dispensers.First(d => d.prefab.GetComponent<Fruit>()).Current;
            bag = dispensers.First(d => d.prefab.GetComponent<Bag>()).Current;
            bell = Object.FindAnyObjectByType<ServiceBell>().GetComponent<XRSimpleInteractable>();
            tray = Object.FindAnyObjectByType<HandoffTray>().GetComponent<XRSocketInteractor>();
            knifeScript.GetComponent<Rigidbody>().isKinematic = true;   // no physics drift while the test drives it
            Assert.IsNotNull(modality, "rig has an XRInputModalityManager");
            Assert.IsNotNull(knife, "knife has an XRGrabInteractable");
            Assert.IsNotNull(fruit, "fruit bin has a fruit");
            Assert.IsNotNull(bag, "bag stack has a bag");
        }

        [UnityTest]
        public IEnumerator EveryCoreLoopItemIsReachableByHandsAndControllers()
        {
            // Bell select must work without a button: poke filter + a poke interactor on every modality.
            Assert.IsNotNull(bell.GetComponent<XRPokeFilter>(), "bell has an XRPokeFilter so a poke selects it");

            foreach (var (name, root) in Modalities())
            {
                Assert.IsNotNull(root, $"XRInputModalityManager.{name} is assigned");
                var grabbers = root.GetComponentsInChildren<IXRSelectInteractor>(true)
                    .Where(i => !(i is XRPokeInteractor) && !(i is XRSocketInteractor)).ToArray();
                var pokers = root.GetComponentsInChildren<XRPokeInteractor>(true);

                foreach (var (item, target) in new (string, IXRInteractable)[] { ("fruit", fruit), ("knife", knife), ("bag", bag) })
                    Assert.IsTrue(grabbers.Any(i => Overlaps(i, target)),
                        $"{name}: no grab interactor shares a layer with the {item} (grab interactors: {Describe(grabbers)})");
                Assert.IsTrue(pokers.Any(p => Overlaps(p, bell)),
                    $"{name}: no poke interactor shares a layer with the bell (poke interactors: {Describe(pokers)})");
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator SocketsOnlyAcceptTheirItem()
        {
            var fruitSocket = board.fruitSocket; var scoop = board.scoopSocket;
            Assert.IsFalse(Overlaps(fruitSocket, knife), "fruit socket must not take the knife");
            Assert.IsFalse(Overlaps(fruitSocket, bag), "fruit socket must not take a bag");
            Assert.IsFalse(Overlaps(scoop, fruit), "scoop spot must not take a fruit");
            Assert.IsFalse(Overlaps(tray, fruit), "tray must not take a fruit");
            Assert.IsFalse(Overlaps(scoop, knife), "scoop spot must not take the knife");
            Assert.IsFalse(Overlaps(tray, knife), "tray must not take the knife");
            Assert.IsTrue(Overlaps(fruitSocket, fruit), "fruit socket takes a fruit");
            Assert.IsTrue(Overlaps(scoop, bag), "scoop spot takes a bag");
            Assert.IsTrue(Overlaps(tray, bag), "tray takes a bag");
            yield break;
        }

        [UnityTest]
        public IEnumerator SwitchingInputMidHoldReleasesKnifeSafely()
        {
            // Keep the manager from toggling roots on device events mid-test; re-enabling it restores its own state.
            modality.enabled = false;
            var root = modality.rightController;
            bool wasActive = root.activeSelf;
            root.SetActive(true);
            yield return null;   // let the interactors register with the interaction manager

            var grabber = root.GetComponentsInChildren<XRBaseInteractor>()   // active ones only
                .FirstOrDefault(i => i.isActiveAndEnabled && !(i is XRPokeInteractor) && !(i is XRSocketInteractor) && Overlaps(i, knife));
            Assert.IsNotNull(grabber, "right controller has an active grab interactor that can take the knife");

            // Manual interaction keeps the select alive without a pressed trigger (plain SelectEnter is dropped next frame).
            grabber.StartManualInteraction((IXRSelectInteractable)knife);
            yield return null;
            Assert.IsTrue(knife.isSelected, $"{grabber.name} holds the knife");

            root.SetActive(false);   // what XRInputModalityManager does when the player puts the controller down
            Assert.IsFalse(knife.isSelected, "deactivating the controller releases the knife at once");
            for (int i = 0; i < 5; i++) yield return null;

            Assert.IsFalse(knife.isSelected, "knife stays released");
            Assert.IsTrue(knife != null, "knife still exists");
            Assert.IsTrue(knife.gameObject.activeInHierarchy, "knife is still active (not left parented under the hidden controller)");

            root.SetActive(wasActive);
            modality.enabled = true;
        }

        // ---------- helpers ----------

        (string, GameObject)[] Modalities() => new[]
        {
            ("leftHand", modality.leftHand), ("rightHand", modality.rightHand),
            ("leftController", modality.leftController), ("rightController", modality.rightController),
        };

        static bool Overlaps(IXRInteractor a, IXRInteractable b) => (a.interactionLayers.value & b.interactionLayers.value) != 0;

        static string Describe(System.Collections.Generic.IEnumerable<IXRInteractor> list) =>
            string.Join(", ", list.Select(i => $"{i.transform.name}={i.interactionLayers.value}"));
    }
}
