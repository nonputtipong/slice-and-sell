using UnityEngine;

namespace SliceAndSell
{
    // Turns a label toward the player's eyes every frame (yaw only).
    public class FaceCamera : MonoBehaviour
    {
        Transform cam;
        void LateUpdate()
        {
            if (!cam) { var c = Camera.main; if (!c) return; cam = c.transform; }
            var d = transform.position - cam.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
