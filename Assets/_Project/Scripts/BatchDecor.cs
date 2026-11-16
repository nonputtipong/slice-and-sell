using System.Linq;
using UnityEngine;

namespace SliceAndSell
{
    // Draws the stall's still decorations (cart, umbrella, crate walls, bulbs, cards...) as a few combined batches
    // instead of one draw per piece (Quest draw-call budget). Unity's build-time static batching bakes world positions,
    // but the whole stall moves (seated mode lowers it), so the pieces are combined at start under this root, which
    // still moves them. StallBuilder picks them: only things that never move or switch on/off on their own.
    public class BatchDecor : MonoBehaviour
    {
        public GameObject[] decor;

        public int Combined { get; private set; }

        void Start()
        {
            // Runtime combining needs readable meshes (glTFast imports them readable; primitives are).
            var ok = decor.Where(g => g && g.TryGetComponent(out MeshFilter f) && f.sharedMesh && f.sharedMesh.isReadable).ToArray();
            StaticBatchingUtility.Combine(ok, gameObject);
            Combined = ok.Length;
        }
    }
}
