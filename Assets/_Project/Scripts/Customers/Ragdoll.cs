using System.Collections.Generic;
using UnityEngine;

namespace SliceAndSell
{
    // Turns an animated Polyfork character (Mixamo-style bone names) into a ragdoll at run time: 11 bodies with
    // capsule / box / sphere colliders sized in world metres from the bones themselves (so any import scale works),
    // joined by CharacterJoints. Nothing is set up in the prefab; a living customer has no physics at all.
    public static class Ragdoll
    {
        struct Part { public string bone, to, parent; public float radius, mass, extend, swing; }

        // to: the bone the capsule reaches (its length); extend: m added past it (hand, foot).
        static readonly Part[] Parts =
        {
            new Part { bone = "Hips", mass = 12f },
            new Part { bone = "Spine2", to = "Neck", parent = "Hips", radius = 0.13f, mass = 10f, swing = 25f },
            new Part { bone = "Head", parent = "Spine2", radius = 0.11f, mass = 4f, swing = 35f },
            new Part { bone = "LeftArm", to = "LeftForeArm", parent = "Spine2", radius = 0.045f, mass = 2f, swing = 80f },
            new Part { bone = "LeftForeArm", to = "LeftHand", parent = "LeftArm", radius = 0.04f, mass = 1.5f, extend = 0.08f, swing = 70f },
            new Part { bone = "RightArm", to = "RightForeArm", parent = "Spine2", radius = 0.045f, mass = 2f, swing = 80f },
            new Part { bone = "RightForeArm", to = "RightHand", parent = "RightArm", radius = 0.04f, mass = 1.5f, extend = 0.08f, swing = 70f },
            new Part { bone = "LeftUpLeg", to = "LeftLeg", parent = "Hips", radius = 0.07f, mass = 6f, swing = 60f },
            new Part { bone = "LeftLeg", to = "LeftFoot", parent = "LeftUpLeg", radius = 0.055f, mass = 4f, extend = 0.05f, swing = 70f },
            new Part { bone = "RightUpLeg", to = "RightLeg", parent = "Hips", radius = 0.07f, mass = 6f, swing = 60f },
            new Part { bone = "RightLeg", to = "RightFoot", parent = "RightUpLeg", radius = 0.055f, mass = 4f, extend = 0.05f, swing = 70f },
        };

        // Returns the bodies (empty if the skeleton isn't the expected one). They start awake, at the pose the
        // animation left them in.
        public static Rigidbody[] Build(Transform model)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            foreach (var p in Parts) if (!bones.ContainsKey(p.bone)) return new Rigidbody[0];

            var bodies = new Dictionary<string, Rigidbody>();
            var colliders = new List<Collider>();
            foreach (var p in Parts)
            {
                var bone = bones[p.bone];
                float s = Mathf.Max(1e-4f, bone.lossyScale.x);                 // world metres -> bone units
                Collider col;
                if (p.bone == "Hips")
                {
                    var box = bone.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.3f, 0.18f, 0.2f) / s;
                    box.center = bone.InverseTransformVector(Vector3.up * 0.04f);
                    col = box;
                }
                else if (p.to == null)
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();   // the head: a sphere above the neck joint
                    sphere.radius = p.radius / s;
                    sphere.center = bone.InverseTransformVector(Vector3.up * 0.09f);
                    col = sphere;
                }
                else
                {
                    Vector3 world = bones.TryGetValue(p.to, out var end) ? end.position - bone.position : Vector3.down * 0.3f;
                    world += world.normalized * p.extend;
                    var local = bone.InverseTransformVector(world);
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    var cap = bone.gameObject.AddComponent<CapsuleCollider>();
                    cap.direction = axis;
                    cap.radius = p.radius / s;
                    cap.height = Mathf.Abs(local[axis]) + 2f * cap.radius;
                    cap.center = local * 0.5f;
                    col = cap;
                }
                colliders.Add(col);

                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = p.mass;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
                bodies[p.bone] = rb;

                if (p.parent != null)
                {
                    var joint = bone.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = bodies[p.parent];
                    joint.enableProjection = true;
                    joint.lowTwistLimit = new SoftJointLimit { limit = -p.swing * 0.5f };
                    joint.highTwistLimit = new SoftJointLimit { limit = p.swing * 0.5f };
                    joint.swing1Limit = new SoftJointLimit { limit = p.swing };
                    joint.swing2Limit = new SoftJointLimit { limit = p.swing };
                }
            }
            // Its own parts never push each other apart (arms through the chest would jitter).
            for (int i = 0; i < colliders.Count; i++)
                for (int j = i + 1; j < colliders.Count; j++) Physics.IgnoreCollision(colliders[i], colliders[j]);

            // The skinned meshes now follow physics, not the clip: bounds must follow too, or the body vanishes as it falls.
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            var result = new Rigidbody[bodies.Count];
            bodies.Values.CopyTo(result, 0);
            return result;
        }
    }
}
