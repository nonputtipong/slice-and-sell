using System;
using UnityEngine;

namespace SliceAndSell
{
    // A fruit with pre-made cut stages (whole -> halves -> quarters ...). A valid cut shows the next stage.
    // No runtime mesh slicing (GDD > Slicing system).
    public class Fruit : MonoBehaviour
    {
        public string fruitName = "Guava";
        public float thickness = 0.09f;     // m, used by the cut rule
        public GameObject[] stages;         // stage 0 = whole, last = fully cut
        public Collider cutVolume;          // convex collider the blade must pass through

        public event Action<Fruit, bool> Cut;   // (fruit, powerCut)

        public int CutsDone { get; private set; }
        public int CutsRequired => stages.Length - 1;
        public bool IsFullyCut => CutsDone >= CutsRequired;

        // For a convex collider, the closest point to a point inside is the point itself.
        public bool Contains(Vector3 p) => (cutVolume.ClosestPoint(p) - p).sqrMagnitude < 1e-8f;

        public void ApplyCut(bool powerCut)
        {
            if (IsFullyCut) return;
            stages[CutsDone].SetActive(false);
            CutsDone++;
            stages[CutsDone].SetActive(true);
            Cut?.Invoke(this, powerCut);
        }
    }
}
