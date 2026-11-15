using System.Collections.Generic;
using UnityEngine;

namespace SliceAndSell
{
    // The "feel" layer: juice, pieces popping apart and sounds for GameEvents. One in the scene.
    // Listens only; never changes game state. Pooled particles and audio sources, so no allocations per event.
    public class StallEffects : MonoBehaviour
    {
        [Header("Juice")]
        public ParticleSystem juice;        // world space, emission off; we Emit() bursts into it
        public int juiceCount = 20, cleanJuiceCount = 30, powerJuiceCount = 40;

        [Header("Cut sounds")]
        public AudioClip[] crunch;          // every counted cut
        public AudioClip[] powerLayer;      // extra weight on Power Cuts
        public AudioClip[] cleanLayer;      // bright top layer on Clean Cuts
        public AudioClip[] missed;          // blade off the guide: soft thunk

        [Header("Shop sounds")]
        public AudioClip[] coin;            // one per clink on payment
        public AudioClip[] error;           // refused (empty bag) or customer walked out
        public AudioClip[] extra;           // chili / skewer dropped in the bag
        public AudioClip[] shiftWon, shiftLost;

        [Header("Audio pool")]
        public int voices = 8;
        public float minDistance = 0.3f, maxDistance = 8f;

        [Header("Pieces pop apart")]
        public float popDistance = 0.008f, popSeconds = 0.12f, popSpinDegrees = 10f;

        AudioSource[] pool;
        int next;

        struct Pop { public Transform t; public Vector3 from, to; public Quaternion r0, r1; public float start; }
        readonly List<Pop> pops = new List<Pop>(32);

        void Awake()
        {
            pool = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var s = new GameObject("Voice " + i).AddComponent<AudioSource>();
                s.transform.SetParent(transform, false);
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = minDistance; s.maxDistance = maxDistance;
                pool[i] = s;
            }
        }

        void OnEnable()
        {
            GameEvents.Cut += OnCut;
            GameEvents.CutMissed += OnCutMissed;
            GameEvents.Paid += OnPaid;
            GameEvents.Refused += OnError;
            GameEvents.WalkedOut += OnError;
            GameEvents.ExtraAdded += OnExtra;
            GameEvents.ShiftEnded += OnShiftEnded;
        }

        void OnDisable()
        {
            GameEvents.Cut -= OnCut;
            GameEvents.CutMissed -= OnCutMissed;
            GameEvents.Paid -= OnPaid;
            GameEvents.Refused -= OnError;
            GameEvents.WalkedOut -= OnError;
            GameEvents.ExtraAdded -= OnExtra;
            GameEvents.ShiftEnded -= OnShiftEnded;
        }

        void OnCut(Fruit fruit, CutInfo cut, int combo)
        {
            Burst(cut.point, fruit.juiceColor, cut.power ? powerJuiceCount : cut.clean ? cleanJuiceCount : juiceCount);
            Play(crunch, cut.point, 1f, 1f + 0.04f * Mathf.Min(combo, 10));   // GDD: pitch steps up with the combo
            if (cut.power) Play(powerLayer, cut.point, 0.8f, Random.Range(0.9f, 1f));
            if (cut.clean) Play(cleanLayer, cut.point, 0.35f, Random.Range(1.1f, 1.25f));
            PopApart(fruit);
        }

        void OnCutMissed(Vector3 at) => Play(missed, at, 0.7f, Random.Range(0.9f, 1.05f));

        void OnPaid(Vector3 at, int amount, int tip, bool correct)
        {
            // One clink, plus one per tip "coin" (max 3), staggered like coins landing in the box.
            int clinks = 1 + Mathf.Min(3, tip > 0 ? 1 + tip / 3 : 0);
            for (int i = 0; i < clinks; i++) Play(coin, at, 0.7f, Random.Range(0.95f, 1.15f), i * 0.09f);
        }

        void OnError(Vector3 at) => Play(error, at, 0.6f, 1f);
        void OnExtra(Vector3 at, string what) => Play(extra, at, 0.7f, Random.Range(0.95f, 1.1f));

        // The event has no position: play it at the player's head so it is heard evenly.
        void OnShiftEnded(bool won, int stars)
        {
            var cam = Camera.main;
            Play(won ? shiftWon : shiftLost, cam ? cam.transform.position : transform.position, 0.8f, 1f);
        }

        void Burst(Vector3 at, Color colour, int count)
        {
            if (!juice) return;
            var p = new ParticleSystem.EmitParams { position = at, startColor = colour, applyShapeToPosition = true };
            juice.Emit(p, count);
        }

        void Play(AudioClip[] clips, Vector3 at, float volume, float pitch, float delay = 0f)
        {
            if (clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            if (!clip) return;
            var s = pool[next]; next = (next + 1) % pool.Length;   // round robin: steals the oldest voice when busy
            s.transform.position = at;
            s.clip = clip; s.volume = volume; s.pitch = pitch;
            if (delay > 0f) s.PlayDelayed(delay); else s.Play();
        }

        // Nudge each piece of the newly shown stage outward in the fruit's horizontal plane, with a little spin.
        // Assumes stages sit unrotated under the fruit root (as StallBuilder makes them), so stage-local XZ is the fruit's horizontal plane.
        void PopApart(Fruit fruit)
        {
            if (fruit.stages == null || fruit.CutsDone >= fruit.stages.Length) return;
            var stage = fruit.stages[fruit.CutsDone].transform;
            for (int i = 0; i < stage.childCount; i++)
            {
                var t = stage.GetChild(i);
                var dir = t.localPosition; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-8f) continue;   // a piece at the centre has nowhere to go
                // localPosition is in the stage's (unscaled) space, so popDistance stays in metres.
                float spin = popSpinDegrees * (Random.value < 0.5f ? -1f : 1f);
                pops.Add(new Pop
                {
                    t = t, from = t.localPosition, to = t.localPosition + dir.normalized * popDistance,
                    r0 = t.localRotation, r1 = Quaternion.AngleAxis(spin, Vector3.up) * t.localRotation, start = Time.time,
                });
            }
        }

        void Update()
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                if (!p.t) { pops.RemoveAt(i); continue; }   // fruit scooped/destroyed mid-pop
                float k = Mathf.Clamp01((Time.time - p.start) / popSeconds);
                float e = 1f - (1f - k) * (1f - k);         // ease-out: fast split, soft settle
                p.t.localPosition = Vector3.LerpUnclamped(p.from, p.to, e);
                p.t.localRotation = Quaternion.SlerpUnclamped(p.r0, p.r1, e);
                if (k >= 1f) pops.RemoveAt(i);
            }
        }
    }
}
