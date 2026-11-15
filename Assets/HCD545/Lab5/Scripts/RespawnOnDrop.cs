using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HCD545
{
    // Lab 5: puts an object back where it started when it falls below the floor, or when it has been lying on the floor
    // (lower than floorHeight) for a few seconds. Nobody has to bend down or walk away to pick it up.
    public class RespawnOnDrop : MonoBehaviour
    {
        public float belowFloor = -1f;          // fell through the floor or off the 20 x 20 m area
        public float floorHeight = 0.25f;       // resting on the floor
        public float returnAfterSeconds = 3f;

        static readonly List<RespawnOnDrop> all = new List<RespawnOnDrop>();
        Vector3 startPos; Quaternion startRot;
        Rigidbody body; XRBaseInteractable interactable;
        float onFloorTime;

        void Awake()
        {
            startPos = transform.position; startRot = transform.rotation;
            body = GetComponent<Rigidbody>();
            interactable = GetComponent<XRBaseInteractable>();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Update()
        {
            bool held = interactable && interactable.isSelected;
            onFloorTime = (!held && transform.position.y < floorHeight) ? onFloorTime + Time.deltaTime : 0f;
            if (transform.position.y < belowFloor || onFloorTime > returnAfterSeconds) ReturnToStart();
        }

        public void ReturnToStart()
        {
            if (body) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(startPos, startRot);
            onFloorTime = 0f;
        }

        // Used by the Reset room and Reset trial buttons.
        public static void ResetAll() { foreach (var r in all.ToArray()) r.ReturnToStart(); }
    }
}
