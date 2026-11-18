using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell
{
    // Level 1 tutorial (GDD > Level 1 tutorial flow). Intro card with an OK button; then customer 1 gets every step
    // with a glow, a bobbing arrow and a text card, customer 2 the text only, customer 3 nothing.
    // Steps are read from the game's own state each frame (no hooks in the game scripts). Each waits for its action,
    // so nobody can fail; a step also counts when the player is already ahead of it (e.g. the bowl was filled early).
    public class Tutorial : MonoBehaviour
    {
        public OrderManager orders;
        public CuttingBoard board;
        public XRGrabInteractable knife;
        public Dispenser guavaBin, bowlStack;
        public XRSocketInteractor tray;
        public GameObject card, okButton;
        public TMP_Text text;
        public Renderer glow;
        public Transform arrow;

        public bool Active { get; private set; }                // Level 1 is running (prompts or not)
        public float OnboardingSeconds { get; private set; }    // Level 1 start -> customer 1 served (0 until then); pauses excluded

        const string Intro = "<b>Welcome to your fruit stall!</b>\nGrab with grip, or pinch with your hand.";

        // Logged as steps 1..7 (the intro is step 0). Controller wording first, then hands.
        static readonly string[] Steps =
        {
            "<b>Grab a guava from the crate</b>\nGrip, or pinch/grab it with your hand",
            "<b>Put it on the cutting board</b>\nLet go over the board",
            "<b>Pick up the knife</b>\nGrip, or grab the handle with your hand",
            "<b>Cut along the line (1 of 2)</b>\nPush the blade slowly down through the line",
            "<b>Cut along the new line (2 of 2)</b>\nPush the blade slowly down through the line",
            "<b>Put it in a bowl</b>\nLift the cut guava and let go over a bowl",
            "<b>Hand it over</b>\nPut the bowl on the orange tray",
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<Collider> colliders = new List<Collider>();
        MaterialPropertyBlock block;
        Color glowColour;

        float elapsed;
        bool intro;
        int customer, step;
        XRBaseInteractable guava;           // the last item seen waiting in the bin
        Fruit cutFruit;                     // the last fully cut fruit seen on the board (may be in the player's hand now)
        bool bowled;                        // a bowl was filled for this customer (BowlMouth or the scoop spot)

        void OnEnable() => GameEvents.AddedToBowl += OnAddedToBowl;
        void OnDisable() => GameEvents.AddedToBowl -= OnAddedToBowl;
        void OnAddedToBowl(Vector3 _, string what) { if (what != nameof(ExtraKind.Skewer) && what != nameof(ExtraKind.Chili)) bowled = true; }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            glowColour = glow.sharedMaterial.GetColor(BaseColorId);
            Hide();
        }

        void Update()
        {
            bool level1 = orders.Level && orders.levelIndex == 0 && orders.Running && !orders.ShiftOver;
            if (level1 != Active) { if (level1) Begin(); else End(); }
            if (!Active || GamePause.Paused) return;
            elapsed += Time.deltaTime;

            if (guavaBin.Current) guava = guavaBin.Current;
            if (board.Fruit && board.Fruit.IsFullyCut) cutFruit = board.Fruit;

            if (intro && Done(0)) Ok();     // already grabbing: the intro has done its job
            if (!intro)
            {
                while (step < Steps.Length && Done(step)) Complete();
                if (orders.Served > customer) NextCustomer();
                // The fruit is gone before it reached a bowl (taken off uncut, or dropped and despawned): start again from
                // the crate. A cut fruit in the player's hand on its way to a bowl is still there.
                else if (step >= 2 && step <= 5 && !board.Fruit && !cutFruit && !bowled) step = 0;
            }
            Show();
        }

        // Each step's own action, or a later state already reached.
        bool Done(int s)
        {
            if (orders.Served > customer) return true;
            var fruit = board.Fruit;
            bool onBoard = fruit || cutFruit || bowled;
            switch (s)
            {
                case 0: return (guava && guava.isSelected) || onBoard;
                case 1: return onBoard;
                case 2: return knife.isSelected || (fruit && fruit.CutsDone >= 1) || cutFruit || bowled;
                case 3: return (fruit && fruit.CutsDone >= 1) || cutFruit || bowled;
                case 4: return cutFruit || bowled;
                case 5: return bowled;
                default: return false;      // 6: handed over (Served, above)
            }
        }

        // OK button on the intro card.
        public void Ok()
        {
            if (!intro) return;
            intro = false;
            Log(0);
        }

        void Begin()
        {
            Active = true; intro = true;
            elapsed = 0f; OnboardingSeconds = 0f;
            customer = orders.Served; step = 0;
            guava = null; cutFruit = null; bowled = false;
        }

        void End()
        {
            if (orders.ShiftOver) Debug.Log($"[Tutorial] Level 1 done at {elapsed:F1} s");
            Active = false;
            Hide();
        }

        void Complete()
        {
            step++;
            if (customer > 0) return;       // timings are for customer 1 (onboarding)
            Log(step);
            if (step == Steps.Length) OnboardingSeconds = elapsed;
        }

        void Log(int s) => Debug.Log($"[Tutorial] step {s} done at {elapsed:F1} s");

        void NextCustomer()
        {
            customer = orders.Served; step = 0;
            guava = null; cutFruit = null; bowled = false;
        }

        void Show()
        {
            bool prompt = !intro && customer < 2 && step < Steps.Length;
            card.SetActive(intro || prompt);
            okButton.SetActive(intro);
            text.text = intro ? Intro : prompt ? Steps[step] : "";
            text.alignment = intro ? TextAlignmentOptions.Top : TextAlignmentOptions.Center;   // intro leaves room for OK below

            bool point = prompt && customer == 0;
            glow.gameObject.SetActive(point);
            arrow.gameObject.SetActive(point);
            if (!point) return;

            var b = BoundsOf(Target());
            glow.transform.position = b.center;
            glow.transform.localScale = b.size * 1.2f + Vector3.one * 0.03f;
            var c = glowColour; c.a *= 0.65f + 0.35f * Mathf.Sin(Time.time * 5f);
            block.SetColor(BaseColorId, c);
            glow.SetPropertyBlock(block);
            arrow.position = new Vector3(b.center.x, b.max.y + 0.05f + 0.015f * Mathf.Sin(Time.time * 4f), b.center.z);
        }

        Component Target()
        {
            switch (step)
            {
                case 0: return guavaBin.Current ? guavaBin.Current : guavaBin.spawnPoint;
                case 1: return board.fruitSocket;
                case 2: return knife;
                case 3: case 4: return board.Fruit ? board.Fruit : board.fruitSocket;
                case 5: return bowlStack.Current ? bowlStack.Current : bowlStack.spawnPoint;
                default: return tray;
            }
        }

        // World bounds of the target's enabled colliders (sockets: their trigger zone); a 8 cm box if it has none.
        static Bounds BoundsOf(Component c)
        {
            var b = new Bounds(c.transform.position, Vector3.one * 0.08f);
            c.GetComponentsInChildren(colliders);
            bool first = true;
            foreach (var col in colliders)
            {
                if (!col.enabled) continue;
                if (first) b = col.bounds; else b.Encapsulate(col.bounds);
                first = false;
            }
            return b;
        }

        void Hide()
        {
            card.SetActive(false);
            glow.gameObject.SetActive(false);
            arrow.gameObject.SetActive(false);
        }
    }
}
