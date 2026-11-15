using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HCD545
{
    // Lab 4: hold B (right controller) or Y (left controller) for 1 second to recenter.
    // Recenter = turn and move the rig so that you stand at the start point and face the Input panel again.
    // (The Quest's own recenter is: hold the Meta button on the right controller.)
    public class Recenter : MonoBehaviour
    {
        public XROrigin origin;
        public TMP_Text message;           // a line on the panel that shows "Recentered" for 2 seconds
        public float holdSeconds = 1f;
        public Vector3 startPoint = Vector3.zero;
        public Vector3 startForward = Vector3.forward;

        InputAction button;
        float held, messageTimer;

        void OnEnable()
        {
            button = new InputAction(type: InputActionType.Button);
            button.AddBinding("<XRController>{RightHand}/secondaryButton");   // B
            button.AddBinding("<XRController>{LeftHand}/secondaryButton");    // Y
            button.Enable();
        }

        void OnDisable() => button?.Dispose();

        void Update()
        {
            held = button.IsPressed() ? held + Time.unscaledDeltaTime : 0f;
            if (held >= holdSeconds) { DoRecenter(); held = float.NegativeInfinity; }   // once per hold (release to reset)

            if (messageTimer > 0f && (messageTimer -= Time.unscaledDeltaTime) <= 0f && message) message.text = "";
        }

        public void DoRecenter()
        {
            if (!origin) return;
            origin.MatchOriginUpCameraForward(Vector3.up, startForward);                       // face the panel
            var cam = origin.Camera.transform.position;
            origin.MoveCameraToWorldLocation(new Vector3(startPoint.x, cam.y, startPoint.z));  // stand on the start point
            if (message) message.text = "Recentered";
            messageTimer = 2f;
            Debug.Log("[HCD545] Recentered");
        }
    }
}
