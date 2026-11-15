using UnityEngine;

namespace SliceAndSell
{
    // Patience ring above the customer (GDD: patience ring). Faces the player (yaw only), drains clockwise
    // from 12 o'clock, green → yellow → red by thirds.
    // One procedural ring mesh built once; filling just shows fewer of its triangles (no allocation),
    // and only when the visible segment count changes. Colour goes through a property block, so the
    // shared material asset (URP Unlit, assigned by CustomerPrefabBuilder) is never copied.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PatienceRing : MonoBehaviour
    {
        public float outerRadius = 0.08f, innerRadius = 0.057f;  // 16 cm across: legible at ~1.4 m
        const float TrackMargin = 0.006f;                         // the dark track shows 6 mm round the coloured arc
        const int Segments = 48;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color Green = new Color(0.3f, 0.85f, 0.3f), Yellow = new Color(1f, 0.8f, 0.15f), Red = new Color(0.9f, 0.2f, 0.15f),
            TrackColour = new Color(0.13f, 0.09f, 0.07f);

        Mesh mesh, trackMesh;
        MeshRenderer track;
        int[] tris;
        MeshRenderer rend;
        MaterialPropertyBlock block;
        int shownSegments = -1, band = -1;
        Transform cam;

        void Awake()
        {
            rend = GetComponent<MeshRenderer>();
            rend.enabled = false;   // hidden until the customer is at the counter
            block = new MaterialPropertyBlock();
            mesh = new Mesh { name = "Patience ring" };
            mesh.MarkDynamic();

            // Segment k runs counter-clockwise (as the player sees it) from 12 o'clock, so showing the
            // first n segments leaves the gap growing clockwise from 12. Front faces look down local -Z.
            var verts = new Vector3[(Segments + 1) * 2];
            var trackVerts = new Vector3[verts.Length];
            for (int k = 0; k <= Segments; k++)
            {
                float a = 2 * Mathf.PI * k / Segments;
                var dir = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0);
                verts[2 * k] = dir * outerRadius;
                verts[2 * k + 1] = dir * innerRadius;
                trackVerts[2 * k] = dir * (outerRadius + TrackMargin) + Vector3.forward * 0.002f;   // just behind the arc
                trackVerts[2 * k + 1] = dir * (innerRadius - TrackMargin) + Vector3.forward * 0.002f;
            }
            tris = new int[Segments * 6];
            for (int k = 0; k < Segments; k++)
            {
                int o0 = 2 * k, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
                tris[6 * k] = o0; tris[6 * k + 1] = i1; tris[6 * k + 2] = o1;
                tris[6 * k + 3] = o0; tris[6 * k + 4] = i0; tris[6 * k + 5] = i1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();   // full ring; kept as the bounds when fewer segments show
            GetComponent<MeshFilter>().sharedMesh = mesh;

            // The full dark track the arc drains over, so how much is left reads at a glance against any background.
            trackMesh = new Mesh { name = "Patience track", vertices = trackVerts, triangles = tris };
            trackMesh.RecalculateBounds();
            var t = new GameObject("Track", typeof(MeshFilter), typeof(MeshRenderer));
            t.transform.SetParent(transform, false);
            t.GetComponent<MeshFilter>().sharedMesh = trackMesh;
            track = t.GetComponent<MeshRenderer>();
            track.sharedMaterial = rend.sharedMaterial;
            track.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tb = new MaterialPropertyBlock(); tb.SetColor(BaseColor, TrackColour); track.SetPropertyBlock(tb);
            track.enabled = false;
        }

        void OnDestroy() { if (mesh) Destroy(mesh); if (trackMesh) Destroy(trackMesh); }

        public void Set(float t01, bool visible)
        {
            t01 = Mathf.Clamp01(t01);
            int n = Mathf.CeilToInt(t01 * Segments);
            rend.enabled = visible && n > 0;
            track.enabled = visible;
            if (n > 0 && n != shownSegments) mesh.SetTriangles(tris, 0, n * 6, 0, false);
            shownSegments = n;

            int b = t01 > 2f / 3f ? 0 : t01 > 1f / 3f ? 1 : 2;
            if (b == band) return;
            band = b;
            block.SetColor(BaseColor, b == 0 ? Green : b == 1 ? Yellow : Red);
            rend.SetPropertyBlock(block);
        }

        void LateUpdate()
        {
            if (!rend.enabled) return;
            if (!cam) { var c = Camera.main; if (!c) return; cam = c.transform; }
            var away = transform.position - cam.position;
            away.y = 0;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away);   // local -Z faces the player
        }
    }
}
