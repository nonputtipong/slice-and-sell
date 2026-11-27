using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SliceAndSell
{
    // GDD > 3D UI > In-game menu: a wooden fruit-crate panel (built by WristMenuBuilder). Opened and closed with buttons
    // only, never a gesture: the MENU sign on the cart's front-right corner (poke, ray or dwell), the left controller's
    // Menu button, or the panel's own Close. It opens half a metre in front of the eyes and stays put until closed.
    // Opening it doesn't pause; Pause does, and while paused the panel stays open (Close is greyed out) so Resume is
    // always in reach. (The class keeps its old name: the scene and tests reference it.)
    public class WristMenu : MonoBehaviour
    {
        public Transform panel;                         // the UiKit canvas, a child of this object
        public TMP_Text titleLabel, pauseLabel, stanceLabel, volumeLabel, dwellLabel;
        public Button closeButton;
        public OrderManager orders;
        public Transform origin, head;                  // XR Origin root and its camera
        public float distance = 0.45f, below = 0.1f;    // where it opens: ahead of the eyes, a little under eye height
        public UnityEvent onExitToMenu = new UnityEvent();

        public bool IsOpen { get; private set; }

        InputAction menuButton;

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

        void Update()
        {
            if (menuButton.WasPressedThisFrame()) Toggle();
        }

        // The MENU sign and the controller's Menu button.
        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            if (IsOpen) return;
            Place();
            panel.gameObject.SetActive(IsOpen = true);
            Refresh();
        }

        // Ignored while paused: Resume first (the greyed-out Close button says so).
        public void Close()
        {
            if (!IsOpen || GamePause.Paused) return;
            panel.gameObject.SetActive(IsOpen = false);
        }

        // Level, along where the head points (as HandLossPause places its card), facing the eyes.
        void Place()
        {
            var ahead = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.ProjectOnPlane(head.up, Vector3.up);
            ahead.Normalize();
            var pos = head.position + ahead * distance + Vector3.down * below;
            panel.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
        }

        void OnPauseChanged(bool _) => Refresh();

        void Refresh()
        {
            bool paused = GamePause.Paused;
            if (titleLabel) titleLabel.text = paused ? "Paused" : "Menu";
            if (pauseLabel) pauseLabel.text = paused ? "Resume" : "Pause";
            if (closeButton) closeButton.interactable = !paused;
            if (stanceLabel) stanceLabel.text = GameSettings.Seated ? "Seated" : "Standing";
            if (volumeLabel) volumeLabel.text = $"Volume\n{Mathf.RoundToInt(GameSettings.Volume * 100f)}%";
            if (dwellLabel) dwellLabel.text = "Dwell to press: " + (GameSettings.Dwell ? "On" : "Off");
        }

        // Buttons (persistent listeners set by WristMenuBuilder).
        public void TogglePause() => GamePause.Toggle();
        public void Restart() { orders.Retry(); Close(); }      // StartLevel also unpauses
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
            if (IsOpen) Place();                                // the panel follows you back to the stall
        }

        public void ExitToMenu()
        {
            orders.StopShift();                                 // also unpauses
            Close();
            onExitToMenu.Invoke();
        }
    }
}
