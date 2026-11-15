using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SliceAndSell
{
    // GDD > Failure cases, "target out of reach": a tool left (not held) below the board or more than maxDistance from it
    // (horizontally) goes back to its rack pose after `delay` seconds, with a whoosh at the rack. One that falls through
    // the world goes back at once. Replaces Lab 5 RespawnOnDrop for tools.
    // Home is the starting local pose: seated mode lowers the whole stall, and "below" is measured from the board's own y,
    // so both checks follow the stall down.
    [RequireComponent(typeof(Rigidbody))]
    public class ReturnToRack : MonoBehaviour
    {
        public Transform board;             // the board mesh (its pivot is on the board top), not the CuttingBoard root
        public float belowBoard = 0.05f;    // lower than board y minus this = on the floor or a shelf, out of reach
        public float maxDistance = 1f;
        public float delay = 2f;
        public float fallLimit = -1f;       // world y: fell through the floor
        public AudioClip whoosh;

        XRBaseInteractable interactable;
        Rigidbody body;
        Vector3 homePos; Quaternion homeRot;
        float outTime;

        void Awake()
        {
            interactable = GetComponent<XRBaseInteractable>();
            body = GetComponent<Rigidbody>();
            homePos = transform.localPosition; homeRot = transform.localRotation;
        }

        void Update()
        {
            if (interactable && interactable.isSelected) { outTime = 0f; return; }
            var p = transform.position;
            if (p.y < fallLimit) { Return(); return; }
            var d = p - board.position; d.y = 0f;
            bool outOfReach = p.y < board.position.y - belowBoard || d.sqrMagnitude > maxDistance * maxDistance;
            outTime = outOfReach ? outTime + Time.deltaTime : 0f;
            if (outTime >= delay) Return();
        }

        public void Return()
        {
            outTime = 0f;
            // HoldOnTrackingLoss may still hold FreezeAll for a moment; it restores constraints itself, and a frozen body
            // just waits at the rack until then.
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
            transform.SetLocalPositionAndRotation(homePos, homeRot);
            if (whoosh) AudioSource.PlayClipAtPoint(whoosh, transform.position);
        }
    }
}
