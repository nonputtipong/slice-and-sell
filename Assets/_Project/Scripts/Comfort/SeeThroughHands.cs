using UnityEngine;

namespace SliceAndSell
{
    // Playtest 5: the tracked hands hid the knife and fruit they held. The hand visuals (XRI Hands Interaction Demo)
    // are made at run time, so this swaps their hand material for a more see-through copy (HandSeeThrough: lower alpha
    // on the fill, the dark core and the rim) wherever it finds it under the rig. Only the material that matches
    // `original` is replaced; the depth-only prepass in slot 0 stays, so the fingers don't show through each other.
    public class SeeThroughHands : MonoBehaviour
    {
        public Material original, seeThrough;
        public float rescanSeconds = 1f;      // hands appear (and are rebuilt) when tracking starts

        float next;

        void LateUpdate()
        {
            if (!original || !seeThrough || Time.unscaledTime < next) return;
            next = Time.unscaledTime + rescanSeconds;
            Apply(transform, original, seeThrough);
        }

        // Returns how many renderer slots were swapped (tests).
        public static int Apply(Transform root, Material original, Material seeThrough)
        {
            int swapped = 0;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && (mats[i] == original || mats[i].name.StartsWith(original.name + " (Instance)")))
                    { mats[i] = seeThrough; changed = true; swapped++; }
                if (changed) r.sharedMaterials = mats;
            }
            return swapped;
        }
    }
}
