using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace HCD545
{
    // Lab 5: the comfort menu on your left wrist. The same choices as the comfort settings in Lab 2 and Lab 3:
    //   Turn: snap / smooth   Snap angle 30° / 45°   Move: teleport / smooth   Vignette on / off   Seated / Standing   Reset height
    // It opens when you turn the inside of your left wrist towards your face and look at it,
    // or when you press the Menu button on the left controller (press again to close).
    // With hands, the menu gesture (look at your left palm and pinch) does the same as the Menu button.
    // The right-hand palm pinch stays the Quest's own system menu (the sides swap if the Quest is set to left-handed).
    public class ComfortMenu : MonoBehaviour
    {
        [Header("Menu (a small world-space canvas)")]
        public Transform menu;
        public Button turnButton, angleButton, moveButton, vignetteButton, seatedButton, resetHeightButton;

        [Header("Rig parts (found automatically if empty)")]
        public XROrigin origin;
        public XRInputModalityManager modality;
        public GameObject vignette;                     // the TunnelingVignette object under the Main Camera (found by its TunnelingVignetteController)

        [Header("Wrist test")]
        [Range(0f, 1f)] public float facing = 0.55f;    // how much the inside of the wrist must face you (1 = exactly)
        public float seatedEyeHeight = 1.6f;

        bool smoothTurn, angle30, smoothMove, vignetteOn = true, seated, pinned, handMenuWasPressed;
        float heightOffset;
        InputAction menuButton;
        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();

        void Start()
        {
            if (!origin) origin = FindAnyObjectByType<XROrigin>();
            if (!modality) modality = FindAnyObjectByType<XRInputModalityManager>();
            if (!vignette)
            {
                var v = FindAnyObjectByType<TunnelingVignetteController>(FindObjectsInactive.Include);
                if (v) vignette = v.gameObject;
            }
            turnButton.onClick.AddListener(() => { smoothTurn = !smoothTurn; Apply(); });
            angleButton.onClick.AddListener(() => { angle30 = !angle30; Apply(); });
            moveButton.onClick.AddListener(() => { smoothMove = !smoothMove; Apply(); });
            vignetteButton.onClick.AddListener(() => { vignetteOn = !vignetteOn; Apply(); });
            seatedButton.onClick.AddListener(() => { seated = !seated; ResetHeight(); });
            resetHeightButton.onClick.AddListener(ResetHeight);

            menuButton = new InputAction(type: InputActionType.Button, binding: "<XRController>{LeftHand}/{MenuButton}");
            menuButton.performed += _ => pinned = !pinned;
            menuButton.Enable();
            Apply();
        }

        void OnDestroy() => menuButton?.Dispose();

        // Writes the choices into the XRI locomotion components of the rig, then updates the button labels.
        void Apply()
        {
            foreach (var m in FindObjectsByType<ControllerInputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                m.smoothTurnEnabled = smoothTurn;       // thumbstick left/right: snap turn or smooth turn
                // Move: smooth = the LEFT thumbstick moves you. The right thumbstick always keeps teleport and turning:
                // in XRI a thumbstick that does smooth movement cannot turn, so smooth movement on both sticks
                // would leave a seated player with no way to turn.
                m.smoothMotionEnabled = smoothMove && IsLeft(m);
            }
            foreach (var s in FindObjectsByType<SnapTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None)) s.turnAmount = angle30 ? 30f : 45f;
            if (vignette) vignette.SetActive(vignetteOn);

            Label(turnButton, smoothTurn ? "Turn: smooth" : "Turn: snap");
            Label(angleButton, angle30 ? "Snap angle 30°" : "Snap angle 45°");
            Label(moveButton, smoothMove ? "Move: smooth" : "Move: teleport");
            Label(vignetteButton, vignetteOn ? "Vignette on" : "Vignette off");
            Label(seatedButton, seated ? "Seated" : "Standing");
        }

        // Seated: lift the view so that your eyes are at standing height (1.6 m). Standing: no lift.
        public void ResetHeight()
        {
            if (origin)
            {
                var offset = origin.CameraFloorOffsetObject.transform;
                float realHeight = origin.CameraInOriginSpaceHeight - heightOffset;
                heightOffset = seated ? Mathf.Clamp(seatedEyeHeight - realHeight, 0f, 1f) : 0f;
                offset.localPosition = new Vector3(offset.localPosition.x, heightOffset, offset.localPosition.z);
            }
            Apply();
        }

        bool IsLeft(ControllerInputActionManager m)
        {
            if (modality && modality.leftController) return m.transform.IsChildOf(modality.leftController.transform);
            return m.name.Contains("Left");
        }

        static void Label(Button b, string text) { if (b) b.GetComponentInChildren<TMP_Text>().text = text; }

        // Hands: the menu gesture (palm toward you, pinch) sets the Menu Pressed flag of a Meta Aim Hand. Only the
        // non-dominant hand (the left hand by default) ever sets it; the other hand's pinch is the Quest's system menu.
        void Update()
        {
            bool pressed = MenuPinch(MetaAimHand.left) || MenuPinch(MetaAimHand.right);
            if (pressed && !handMenuWasPressed) pinned = !pinned;
            handMenuWasPressed = pressed;
        }

        static bool MenuPinch(MetaAimHand h) =>
            h != null && h.added && (((MetaAimFlags)h.aimFlags.ReadValue()) & MetaAimFlags.MenuPressed) != 0;

        void LateUpdate()
        {
            if (!menu || !origin) return;
            var head = origin.Camera.transform;
            bool show = pinned;
            if (WristPose(out Vector3 wrist, out Vector3 inside))
            {
                Vector3 toHead = (head.position - wrist).normalized;
                bool wristFacesYou = Vector3.Dot(inside, toHead) > facing;
                bool youLookAtIt = Vector3.Dot(head.forward, -toHead) > 0.6f;
                show |= wristFacesYou && youLookAtIt;
                if (show) Place(wrist + Vector3.up * 0.14f, head);
            }
            else if (pinned) Place(head.position + head.forward * 0.45f - Vector3.up * 0.15f, head);
            menu.gameObject.SetActive(show);
        }

        void Place(Vector3 pos, Transform head)
        {
            menu.position = pos;
            menu.rotation = Quaternion.LookRotation(pos - head.position, Vector3.up);   // the menu faces you
        }

        // Where is the left wrist, and which way does its inside (the palm side) point?
        bool WristPose(out Vector3 pos, out Vector3 inside)
        {
            pos = inside = Vector3.zero;
            var mode = XRInputModalityManager.currentInputMode.Value;
            if (mode == XRInputModalityManager.InputMode.TrackedHand)
            {
                SubsystemManager.GetSubsystems(hands);
                foreach (var h in hands)
                {
                    if (!h.running || !h.leftHand.isTracked) continue;
                    if (!h.leftHand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose p)) continue;
                    var space = origin.CameraFloorOffsetObject.transform;               // hand joints are in rig space
                    pos = space.TransformPoint(p.position);
                    inside = space.TransformDirection(p.rotation * Vector3.down);       // the palm side is -Y of the joint
                    return true;
                }
                return false;
            }
            var controller = modality ? modality.leftController : null;
            if (controller && controller.activeInHierarchy)
            {
                pos = controller.transform.position;
                inside = controller.transform.right;    // left hand holding a controller: the palm faces the controller's +X
                return true;
            }
            return false;
        }
    }
}
