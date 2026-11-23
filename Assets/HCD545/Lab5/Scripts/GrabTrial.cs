using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HCD545
{
    // Lab 5: counts how well grabbing works on the 3 cm, 6 cm and 12 cm cubes.
    //   Grabs  = a cube was picked up
    //   Drops  = a cube fell to the floor after you let go (or slipped out of your hand)
    //   Misses = you pressed grip / pinched within 10 cm of a cube, but nothing was picked up
    // Compare controllers with hands: which cube size gives the most misses?
    public class GrabTrial : MonoBehaviour
    {
        public XRGrabInteractable[] cubes;
        public TMP_Text grabsText, dropsText, missesText;
        public Button resetButton;
        public InputActionReference leftSelect, rightSelect;   // XRI Left/Right Interaction > Select (grip button or pinch)
        public float missDistance = 0.10f;

        int grabs, drops, misses;
        readonly bool[] onFloorCounted = new bool[8];

        void OnEnable()
        {
            foreach (var c in cubes) c.selectEntered.AddListener(OnGrab);
            if (resetButton) resetButton.onClick.AddListener(ResetTrial);
            if (leftSelect) { leftSelect.action.performed += OnLeftSelect; leftSelect.action.Enable(); }
            if (rightSelect) { rightSelect.action.performed += OnRightSelect; rightSelect.action.Enable(); }
            Show();
        }

        void OnDisable()
        {
            foreach (var c in cubes) c.selectEntered.RemoveListener(OnGrab);
            if (resetButton) resetButton.onClick.RemoveListener(ResetTrial);
            if (leftSelect) leftSelect.action.performed -= OnLeftSelect;
            if (rightSelect) rightSelect.action.performed -= OnRightSelect;
        }

        void OnGrab(SelectEnterEventArgs a) { grabs++; Show(); }

        void Update()
        {
            // A drop: a cube that nobody holds is lower than 0.3 m (on the floor). Count it once until it is picked up again.
            for (int i = 0; i < cubes.Length; i++)
            {
                bool low = !cubes[i].isSelected && cubes[i].transform.position.y < 0.3f;
                if (low && !onFloorCounted[i]) { drops++; onFloorCounted[i] = true; Show(); }
                if (cubes[i].isSelected) onFloorCounted[i] = false;
            }
        }

        void OnLeftSelect(InputAction.CallbackContext c) => StartCoroutine(CheckMiss(InteractorHandedness.Left));
        void OnRightSelect(InputAction.CallbackContext c) => StartCoroutine(CheckMiss(InteractorHandedness.Right));

        // Wait one frame so that XRI can make the grab, then look: did this hand pick something up?
        IEnumerator CheckMiss(InteractorHandedness hand)
        {
            yield return null;
            var interactors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None);
            foreach (var it in interactors)
                if (it.handedness == hand && it.isActiveAndEnabled && it.hasSelection) yield break;   // it grabbed something
            foreach (var it in interactors)
            {
                if (it.handedness != hand || !it.isActiveAndEnabled) continue;
                foreach (var c in cubes)
                    if (Vector3.Distance(it.attachTransform.position, c.transform.position) < missDistance) { misses++; Show(); yield break; }
            }
        }

        public void ResetTrial()
        {
            grabs = drops = misses = 0;
            foreach (var c in cubes) { var r = c.GetComponent<RespawnOnDrop>(); if (r) r.ReturnToStart(); }
            for (int i = 0; i < onFloorCounted.Length; i++) onFloorCounted[i] = false;
            Show();
        }

        void Show()
        {
            if (grabsText) grabsText.text = grabs.ToString();
            if (dropsText) dropsText.text = drops.ToString();
            if (missesText) missesText.text = misses.ToString();
        }
    }
}
