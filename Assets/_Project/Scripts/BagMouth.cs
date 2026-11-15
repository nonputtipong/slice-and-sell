using UnityEngine;

namespace SliceAndSell
{
    // Trigger at the top of a bag.
    public class BagMouth : MonoBehaviour
    {
        public Bag bag;

        void OnTriggerEnter(Collider other)
        {
            var extra = other.GetComponentInParent<Extra>();
            if (extra && bag.AddExtra(extra.kind)) Destroy(extra.gameObject);
        }
    }
}
