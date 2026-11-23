using UnityEngine;
using UnityEngine.XR.Management;

namespace HCD545
{
    // Turns on the XR Interaction Simulator only when you press Play in the Unity Editor and no headset is connected.
    // On the Quest (and in the Editor with Meta Horizon Link) it stays off, so it never fights the real controllers.
    public class EditorSimulatorSwitch : MonoBehaviour
    {
        public GameObject simulator;   // the "XR Interaction Simulator" object; it starts switched off in the scene

        void Start()
        {
            bool headset = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                           && XRGeneralSettings.Instance.Manager.activeLoader != null;
            if (simulator) simulator.SetActive(Application.isEditor && !headset);
        }
    }
}
