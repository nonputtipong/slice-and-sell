using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // Samples the cutting edge every frame against the fruit on the board. A fast swing is split into
    // 1 cm sub-steps, so it cannot jump over a fruit between two frames.
    // A pass deep enough to cut must also follow the next guide (Fruit.CountAngle), else it's a miss.
    public class Knife : MonoBehaviour
    {
        public Transform edgeStart, edgeEnd;    // the two ends of the cutting edge
        public CuttingBoard board;
        public float powerCutSpeed = 1.5f;      // m/s, bonus only (GDD: Power Cut)

        const int EdgeSamples = 5;
        const float SubStep = 0.01f;

        Vector3 lastStart, lastEnd;
        Fruit tracked;
        int trackedCuts;
        CutTracker tracker;

        // The blade is a thin box along local Z; its flat faces point along local X.
        Vector3 BladeNormal => transform.right;

        XRGrabInteractable grab;

        void Awake() => grab = GetComponent<XRGrabInteractable>();
        void OnEnable() { lastStart = edgeStart.position; lastEnd = edgeEnd.position; }

        // LateUpdate: after XRI has moved the held knife this frame.
        void LateUpdate()
        {
            Vector3 s = edgeStart.position, e = edgeEnd.position;
            var fruit = board ? board.Fruit : null;
            // Check !fruit first: a destroyed fruit (bagged) and null compare equal, so fruit != tracked misses it.
            if (!fruit || fruit.IsFullyCut) { tracked = null; tracker = null; }
            else if (fruit != tracked || tracker == null)
            {
                tracked = fruit; trackedCuts = fruit.CutsDone;
                tracker = new CutTracker(fruit.NextDepth);
            }
            else if (fruit.CutsDone != trackedCuts)
            {
                // Same pass continues: a new tracker here would let the knife cut again on the way out.
                trackedCuts = fruit.CutsDone;
                tracker.SetThickness(fruit.NextDepth);
            }

            if (tracker != null)
            {
                float move = Mathf.Max((s - lastStart).magnitude, (e - lastEnd).magnitude);
                int steps = Mathf.Clamp(Mathf.CeilToInt(move / SubStep), 1, 100);
                float speed = Time.deltaTime > 0f ? move / Time.deltaTime : 0f;
                for (int i = 1; i <= steps && tracker != null; i++)
                {
                    float t = (float)i / steps;
                    Vector3 a = Vector3.Lerp(lastStart, s, t), b = Vector3.Lerp(lastEnd, e, t);
                    bool inside = false;
                    for (int k = 0; k < EdgeSamples && !inside; k++)
                        inside = fruit.Contains(Vector3.Lerp(a, b, k / (EdgeSamples - 1f)));
                    Vector3 mid = (a + b) * 0.5f;
                    if (tracker.Feed(inside, mid)) Complete(fruit, mid, speed);
                    else if (tracker.ExitedShallow) GameEvents.RaiseCutMissed(mid);
                }
            }
            lastStart = s; lastEnd = e;
        }

        void Complete(Fruit fruit, Vector3 point, float speed)
        {
            float angle = fruit.AngleToGuide(BladeNormal);
            if (angle > Fruit.CountAngle) { GameEvents.RaiseCutMissed(point); return; }
            bool clean = angle <= Fruit.CleanAngle && fruit.DistanceToGuide(point) <= Fruit.CleanDistance;
            bool power = speed >= powerCutSpeed;
            fruit.ApplyCut(new CutInfo { power = power, clean = clean, point = point });
            if (grab) Haptics.Pulse(grab, power ? 0.9f : 0.45f, power ? 0.12f : 0.06f);
        }
    }
}
