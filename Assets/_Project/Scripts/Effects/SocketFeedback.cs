using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // Visible states for a socket (board fruit socket, bag scoop spot, hand-off tray), assignment §7:
    //   hover   -> an item it accepts is in range: its mesh tints toward the highlight
    //   select  -> the item snaps in: a brief bright flash + a click
    //   release -> the item is taken out: back to normal
    // The socket only hovers items its layers and filters accept, so "acceptable" needs no check here.
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SocketFeedback : MonoBehaviour
    {
        public Renderer[] targets;          // the board, scoop mark or tray mesh
        public Color highlightColour = new Color(0.55f, 0.8f, 1f);
        [Range(0f, 1f)] public float highlightBlend = 0.5f;
        public Color flashColour = Color.white;
        [Range(0f, 1f)] public float flashBlend = 0.8f;
        public float flashSeconds = 0.15f;
        public AudioClip snapSound;

        XRSocketInteractor socket;
        RendererTint tint;
        AudioSource audioSource;
        bool flashing;

        void Awake()
        {
            socket = GetComponent<XRSocketInteractor>();
            tint = new RendererTint(targets);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.playOnAwake = false;
        }

        void OnEnable()
        {
            socket.hoverEntered.AddListener(OnHover);
            socket.hoverExited.AddListener(OnHoverEnd);
            socket.selectEntered.AddListener(OnSnap);
            socket.selectExited.AddListener(OnTakenOut);
        }

        void OnDisable()
        {
            socket.hoverEntered.RemoveListener(OnHover);
            socket.hoverExited.RemoveListener(OnHoverEnd);
            socket.selectEntered.RemoveListener(OnSnap);
            socket.selectExited.RemoveListener(OnTakenOut);
            CancelInvoke(nameof(EndFlash));
            flashing = false;
            tint.Restore();
        }

        void OnHover(HoverEnterEventArgs a) => Apply();
        void OnHoverEnd(HoverExitEventArgs a) => Apply();
        void OnTakenOut(SelectExitEventArgs a) => Apply();

        void OnSnap(SelectEnterEventArgs a)
        {
            CancelInvoke(nameof(EndFlash));
            flashing = true;
            tint.Set(flashColour, flashBlend);
            Invoke(nameof(EndFlash), flashSeconds);
            if (snapSound) audioSource.PlayOneShot(snapSound);
        }

        void EndFlash() { flashing = false; Apply(); }

        // An occupied socket keeps hovering its own item, so only an empty socket shows the highlight.
        void Apply()
        {
            if (flashing) return;     // let the snap flash finish
            if (!socket.hasSelection && socket.hasHover) tint.Set(highlightColour, highlightBlend);
            else tint.Restore();
        }
    }
}
