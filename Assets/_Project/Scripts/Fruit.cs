using System;
using UnityEngine;

namespace SliceAndSell
{
    // One planned cut: a plane in the fruit's local space, and how far the blade must travel
    // through the fruit for it to count (the 70% rule uses this depth).
    [Serializable]
    public struct CutGuide
    {
        public Vector3 normal;  // local
        public Vector3 point;   // local
        public float depth;     // m, the fruit's extent along the blade's travel for this cut
    }

    // A fruit with pre-made cut stages (whole -> 2 pieces -> ...). A valid cut shows the next stage.
    // No runtime mesh slicing (GDD > Slicing system). Cuts follow `guides` in order; the next guide is shown.
    public class Fruit : MonoBehaviour
    {
        public const float CountAngle = 45f;     // blade within this of the guide: the cut counts
        public const float CleanAngle = 20f;     // ... within this and CleanDistance: Clean Cut
        public const float CleanDistance = 0.015f;

        public string fruitName = "Guava";
        public Color juiceColor = new Color(0.98f, 0.6f, 0.55f);
        public GameObject[] stages;         // stage 0 = whole, last = fully cut; stages.Length = guides.Length + 1
        public CutGuide[] guides;
        public Collider cutVolume;          // convex collider the blade must pass through
        public bool needsCleaver;           // too big for the paring knife (watermelon): only the two-handed cleaver cuts it

        public event Action<Fruit, CutInfo> Cut;
        public static event Action<Fruit, CutInfo> AnyCut;     // scoring listens here, then raises GameEvents.Cut with the combo

        public int CutsDone { get; private set; }
        public int CutsRequired => stages.Length - 1;
        public bool IsFullyCut => CutsDone >= CutsRequired;
        public CutGuide NextGuide => guides[Mathf.Min(CutsDone, guides.Length - 1)];
        public float NextDepth => guides != null && guides.Length > 0 ? NextGuide.depth : 0.09f;

        // For a convex collider, the closest point to a point inside is the point itself.
        public bool Contains(Vector3 p) => (cutVolume.ClosestPoint(p) - p).sqrMagnitude < 1e-8f;

        // Angle between the blade's plane and the next guide's plane, 0-90 degrees (blade can face either way).
        public float AngleToGuide(Vector3 bladeNormalWorld)
        {
            float a = Vector3.Angle(transform.TransformDirection(NextGuide.normal), bladeNormalWorld);
            return a > 90f ? 180f - a : a;
        }

        public float DistanceToGuide(Vector3 worldPoint)
        {
            var g = NextGuide;
            var n = transform.TransformDirection(g.normal).normalized;
            return Mathf.Abs(Vector3.Dot(worldPoint - transform.TransformPoint(g.point), n));
        }

        public void ApplyCut(CutInfo info)
        {
            if (IsFullyCut) return;
            stages[CutsDone].SetActive(false);
            CutsDone++;
            stages[CutsDone].SetActive(true);
            Cut?.Invoke(this, info);
            AnyCut?.Invoke(this, info);
        }

        // Kept for M1 callers and tests: a straight cut with no position information.
        public void ApplyCut(bool powerCut) => ApplyCut(new CutInfo { power = powerCut, clean = true, point = transform.position });
    }
}
