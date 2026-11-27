using System;
using UnityEngine;

namespace SliceAndSell
{
    // A fast tracer round. With a target (the pistol's aim assist found a thief) it steers onto his chest and hits
    // him; without one it flies straight and fades. Bullets only ever affect thieves.
    public class Bullet : MonoBehaviour
    {
        public float speed = 30f, turnDegreesPerSecond = 2400f, life = 0.8f;

        Thief target;
        Vector3 velocity;
        Action<Thief, bool> onHit;          // (thief, put down?)

        public void Launch(Vector3 direction, Thief aimAt, Action<Thief, bool> hit)
        {
            velocity = direction.normalized * speed;
            target = aimAt;
            onHit = hit;
        }

        public static Vector3 AimPoint(Thief t) => t.transform.position + Vector3.up * 1.15f;   // his chest

        void Update()
        {
            if (GamePause.Paused) return;
            float dt = Time.deltaTime;
            if ((life -= dt) <= 0f) { Destroy(gameObject); return; }
            if (target && target.State != Thief.Phase.Caught && target.State != Thief.Phase.Gone)
            {
                var to = AimPoint(target) - transform.position;
                float step = speed * dt;
                if (to.magnitude <= step)
                {
                    var t = target; target = null;
                    transform.position = AimPoint(t);
                    bool down = t.Shot(transform.position, velocity.normalized);
                    onHit?.Invoke(t, down);
                    Destroy(gameObject, 0.05f);
                    return;
                }
                // Home in: turn toward him, fast enough that a near miss within the aim cone still lands.
                velocity = Vector3.RotateTowards(velocity, to, turnDegreesPerSecond * Mathf.Deg2Rad * dt, 0f).normalized * speed;
            }
            transform.position += velocity * dt;
        }
    }
}
