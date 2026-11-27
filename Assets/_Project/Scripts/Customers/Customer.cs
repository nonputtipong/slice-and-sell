using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    public enum CustomerMood { Happy, Neutral, Angry }

    // A walk-on customer: walks to its counter spot, faces the player and waits, reacts, walks off.
    // Kinematic (transform only, no physics/navmesh): it stops exactly at the spot it is given, so the
    // caller keeps that spot clear of the counter. Clips are legacy Animation (glTFast import), played by name.
    // Thrown things: two trigger hitboxes (head, body) made at start. Food in the face splats there and the customer
    // shakes their head; a thrown knife just bounces off anyone. A Thief (same model, extra component) reacts instead:
    // food slows him, and the pistol (Bullet) knocks him down (Die: ragdoll).
    public class Customer : MonoBehaviour
    {
        public Animation anim;
        public PatienceRing ring;
        public Transform model;   // the imported model: hop / wiggle / stomp move it, the clips only drive its bones
        public Transform head;    // nod / head-shake, layered on the clip in LateUpdate (every clip keys Head rotation)
        public float walkSpeed = 1.1f, turnSpeed = 360f;   // m/s, deg/s

        public const float MinThrowSpeed = 1.5f;           // m/s: slower than this is a drop or a toss, not a throw
        public const float MinTossSpeed = 0.5f;            // m/s: a bowl tossed to the customer is enough to serve it
        static OrderManager orders;
        const float Fade = 0.2f, ReactTime = 0.6f, ShakeTime = 0.6f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool AtCounter { get; private set; }
        public bool Dead { get; private set; }
        public float SpeedScale { get; set; } = 1f;        // the thief slows when hit; walking and the clips scale with it
        public int Splats { get; private set; }

        // Every hit a thrown thing makes on a living customer or thief: (who, the thrown rigidbody, head?, is a knife).
        public event Action<Customer, Rigidbody, bool, bool> Hit;

        float headYaw, headPitch, shake;
        Collider headBox, bodyBox;
        readonly Dictionary<Rigidbody, float> recentHits = new Dictionary<Rigidbody, float>();

        // Paused (GamePause): walking, turning and reacting stop where they are, and the clips freeze.
        static float Dt => GamePause.Paused ? 0f : Time.deltaTime;

        void Awake()
        {
            // The importer already loops them; this keeps it true if a clip is swapped by hand.
            foreach (AnimationState s in anim) s.wrapMode = WrapMode.Loop;
            MakeHitboxes();
            if (!sphereMesh)
            {
                // The built-in sphere for splat blobs, taken once here: hits arrive in physics callbacks, where a
                // primitive's collider can't be removed at once.
                var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                p.GetComponent<Collider>().enabled = false;
                sphereMesh = p.GetComponent<MeshFilter>().sharedMesh;
                Destroy(p);
            }
        }

        static Mesh sphereMesh;

        void OnEnable() { GamePause.Changed += OnPauseChanged; OnPauseChanged(GamePause.Paused); }
        void OnDisable() => GamePause.Changed -= OnPauseChanged;

        void OnPauseChanged(bool _) => ApplyClipSpeed();

        void ApplyClipSpeed()
        {
            if (!anim || Dead) return;
            foreach (AnimationState s in anim) s.speed = GamePause.Paused ? 0f : SpeedScale;
        }

        public void SetSpeedScale(float scale) { SpeedScale = scale; ApplyClipSpeed(); }

        public void Arrive(Vector3 from, Vector3 counterSpot, Action onArrived)
        {
            if (Dead) return;
            StopAllCoroutines();
            AtCounter = false;
            transform.position = from;
            var dir = counterSpot - from; dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir);
            StartCoroutine(ArriveRoutine(counterSpot, onArrived));
        }

        public void SetPatience(float t01, bool visible) { if (ring) ring.Set(t01, visible); }

        public void Leave(CustomerMood mood, Vector3 exit)
        {
            if (Dead) return;
            StopAllCoroutines();
            AtCounter = false;
            SetPatience(0, false);
            StartCoroutine(LeaveRoutine(mood, exit));
        }

        // Walks (or, with the Run clip, runs) through the points, then calls `done`. Used by the thief.
        public void Travel(Vector3[] path, string clip, float speed, Action done)
        {
            if (Dead) return;
            StopAllCoroutines();
            AtCounter = false;
            StartCoroutine(TravelRoutine(path, clip, speed, done));
        }

        // Turns to face a direction (yaw only), then calls `done`.
        public void Face(Vector3 direction, Action done)
        {
            if (Dead) return;
            StopAllCoroutines();
            StartCoroutine(FaceRoutine(Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z)), done));
        }

        IEnumerator ArriveRoutine(Vector3 spot, Action onArrived)
        {
            yield return WalkTo(spot, "Walk", walkSpeed);
            yield return FaceRoutine(Quaternion.LookRotation(Vector3.back), null);
            AtCounter = true;
            onArrived?.Invoke();
        }

        IEnumerator FaceRoutine(Quaternion target, Action done)
        {
            while (Quaternion.Angle(transform.rotation, target) > 0.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Dt);
                yield return null;
            }
            transform.rotation = target;
            done?.Invoke();
        }

        IEnumerator LeaveRoutine(CustomerMood mood, Vector3 exit)
        {
            anim.CrossFade("Idle", Fade);
            for (float t = 0; t < 1; t += Dt / ReactTime) { React(mood, t); yield return null; }
            model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
            headYaw = headPitch = 0;
            yield return WalkTo(exit, "Walk", walkSpeed);
            Destroy(gameObject);
        }

        IEnumerator TravelRoutine(Vector3[] path, string clip, float speed, Action done)
        {
            foreach (var p in path) yield return WalkTo(p, clip, speed);
            done?.Invoke();
        }

        IEnumerator WalkTo(Vector3 target, string clip, float speed)
        {
            anim.CrossFade(anim[clip] ? clip : "Walk", Fade);
            while ((transform.position - target).sqrMagnitude > 1e-6f)
            {
                var dir = target - transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Dt);
                transform.position = Vector3.MoveTowards(transform.position, target, speed * SpeedScale * Dt);
                yield return null;
            }
            anim.CrossFade("Idle", Fade);
        }

        // t runs 0 → 1 over ReactTime.
        void React(CustomerMood mood, float t)
        {
            float bump = Mathf.Sin(Mathf.PI * t);          // 0 → 1 → 0
            float shake = Mathf.Sin(6 * Mathf.PI * t);     // three shakes
            switch (mood)
            {
                case CustomerMood.Happy:
                    model.localPosition = new Vector3(0, 0.12f * bump, 0);   // little hop
                    break;
                case CustomerMood.Neutral:
                    headPitch = 20f * bump;   // one nod
                    break;
                case CustomerMood.Angry:
                    headYaw = 30f * shake;
                    model.localRotation = Quaternion.Euler(0, 8f * shake, 0);
                    // Stomp in the last 30%: lift 4 cm, then slam 1.5 cm down.
                    float s = t > 0.7f ? Mathf.Sin(2 * Mathf.PI * (t - 0.7f) / 0.3f) : 0;
                    model.localPosition = new Vector3(0, s > 0 ? 0.04f * s : 0.015f * s, 0);
                    break;
            }
        }

        void LateUpdate()
        {
            if (Dead) return;                               // physics owns the bones now
            if (shake > 0f)
            {
                shake = Mathf.Max(0f, shake - Dt);
                float t = 1f - shake / ShakeTime;
                headYaw = shake > 0f ? 25f * Mathf.Sin(6f * Mathf.PI * t) : 0f;
            }
            // Runs after the Animation has posed the head this frame, so the offset never accumulates.
            if (head && (headYaw != 0 || headPitch != 0))
                head.rotation = Quaternion.AngleAxis(headYaw, transform.up) * Quaternion.AngleAxis(headPitch, transform.right) * head.rotation;
        }

        // ---------- thrown things ----------

        // Head: a sphere round the face, generous (a throw moves ~12 cm per physics step and triggers get no CCD).
        // Body: a capsule from the knees to the shoulders. Both on Ignore Raycast, so rays to menus never stop on them.
        void MakeHitboxes()
        {
            if (head)
            {
                var go = new GameObject("Head hitbox") { layer = 2 };
                go.transform.SetParent(head, false);
                go.transform.position = head.position + Vector3.up * 0.09f;
                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 0.15f / Mathf.Max(1e-4f, go.transform.lossyScale.x);
                go.AddComponent<Hitbox>().Init(this, true);
                headBox = sphere;
            }
            var b = new GameObject("Body hitbox") { layer = 2 };
            b.transform.SetParent(transform, false);
            var cap = b.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.center = new Vector3(0f, 1.0f, 0f); cap.height = 1.1f; cap.radius = 0.25f;
            b.AddComponent<Hitbox>().Init(this, false);
            bodyBox = cap;
        }

        // From a Hitbox. One thrown thing counts once (a knife has two colliders, a bowl its box and its mouth).
        internal void OnHitbox(Collider other, bool isHead)
        {
            if (Dead || GamePause.Paused) return;
            var rb = other.attachedRigidbody;
            if (!rb || rb.isKinematic) return;                                  // held items are kinematic
            var grab = rb.GetComponent<XRGrabInteractable>();
            if (grab && grab.isSelected) return;
            var bowl = rb.GetComponent<Bowl>();
            // A bowl only needs a toss to be served; anything else must be thrown.
            if (rb.linearVelocity.magnitude < (bowl ? MinTossSpeed : MinThrowSpeed)) return;
            if (recentHits.TryGetValue(rb, out float at) && Time.time - at < 0.5f) return;
            recentHits[rb] = Time.time;

            // Serving by throwing (playtest: placing the bowl on the tray was fiddly): a bowl that reaches the customer
            // waiting at the counter is handed over, paid and used up exactly as on the HAND OVER tray.
            if (bowl && !GetComponent<Thief>())
            {
                if (!orders) orders = FindAnyObjectByType<OrderManager>();
                if (orders && orders.DeliverTo(this, bowl) != DeliveryResult.NoCustomer) { Destroy(bowl.gameObject); return; }
            }

            bool knife = rb.GetComponent<Knife>();
            Hit?.Invoke(this, rb, isHead, knife);
            if (knife)
            {
                rb.linearVelocity *= -0.2f;                                     // nobody is a knife target: it bounces off
                return;
            }
            if (isHead && SplatColour(rb.gameObject, out var colour)) Splat(rb, colour);
            if (!GetComponent<Thief>())
            {
                if (isHead) shake = ShakeTime;                                  // "hey!"
                else return;                                                    // a hit on the body: the food just drops
            }
            Destroy(rb.gameObject);                                             // it burst on them
        }

        // Fruit: its juice. A bowl: its slices' colour (an empty bowl doesn't splat). Chili: red. Anything else: no.
        static bool SplatColour(GameObject item, out Color colour)
        {
            colour = default;
            var fruit = item.GetComponent<Fruit>();
            if (fruit) { colour = fruit.juiceColor; return true; }
            var bowl = item.GetComponent<Bowl>();
            if (bowl && bowl.IsFilled)
            {
                var r = bowl.Slices ? bowl.Slices.GetComponentInChildren<MeshRenderer>() : null;
                colour = r && r.sharedMaterial && r.sharedMaterial.HasProperty(BaseColorId) ? r.sharedMaterial.GetColor(BaseColorId) : new Color(0.98f, 0.8f, 0.55f);
                return true;
            }
            var extra = item.GetComponent<Extra>();
            if (extra && extra.kind == ExtraKind.Chili) { colour = new Color(0.85f, 0.12f, 0.08f); return true; }
            return false;
        }

        // A few flattened blobs stuck to the face where it hit, in the thrown item's own material tinted to the juice
        // (no Shader.Find: the item's URP Lit material is in the build), plus a burst of the stall's juice particles.
        void Splat(Rigidbody item, Color colour)
        {
            var centre = headBox.bounds.center;
            var point = headBox.ClosestPoint(item.position - item.linearVelocity.normalized * 0.3f);
            var normal = (point - centre).sqrMagnitude > 1e-6f ? (point - centre).normalized : -item.linearVelocity.normalized;
            point = centre + normal * 0.1f;                                     // on the face, not at the hitbox's edge
            var src = item.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, colour);
            for (int i = 0; i < 5; i++)
            {
                var blob = new GameObject("Splat", typeof(MeshFilter), typeof(MeshRenderer));
                blob.GetComponent<MeshFilter>().sharedMesh = sphereMesh;
                var offset = Vector3.ProjectOnPlane(UnityEngine.Random.insideUnitSphere, normal) * (i == 0 ? 0f : 0.045f);
                blob.transform.SetPositionAndRotation(point + offset, Quaternion.LookRotation(normal) * Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 180f)));
                float size = i == 0 ? 0.07f : UnityEngine.Random.Range(0.02f, 0.04f);
                blob.transform.localScale = new Vector3(size, size * UnityEngine.Random.Range(0.6f, 1f), size * 0.25f);
                blob.transform.SetParent(head, true);                           // keeps its world size on any bone scale
                var r = blob.GetComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (src && src.sharedMaterial) r.sharedMaterial = src.sharedMaterial;
                r.SetPropertyBlock(block);
            }
            Splats++;
            GameEvents.RaiseSplat(point, colour);
        }

        // A hit that doesn't put him down: the head jerks (same shake as a splat).
        public void Stagger() { if (!Dead) shake = ShakeTime; }

        // ---------- death ----------

        // Ragdoll: the clip stops, the bones get bodies, and the hit throws him over. The body goes after a while.
        public void Die(Vector3 push, Vector3 at)
        {
            if (Dead) return;
            Dead = true;
            StopAllCoroutines();
            if (anim) { anim.Stop(); anim.enabled = false; }
            headYaw = headPitch = shake = 0f;
            SetPatience(0, false);
            if (headBox) headBox.enabled = false;
            if (bodyBox) bodyBox.enabled = false;
            var bodies = Ragdoll.Build(model);
            Rigidbody nearest = null; float best = float.MaxValue;
            foreach (var rb in bodies)
            {
                float d = (rb.worldCenterOfMass - at).sqrMagnitude;
                if (d < best) { best = d; nearest = rb; }
            }
            // Far more than a real round would do: it is a cartoon, he should go over.
            if (nearest) nearest.AddForceAtPosition(push, at, ForceMode.Impulse);
            Destroy(gameObject, 8f);
        }
    }
}
