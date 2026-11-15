using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // M2: tracked hands hold the knife at their own grip point; controllers and sockets keep the normal one.
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

        [UnityTest]
        public IEnumerator HandsUseHandGripControllersUseNormalGrip()
        {
            Assert.IsNotNull(knife.handAttachTransform, "knife has a hand grip point");
            foreach (var hand in new[] { modality.leftHand, modality.rightHand })
                Assert.AreSame(knife.handAttachTransform, knife.GetAttachTransform(GrabInteractor(hand)), hand.name);
            foreach (var controller in new[] { modality.leftController, modality.rightController })
                Assert.AreSame(knife.attachTransform, knife.GetAttachTransform(GrabInteractor(controller)), controller.name);
            Assert.AreSame(knife.attachTransform, knife.GetAttachTransform(Object.FindAnyObjectByType<CuttingBoard>().fruitSocket), "socket");
            yield break;
        }
    }
}
