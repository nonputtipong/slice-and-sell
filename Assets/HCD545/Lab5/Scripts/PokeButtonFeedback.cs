using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HCD545
{
    // Lab 5: feedback for one poke button. There is nothing to feel when a fingertip touches the air,
    // so the button shows and plays the press instead:
    //   rest    -> the button face stands out from the panel (raised by 'depth' mm)
    //   press   -> the face drops flat onto the panel, shrinks a little, and a click plays
    //   release -> the face comes back up, and a softer click plays
    // It works the same for a fingertip (poke), a controller tip and a ray with the Trigger.
    // The hit area (the invisible Image on this object) never moves, so the press does not flicker.
    [RequireComponent(typeof(Selectable))]
    public class PokeButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform face;              // the visible part of the button (its Image and label)
        public AudioClip pressSound, releaseSound;
        public float depth = 6f;                // mm the face stands out at rest (1 canvas unit = 1 mm)
        [Range(0.5f, 1f)] public float pressedScale = 0.94f;
        [Range(0f, 1f)] public float volume = 0.8f;

        AudioSource audioSource;
        Selectable selectable;
        bool down;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            audioSource = GetComponent<AudioSource>();
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;      // the click comes from the button
            audioSource.minDistance = 0.3f;
            Show(false);
        }

        void OnDisable() { down = false; Show(false); }

        public void OnPointerDown(PointerEventData e)
        {
            if (down || !selectable.IsInteractable()) return;
            down = true;
            Show(true);
            if (pressSound) audioSource.PlayOneShot(pressSound, volume);
        }

        public void OnPointerUp(PointerEventData e) => Release();
        public void OnPointerExit(PointerEventData e) { if (down) Release(); }

        void Release()
        {
            if (!down) return;
            down = false;
            Show(false);
            if (releaseSound) audioSource.PlayOneShot(releaseSound, volume * 0.6f);
        }

        // Pressed: flat on the panel. At rest: 'depth' mm toward the user (a world-space canvas faces -Z).
        void Show(bool pressed)
        {
            if (!face) return;
            face.localPosition = new Vector3(face.localPosition.x, face.localPosition.y, pressed ? 0f : -depth);
            face.localScale = Vector3.one * (pressed ? pressedScale : 1f);
        }

        public bool isPressed => down;
    }
}
