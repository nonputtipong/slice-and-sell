using System.Linq;
using TMPro;
using UnityEngine;

namespace SliceAndSell
{
    // The fruit thief: a customer model in dark clothes with "THIEF!" over his head. He walks down the soi to the
    // left side of the cart, takes a fruit from the nearest crate, and runs back up the street with it.
    // Thrown food slows him (and splats his face); a pistol shot knocks him down (ragdoll) for the reward. A thrown
    // knife just bounces off, like off anyone.
    // If he reaches the end of the street with the fruit, the stall loses the penalty.
    // Playtest 4: thieves escape along different routes, and a tough one ("THIEF!!", bigger, dark red) takes two shots:
    // the first only staggers and slows him.
    [RequireComponent(typeof(Customer))]
    public class Thief : MonoBehaviour
    {
        public enum Phase { Coming, Stealing, Running, Caught, Gone }
        public enum Escape { LeftStreet, CrossRight, Zigzag }

        // Down the left of the soi, to the cart's left side (by the crates, ~1.2 m from the player), and away.
        public static readonly Vector3 Entry = new Vector3(-1.4f, 0f, 11f), StealSpot = new Vector3(-1.05f, 0f, 0.55f),
            ExitBend = new Vector3(-1.6f, 0f, 2.5f), Exit = new Vector3(-1.6f, 0f, 13f);

        // Escape routes from the steal spot. CrossRight passes behind the customer at the counter (z 1.35) to the far side.
        public static Vector3[] RouteOf(Escape e)
        {
            switch (e)
            {
                case Escape.CrossRight: return new[] { new Vector3(-1.2f, 0f, 2.1f), new Vector3(1.5f, 0f, 2.8f), new Vector3(1.6f, 0f, 13f) };
                case Escape.Zigzag: return new[] { new Vector3(-1.6f, 0f, 3f), new Vector3(0.9f, 0f, 5.5f), new Vector3(-1.2f, 0f, 8.5f), new Vector3(0.8f, 0f, 13f) };
                default: return new[] { ExitBend, Exit };
            }
        }

        public Escape Route { get; private set; }
        public bool Tough { get; private set; }
        public int Health { get; private set; } = 1;
        public const float WalkSpeed = 1.6f, RunSpeed = 2.6f, StealSeconds = 1.5f;
        public const float SlowScale = 0.4f, SlowSeconds = 2.5f, KnockBack = 40f;

        public OrderManager orders;
        public Phase State { get; private set; }
        public Customer Body { get; private set; }

        GameObject loot;            // the stolen fruit, in his right hand
        float slowLeft, stealLeft;

        void Awake()
        {
            Body = GetComponent<Customer>();
            Body.Hit += OnHit;
            Body.SetPatience(0f, false);
            if (Body.ring) Body.ring.gameObject.SetActive(false);
        }

        void Start()
        {
            Disguise();
            transform.position = Entry;
            State = Phase.Coming;
            Body.Travel(new[] { StealSpot }, "Walk", WalkSpeed, () => Body.Face(Vector3.right, () => { State = Phase.Stealing; stealLeft = StealSeconds; }));
        }

        void Update()
        {
            if (GamePause.Paused || State == Phase.Caught || State == Phase.Gone) return;
            if (slowLeft > 0f)
            {
                slowLeft -= Time.deltaTime;
                if (slowLeft <= 0f) Body.SetSpeedScale(1f);
            }
            if (State == Phase.Stealing && (stealLeft -= Time.deltaTime) <= 0f) Steal();
            // A new shift (or the main menu): he just walks off, nothing lost.
            if (orders && orders.ShiftId != shiftAtStart && State != Phase.Running) RunOff();
        }

        int shiftAtStart;
        public void Begin(OrderManager o) => Begin(o, Escape.LeftStreet, false);
        public void Begin(OrderManager o, Escape route, bool tough)
        {
            orders = o; shiftAtStart = o ? o.ShiftId : 0;
            Route = route; Tough = tough; Health = tough ? 2 : 1;
            if (tough) transform.localScale = Vector3.one * 1.12f;
        }

        void Steal()
        {
            // The fruit waiting in the nearest crate goes into his hand (the crate makes a new one, as when you take it).
            var crate = FindObjectsByType<Dispenser>(FindObjectsSortMode.None)
                .Where(d => d.Current && d.prefab && d.prefab.GetComponent<Fruit>() && !d.Current.isSelected)
                .OrderBy(d => (d.spawnPoint.position - transform.position).sqrMagnitude).FirstOrDefault();
            if (crate)
            {
                var fruit = crate.Current.GetComponent<Fruit>();
                var hand = Body.model.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "RightHand") ?? transform;
                if (fruit.stages != null && fruit.stages.Length > 0)
                {
                    loot = Instantiate(fruit.stages[0], hand, false);
                    loot.name = "Stolen " + fruit.fruitName;
                    loot.SetActive(true);
                    loot.transform.localPosition = Vector3.zero;
                    loot.transform.localScale = Vector3.one / Mathf.Max(1e-4f, hand.lossyScale.x);
                    loot.transform.position = hand.position + transform.forward * 0.03f - Vector3.up * 0.05f;
                }
                Destroy(crate.Current.gameObject);
            }
            RunOff();
        }

        void RunOff()
        {
            State = Phase.Running;
            Body.Travel(RouteOf(Route), "Run", RunSpeed, Escaped);
        }

        void Escaped()
        {
            State = Phase.Gone;
            if (loot && orders && orders.ShiftId == shiftAtStart) orders.ThiefEscaped(transform.position);
            Destroy(gameObject);
        }

        void OnHit(Customer _, Rigidbody thrown, bool head, bool knife)
        {
            if (State == Phase.Caught || State == Phase.Gone) return;
            if (knife) return;                                          // bounces off (Customer)
            slowLeft = SlowSeconds;                                     // food: slowed (Customer also splats the face)
            Body.SetSpeedScale(SlowScale);
        }

        // A pistol round (Bullet). A tough thief's first hit only staggers and slows him (returns false); the last one
        // puts him down as a ragdoll, thrown along the shot, and the stall gets the reward (returns true).
        public bool Shot(Vector3 at, Vector3 direction)
        {
            if (State == Phase.Caught || State == Phase.Gone) return false;
            if (--Health > 0)
            {
                slowLeft = SlowSeconds;
                Body.SetSpeedScale(SlowScale);
                Body.Stagger();
                return false;
            }
            State = Phase.Caught;
            DropLoot();
            Body.Die((direction.normalized + Vector3.up * 0.3f) * KnockBack, at);
            if (orders) orders.ThiefCaught(transform.position + Vector3.up);
            return true;
        }

        // The fruit falls out of his hand when he goes down.
        void DropLoot()
        {
            if (!loot) return;
            loot.transform.SetParent(null, true);
            var rb = loot.AddComponent<Rigidbody>(); rb.mass = 0.3f;
            var col = loot.AddComponent<SphereCollider>(); col.radius = 0.04f / Mathf.Max(1e-4f, loot.transform.lossyScale.x);
            Destroy(loot, 8f);
            loot = null;
        }

        // Dark clothes (every renderer tinted through a property block: the shared customer materials stay as they are)
        // and a red "THIEF!" over the head, so he never reads as a customer.
        void Disguise()
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in Body.model.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (!m) continue;
                r.GetPropertyBlock(block);
                var dark = Tough ? new Color(0.35f, 0.08f, 0.08f) : new Color(0.22f, 0.22f, 0.26f);
                if (m.HasProperty("_BaseColor")) block.SetColor("_BaseColor", dark);
                if (m.HasProperty("baseColorFactor")) block.SetColor("baseColorFactor", dark);
                r.SetPropertyBlock(block);
            }
            var label = new GameObject("Thief label", typeof(RectTransform));
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            var t = label.AddComponent<TextMeshPro>();
            if (orders && orders.orderCard) t.font = orders.orderCard.font;
            t.text = Tough ? "THIEF!!" : "THIEF!"; t.fontSize = 2.2f; t.fontStyle = FontStyles.Bold;
            t.color = new Color(0.95f, 0.15f, 0.1f); t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            ((RectTransform)label.transform).sizeDelta = new Vector2(1f, 0.3f);
            label.AddComponent<FaceCamera>();
        }
    }
}
