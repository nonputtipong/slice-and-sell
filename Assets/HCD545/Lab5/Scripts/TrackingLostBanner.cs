using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace HCD545
{
    // Lab 5: when you use your hands and the headset loses both of them (hands behind your back, out of the cameras' view,
    // too dark), a banner appears in front of you: "Hands not tracked: bring your hands into view".
    // It waits a short moment first, so a one-frame tracking glitch does not flash the banner.
    public class TrackingLostBanner : MonoBehaviour
    {
        public GameObject banner;          // child of the camera, about 1 m in front and a little low
        public float delaySeconds = 0.5f;

        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();
        bool usedHands; float lostTime;

        void Update()
        {
            var mode = XRInputModalityManager.currentInputMode.Value;
            if (mode == XRInputModalityManager.InputMode.TrackedHand) usedHands = true;
            if (mode == XRInputModalityManager.InputMode.MotionController) usedHands = false;   // controllers: no banner

            bool anyHand = false;
            SubsystemManager.GetSubsystems(hands);
            foreach (var h in hands) if (h.running && (h.leftHand.isTracked || h.rightHand.isTracked)) anyHand = true;

            lostTime = (usedHands && !anyHand) ? lostTime + Time.unscaledDeltaTime : 0f;
            if (banner) banner.SetActive(lostTime > delaySeconds);
        }
    }
}
