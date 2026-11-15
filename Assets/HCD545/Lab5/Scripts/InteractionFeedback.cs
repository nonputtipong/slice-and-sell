using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HCD545
{
    // Lab 5: feedback for one interactable object (a grab cube, the ray sphere).
    //   hover   -> the object turns light blue (you know you can take it)
    //   select  -> orange + a click sound + a short buzz on a controller (hands have no buzz, so the sound matters more)
    //   release -> back to its own color + a softer sound
    [RequireComponent(typeof(XRBaseInteractable))]
    public class InteractionFeedback : MonoBehaviour
    {
        public Color hoverColour = new Color(0.55f, 0.8f, 1f);
        public Color selectColour = new Color(1f, 0.34f, 0.17f);
        public AudioClip selectSound, releaseSound;
        [Range(0f, 1f)] public float hapticStrength = 0.5f;
        public float hapticSeconds = 0.08f;

        XRBaseInteractable interactable;
        Renderer rend;
        AudioSource audioSource;
        MaterialPropertyBlock block;
        Color baseColour;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            rend = GetComponentInChildren<Renderer>();
            baseColour = rend.sharedMaterial.GetColor(BaseColorId);
            block = new MaterialPropertyBlock();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;      // the sound comes from the object
            audioSource.playOnAwake = false;
        }

        void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHover);
            interactable.hoverExited.AddListener(OnHoverEnd);
            interactable.selectEntered.AddListener(OnSelect);
            interactable.selectExited.AddListener(OnRelease);
        }

        void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHover);
            interactable.hoverExited.RemoveListener(OnHoverEnd);
            interactable.selectEntered.RemoveListener(OnSelect);
            interactable.selectExited.RemoveListener(OnRelease);
        }

        void OnHover(HoverEnterEventArgs a) { if (!interactable.isSelected) Tint(hoverColour); }
        void OnHoverEnd(HoverExitEventArgs a) { if (!interactable.isSelected && !interactable.isHovered) Tint(baseColour); }

        void OnSelect(SelectEnterEventArgs a)
        {
            Tint(selectColour);
            if (selectSound) audioSource.PlayOneShot(selectSound);
            // A controller interactor can buzz. A hand interactor has no haptic device, so this does nothing for hands.
            if (a.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(hapticStrength, hapticSeconds);
        }

        void OnRelease(SelectExitEventArgs a)
        {
            Tint(interactable.isHovered ? hoverColour : baseColour);
            if (releaseSound) audioSource.PlayOneShot(releaseSound, 0.6f);
        }

        // A property block changes the color of this object only, without making a copy of the material.
        void Tint(Color c)
        {
            rend.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            rend.SetPropertyBlock(block);
        }
    }
}
