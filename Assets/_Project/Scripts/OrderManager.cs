using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SliceAndSell
{
    [Serializable]
    public class Order
    {
        public string fruit = "Guava";
        public int cuts = 2;
        public int price = 20;
        public bool skewer;
        public bool chili;

        public string CardText => $"{fruit}\n{cuts} cuts" + (skewer || chili ? "\n+ " + (skewer && chili ? "skewer, chili" : skewer ? "skewer" : "chili") : "");
    }

    public enum DeliveryResult { NoCustomer, Empty, Wrong, Correct }

    // Runs a shift: one customer at a time, orders, patience, scoring, the shift timer and the end of the shift.
    // With no levels assigned it runs the M1 greybox: the fixed `orders` list and `customer` toggled on and off.
    public class OrderManager : MonoBehaviour
    {
        [Header("Levels (empty = M1 greybox mode)")]
        public LevelDefinition[] levels;
        public int levelIndex;
        public bool autoStart = true;       // false when a main menu starts the shift
        public FruitMenuItem[] menu;
        public Customer[] customerPrefabs;
        public Transform customerEntry, counterSpot, customerExit;
        public Transform cashBox;           // coins clink here (GDD > Spatial audio); the bag's position if unset

        [Header("M1 greybox mode")]
        public List<Order> orders = new List<Order>();
        public GameObject customer;

        [Header("Displays")]
        public TMP_Text orderCard, chalkboard;
        public float autoNextDelay = 3f;

        public int Money => scoring.Money;
        public int Combo => scoring.Combo;
        public int Served { get; private set; }
        public Order Current => waiting ? orders[index] : null;
        public LevelDefinition Level => levels != null && levels.Length > 0 ? levels[levelIndex] : null;
        public bool ShiftOver => Level ? shiftEnded : !waiting && index + 1 >= orders.Count;
        public bool ShiftWon { get; private set; }
        public int Stars { get; private set; }
        public float TimeLeft { get; private set; }
        public float Patience01 { get; private set; } = 1f;
        public bool Running => Level ? running : true;   // a shift is in progress (or ended, waiting for the bell)

        readonly Scoring scoring = new Scoring();
        System.Random rng;
        int index = -1;
        bool waiting, arriving, shiftEnded, running;
        float patienceLeft;
        Customer present;

        void OnEnable() => Fruit.AnyCut += OnCut;
        void OnDisable() => Fruit.AnyCut -= OnCut;

        void Start()
        {
            if (Level) { if (autoStart) StartLevel(levelIndex); }
            else { if (customer) customer.SetActive(false); NextCustomer(); }
        }

        public void StartLevel(int i)
        {
            CancelInvoke();
            if (present) present.Leave(CustomerMood.Neutral, customerExit.position);
            present = null;
            levelIndex = i;
            running = true;
            GamePause.Set(false);
            var level = Level;
            scoring.Reset();
            Served = 0; index = -1; waiting = arriving = shiftEnded = false; ShiftWon = false; Stars = 0;
            TimeLeft = level.shiftSeconds;
            rng = new System.Random(level.seed);
            orders.Clear();
            for (int k = 0; k < level.customers; k++) orders.Add(MakeOrder());
            NextCustomer();
        }

        Order MakeOrder()
        {
            var level = Level;
            string name = level.fruits[rng.Next(level.fruits.Length)];
            var item = Array.Find(menu, m => m.name == name) ?? new FruitMenuItem { name = name };
            return new Order { fruit = item.name, cuts = item.cuts, price = item.price, skewer = level.skewer, chili = rng.NextDouble() < level.chiliChance };
        }

        // Results board buttons.
        public void Retry() => StartLevel(levelIndex);
        public void NextLevel() => StartLevel(ShiftWon && levelIndex + 1 < levels.Length ? levelIndex + 1 : levelIndex);

        // Exit to the main menu: no shift, nobody at the counter.
        public void StopShift()
        {
            CancelInvoke();
            Dismiss(CustomerMood.Neutral);
            waiting = arriving = shiftEnded = false;
            running = false;
            GamePause.Set(false);
            Show("");
        }

        // Bell, or automatically after a delivery. After the shift, the bell starts the next level (or a retry).
        public void NextCustomer()
        {
            CancelInvoke(nameof(NextCustomer));
            if (Level && !running) return;
            if (shiftEnded) { NextLevel(); return; }
            if (waiting || arriving) return;
            if (Level && Level.customers == 0) orders.Add(MakeOrder());     // continuous shift
            if (index + 1 >= orders.Count) return;
            index++;

            if (customerPrefabs != null && customerPrefabs.Length > 0)
            {
                arriving = true;
                present = Instantiate(customerPrefabs[index % customerPrefabs.Length], customerEntry.position, Quaternion.identity, transform);
                present.Arrive(customerEntry.position, counterSpot.position, OnArrived);
                Show("");
            }
            else { if (customer) customer.SetActive(true); OnArrived(); }
        }

        void OnArrived()
        {
            arriving = false;
            waiting = true;
            patienceLeft = Level ? Level.patienceSeconds : 0f;
            Patience01 = 1f;
            Show(orders[index].CardText);
        }

        void Update()
        {
            var level = Level;
            if (!level || !running || shiftEnded || GamePause.Paused) return;
            if (level.shiftSeconds > 0f)
            {
                TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                if (TimeLeft <= 0f) { EndShift(); return; }
            }
            if (waiting && level.patienceSeconds > 0f)
            {
                patienceLeft -= Time.deltaTime;
                Patience01 = Mathf.Clamp01(patienceLeft / level.patienceSeconds);
                if (present) present.SetPatience(Patience01, true);
                if (patienceLeft <= 0f) WalkOut();
            }
            UpdateBoard();
        }

        void OnCut(Fruit fruit, CutInfo cut)
        {
            scoring.OnCut(cut);
            GameEvents.RaiseCut(fruit, cut, scoring.Combo);
            UpdateBoard();
        }

        public DeliveryResult Deliver(Bag bag)
        {
            if (!waiting) return DeliveryResult.NoCustomer;
            if (!bag.IsFilled) { Show("It's empty!"); GameEvents.RaiseRefused(bag.transform.position); return DeliveryResult.Empty; }

            var order = orders[index];
            bool correct = bag.Contents == order.fruit && bag.Cuts == order.cuts
                           && (!order.skewer || bag.HasSkewer) && (!order.chili || bag.HasChili);
            bool tipsOn = Level && Level.patienceSeconds > 0f;
            var (amount, tip) = scoring.OnServed(order.price, correct, tipsOn, Patience01);
            Served++;
            waiting = false;
            GameEvents.RaisePaid(cashBox ? cashBox.position : bag.transform.position, amount, tip, correct);
            Dismiss(correct ? CustomerMood.Happy : CustomerMood.Angry);

            bool lastFixed = index + 1 >= orders.Count && (!Level || Level.customers > 0);
            if (lastFixed && Level) EndShift();
            else
            {
                Show(lastFixed ? "Shift over!" : correct ? $"Thanks!\n+฿{amount}" : "Hmm... not quite.");
                if (!lastFixed) Invoke(nameof(NextCustomer), autoNextDelay);
            }
            return correct ? DeliveryResult.Correct : DeliveryResult.Wrong;
        }

        void WalkOut()
        {
            waiting = false;
            scoring.OnWalkedOut();
            GameEvents.RaiseWalkedOut(present ? present.transform.position : counterSpot ? counterSpot.position : transform.position);
            Dismiss(CustomerMood.Angry);
            Show("They left...");
            Invoke(nameof(NextCustomer), autoNextDelay);
        }

        void EndShift()
        {
            CancelInvoke(nameof(NextCustomer));
            if (waiting || arriving) Dismiss(CustomerMood.Neutral);
            waiting = arriving = false;
            shiftEnded = true;
            ShiftWon = Money >= Level.target;
            Stars = Scoring.Stars(Money, Level.target);
            GameEvents.RaiseShiftEnded(ShiftWon, Stars);
            Show((ShiftWon ? "Shift won!" : "Shift over") + $"\n฿{Money} / ฿{Level.target}  " + new string('*', Stars) +
                 (ShiftWon ? "\nRing the bell: next" : "\nRing the bell: retry"));
        }

        void Dismiss(CustomerMood mood)
        {
            if (present) { present.SetPatience(0f, false); present.Leave(mood, customerExit.position); present = null; }
            else if (customer) customer.SetActive(false);
        }

        void Show(string card)
        {
            if (orderCard) orderCard.text = card;
            UpdateBoard();
        }

        void UpdateBoard()
        {
            if (!chalkboard) return;
            var level = Level;
            if (!level) { chalkboard.text = $"฿{Money}\nServed {Served}/{orders.Count}"; return; }
            string time = level.shiftSeconds > 0f ? $"\n{(int)TimeLeft / 60}:{(int)TimeLeft % 60:00}" : $"\nServed {Served}/{orders.Count}";
            chalkboard.text = $"฿{Money} / ฿{level.target}\nCombo x{Combo}{time}";
        }
    }
}
