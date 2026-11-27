using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // M2 / playtest 5: the knife keeps the angle it was picked up at, with tracked hands and with controllers.
    public class KnifeGripTests
    {
        KnifeGrab knife;
        XRInputModalityManager modality;

        [UnitySetUp]
        public IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Stall.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            knife = Object.FindAnyObjectByType<KnifeGrab>();
            modality = Object.FindAnyObjectByType<XRInputModalityManager>();
        }

        // A grab-capable interactor under the given rig root (hand or controller), active or not.
        static IXRSelectInteractor GrabInteractor(GameObject root) =>
            root.GetComponentsInChildren<IXRSelectInteractor>(true)
                .First(i => !(i is XRPokeInteractor) && !(i is XRSocketInteractor));

        // Playtest 5: the knife and the Eto keep the angle they were picked up at (dynamic attach), with hands and
        // controllers alike: no fixed hand grip overrides it.
        [UnityTest]
        public IEnumerator KnifeAndEtoKeepTheGrabbedAngle()
        {
            Assert.IsTrue(knife.useDynamicAttach, "paring knife: dynamic attach");
            var eto = Object.FindObjectsByType<Knife>(FindObjectsSortMode.None).Single(k => k.cleaver).GetComponent<XRGrabInteractable>();
            Assert.IsTrue(eto.useDynamicAttach, "Eto: dynamic attach");
            Assert.IsNull(knife.handAttachTransform, "no fixed hand grip");
            foreach (var hand in new[] { modality.leftHand, modality.rightHand })
                Assert.IsNotNull(knife.GetAttachTransform(GrabInteractor(hand)), hand.name);
            yield break;
        }
    }
}
