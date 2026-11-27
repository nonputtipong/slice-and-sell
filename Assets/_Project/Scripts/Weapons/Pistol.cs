using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // The stall's pistol, for the fruit thief. Playtest 5: no magazine, it just fires (a short cooldown between shots).
    // Controllers: the trigger. Hands (nothing sends "activate" for a tracked hand): curl the index finger (straighten
    // it again before the next shot), or simply keep a thief in the aim cone for `aimSeconds`. Aim assist: a thief
    // within `assistAngle` of where the barrel points is tracked by the bullet. Haptics: a kick on every shot, a medium
    // buzz when a tough thief is hit, a long hard buzz when the thief is taken down.
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Pistol : MonoBehaviour
    {
        public Transform muzzle;
        public Bullet bulletPrefab;
        public AudioClip shotSound;
        public float assistAngle = 25f, range = 25f;

        public const float ShotAmplitude = 0.55f, ShotSeconds = 0.07f, TakedownAmplitude = 1f, TakedownSeconds = 0.4f;
        public const float WoundAmplitude = 0.8f, WoundSeconds = 0.15f;       // a tough thief hit but still running
        public const float Cooldown = 0.15f;
        // Hands: aim-to-shoot dwell and re-arm; index bend (degrees, summed over its two knuckles) to fire / to re-arm.
        public const float AimSeconds = 0.5f, AimRearmSeconds = 0.6f, CurlFireDegrees = 90f, CurlReleaseDegrees = 45f;

        public int Shots { get; private set; }
        public int Takedowns { get; private set; }
        public int Hits { get; private set; }
        public float LastHaptic { get; private set; }          // amplitude of the last pulse (tests; hands have no motor)

        XRGrabInteractable grab;
        AudioSource audioSource;
        bool curled;
        float lastShot = -1f, aimHeld, aimRearm;
        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; audioSource.playOnAwake = false; audioSource.minDistance = 0.5f;
        }

        void OnEnable() => grab.activated.AddListener(OnActivated);
        void OnDisable() => grab.activated.RemoveListener(OnActivated);
        void OnActivated(ActivateEventArgs _) => Fire();

        void Update()
        {
            var hand = HoldingHand();
            if (hand.HasValue) { HandTrigger(hand.Value); AimToShoot(Time.deltaTime); }
            // Not held by a hand: counts as curled, so the grasp that picks it up never fires it; the index must be
            // seen straight first.
            else { curled = true; aimHeld = 0f; aimRearm = 0f; }
        }

        // Fires one round (no magazine). Returns whether it fired: not while paused or within the cooldown.
        public bool Fire()
        {
            if (GamePause.Paused) return false;
            if (lastShot >= 0f && Time.time - lastShot < Cooldown) return false;
            lastShot = Time.time;
            Shots++;
            Play(shotSound, 1f, Random.Range(0.95f, 1.05f));
            Pulse(ShotAmplitude, ShotSeconds);
            if (bulletPrefab)
            {
                var b = Instantiate(bulletPrefab, muzzle.position, muzzle.rotation);
                b.Launch(muzzle.forward, FindTarget(), OnHit);
            }
            return true;
        }

        // The thief nearest the barrel's line, within the aim cone and range (null: the shot just flies straight).
        public Thief FindTarget()
        {
            Thief best = null; float bestAngle = assistAngle;
            foreach (var t in FindObjectsByType<Thief>(FindObjectsSortMode.None))
            {
                if (t.State == Thief.Phase.Caught || t.State == Thief.Phase.Gone) continue;
                var to = Bullet.AimPoint(t) - muzzle.position;
                if (to.magnitude > range) continue;
                float a = Vector3.Angle(muzzle.forward, to);
                if (a <= bestAngle) { bestAngle = a; best = t; }
            }
            return best;
        }

        // Hands: a thief kept in the aim cone for AimSeconds fires once, then it re-arms after AimRearmSeconds (so a
        // tough thief gets his second shot). Called every frame while a tracked hand holds the pistol. Returns whether it fired.
        public bool AimToShoot(float dt)
        {
            if (GamePause.Paused) return false;
            if (aimRearm > 0f) { aimRearm -= dt; return false; }
            if (!FindTarget()) { aimHeld = 0f; return false; }
            if ((aimHeld += dt) < AimSeconds) return false;
            aimHeld = 0f; aimRearm = AimRearmSeconds;
            return Fire();
        }

        void OnHit(Thief _, bool down)
        {
            Hits++;
            if (!down) { Pulse(WoundAmplitude, WoundSeconds); return; }
            Takedowns++;
            Pulse(TakedownAmplitude, TakedownSeconds);
        }

        // Left/right if a tracked hand (not a controller) holds the pistol, else null.
        bool? HoldingHand()
        {
            var holder = grab.isSelected ? grab.firstInteractorSelecting as XRBaseInteractor : null;
            if (!holder) return null;
            bool left = holder.handedness == InteractorHandedness.Left;
            bool handTracked = left ? XRInputTrackingAggregator.GetLeftTrackedHandStatus().isTracked : XRInputTrackingAggregator.GetRightTrackedHandStatus().isTracked;
            return handTracked ? left : (bool?)null;
        }

        // The index finger's bend at its two knuckles (proximal and middle joints): curling past CurlFireDegrees fires
        // once; it must straighten below CurlReleaseDegrees before the next shot. Joint angles, unlike the old
        // fingertip-to-palm distance, don't depend on hand size or how the grip closes around the pistol.
        void HandTrigger(bool left)
        {
            SubsystemManager.GetSubsystems(hands);
            foreach (var h in hands)
            {
                if (!h.running) continue;
                var hand = left ? h.leftHand : h.rightHand;
                if (!hand.isTracked) continue;
                if (!Joint(hand, XRHandJointID.IndexMetacarpal, out var m) || !Joint(hand, XRHandJointID.IndexProximal, out var p) ||
                    !Joint(hand, XRHandJointID.IndexIntermediate, out var i) || !Joint(hand, XRHandJointID.IndexDistal, out var d)) return;
                float bend = IndexBend(m, p, i, d);
                if (!curled && bend > CurlFireDegrees) { curled = true; Fire(); }
                else if (curled && bend < CurlReleaseDegrees) curled = false;
                return;
            }
        }

        // Sum of the bends at the proximal and intermediate joints (0 = a straight finger).
        public static float IndexBend(Vector3 metacarpal, Vector3 proximal, Vector3 intermediate, Vector3 distal) =>
            Vector3.Angle(proximal - metacarpal, intermediate - proximal) + Vector3.Angle(intermediate - proximal, distal - intermediate);

        static bool Joint(XRHand hand, XRHandJointID id, out Vector3 position)
        {
            bool ok = hand.GetJoint(id).TryGetPose(out Pose pose);
            position = pose.position;
            return ok;
        }

        void Pulse(float amplitude, float seconds)
        {
            LastHaptic = amplitude;
            Haptics.Pulse(grab, amplitude, seconds);
        }

        void Play(AudioClip clip, float volume, float pitch = 1f)
        {
            if (!clip) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);            // GameSettings.Volume is on the listener
        }
    }
}
