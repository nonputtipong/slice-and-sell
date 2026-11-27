using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SliceAndSell
{
    // The serving bowl a customer gets: one cut fruit plus optional extras (skewer, chili).
    // Filling it copies the cut fruit's own slices into the bowl at their real size, and lets them fall in: a hidden
    // physics copy of the bowl's inside (floor + sloped wall) settles them for 1.6 s in one go, off screen, and the
    // visible slices take the poses they came to rest in, so they lie in the bowl, lean on the wall and on each other,
    // and a slice wider than the bowl rests across the rim (playtest 4: they used to float at rim height).
    public class Bowl : MonoBehaviour
    {
        public GameObject filledVisual;             // fallback heap, shown only if the fruit has no slices to copy
        public GameObject skewerVisual, chiliVisual;

        // The Blender bowl (Ø 15 cm, 6.5 cm tall); the prefab root is its middle, so the foot is at y -0.0325.
        public const float FootY = -0.0325f;

        // Inside radius of the bowl at height h above its foot (m), from the Bowl.glb profile: the inner floor is flat
        // to r 3.6 cm at 9 mm, the wall climbs to r 6.6 cm at 4.5 cm and to r 7.05 cm at the rim (6.5 cm).
        public static float InnerRadius(float h)
        {
            if (h <= 0.009f) return 0.036f;
            if (h <= 0.045f) return 0.036f + (h - 0.009f) * (0.03f / 0.036f);
            return Mathf.Min(0.0705f, 0.066f + (h - 0.045f) * (0.0045f / 0.02f));
        }

        public string Contents { get; private set; }
        public int Cuts { get; private set; }
        public bool IsFilled => Contents != null;
        public bool HasSkewer { get; private set; }
        public bool HasChili { get; private set; }
        public Transform Slices { get; private set; }   // the copied slices (null until filled, or for a fruit without stages)
        public bool Settled { get; private set; }       // the slices have fallen into place (the frame after Fill)

        float heapTop = 0.032f;                         // where the chilli sits: on the slices once there are some
        Vector3[] boxCentres, boxSizes;                 // each slice's mesh bounds (its physics box while settling)

        public void Fill(Fruit fruit)
        {
            Contents = fruit.fruitName;
            Cuts = fruit.CutsDone;
            Slices = CopySlices(fruit);
            Settled = !Slices;
            if (filledVisual) filledVisual.SetActive(!Slices);
            PlaceChili();
            GameEvents.RaiseAddedToBowl(transform.position, fruit.fruitName);
        }

        // Fill often runs inside a physics callback (BowlMouth.OnTriggerStay), where another physics scene can't be
        // stepped; the slices settle on the next frame instead.
        void Update()
        {
            if (Settled || !Slices) return;
            Settle();
            Settled = true;
            PlaceChili();
        }

        void PlaceChili()
        {
            if (chiliVisual) chiliVisual.transform.localPosition = new Vector3(-0.015f, heapTop, 0.01f);
        }

        // Returns false if the bowl already has one (the extra is then left alone).
        public bool AddExtra(ExtraKind kind)
        {
            if (kind == ExtraKind.Skewer ? HasSkewer : HasChili) return false;
            if (kind == ExtraKind.Skewer) { HasSkewer = true; if (skewerVisual) skewerVisual.SetActive(true); }
            else { HasChili = true; if (chiliVisual) chiliVisual.SetActive(true); }
            GameEvents.RaiseAddedToBowl(transform.position, kind.ToString());
            return true;
        }

        // Each piece of the fruit's current stage becomes a plain mesh (no collider, rigidbody or grab) at its real size,
        // first held just over the bowl, lying on its thinnest side (a slice on its cut face), fanned round the middle.
        Transform CopySlices(Fruit fruit)
        {
            if (fruit.stages == null || fruit.stages.Length == 0) return null;
            var stage = fruit.stages[Mathf.Clamp(fruit.CutsDone, 0, fruit.stages.Length - 1)];
            if (!stage) return null;
            var pieces = stage.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh && m.GetComponent<MeshRenderer>()).ToArray();
            if (pieces.Length == 0) return null;

            var root = new GameObject("Slices in bowl").transform;
            root.SetParent(transform, false);
            boxCentres = new Vector3[pieces.Length];
            boxSizes = new Vector3[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                var src = pieces[i];
                var b = src.sharedMesh.bounds;
                var scale = src.transform.lossyScale;                       // real size, never shrunk
                var s = Vector3.Scale(b.size, scale);
                var up = s.x <= s.y && s.x <= s.z ? Vector3.right : s.z <= s.y ? Vector3.forward : Vector3.up;
                var rot = Quaternion.Euler(0f, i * 137.5f, 0f) * Quaternion.Euler(i % 2 == 0 ? 10f : -10f, 0f, 0f) * Quaternion.FromToRotation(up, Vector3.up);
                float lift = Mathf.Abs((rot * s).y);
                var centre = Quaternion.Euler(0f, i * 137.5f, 0f) * new Vector3(0f, 0f, pieces.Length > 1 ? 0.012f : 0f)
                             + Vector3.up * (FootY + 0.075f + i * 0.05f + lift * 0.5f);     // dropped one after another, just over the rim

                var go = new GameObject(src.name);
                var t = go.transform;
                t.SetParent(root, false);
                t.localScale = scale;
                t.localRotation = rot;
                t.localPosition = centre - rot * Vector3.Scale(b.center, scale);
                go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // small, inside the bowl's own shadow
                boxCentres[i] = b.center; boxSizes[i] = b.size;
            }
            return root;
        }

        // ---------- settling ----------

        static Scene settleScene;
        static PhysicsScene settlePhysics;
        const int WallSegments = 20;
        const float WallThickness = 0.008f, Step = 0.02f;
        const int Steps = 80;                           // 1.6 s of falling and settling, simulated at once

        // A physics scene of its own (never stepped by Unity, nothing rendered): the bowl's inside and the table round it.
        static void EnsureSettleScene()
        {
            if (settleScene.IsValid() && settleScene.isLoaded) return;
            settleScene = SceneManager.CreateScene("Bowl settle " + System.Guid.NewGuid().ToString("N").Substring(0, 6),
                                                   new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            settlePhysics = settleScene.GetPhysicsScene();
            var shape = new GameObject("Bowl inside");
            SceneManager.MoveGameObjectToScene(shape, settleScene);
            AddBox(shape, new Vector3(0f, FootY + 0.009f - 0.005f, 0f), Quaternion.identity, new Vector3(0.1f, 0.01f, 0.1f));   // inner floor
            AddBox(shape, new Vector3(0f, FootY - 0.005f, 0f), Quaternion.identity, new Vector3(1f, 0.01f, 1f));              // the table
            Band(shape, 0.036f, 0.009f, 0.066f, 0.045f);                                                                      // lower wall
            Band(shape, 0.066f, 0.045f, 0.0705f, 0.066f);                                                                     // up to the rim
        }

        // A ring of boxes whose inner faces follow the bowl's wall from (r0, h0) to (r1, h1), heights above the foot.
        static void Band(GameObject parent, float r0, float h0, float r1, float h1)
        {
            float dr = r1 - r0, dh = h1 - h0, len = Mathf.Sqrt(dr * dr + dh * dh) + 0.004f;
            float lean = Mathf.Atan2(dr, dh) * Mathf.Rad2Deg;              // outward from vertical
            float rm = (r0 + r1) * 0.5f, hm = (h0 + h1) * 0.5f;
            float width = 2f * Mathf.PI * Mathf.Max(r0, r1) / WallSegments * 1.2f;
            for (int i = 0; i < WallSegments; i++)
            {
                var yaw = Quaternion.Euler(0f, i * 360f / WallSegments, 0f);
                var rot = yaw * Quaternion.Euler(lean, 0f, 0f);             // local y along the wall, local z out of it
                var onWall = yaw * new Vector3(0f, FootY + hm, rm);
                AddBox(parent, onWall + rot * Vector3.forward * (WallThickness * 0.5f), rot, new Vector3(width, len, WallThickness));
            }
        }

        static void AddBox(GameObject parent, Vector3 pos, Quaternion rot, Vector3 size)
        {
            var go = new GameObject("wall");
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var c = go.AddComponent<BoxCollider>();
            c.size = size; c.contactOffset = 0.001f;
        }

        // Drops a box per slice (the slice's mesh bounds) into the hidden bowl, steps physics, copies the resting poses.
        void Settle()
        {
            EnsureSettleScene();
            var slices = Slices.Cast<Transform>().ToArray();
            var bodies = new Transform[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                var go = new GameObject("slice");
                SceneManager.MoveGameObjectToScene(go, settleScene);
                go.transform.localScale = slices[i].localScale;
                go.transform.SetPositionAndRotation(slices[i].localPosition, slices[i].localRotation);
                var c = go.AddComponent<BoxCollider>();
                c.center = boxCentres[i]; c.size = Vector3.Max(boxSizes[i], Vector3.one * 0.004f); c.contactOffset = 0.001f;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Clamp(boxSizes[i].x * boxSizes[i].y * boxSizes[i].z * 900f, 0.02f, 1f);
                rb.linearDamping = 0.4f; rb.angularDamping = 0.8f; rb.solverIterations = 16;
                bodies[i] = go.transform;
            }
            for (int s = 0; s < Steps; s++) settlePhysics.Simulate(Step);

            heapTop = 0.032f;
            for (int i = 0; i < slices.Length; i++)
            {
                var p = bodies[i].position; var r = bodies[i].rotation;
                // A slice that slid off onto the table goes back on top, lying flat over the middle.
                if (new Vector2(p.x, p.z).magnitude > 0.09f || p.y < FootY + 0.004f)
                {
                    r = Quaternion.FromToRotation(r * Vector3.up, Vector3.up) * r;
                    p = new Vector3(0f, heapTop + 0.015f, 0f) - r * Vector3.Scale(boxCentres[i], slices[i].localScale);
                }
                slices[i].localPosition = p; slices[i].localRotation = r;
                var top = slices[i].GetComponent<MeshRenderer>().bounds.max;
                heapTop = Mathf.Max(heapTop, transform.InverseTransformPoint(top).y + 0.004f);
                bodies[i].gameObject.SetActive(false);
                Destroy(bodies[i].gameObject);
            }
        }
    }
}
