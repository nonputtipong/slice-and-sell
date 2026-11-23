using UnityEngine;

namespace SliceAndSell
{
    // The one slicing rule (GDD > Slicing system): a cut counts when the blade travels at least
    // `fraction` of the fruit's thickness while inside it. Speed never matters, so a slow hand push
    // and a fast controller swing both cut. One cut per pass: the blade must leave the fruit before the next.
    public class CutTracker
    {
        const float ShallowFraction = 0.3f;     // a pass this deep that doesn't cut gets the "thunk"

        float needed, thickness;
        bool inside, cutThisPass;
        Vector3 entry;
        float deepest;

        // True on the step where the blade left the fruit after going in at least 30% without cutting.
        public bool ExitedShallow { get; private set; }

        readonly float fraction;

        public CutTracker(float thickness, float fraction = 0.7f) { this.fraction = fraction; SetThickness(thickness); }

        // Next cut, same pass: keeps "the blade must leave first", so pulling the knife back out never cuts again.
        public void SetThickness(float t) { thickness = t; needed = t * fraction; }

        // Feed one blade sample per (sub)step. Returns true on the step that completes a cut.
        public bool Feed(bool bladeInside, Vector3 bladePoint)
        {
            ExitedShallow = false;
            if (!bladeInside)
            {
                ExitedShallow = inside && !cutThisPass && deepest >= thickness * ShallowFraction;
                inside = false; cutThisPass = false; deepest = 0f;
                return false;
            }
            if (!inside) { inside = true; entry = bladePoint; return false; }
            deepest = Mathf.Max(deepest, (bladePoint - entry).magnitude);
            if (cutThisPass || deepest < needed) return false;
            cutThisPass = true;
            return true;
        }
    }
}
