using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HCD545
{
    // Lab 4: the Input panel. It shows, live, what each controller sends to the app, and writes one line to the log per press.
    // Read the log on your computer with:  adb logcat -s Unity     (look for lines that start with [HCD545])
    // Trigger and grip are analog (0.00 to 1.00). The A/B/X/Y buttons are digital (0 or 1). The thumbstick is two analog axes.
    public class InputPanel : MonoBehaviour
    {
        [Header("XRI Default Input Actions (from the Starter Assets sample)")]
        public InputActionReference leftTrigger, rightTrigger;   // XRI Left/Right Interaction > Activate Value
        public InputActionReference leftGrip, rightGrip;         // XRI Left/Right Interaction > Select Value
        public InputActionReference leftStick, rightStick;       // XRI Left/Right > Thumbstick (shown as "x, y")
        public InputActionReference leftTracked, rightTracked;   // XRI Left/Right > Is Tracked

        [Header("Value text on the panel")]
        public TMP_Text triggerL, triggerR, gripL, gripR, stickL, stickR;
        public TMP_Text buttonXA_L, buttonXA_R, buttonYB_L, buttonYB_R, trackingL, trackingR, headHeight;

        [Header("Used for the head height (meters above the floor)")]
        public XROrigin origin;

        // The XRI default actions have no A, B, X or Y button actions, so we make our own four here.
        InputAction xButton, yButton, aButton, bButton;
        readonly Press[] presses = new Press[8];

        void OnEnable()
        {
            xButton = Make("<XRController>{LeftHand}/primaryButton");    // X on the left controller
            yButton = Make("<XRController>{LeftHand}/secondaryButton");  // Y on the left controller
            aButton = Make("<XRController>{RightHand}/primaryButton");   // A on the right controller
            bButton = Make("<XRController>{RightHand}/secondaryButton"); // B on the right controller
            foreach (var r in new[] { leftTrigger, rightTrigger, leftGrip, rightGrip, leftStick, rightStick, leftTracked, rightTracked })
                if (r != null) r.action.Enable();
            string[] names = { "Left Trigger", "Right Trigger", "Left Grip", "Right Grip", "Left X button", "Left Y button", "Right A button", "Right B button" };
            for (int i = 0; i < presses.Length; i++) presses[i] = new Press(names[i]);
        }

        void OnDisable()
        {
            foreach (var a in new[] { xButton, yButton, aButton, bButton }) a?.Dispose();
        }

        static InputAction Make(string binding)
        {
            var a = new InputAction(type: InputActionType.Button, binding: binding);
            a.Enable();
            return a;
        }

        void Update()
        {
            float tl = Read(leftTrigger), tr = Read(rightTrigger), gl = Read(leftGrip), gr = Read(rightGrip);
            Show(triggerL, tl); Show(triggerR, tr); Show(gripL, gl); Show(gripR, gr);
            ShowStick(stickL, leftStick); ShowStick(stickR, rightStick);

            float x = xButton.ReadValue<float>(), y = yButton.ReadValue<float>(), a = aButton.ReadValue<float>(), b = bButton.ReadValue<float>();
            Show(buttonXA_L, x); Show(buttonXA_R, a); Show(buttonYB_L, y); Show(buttonYB_R, b);

            if (trackingL) trackingL.text = IsOn(leftTracked) ? "tracked" : "lost";
            if (trackingR) trackingR.text = IsOn(rightTracked) ? "tracked" : "lost";

            if (headHeight && origin) headHeight.text = $"{origin.CameraInOriginSpaceHeight:0.00} m";

            // One log line per press: when the value drops back below 0.1, log the highest value reached during that press.
            float[] values = { tl, tr, gl, gr, x, y, a, b };
            for (int i = 0; i < presses.Length; i++) presses[i].Update(values[i]);
        }

        static float Read(InputActionReference r) => r != null ? r.action.ReadValue<float>() : 0f;
        static bool IsOn(InputActionReference r) => r != null && r.action.IsPressed();
        static void Show(TMP_Text t, float v) { if (t) t.text = v.ToString("0.00"); }

        static void ShowStick(TMP_Text t, InputActionReference r)
        {
            if (!t || r == null) return;
            Vector2 v = r.action.ReadValue<Vector2>();
            t.text = $"{v.x:0.00}, {v.y:0.00}";      // x (left -1 to right +1), y (back -1 to forward +1)
        }

        // Remembers the peak of one press so that we write one line per press, not one line per frame.
        class Press
        {
            readonly string name; bool down; float peak;
            public Press(string n) { name = n; }
            public void Update(float v)
            {
                if (v > 0.1f) { down = true; peak = Mathf.Max(peak, v); }
                else if (down) { Debug.Log($"[HCD545] {name} {peak:0.00}"); down = false; peak = 0f; }
            }
        }
    }
}
