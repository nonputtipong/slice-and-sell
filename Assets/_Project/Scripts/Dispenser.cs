using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Endless bin (fruit, bags): there is always one item waiting; taking it makes the next one.
    public class Dispenser : MonoBehaviour
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public float respawnDelay = 0.3f;

        public XRGrabInteractable Current { get; private set; }

        void Start() => Spawn();

        void Spawn()
        {
            Current = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation).GetComponent<XRGrabInteractable>();
            Current.selectEntered.AddListener(OnTaken);
        }

        void OnTaken(SelectEnterEventArgs args)
        {
            Current.selectEntered.RemoveListener(OnTaken);
            Current = null;
            Invoke(nameof(Spawn), respawnDelay);
        }
    }
}
