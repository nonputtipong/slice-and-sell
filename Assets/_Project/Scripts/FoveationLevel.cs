using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace SliceAndSell
{
    // Fixed foveated rendering on Quest (OpenXR Foveated Rendering feature, SRP foveation API): the edges of each eye
    // are shaded at lower resolution, which saves GPU fill. The feature only makes it possible; the level is set here,
    // once the XR display is running (it may start after this component). No eye tracking on Quest 3: no gaze flag.
    // Does nothing without an XR display (Editor without a headset, the XR simulator).
    public class FoveationLevel : MonoBehaviour
    {
        [Range(0f, 1f)] public float level = 0.5f;      // 0 off .. 1 strongest; 0.5 ~ Meta's "medium"

        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        void Update()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                if (!d.running) continue;
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
                d.foveatedRenderingLevel = level;
                enabled = false;                        // set once; the setting stays with the display
                return;
            }
        }
    }
}
