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
        XRGrabInteractable fruit, knife, bowl, pistol, eto;
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
            var knifeScript = Object.FindObjectsByType<Knife>(FindObjectsSortMode.None).Single(k => !k.cleaver);   // the paring knife, not the cleaver
            knife = knifeScript.GetComponent<XRGrabInteractable>();
            eto = Object.FindObjectsByType<Knife>(FindObjectsSortMode.None).Single(k => k.cleaver).GetComponent<XRGrabInteractable>();
            pistol = Object.FindAnyObjectByType<Pistol>().GetComponent<XRGrabInteractable>();
            var dispensers = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None);
            fruit = dispensers.First(d => d.prefab.GetComponent<Fruit>()).Current;
            bowl = dispensers.First(d => d.prefab.GetComponent<Bowl>()).Current;
            bell = Object.FindAnyObjectByType<ServiceBell>().GetComponent<XRSimpleInteractable>();
            tray = Object.FindAnyObjectByType<HandoffTray>().GetComponent<XRSocketInteractor>();
            knifeScript.GetComponent<Rigidbody>().isKinematic = true;   // no physics drift while the test drives it
            Assert.IsNotNull(modality, "rig has an XRInputModalityManager");
            Assert.IsNotNull(knife, "knife has an XRGrabInteractable");
            Assert.IsNotNull(fruit, "fruit bin has a fruit");
            Assert.IsNotNull(bowl, "bowl stack has a bowl");
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

                foreach (var (item, target) in new (string, IXRInteractable)[] { ("fruit", fruit), ("knife", knife), ("bowl", bowl), ("Eto", eto), ("pistol", pistol) })
                    Assert.IsTrue(grabbers.Any(i => Overlaps(i, target)),
                        $"{name}: no grab interactor shares a layer with the {item} (grab interactors: {Describe(grabbers)})");
                Assert.IsTrue(pokers.Any(p => Overlaps(p, bell)),
                    $"{name}: no poke interactor shares a layer with the bell (poke interactors: {Describe(pokers)})");
            }
            yield break;
        }

        // Playtest 5: the tracked hands are more see-through than the sample's, so they don't hide what they hold.
        [UnityTest]
        public IEnumerator TrackedHandsAreSeeThrough()
        {
            var hands = Object.FindAnyObjectByType<SeeThroughHands>();
            Assert.IsNotNull(hands, "the rig swaps the hand material");
            Assert.IsNotNull(hands.original); Assert.IsNotNull(hands.seeThrough);
            foreach (var prop in new[] { "_Color", "_MainColor", "_EdgeColor" })
                if (hands.original.HasProperty(prop))
                    Assert.Less(hands.seeThrough.GetColor(prop).a, hands.original.GetColor(prop).a, $"{prop} is more transparent");
            // A stand-in hand mesh with the depth prepass in slot 0 and the hand material in slot 1: only slot 1 changes.
            var go = new GameObject("Hand mesh"); go.transform.SetParent(hands.transform, false);
            var r = go.AddComponent<SkinnedMeshRenderer>();
            var depth = new Material(hands.original);
            r.sharedMaterials = new[] { depth, hands.original };
            Assert.AreEqual(1, SeeThroughHands.Apply(go.transform, hands.original, hands.seeThrough));
            Assert.AreSame(depth, r.sharedMaterials[0]); Assert.AreSame(hands.seeThrough, r.sharedMaterials[1]);
            Object.Destroy(go); Object.Destroy(depth);
            yield break;
        }

        [UnityTest]
        public IEnumerator SocketsOnlyAcceptTheirItem()
        {
            var fruitSocket = board.fruitSocket; var scoop = board.scoopSocket;
            Assert.IsFalse(Overlaps(fruitSocket, knife), "fruit socket must not take the knife");
            Assert.IsFalse(Overlaps(fruitSocket, bowl), "fruit socket must not take a bowl");
            Assert.IsFalse(Overlaps(scoop, fruit), "scoop spot must not take a fruit");
            Assert.IsFalse(Overlaps(tray, fruit), "tray must not take a fruit");
            Assert.IsFalse(Overlaps(scoop, knife), "scoop spot must not take the knife");
            Assert.IsFalse(Overlaps(tray, knife), "tray must not take the knife");
            Assert.IsTrue(Overlaps(fruitSocket, fruit), "fruit socket takes a fruit");
            Assert.IsTrue(Overlaps(scoop, bowl), "scoop spot takes a bowl");
            Assert.IsTrue(Overlaps(tray, bowl), "tray takes a bowl");
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
