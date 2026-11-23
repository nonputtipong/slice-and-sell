using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // GDD > Failure cases: a hand that loses tracking lets go of what it holds. If the releasing hand (and that side's
    // controller) isn't tracked at that moment, the item stays frozen where it was for up to 2 s, or until grabbed again,
    // so a brief loss doesn't throw or drop the fruit. Normal hand releases and controller releases are unchanged.
    // Frozen with constraints, not isKinematic: XRGrabInteractable saves/restores isKinematic itself on grab and drop
    // (and warns when it throws a kinematic body); constraints it leaves alone.
    [RequireComponent(typeof(Rigidbody))]
    public class HoldOnTrackingLoss : MonoBehaviour
    {
        public float holdSeconds = 2f;

        XRBaseInteractable interactable;
        Rigidbody body;
        RigidbodyConstraints saved;
        bool frozen;

        void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnGrabbed);
            interactable.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnGrabbed);
            interactable.selectExited.RemoveListener(OnReleased);
            Unfreeze();
        }

        void OnReleased(SelectExitEventArgs args)
        {
            var hand = args.interactorObject.handedness;    // sockets are None
            if (frozen || interactable.isSelected || hand == InteractorHandedness.None || !LostHand(hand == InteractorHandedness.Left)) return;
            saved = body.constraints;
            body.constraints = RigidbodyConstraints.FreezeAll;
            frozen = true;
            Invoke(nameof(Unfreeze), holdSeconds);
        }

        void OnGrabbed(SelectEnterEventArgs _) => Unfreeze();

        void Unfreeze()
        {
            if (!frozen) return;
            CancelInvoke(nameof(Unfreeze));
            frozen = false;
            body.constraints = saved;
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;   // a gentle drop, not the throw queued on release
        }

        // A hand-tracking system is running, but neither that hand nor that side's controller is tracked.
        // (No XR at all, e.g. edit-mode tests: isConnected is false, so nothing freezes.)
        static bool LostHand(bool left)
        {
            var hand = left ? XRInputTrackingAggregator.GetLeftTrackedHandStatus() : XRInputTrackingAggregator.GetRightTrackedHandStatus();
            var controller = left ? XRInputTrackingAggregator.GetLeftControllerStatus() : XRInputTrackingAggregator.GetRightControllerStatus();
            return hand.isConnected && !hand.isTracked && !controller.isTracked;
        }
    }
}
