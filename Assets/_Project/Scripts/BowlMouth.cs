using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Trigger over a bowl's opening. An extra dropped in is added at once (hands only need a grab and a release).
    // A fully cut fruit let go over it (nothing holding it: no hand, no socket) goes in, wherever the bowl stands,
    // so the player never has to carry the bowl to the board first.
    public class BowlMouth : MonoBehaviour
    {
        public Bowl bowl;

        void OnTriggerEnter(Collider other)
        {
            var extra = other.GetComponentInParent<Extra>();
            if (extra && bowl.AddExtra(extra.kind)) Destroy(extra.gameObject);
        }

        // Stay, not Enter: the fruit usually enters while still held and is let go a moment later.
        void OnTriggerStay(Collider other)
        {
            if (bowl.IsFilled) return;
            var fruit = other.GetComponentInParent<Fruit>();
            if (!fruit || !fruit.IsFullyCut) return;
            var grab = fruit.GetComponent<XRBaseInteractable>();
            if (grab && grab.isSelected) return;
            bowl.Fill(fruit);
            Destroy(fruit.gameObject);
        }
    }
}
