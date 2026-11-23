using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace HCD545
{
    // Lab 5: shows how strongly each hand pinches, from 0.00 (open) to 1.00 (thumb tip touches index tip).
    // It reads the hand joints from XR Hands. 1.00 at 1.5 cm or closer, 0.00 at 8 cm or more.
    // XRI grabs with a pinch at about 0.75 or more, so watch the number when a pinch "does not take".
    public class PinchReadout : MonoBehaviour
    {
        public TMP_Text leftText, rightText;
        public float closed = 0.015f, open = 0.08f;

        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();

        void Update()
        {
            XRHandSubsystem sub = null;
            SubsystemManager.GetSubsystems(hands);
            foreach (var h in hands) if (h.running) { sub = h; break; }
            if (leftText) leftText.text = sub == null ? "--" : Strength(sub.leftHand);
            if (rightText) rightText.text = sub == null ? "--" : Strength(sub.rightHand);
        }

        string Strength(XRHand hand)
        {
            if (!hand.isTracked) return "--";
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)) return "--";
            float d = Vector3.Distance(thumb.position, index.position);
            return Mathf.InverseLerp(open, closed, d).ToString("0.00");
        }
    }
}
