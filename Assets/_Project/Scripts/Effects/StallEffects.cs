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
        public int juiceCount = 24, cleanJuiceCount = 36, powerJuiceCount = 48;

        [Header("Cut sounds")]
        public AudioClip[] crunch;          // every counted cut
        public AudioClip[] slice;           // the blade drawing through the flesh (under the crunch)
        public AudioClip[] boardKnock;      // the blade meeting the board just after
        public AudioClip[] powerLayer;      // extra weight on Power Cuts
        public AudioClip[] cleanLayer;      // bright top layer on Clean Cuts
        public AudioClip[] missed;          // blade off the guide: soft thunk

        [Header("Shop sounds")]
        public AudioClip[] coin;            // one per clink on payment
        public AudioClip[] error;           // refused (empty bowl) or customer walked out
        public AudioClip[] extra;           // fruit, chili or skewer dropped in a bowl
        public AudioClip[] shiftWon, shiftLost;

        [Header("Audio pool")]
        public int voices = 8;
        public float minDistance = 0.3f, maxDistance = 8f;

        [Header("Pieces fall apart")]
        [Tooltip("Each piece moves away from the fruit's middle by this fraction of its distance from it (equal gaps between equal slices).")]
        public float popSpread = 0.22f;
        public float popMin = 0.004f, popSeconds = 0.14f, popSpinDegrees = 8f;

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
            GameEvents.AddedToBowl += OnExtra;
            GameEvents.ShiftEnded += OnShiftEnded;
            GameEvents.Splat += OnSplat;
        }

        void OnDisable()
        {
            GameEvents.Cut -= OnCut;
            GameEvents.CutMissed -= OnCutMissed;
            GameEvents.Paid -= OnPaid;
            GameEvents.Refused -= OnError;
            GameEvents.WalkedOut -= OnError;
            GameEvents.AddedToBowl -= OnExtra;
            GameEvents.ShiftEnded -= OnShiftEnded;
            GameEvents.Splat -= OnSplat;
        }

        void OnCut(Fruit fruit, CutInfo cut, int combo)
        {
            BurstAlongCut(fruit, cut.point, cut.power ? powerJuiceCount : cut.clean ? cleanJuiceCount : juiceCount);
            Play(slice, cut.point, 0.55f, Random.Range(1.25f, 1.45f));
            Play(crunch, cut.point, 1f, 1f + 0.04f * Mathf.Min(combo, 10));   // GDD: pitch steps up with the combo
            Play(boardKnock, cut.point, 0.5f, Random.Range(0.95f, 1.1f), 0.05f);
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

        // Thrown food burst on a face: a juice spray and a wet crunch there.
        void OnSplat(Vector3 at, Color colour)
        {
            if (juice) juice.Emit(new ParticleSystem.EmitParams { startColor = colour, position = at, applyShapeToPosition = true }, powerJuiceCount);
            Play(crunch, at, 0.9f, Random.Range(0.8f, 0.9f));
        }

        // Juice sprays from the whole cut line (where the blade went in across the fruit), not one point.
        void BurstAlongCut(Fruit fruit, Vector3 at, int count)
        {
            if (!juice) return;
            var p = new ParticleSystem.EmitParams { startColor = fruit.juiceColor, applyShapeToPosition = true };
            var along = Vector3.zero;
            if (fruit.guides != null && fruit.CutsDone > 0 && fruit.CutsDone <= fruit.guides.Length)
            {
                var n = fruit.transform.TransformDirection(fruit.guides[fruit.CutsDone - 1].normal);
                along = Vector3.Cross(n, Vector3.up);
                if (along.sqrMagnitude < 0.01f) along = Vector3.Cross(n, Vector3.forward);   // a horizontal cut (pineapple crown)
                along = along.normalized * Mathf.Min(fruit.cutVolume.bounds.extents.x, fruit.cutVolume.bounds.extents.z) * 0.7f;
            }
            const int Points = 3;
            for (int i = 0; i < Points; i++)
            {
                p.position = at + along * (i - 1);
                juice.Emit(p, count / Points);
            }
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

        // The newly shown stage falls apart: each piece slides away from the fruit's middle in the horizontal plane, with
        // a little spin. The pieces stay children of the one fruit (one Rigidbody, one grab), so it still lifts as one.
        // Every piece's pivot is the fruit centre (Blender export), so a piece's place is its mesh centre, not its
        // localPosition. Assumes stages sit unrotated and unscaled under the fruit root (as StallBuilder makes them).
        // The pieces split from half their final spread: the gaps of earlier cuts don't snap shut on the next one.
        void PopApart(Fruit fruit)
        {
            if (fruit.stages == null || fruit.CutsDone >= fruit.stages.Length) return;
            var stage = fruit.stages[fruit.CutsDone].transform;
            int count = stage.childCount;
            if (count < 2) return;
            var middle = Vector3.zero;
            for (int i = 0; i < count; i++) middle += PieceCentre(stage.GetChild(i));
            middle /= count;
            for (int i = 0; i < count; i++)
            {
                var t = stage.GetChild(i);
                var dir = PieceCentre(t) - middle; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-8f) continue;   // the middle slice of an odd count stays put
                var offset = dir * popSpread;
                if (offset.magnitude < popMin) offset = dir.normalized * popMin;
                float spin = popSpinDegrees * (Random.value < 0.5f ? -1f : 1f);
                var home = t.localPosition;
                pops.Add(new Pop
                {
                    t = t, from = home + offset * 0.5f, to = home + offset,
                    r0 = t.localRotation, r1 = Quaternion.AngleAxis(spin, Vector3.up) * t.localRotation, start = Time.time,
                });
            }
        }

        // A piece's mesh centre in its stage's space (its mesh may sit on the piece or on child primitives).
        static readonly List<MeshFilter> meshes = new List<MeshFilter>(4);
        static Vector3 PieceCentre(Transform piece)
        {
            piece.GetComponentsInChildren(true, meshes);
            var stage = piece.parent;
            var sum = Vector3.zero; int n = 0;
            foreach (var f in meshes)
            {
                if (!f.sharedMesh) continue;
                sum += stage.InverseTransformPoint(f.transform.TransformPoint(f.sharedMesh.bounds.center));
                n++;
            }
            return n > 0 ? sum / n : piece.localPosition;
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
