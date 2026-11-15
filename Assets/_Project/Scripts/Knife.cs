using UnityEngine;

namespace SliceAndSell
{
    // Samples the cutting edge every frame against the fruit on the board. A fast swing is split into
    // 1 cm sub-steps, so it cannot jump over a fruit between two frames.
    public class Knife : MonoBehaviour
    {
        public Transform edgeStart, edgeEnd;    // the two ends of the cutting edge
        public CuttingBoard board;
        public float powerCutSpeed = 1.5f;      // m/s, bonus only (GDD: Power Cut)

        const int EdgeSamples = 5;
        const float SubStep = 0.01f;

        Vector3 lastStart, lastEnd;
        Fruit tracked;
        CutTracker tracker;

        void OnEnable() { lastStart = edgeStart.position; lastEnd = edgeEnd.position; }

        // LateUpdate: after XRI has moved the held knife this frame.
        void LateUpdate()
        {
            Vector3 s = edgeStart.position, e = edgeEnd.position;
            var fruit = board ? board.Fruit : null;
            // Check !fruit first: a destroyed fruit (bagged) and null compare equal, so fruit != tracked misses it.
            if (!fruit) { tracked = null; tracker = null; }
            else if (fruit != tracked) { tracked = fruit; tracker = new CutTracker(fruit.thickness); }

            if (tracker != null)
            {
                float move = Mathf.Max((s - lastStart).magnitude, (e - lastEnd).magnitude);
                int steps = Mathf.Clamp(Mathf.CeilToInt(move / SubStep), 1, 100);
                float speed = Time.deltaTime > 0f ? move / Time.deltaTime : 0f;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector3 a = Vector3.Lerp(lastStart, s, t), b = Vector3.Lerp(lastEnd, e, t);
                    bool inside = false;
                    for (int k = 0; k < EdgeSamples && !inside; k++)
                        inside = fruit.Contains(Vector3.Lerp(a, b, k / (EdgeSamples - 1f)));
                    if (tracker.Feed(inside, (a + b) * 0.5f)) fruit.ApplyCut(speed >= powerCutSpeed);
                }
            }
            lastStart = s; lastEnd = e;
        }
    }
}
