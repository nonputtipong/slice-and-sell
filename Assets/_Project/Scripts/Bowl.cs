using System.Linq;
using UnityEngine;

namespace SliceAndSell
{
    // The serving bowl a customer gets: one cut fruit plus optional extras (skewer, chili).
    // Filling it copies the cut fruit's own slices into the bowl, laid flat and fanned on the floor of the bowl,
    // so the bowl shows the fruit that went in (not a generic heap).
    public class Bowl : MonoBehaviour
    {
        public GameObject filledVisual;             // fallback heap, shown only if the fruit has no slices to copy
        public GameObject skewerVisual, chiliVisual;

        // The Blender bowl (Ø 15 cm, 6.5 cm tall); the prefab root is its middle, so the foot is at y -0.0325.
        // Slices keep their real size (playtest: shrinking them to the bowl looked wrong). Each is lifted until its corners
        // clear the inner wall; one wider than the bowl rests on the rim and hangs over it, like a real heap.
        public const float FootY = -0.0325f;
        const float FanOffset = 0.008f, Tilt = 8f, Overlap = 0.55f;

        // Inside radius of the bowl at height h above its foot (m), from the Bowl.glb profile: the inner floor is flat
        // to r 3.6 cm at 9 mm, the wall climbs to r 6.6 cm at 4.5 cm and to r 7.05 cm at the rim (6.5 cm).
        public static float InnerRadius(float h)
        {
            if (h <= 0.009f) return 0.036f;
            if (h <= 0.045f) return 0.036f + (h - 0.009f) * (0.03f / 0.036f);
            return Mathf.Min(0.0705f, 0.066f + (h - 0.045f) * (0.0045f / 0.02f));
        }

        // The lowest height above the foot where the inside is at least `r` wide (above the rim: the rim height).
        static float HeightForRadius(float r)
        {
            if (r <= 0.036f) return 0.009f;
            if (r <= 0.066f) return 0.009f + (r - 0.036f) * (0.036f / 0.03f);
            return Mathf.Min(0.065f, 0.045f + (r - 0.066f) * (0.02f / 0.0045f));
        }

        public string Contents { get; private set; }
        public int Cuts { get; private set; }
        public bool IsFilled => Contents != null;
        public bool HasSkewer { get; private set; }
        public bool HasChili { get; private set; }
        public Transform Slices { get; private set; }   // the copied slices (null until filled, or for a fruit without stages)

        float heapTop = 0.032f;                         // where the chilli sits: on the slices once there are some

        public void Fill(Fruit fruit)
        {
            Contents = fruit.fruitName;
            Cuts = fruit.CutsDone;
            Slices = CopySlices(fruit);
            if (filledVisual) filledVisual.SetActive(!Slices);
            if (chiliVisual) chiliVisual.transform.localPosition = new Vector3(-0.015f, heapTop, 0.01f);
            GameEvents.RaiseAddedToBowl(transform.position, fruit.fruitName);
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

        // Each piece of the fruit's current stage becomes a plain mesh in the bowl (no collider, rigidbody or grab):
        // turned so its thinnest side faces up (a slice lies on its cut face), at its real size, and fanned round the middle, each a little higher than the last, like slices served in a bowl.
        Transform CopySlices(Fruit fruit)
        {
            if (fruit.stages == null || fruit.stages.Length == 0) return null;
            var stage = fruit.stages[Mathf.Clamp(fruit.CutsDone, 0, fruit.stages.Length - 1)];
            if (!stage) return null;
            var pieces = stage.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh && m.GetComponent<MeshRenderer>()).ToArray();
            if (pieces.Length == 0) return null;

            // Every piece's size in fruit space (the mesh as it sat on the board, whichever way the fruit is held now),
            // and the turn that lays it on its thinnest side.
            var turns = new Quaternion[pieces.Length];
            var sizes = new Vector3[pieces.Length];
            float widest = 0f;
            for (int i = 0; i < pieces.Length; i++)
            {
                var b = pieces[i].sharedMesh.bounds;
                var s = Vector3.Scale(b.size, pieces[i].transform.lossyScale);
                var up = s.x <= s.y && s.x <= s.z ? Vector3.right : s.z <= s.y ? Vector3.forward : Vector3.up;
                turns[i] = Quaternion.FromToRotation(up, Vector3.up);
                sizes[i] = Abs(turns[i] * s);
                widest = Mathf.Max(widest, sizes[i].x, sizes[i].z);
            }
            const float scale = 1f;                                         // real size, never shrunk

            var root = new GameObject("Slices in bowl").transform;
            root.SetParent(transform, false);
            float y = FootY + 0.011f;                                       // 2 mm over the inner floor
            for (int i = 0; i < pieces.Length; i++)
            {
                var src = pieces[i];
                var size = sizes[i] * scale;
                float angle = i * 137.5f;                                   // golden angle: an even fan for any count
                var yaw = Quaternion.Euler(0f, angle, 0f);
                var tilt = Quaternion.Euler(i % 2 == 0 ? Tilt : -Tilt, 0f, 0f);   // not perfectly flat: they rest on each other
                float r = pieces.Length > 1 ? FanOffset : 0f;
                // A wide slice rests on the sloping wall, not the floor: its far corner (fan offset + half its diagonal)
                // must be inside the bowl at the height of its lowest (tilted-down) edge.
                float corner = r + 0.5f * new Vector2(size.x, size.z).magnitude;
                float drop = Mathf.Sin(Tilt * Mathf.Deg2Rad) * 0.5f * Mathf.Max(size.x, size.z);
                y = Mathf.Max(y, FootY + HeightForRadius(corner + 0.002f) + drop);
                var centre = yaw * new Vector3(0f, 0f, r) + Vector3.up * (y + size.y * 0.5f);

                var go = new GameObject(src.name);
                var t = go.transform;
                t.SetParent(root, false);
                t.localRotation = yaw * tilt * turns[i];
                t.localScale = src.transform.lossyScale * scale;
                // Centre the mesh's own bounds on `centre` (piece meshes are modelled in place, off their origin).
                t.localPosition = centre - t.localRotation * Vector3.Scale(src.sharedMesh.bounds.center, t.localScale);
                go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // small, inside the bowl's own shadow

                y += size.y * Overlap;
                heapTop = Mathf.Max(heapTop, centre.y + size.y * 0.5f + 0.004f);
            }
            return root;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
