using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace SliceAndSell
{
    // GDD > 3D UI > Wrist menu: a wooden fruit-crate panel above the left wrist (built by WristMenuBuilder).
    // Opens while the inside of the left wrist faces your eyes and you look at it (hand or controller);
    // the left controller's Menu button pins it open / closed. Hidden while the left hand/controller isn't tracked.
    // Opening it doesn't pause: pausing is a button (GDD), so a glance at the wrist mid-order costs nothing.
    public class WristMenu : MonoBehaviour
    {
        public Transform panel;                         // the UiKit canvas, a child of this object
        public TMP_Text pauseLabel, stanceLabel, volumeLabel, dwellLabel;
        public OrderManager orders;
        public Transform origin, head;                  // XR Origin root and its camera
        public GameObject leftController;               // XRInputModalityManager.leftController
        [Range(0f, 1f)] public float facing = 0.55f;    // how much the inside of the wrist must face you (1 = exactly)
        public float above = 0.19f;                     // panel centre above the wrist (m); the panel is 32 cm tall
        public UnityEvent onExitToMenu = new UnityEvent();

        bool pinned, open;
        InputAction menuButton;
        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();

        void OnEnable()
        {
            menuButton = new InputAction(type: InputActionType.Button, binding: "<XRController>{LeftHand}/{MenuButton}");
            menuButton.Enable();
            GamePause.Changed += OnPauseChanged;
            GameSettings.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            menuButton.Dispose();
            GamePause.Changed -= OnPauseChanged;
            GameSettings.Changed -= Refresh;
        }

        void LateUpdate()
        {
            if (menuButton.WasPressedThisFrame()) pinned = !pinned;

            bool show = false;
            if (WristPose(out Vector3 wrist, out Vector3 inside))
            {
                Vector3 toHead = (head.position - wrist).normalized;
                float slack = open ? 0.2f : 0f;         // poking with the other hand tilts the wrist: don't flicker shut
                show = pinned || (Vector3.Dot(inside, toHead) > facing - slack && Vector3.Dot(head.forward, -toHead) > 0.6f - slack);
                if (show)
                {
                    var pos = wrist + Vector3.up * above;
                    panel.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));   // faces you
                }
            }
            if (show != open) panel.gameObject.SetActive(open = show);
        }

        // Where is the left wrist, and which way does its inside (the palm side) point? (Same test as Lab 5's ComfortMenu.)
        bool WristPose(out Vector3 pos, out Vector3 inside)
        {
            SubsystemManager.GetSubsystems(hands);
            foreach (var h in hands)
            {
                if (!h.running || !h.leftHand.isTracked || !h.leftHand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose p)) continue;
                var rig = head.parent;                                          // hand joints are in rig (camera offset) space
                pos = rig.TransformPoint(p.position);
                inside = rig.TransformDirection(p.rotation * Vector3.down);     // the palm side is -Y of the wrist joint
                return true;
            }
            if (leftController && leftController.activeInHierarchy && XRInputTrackingAggregator.GetLeftControllerStatus().isTracked)
            {
                pos = leftController.transform.position;
                inside = leftController.transform.right;    // left hand holding a controller: the palm faces its +X
                return true;
            }
            pos = inside = Vector3.zero;
            return false;
        }

        void OnPauseChanged(bool _) => Refresh();

        void Refresh()
        {
            if (pauseLabel) pauseLabel.text = GamePause.Paused ? "Resume" : "Pause";
            if (stanceLabel) stanceLabel.text = GameSettings.Seated ? "Seated" : "Standing";
            if (volumeLabel) volumeLabel.text = $"Volume\n{Mathf.RoundToInt(GameSettings.Volume * 100f)}%";
            if (dwellLabel) dwellLabel.text = "Dwell to press: " + (GameSettings.Dwell ? "On" : "Off");
        }

        // Buttons (persistent listeners set by WristMenuBuilder).
        public void TogglePause() => GamePause.Toggle();
        public void Restart() => orders.Retry();                // StartLevel also unpauses
        public void ToggleSeated() => GameSettings.ToggleSeated();
        public void ToggleDwell() => GameSettings.ToggleDwell();
        public void VolumeDown() => GameSettings.VolumeDown();
        public void VolumeUp() => GameSettings.VolumeUp();

        // Back to the stall's player spot: camera over (0, y, 0), facing +Z (Lab 4 Recenter, without XROrigin).
        public void Recenter()
        {
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(head.forward, Vector3.up), Vector3.forward, Vector3.up);
            origin.RotateAround(head.position, Vector3.up, yaw);
            var cam = head.position;
            origin.position -= new Vector3(cam.x, 0f, cam.z);
        }

        public void ExitToMenu()
        {
            pinned = false;
            orders.StopShift();                                 // also unpauses
            onExitToMenu.Invoke();
        }
    }
}
