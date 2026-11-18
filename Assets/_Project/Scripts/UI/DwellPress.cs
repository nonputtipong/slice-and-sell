using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Dwell to press (accessibility, GameSettings.Dwell, off by default): resting a ray or fingertip on a button for
    // `Seconds` presses it, with no pinch, trigger or poke. A bar along the button's bottom fills while you dwell.
    // One press per visit: move off and back on to press again.
    // On a UI Button it listens to the XR UI pointer (ray and poke both send enter/exit); on a 3D interactable (the
    // service bell, the radio) to XRI hover, and calls `onDwell` (set by the builder to the same action a select does).
    public class DwellPress : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const float Seconds = 1f;

        public Image fill;                  // Filled image, 0..1 (UI buttons; null on 3D objects)
        public UnityEvent onDwell = new UnityEvent();

        Button button;
        XRBaseInteractable interactable;
        int pointers;                       // UI pointers over the button (a ray and a fingertip can both be)
        float held;
        bool armed = true;

        public float Progress => held / Seconds;

        bool Hovered => pointers > 0 || (interactable && interactable.isHovered);

        void Awake()
        {
            button = GetComponent<Button>();
            interactable = GetComponent<XRBaseInteractable>();
            if (fill) fill.fillAmount = 0f;
        }

        void OnDisable() { pointers = 0; armed = true; Clear(); }

        public void OnPointerEnter(PointerEventData _) => pointers++;
        public void OnPointerExit(PointerEventData _) => pointers = Mathf.Max(0, pointers - 1);

        void Update()
        {
            if (!Hovered) armed = true;
            if (!GameSettings.Dwell || !Hovered || !armed) { if (held > 0f) Clear(); return; }
            held += Time.unscaledDeltaTime;        // menus also work while the game is paused
            if (fill) fill.fillAmount = Mathf.Clamp01(Progress);
            if (held < Seconds) return;
            armed = false;
            Clear();
            if (button) { if (button.IsInteractable()) button.onClick.Invoke(); }
            else onDwell.Invoke();
        }

        void Clear()
        {
            held = 0f;
            if (fill) fill.fillAmount = 0f;
        }
    }
}
