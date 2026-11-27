using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // The stall's pistol, for the fruit thief. It only fires with a magazine pushed into the bottom of the grip (a
    // socket there takes magazines and nothing else). Controllers: the trigger. Hands: hold it, then curl the index
    // finger (straighten it again before the next shot). Aim assist: a thief within `assistAngle` of where the barrel
    // points is tracked by the bullet. Haptics: a kick on every shot, a long hard buzz when the thief goes down.
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Pistol : MonoBehaviour
    {
        public Transform muzzle;
        public XRSocketInteractor magSocket;
        public Bullet bulletPrefab;
        public AudioClip shotSound, drySound, magInSound;
        public float assistAngle = 25f, range = 25f;

        public const float ShotAmplitude = 0.55f, ShotSeconds = 0.07f, KillAmplitude = 1f, KillSeconds = 0.4f, DryAmplitude = 0.15f;
        public const float WoundAmplitude = 0.8f, WoundSeconds = 0.15f;       // a tough thief hit but still running

        public Magazine Mag => magSocket && magSocket.hasSelection ? magSocket.firstInteractableSelected.transform.GetComponent<Magazine>() : null;
        public int Shots { get; private set; }
        public int Kills { get; private set; }
        public int Hits { get; private set; }
        public float LastHaptic { get; private set; }          // amplitude of the last pulse (tests; hands have no motor)

        XRGrabInteractable grab;
        AudioSource audioSource;
        bool curled;
        readonly List<XRHandSubsystem> hands = new List<XRHandSubsystem>();

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; audioSource.playOnAwake = false; audioSource.minDistance = 0.5f;
        }

        void OnEnable()
        {
            grab.activated.AddListener(OnActivated);
            if (magSocket) magSocket.selectEntered.AddListener(OnMagIn);
        }

        void OnDisable()
        {
            grab.activated.RemoveListener(OnActivated);
            if (magSocket) magSocket.selectEntered.RemoveListener(OnMagIn);
        }

        void OnActivated(ActivateEventArgs _) => Fire();

        void OnMagIn(SelectEnterEventArgs args)
        {
            var mag = args.interactableObject.transform.GetComponent<Magazine>();
            if (mag && mag.IsEmpty) { StartCoroutine(Eject(mag)); return; }   // an empty one never stays in
            Play(magInSound, 0.8f);
            Pulse(0.3f, 0.04f);
        }

        void Update() => HandTrigger();

        // Fires one round if there is one. Returns whether it fired.
        public bool Fire()
        {
            if (GamePause.Paused) return false;
            var mag = Mag;
            if (!mag || mag.IsEmpty) { Play(drySound, 0.7f); Pulse(DryAmplitude, 0.03f); return false; }
            mag.rounds--;
            Shots++;
            Play(shotSound, 1f, Random.Range(0.95f, 1.05f));
            Pulse(ShotAmplitude, ShotSeconds);
            if (bulletPrefab)
            {
                var b = Instantiate(bulletPrefab, muzzle.position, muzzle.rotation);
                b.Launch(muzzle.forward, FindTarget(), OnKill);
            }
            if (mag.IsEmpty) StartCoroutine(Eject(mag));
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

        void OnKill(Thief _, bool down)
        {
            if (!down) { Hits++; Pulse(WoundAmplitude, WoundSeconds); return; }
            Hits++; Kills++;
            Pulse(KillAmplitude, KillSeconds);
        }

        // The empty magazine drops out of the grip; the socket stays off long enough for it to fall clear.
        IEnumerator Eject(Magazine mag)
        {
            var magGrab = mag.GetComponent<XRGrabInteractable>();
            magSocket.socketActive = false;
            if (magGrab && magSocket.interactionManager && magGrab.isSelected && magSocket.IsSelecting(magGrab))
                magSocket.interactionManager.SelectExit((IXRSelectInteractor)magSocket, (IXRSelectInteractable)magGrab);
            if (magGrab) magGrab.interactionLayers = 1 << 0;        // can still be picked up, never socketed again
            var rb = mag.GetComponent<Rigidbody>();
            if (rb) { rb.isKinematic = false; rb.linearVelocity = -transform.up * 0.6f; }
            Destroy(mag.gameObject, 3f);
            yield return new WaitForSeconds(0.6f);
            magSocket.socketActive = true;
        }

        // Hand tracking has no trigger: while a tracked hand holds the pistol, the index fingertip curling in toward
        // the palm fires once; it must straighten again (fingertip away from the palm) before the next shot.
        void HandTrigger()
        {
            var holder = grab.isSelected ? grab.firstInteractorSelecting as XRBaseInteractor : null;
            if (!holder) { curled = false; return; }
            bool left = holder.handedness == InteractorHandedness.Left;
            // That hand is tracked as a hand, not holding a controller (which has a real trigger).
            bool handTracked = left ? XRInputTrackingAggregator.GetLeftTrackedHandStatus().isTracked : XRInputTrackingAggregator.GetRightTrackedHandStatus().isTracked;
            bool controller = left ? XRInputTrackingAggregator.GetLeftControllerStatus().isTracked : XRInputTrackingAggregator.GetRightControllerStatus().isTracked;
            if (!handTracked || (controller && !handTracked)) { curled = false; return; }
            SubsystemManager.GetSubsystems(hands);
            foreach (var h in hands)
            {
                if (!h.running) continue;
                var hand = left ? h.leftHand : h.rightHand;
                if (!hand.isTracked) continue;
                if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip) || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm)) return;
                float d = Vector3.Distance(tip.position, palm.position);
                if (!curled && d < 0.055f) { curled = true; Fire(); }
                else if (curled && d > 0.075f) curled = false;
                return;
            }
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
