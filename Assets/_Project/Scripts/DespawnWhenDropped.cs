using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Endless items that end up on the floor are removed after a moment (GDD > Failure cases);
    // the bin or rack always has another.
    public class DespawnWhenDropped : MonoBehaviour
    {
        public float belowHeight = 0.5f;    // well under the 0.9 m counter top
        public float seconds = 2f;
        XRBaseInteractable interactable;
        float low;

        void Awake() => interactable = GetComponent<XRBaseInteractable>();

        void Update()
        {
            bool held = interactable && interactable.isSelected;
            low = !held && transform.position.y < belowHeight ? low + Time.deltaTime : 0f;
            if (low > seconds) Destroy(gameObject);
        }
    }
}
