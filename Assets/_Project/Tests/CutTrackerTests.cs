using NUnit.Framework;
using UnityEngine;

namespace SliceAndSell.Tests
{
    public class CutTrackerTests
    {
        const float Thickness = 0.1f;

        // Moves a blade point straight down from y=0 to -depth in steps, everything inside the fruit.
        static int Push(CutTracker t, float depth, float step)
        {
            int cuts = 0;
            for (float y = 0; y <= depth + 1e-6f; y += step) if (t.Feed(true, new Vector3(0, -y, 0))) cuts++;
            return cuts;
        }

        [Test] public void SlowPushThroughCutsOnce() => Assert.AreEqual(1, Push(new CutTracker(Thickness), 0.1f, 0.001f));
        [Test] public void FastPassCutsOnce() => Assert.AreEqual(1, Push(new CutTracker(Thickness), 0.1f, 0.04f));
        [Test] public void ShallowPushDoesNotCut() => Assert.AreEqual(0, Push(new CutTracker(Thickness), 0.06f, 0.001f));

        [Test]
        public void NextCutsDepthKeepsThePassSoPullingOutDoesNotCutAgain()
        {
            var t = new CutTracker(Thickness);
            Assert.AreEqual(1, Push(t, 0.1f, 0.01f));
            t.SetThickness(0.05f);       // next guide is shallower
            for (float y = 0.1f; y >= 0f; y -= 0.01f) Assert.IsFalse(t.Feed(true, new Vector3(0, -y, 0)), "pulling back up");
        }

        [Test]
        public void ShallowPassThatLeavesReportsAThunk()
        {
            var t = new CutTracker(Thickness);
            Push(t, 0.05f, 0.01f);       // half way: no cut
            t.Feed(false, Vector3.up);
            Assert.IsTrue(t.ExitedShallow);
        }

        [Test]
        public void LeavingAndReenteringAllowsSecondCut()
        {
            var t = new CutTracker(Thickness);
            Assert.AreEqual(1, Push(t, 0.1f, 0.01f));
            Assert.AreEqual(0, Push(t, 0.1f, 0.01f), "same pass must not cut twice");
            t.Feed(false, Vector3.up);
            Assert.AreEqual(1, Push(t, 0.1f, 0.01f));
        }
    }
}
