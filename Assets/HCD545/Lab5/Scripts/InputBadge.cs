using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace HCD545
{
    // Lab 5: shows which input the player is using right now: "Input: Controllers" or "Input: Hands".
    // XRI's XR Input Modality Manager (on the XR Origin Hands rig) does the switching; we only read its current mode.
    // Put the controllers down and show your hands: the badge changes within about a second.
    public class InputBadge : MonoBehaviour
    {
        public TMP_Text label;

        XRInputModalityManager.InputMode last = (XRInputModalityManager.InputMode)(-1);

        void Update()
        {
            var mode = XRInputModalityManager.currentInputMode.Value;
            if (mode == last || !label) return;
            last = mode;
            label.text = mode switch
            {
                XRInputModalityManager.InputMode.TrackedHand => "Input: Hands",
                XRInputModalityManager.InputMode.MotionController => "Input: Controllers",
                _ => "Input: none",
            };
            Debug.Log("[HCD545] " + label.text);
        }
    }
}
