using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // The board socket holds one fruit. When that fruit is fully cut and an empty bowl sits on the
    // scoop spot, the slices go into the bowl (whichever happens last triggers it). The player can also lift the
    // cut fruit and let go over any bowl (BowlMouth).
    // It also draws the next cut's guide line on the fruit (GDD > Precision).
    public class CuttingBoard : MonoBehaviour
    {
        public XRSocketInteractor fruitSocket, scoopSocket;
        public LineRenderer guideLine;

        public Fruit Fruit => fruitSocket.hasSelection ? fruitSocket.firstInteractableSelected.transform.GetComponent<Fruit>() : null;
        Bowl ScoopBowl => scoopSocket.hasSelection ? scoopSocket.firstInteractableSelected.transform.GetComponent<Bowl>() : null;

        void Update()
        {
            var fruit = Fruit; var bowl = ScoopBowl;
            if (fruit && fruit.IsFullyCut && bowl && !bowl.IsFilled)
            {
                bowl.Fill(fruit);
                Destroy(fruit.gameObject);
                fruit = null;
            }
            ShowGuide(fruit);
        }

        // A line where the next cut plane meets the fruit: across the top for vertical cuts,
        // across the front for horizontal ones (the pineapple crown).
        void ShowGuide(Fruit fruit)
        {
            if (!guideLine) return;
            bool show = fruit && !fruit.IsFullyCut && fruit.guides != null && fruit.guides.Length > 0;
            guideLine.enabled = show;
            if (!show) return;

            var g = fruit.NextGuide;
            var t = fruit.transform;
            var n = t.TransformDirection(g.normal).normalized;
            var b = fruit.cutVolume.bounds;
            var onPlane = t.TransformPoint(g.point);
            Vector3 dir, centre;
            if (Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.7f)
            {
                dir = t.right;
                centre = new Vector3(b.center.x, onPlane.y, b.min.z - 0.003f);
            }
            else
            {
                dir = Vector3.Cross(n, Vector3.up).normalized;
                // the point of the plane above the fruit's centre line, just over the top
                var above = new Vector3(b.center.x, b.max.y + 0.003f, b.center.z);
                centre = above - n * Vector3.Dot(above - onPlane, n);
            }
            float half = Mathf.Max(b.extents.x, b.extents.z) + 0.01f;
            guideLine.SetPosition(0, centre - dir * half);
            guideLine.SetPosition(1, centre + dir * half);
        }
    }
}
