using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Endless bin (fruit, bowls, extras). Playtest 5: taking an item no longer makes the next one at once (the counter
    // filled up with fruit nobody needed); a bin refills only once its item is used up: cut into a bowl, stolen,
    // burst on someone, handed over, or dropped and despawned. Until then the item is still the bin's `Current`,
    // wherever it is. (onlyWhenGone = false brings back the old refill-on-take, for a bin that should never run dry.)
    // The next one also waits until the last one has left the spawn spot: spawned inside an item still there, it
    // would be shoved out of the bin.
    public class Dispenser : MonoBehaviour
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public float respawnDelay = 0.3f;
        public float leftBin = 0.12f;       // m from the spawn point: out of every crate (the widest is 23 cm)
        // On (every bin since playtest 5): picking the item up or carrying it away makes no new one; the next appears
        // only once that item is gone.
        public bool onlyWhenGone = true;

        public XRGrabInteractable Current { get; private set; }
        // The item is still in its bin, untouched (a thief only takes one of these).
        public bool Waiting => Current && !Current.isSelected && !Away(Current);

        XRGrabInteractable last;            // the item that left (or is leaving) the bin
        float due = -1f;                    // time the next one may appear; < 0 while one is waiting

        void Start() => Spawn();

        void Spawn()
        {
            due = -1f;
            last = null;
            Current = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation).GetComponent<XRGrabInteractable>();
            Current.selectEntered.AddListener(OnTaken);
        }

        void Update()
        {
            if (due >= 0f)
            {
                if (Time.time >= due && (!last || Away(last))) Spawn();
                return;
            }
            // Unity null: destroyed without a grab. Far from the spawn point: knocked out of the bin.
            if (!Current || (!onlyWhenGone && Away(Current))) Release();
        }

        bool Away(Component item) => (item.transform.position - spawnPoint.position).sqrMagnitude > leftBin * leftBin;

        void OnTaken(SelectEnterEventArgs args) { if (!onlyWhenGone) Release(); }

        void Release()
        {
            if (Current) Current.selectEntered.RemoveListener(OnTaken);
            last = Current;
            Current = null;
            due = Time.time + respawnDelay;
        }
    }
}
