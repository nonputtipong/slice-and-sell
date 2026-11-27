using UnityEngine;

namespace SliceAndSell
{
    // Sends a thief now and then during a shift: never during the Level 1 tutorial's first two customers, never two
    // at once, never while paused, after the shift has ended or in the main menu. Added by OrderManager at start (no
    // scene object), using one of its customer models.
    public class ThiefSpawner : MonoBehaviour
    {
        public OrderManager orders;
        public Vector2 firstDelay = new Vector2(12f, 20f), every = new Vector2(30f, 50f);   // s, random in the range

        // Performance measuring (Development build on the device only): the same thieves at the same times in every run,
        // so runs compare. 60 s into the shift, then every 60 s, the customer models in turn. The real game stays random.
        public static bool FixedTiming => Debug.isDebugBuild && !Application.isEditor;
        public const float FixedFirst = 60f, FixedEvery = 60f;
        int spawned;

        public Thief Current { get; private set; }
        float due = -1f;
        int shift = -1;

        void Update()
        {
            if (!orders || orders.customerPrefabs == null || orders.customerPrefabs.Length == 0) return;
            if (orders.ShiftId != shift) { shift = orders.ShiftId; spawned = 0; due = FixedTiming ? FixedFirst : Random.Range(firstDelay.x, firstDelay.y); }
            if (Current || GamePause.Paused || !orders.Level || !orders.Running || orders.ShiftEnded) return;
            if (orders.levelIndex == 0 && orders.Served < 2) return;          // let the tutorial finish first
            if ((due -= Time.deltaTime) > 0f) return;
            Spawn();
            due = FixedTiming ? FixedEvery : Random.Range(every.x, every.y);
        }

        // route / tough null: chosen as below (tests pass them to get a known thief).
        public Thief Spawn(Thief.Escape? forceRoute = null, bool? forceTough = null)
        {
            int pick = FixedTiming ? spawned % orders.customerPrefabs.Length : Random.Range(0, orders.customerPrefabs.Length);
            spawned++;
            var prefab = orders.customerPrefabs[pick];
            var body = Instantiate(prefab, Thief.Entry, Quaternion.identity, orders.transform);
            body.name = "Thief";
            var thief = body.gameObject.AddComponent<Thief>();
            // Routes in turn and every second thief tough when measuring (FixedTiming); otherwise random, about 1 in 3 tough.
            var route = FixedTiming ? (Thief.Escape)((spawned - 1) % 3) : (Thief.Escape)Random.Range(0, 3);
            bool tough = FixedTiming ? spawned % 2 == 0 : Random.value < 0.35f;
            thief.Begin(orders, forceRoute ?? route, forceTough ?? tough);
            return Current = thief;
        }
    }
}
