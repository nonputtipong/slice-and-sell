using System;
using System.Collections;
using UnityEngine;

namespace SliceAndSell
{
    public enum CustomerMood { Happy, Neutral, Angry }

    // A walk-on customer: walks to its counter spot, faces the player and waits, reacts, walks off.
    // Kinematic (transform only, no physics/navmesh): it stops exactly at the spot it is given, so the
    // caller keeps that spot clear of the counter. Clips are legacy Animation (glTFast import), played by name.
    public class Customer : MonoBehaviour
    {
        public Animation anim;
        public PatienceRing ring;
        public Transform model;   // the imported model: hop / wiggle / stomp move it, the clips only drive its bones
        public Transform head;    // nod / head-shake, layered on the clip in LateUpdate (every clip keys Head rotation)
        public float walkSpeed = 1.1f, turnSpeed = 360f;   // m/s, deg/s

        const float Fade = 0.2f, ReactTime = 0.6f;

        public bool AtCounter { get; private set; }

        float headYaw, headPitch;

        void Awake()
        {
            // The importer already loops them; this keeps it true if a clip is swapped by hand.
            foreach (AnimationState s in anim) s.wrapMode = WrapMode.Loop;
        }

        public void Arrive(Vector3 from, Vector3 counterSpot, Action onArrived)
        {
            StopAllCoroutines();
            AtCounter = false;
            transform.position = from;
            var dir = counterSpot - from; dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir);
            StartCoroutine(ArriveRoutine(counterSpot, onArrived));
        }

        public void SetPatience(float t01, bool visible) => ring.Set(t01, visible);

        public void Leave(CustomerMood mood, Vector3 exit)
        {
            StopAllCoroutines();
            AtCounter = false;
            ring.Set(0, false);
            StartCoroutine(LeaveRoutine(mood, exit));
        }

        IEnumerator ArriveRoutine(Vector3 spot, Action onArrived)
        {
            yield return WalkTo(spot);
            var facePlayer = Quaternion.LookRotation(Vector3.back);
            while (Quaternion.Angle(transform.rotation, facePlayer) > 0.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facePlayer, turnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = facePlayer;
            AtCounter = true;
            onArrived?.Invoke();
        }

        IEnumerator LeaveRoutine(CustomerMood mood, Vector3 exit)
        {
            anim.CrossFade("Idle", Fade);
            for (float t = 0; t < 1; t += Time.deltaTime / ReactTime) { React(mood, t); yield return null; }
            model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
            headYaw = headPitch = 0;
            yield return WalkTo(exit);
            Destroy(gameObject);
        }

        IEnumerator WalkTo(Vector3 target)
        {
            anim.CrossFade("Walk", Fade);
            while ((transform.position - target).sqrMagnitude > 1e-6f)
            {
                var dir = target - transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
                transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
                yield return null;
            }
            anim.CrossFade("Idle", Fade);
        }

        // t runs 0 → 1 over ReactTime.
        void React(CustomerMood mood, float t)
        {
            float bump = Mathf.Sin(Mathf.PI * t);          // 0 → 1 → 0
            float shake = Mathf.Sin(6 * Mathf.PI * t);     // three shakes
            switch (mood)
            {
                case CustomerMood.Happy:
                    model.localPosition = new Vector3(0, 0.12f * bump, 0);   // little hop
                    break;
                case CustomerMood.Neutral:
                    headPitch = 20f * bump;   // one nod
                    break;
                case CustomerMood.Angry:
                    headYaw = 30f * shake;
                    model.localRotation = Quaternion.Euler(0, 8f * shake, 0);
                    // Stomp in the last 30%: lift 4 cm, then slam 1.5 cm down.
                    float s = t > 0.7f ? Mathf.Sin(2 * Mathf.PI * (t - 0.7f) / 0.3f) : 0;
                    model.localPosition = new Vector3(0, s > 0 ? 0.04f * s : 0.015f * s, 0);
                    break;
            }
        }

        void LateUpdate()
        {
            // Runs after the Animation has posed the head this frame, so the offset never accumulates.
            if (head && (headYaw != 0 || headPitch != 0))
                head.rotation = Quaternion.AngleAxis(headYaw, transform.up) * Quaternion.AngleAxis(headPitch, transform.right) * head.rotation;
        }
    }
}
