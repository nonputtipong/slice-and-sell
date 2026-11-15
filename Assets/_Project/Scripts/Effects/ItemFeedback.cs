using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // Hover / select / release feedback for a grabbable (replaces Lab 5 InteractionFeedback, which tinted one renderer).
    //   hover   -> every material blends toward light blue (still recognisably the same fruit)
    //   select  -> toward orange + a sound + a buzz on controllers (hands have no buzz, so the sound matters more)
    //   release -> own colours + a softer sound, once the last hand lets go (the cleaver takes two)
    // Sockets don't count: a fruit sitting on the board is not "held"; the socket shows its own feedback.
    [RequireComponent(typeof(XRBaseInteractable))]
    public class ItemFeedback : MonoBehaviour
    {
        public Color hoverColour = new Color(0.55f, 0.8f, 1f);
        public Color selectColour = new Color(1f, 0.34f, 0.17f);
        [Range(0f, 1f)] public float blend = 0.45f;
        public AudioClip selectSound, releaseSound;
        [Range(0f, 1f)] public float hapticStrength = 0.5f;
        public float hapticSeconds = 0.08f;

        XRBaseInteractable interactable;
        RendererTint tint;
        AudioSource audioSource;
        bool wasHeld;

        void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            tint = new RendererTint(GetComponentsInChildren<Renderer>(true));   // inactive cut stages / bag contents too
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

        void OnHover(HoverEnterEventArgs a) => Apply();
        void OnHoverEnd(HoverExitEventArgs a) => Apply();

        void OnSelect(SelectEnterEventArgs a)
        {
            if (a.interactorObject is XRSocketInteractor) return;
            Apply();
            if (selectSound) audioSource.PlayOneShot(selectSound);
            Haptics.Pulse(interactable, hapticStrength, hapticSeconds);
        }

        void OnRelease(SelectExitEventArgs a)
        {
            bool held = wasHeld;
            Apply();
            if (held && !wasHeld && releaseSound) audioSource.PlayOneShot(releaseSound, 0.6f);
        }

        void Apply()
        {
            wasHeld = Any(interactable.interactorsSelecting);
            if (wasHeld) tint.Set(selectColour, blend);
            else if (Any(interactable.interactorsHovering)) tint.Set(hoverColour, blend);
            else tint.Restore();
        }

        static bool Any<T>(System.Collections.Generic.List<T> interactors) where T : IXRInteractor
        {
            foreach (var i in interactors) if (!(i is XRSocketInteractor)) return true;
            return false;
        }
    }

    // Blends every material slot of a set of renderers toward a colour, with property blocks (_BaseColor, URP Lit):
    // materials stay shared and instanced. Blocks are set on inactive renderers too, so a cut stage that is switched
    // on mid-hover already shows the tint.
    public class RendererTint
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        readonly Renderer[] renderers;
        readonly Color[][] originals;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public RendererTint(Renderer[] renderers)
        {
            this.renderers = renderers;
            originals = new Color[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r].sharedMaterials;
                originals[r] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    originals[r][m] = mats[m] && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId) : Color.white;
            }
        }

        public void Set(Color target, float amount) => Write(target, amount);
        public void Restore() => Write(Color.white, 0f);

        void Write(Color target, float amount)
        {
            for (int r = 0; r < renderers.Length; r++)
            {
                if (!renderers[r]) continue;
                for (int m = 0; m < originals[r].Length; m++)
                {
                    var c = Color.Lerp(originals[r][m], target, amount);
                    c.a = originals[r][m].a;
                    renderers[r].GetPropertyBlock(block, m);
                    block.SetColor(BaseColorId, c);
                    renderers[r].SetPropertyBlock(block, m);
                }
            }
        }
    }
}
