using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace HCD545
{
    // Lab 4: the last row of the Input panel. Display Hz = how often the headset shows a new image (Quest 3: 72, 80, 90 or 120).
    // App FPS = how many frames per second this app really makes. If App FPS is lower than Display Hz, the app is too slow.
    // Frame budget: 72 Hz = 13.9 ms per frame, 90 Hz = 11.1 ms, 120 Hz = 8.3 ms.
    public class RefreshRateReadout : MonoBehaviour
    {
        public TMP_Text displayHz, appFps;

        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        int frames; float time;

        void Update()
        {
            frames++; time += Time.unscaledDeltaTime;
            if (time < 0.5f) return;                       // update the text twice a second, so it is readable

            if (appFps) appFps.text = (frames / time).ToString("0");
            if (displayHz) displayHz.text = DisplayRate().ToString("0");
            frames = 0; time = 0f;
        }

        float DisplayRate()
        {
            // In the headset: ask the XR display (OpenXR) for its refresh rate.
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
                if (d.running && d.TryGetDisplayRefreshRate(out float hz) && hz > 0f) return hz;
            // On a monitor (Editor, simulator): the monitor's refresh rate.
            return (float)Screen.currentResolution.refreshRateRatio.value;
        }
    }
}
