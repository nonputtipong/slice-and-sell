using UnityEngine;

namespace SliceAndSell.Perf
{
    // Performance measuring only: this object exists in Development builds on the device and nowhere else. A release
    // build (what ships) and the Editor (Play Mode tests) remove it at once, so the game stays exactly as it was.
    [DefaultExecutionOrder(-1000)]
    public class DevelopmentOnly : MonoBehaviour
    {
        public static bool Active => Debug.isDebugBuild && !Application.isEditor;

        void Awake()
        {
            if (!Active) { gameObject.SetActive(false); Destroy(gameObject); }
        }
    }
}
