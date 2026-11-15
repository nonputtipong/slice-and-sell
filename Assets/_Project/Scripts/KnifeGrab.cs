using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // A pinching hand holds the knife at a different point than a controller grip does, so tracked hands
    // get their own attach point (handAttachTransform); controllers and sockets use the normal one.
    // A hand interactor is one that sits under the rig's hand GameObjects (XRInputModalityManager).
    public class KnifeGrab : XRGrabInteractable
    {
        public Transform handAttachTransform;
        XRInputModalityManager modality;

        public override Transform GetAttachTransform(IXRInteractor interactor) =>
            handAttachTransform && IsHand(interactor) ? handAttachTransform : base.GetAttachTransform(interactor);

        bool IsHand(IXRInteractor interactor)
        {
            if (!modality) modality = FindAnyObjectByType<XRInputModalityManager>();
            if (!modality) return false;
            var t = interactor.transform;
            return (modality.leftHand && t.IsChildOf(modality.leftHand.transform)) ||
                   (modality.rightHand && t.IsChildOf(modality.rightHand.transform));
        }
    }
}
