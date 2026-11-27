using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Endless bin (fruit, bowls, extras): there is always one item waiting. The next one is made when the waiting one
    // is taken, knocked out of the bin, or used up / despawned without ever being grabbed (a chili tipped into a bowl,
    // a bowl filled where it stands and knocked off the counter).
    // The next one waits until the last one has left the spawn spot: spawned inside an item still held there, it
    // would be shoved out of the bin and count as knocked out itself.
    public class Dispenser : MonoBehaviour
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public float respawnDelay = 0.3f;
        public float leftBin = 0.12f;       // m from the spawn point: out of every crate (the widest is 23 cm)
        // The bowl spot: one bowl waits there, and picking it up or carrying it away makes no new one. The next appears
        // only once that bowl is gone: handed over (the tray uses it up) or dropped and despawned.
        public bool onlyWhenGone;

        public XRGrabInteractable Current { get; private set; }

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
