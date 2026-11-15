using UnityEngine;

namespace SliceAndSell
{
    // The one slicing rule (GDD > Slicing system): a cut counts when the blade travels at least
    // `fraction` of the fruit's thickness while inside it. Speed never matters, so a slow hand push
    // and a fast controller swing both cut. One cut per pass: the blade must leave the fruit before the next.
    public class CutTracker
    {
        readonly float needed;
        bool inside, cutThisPass;
        Vector3 entry;

        public CutTracker(float thickness, float fraction = 0.7f) => needed = thickness * fraction;

        // Feed one blade sample per (sub)step. Returns true on the step that completes a cut.
        public bool Feed(bool bladeInside, Vector3 bladePoint)
        {
            if (!bladeInside) { inside = false; cutThisPass = false; return false; }
            if (!inside) { inside = true; entry = bladePoint; return false; }
            if (cutThisPass || (bladePoint - entry).magnitude < needed) return false;
            cutThisPass = true;
            return true;
        }
    }
}
