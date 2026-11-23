using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace SliceAndSell
{
    // GDD > Failure cases: playing with hands and both are lost for over 1 s -> pause and show a "Show your hands"
    // card 1 m in front of the eyes. A hand or controller back -> hide it, and resume only if this pause was ours
    // (a pause from the wrist menu stays). Replaces Lab 5's TrackingLostBanner.
    public class HandLossPause : MonoBehaviour
    {
        public GameObject card;
        public Transform head;
        public float delaySeconds = 1f, distance = 1f;

        bool usingHands, pausedByUs;
        float lostTime;

        void Update()
        {
            bool hands = XRInputTrackingAggregator.GetLeftTrackedHandStatus().isTracked || XRInputTrackingAggregator.GetRightTrackedHandStatus().isTracked;
            bool controllers = XRInputTrackingAggregator.GetLeftControllerStatus().isTracked || XRInputTrackingAggregator.GetRightControllerStatus().isTracked;
            if (controllers) usingHands = false;
            if (hands) usingHands = true;   // after the controller check: the OpenXR hand-interaction profile also counts as a controller

            lostTime = usingHands && !hands && !controllers ? lostTime + Time.unscaledDeltaTime : 0f;
            if (lostTime > delaySeconds && !card.activeSelf) Show();
            else if (lostTime == 0f && card.activeSelf) Hide();
        }

        void Show()
        {
            // Level, along where the head points, just under eye height: looking down at the cart would otherwise
            // put the card inside the counter, hidden.
            var ahead = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.ProjectOnPlane(head.up, Vector3.up);
            ahead.Normalize();
            var pos = head.position + ahead * distance + Vector3.down * 0.08f;
            card.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position));
            card.SetActive(true);
            if (!GamePause.Paused) { GamePause.Set(true); pausedByUs = true; }
        }

        void Hide()
        {
            card.SetActive(false);
            if (pausedByUs) GamePause.Set(false);
            pausedByUs = false;
        }
    }
}
