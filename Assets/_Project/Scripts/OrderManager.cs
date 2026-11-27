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
        public Transform queueSpot;         // where the next customer waits in a queue level
        public Transform cashBox;           // coins clink here (GDD > Spatial audio); the bowl's position if unset

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
        public Customer Queued => queued;
        public int Stars { get; private set; }
        public float TimeLeft { get; private set; }
        public float Patience01 { get; private set; } = 1f;
        public bool Running => Level ? running : true;   // a shift is in progress (or ended, waiting for the bell)
        public bool ShiftEnded => shiftEnded;
        public int ShiftId { get; private set; }          // +1 on every start / stop: a thief from an old shift leaves

        // Thief (ThiefSpawner): a takedown (pistol or knife) earns the reward; he costs the penalty if he gets away with the fruit.
        public const int ThiefReward = 30, ThiefPenalty = 10;

        readonly Scoring scoring = new Scoring();
        System.Random rng;
        int index = -1;
        bool waiting, arriving, shiftEnded, running;
        float patienceLeft;
        Customer present, queued;
        // What the chalkboard shows now: it is rebuilt only when one of these changes (Update calls it every frame).
        LevelDefinition boardLevel;
        int boardMoney = int.MinValue, boardTarget, boardCombo, boardSecond, boardServed, boardCount;

        // While paused the order card says so (the order comes back on resume), customers stand still (Customer),
        // knives don't cut (Knife) and the hand-off tray doesn't serve, so a pause really stops the game.
        public const string PausedCard = "Paused\n<size=60%>Resume in the menu</size>";
        string cardText = "";

        void OnEnable() { Fruit.AnyCut += OnCut; GamePause.Changed += OnPauseChanged; }
        void OnDisable() { Fruit.AnyCut -= OnCut; GamePause.Changed -= OnPauseChanged; }

        void OnPauseChanged(bool paused)
        {
            if (orderCard) orderCard.text = paused ? PausedCard : cardText;
        }

        void Start()
        {
            if (customerPrefabs != null && customerPrefabs.Length > 0 && !GetComponent<ThiefSpawner>())
                gameObject.AddComponent<ThiefSpawner>().orders = this;
            if (Level) { if (autoStart) StartLevel(levelIndex); }
            else { if (customer) customer.SetActive(false); NextCustomer(); }
        }

        public void StartLevel(int i)
        {
            CancelInvoke();
            if (present) present.Leave(CustomerMood.Neutral, customerExit.position);
            present = null;
            DismissQueue();
            levelIndex = i;
            running = true;
            GamePause.Set(false);
            ClearFilledBowls();
            ShiftId++;
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
            DismissQueue();
            waiting = arriving = shiftEnded = false;
            running = false;
            GamePause.Set(false);
            ClearFilledBowls();
            ShiftId++;
            Show("");
        }

        public void ThiefCaught(Vector3 at)
        {
            scoring.AddMoney(ThiefReward);
            GameEvents.RaisePaid(at, ThiefReward, 0, true);      // coins
            UpdateBoard();
        }

        public void ThiefEscaped(Vector3 at)
        {
            if (!running || shiftEnded) return;                 // after the shift nothing is lost
            scoring.AddMoney(-ThiefPenalty);
            GameEvents.RaiseRefused(at);                         // the "aw" sound
            UpdateBoard();
        }

        // A bowl filled in the last shift (or filled wrong) doesn't carry over: it goes, and the bowl spot makes a
        // fresh empty one. Empty bowls stay where they are.
        static void ClearFilledBowls()
        {
            foreach (var b in FindObjectsByType<Bowl>(FindObjectsSortMode.None))
                if (b.IsFilled) Destroy(b.gameObject);
        }

        // Bell, or automatically after a delivery. After the shift, the bell starts the next level (or a retry).
        public void NextCustomer()
        {
            CancelInvoke(nameof(NextCustomer));
            if (Level && !running) return;
            if (GamePause.Paused) { Invoke(nameof(NextCustomer), 0.25f); return; }     // the next one comes after the pause
            if (shiftEnded) { NextLevel(); return; }
            if (waiting || arriving) return;
            if (Level && Level.customers == 0) orders.Add(MakeOrder());     // continuous shift
            if (index + 1 >= orders.Count) return;
            index++;

            if (customerPrefabs != null && customerPrefabs.Length > 0)
            {
                arriving = true;
                // The one waiting in line steps up; otherwise someone walks in from the street.
                if (queued) { present = queued; queued = null; }
                else { present = Instantiate(customerPrefabs[index % customerPrefabs.Length], customerEntry.position, Quaternion.identity, transform); present.Orders = this; }
                present.Arrive(present.transform.position, counterSpot.position, OnArrived);
                if (Level && Level.queue && queueSpot && (Level.customers == 0 || index + 1 < orders.Count))
                {
                    queued = Instantiate(customerPrefabs[(index + 1) % customerPrefabs.Length], customerEntry.position, Quaternion.identity, transform);
                    queued.Orders = this;
                    queued.Arrive(customerEntry.position, queueSpot.position, null);
                }
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

        // A bowl thrown at a customer (Customer hitboxes): served only if that customer is the one waiting at the counter.
        public DeliveryResult DeliverTo(Customer customer, Bowl bowl) => customer && customer == present ? Deliver(bowl) : DeliveryResult.NoCustomer;

        public Customer Present => present;     // the customer at (or walking to) the counter; null when none (OrderBubble)

        public DeliveryResult Deliver(Bowl bowl)
        {
            if (!waiting || GamePause.Paused) return DeliveryResult.NoCustomer;
            if (!bowl.IsFilled)
            {
                Show("It's empty!");
                GameEvents.RaiseRefused(bowl.transform.position);
                Invoke(nameof(ShowOrder), RefusedSeconds);      // then the card says what they want again
                return DeliveryResult.Empty;
            }

            CancelInvoke(nameof(ShowOrder));
            var order = orders[index];
            bool correct = bowl.Contents == order.fruit && bowl.Cuts == order.cuts
                           && (!order.skewer || bowl.HasSkewer) && (!order.chili || bowl.HasChili);
            bool tipsOn = Level && Level.patienceSeconds > 0f;
            var (amount, tip) = scoring.OnServed(order.price, correct, tipsOn, Patience01);
            Served++;
            waiting = false;
            GameEvents.RaisePaid(cashBox ? cashBox.position : bowl.transform.position, amount, tip, correct);
            Dismiss(correct ? CustomerMood.Happy : CustomerMood.Angry);

            bool lastFixed = index + 1 >= orders.Count && (!Level || Level.customers > 0);
            if (lastFixed && Level) EndShift();
            else
            {
                Show(lastFixed ? "Shift over!" : correct ? $"Thanks!\n+฿{amount}" : "Hmm... not quite.");
                if (!lastFixed) Invoke(nameof(NextCustomer), NextDelay);
            }
            return correct ? DeliveryResult.Correct : DeliveryResult.Wrong;
        }

        // How long "It's empty!" stays on the card before the order shows again.
        public const float RefusedSeconds = 1.5f;

        // Only while that customer still waits: if they left (or the shift moved on) the card already says so.
        void ShowOrder()
        {
            if (waiting) Show(orders[index].CardText);
        }

        void WalkOut()
        {
            waiting = false;
            scoring.OnWalkedOut();
            GameEvents.RaiseWalkedOut(present ? present.transform.position : counterSpot ? counterSpot.position : transform.position);
            Dismiss(CustomerMood.Angry);
            Show("They left...");
            Invoke(nameof(NextCustomer), NextDelay);
        }

        void EndShift()
        {
            CancelInvoke(nameof(NextCustomer));
            if (waiting || arriving) Dismiss(CustomerMood.Neutral);
            DismissQueue();
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

        // Someone already in line steps up sooner: that is the rush of a queue level.
        float NextDelay => queued ? Mathf.Min(1f, autoNextDelay) : autoNextDelay;

        void DismissQueue()
        {
            if (queued) { queued.Leave(CustomerMood.Neutral, customerExit.position); queued = null; }
        }

        void Show(string card)
        {
            cardText = card;
            if (orderCard) orderCard.text = GamePause.Paused ? PausedCard : card;
            UpdateBoard();
        }

        void UpdateBoard()
        {
            if (!chalkboard) return;
            var level = Level;
            int second = (int)TimeLeft, target = level ? level.target : 0;
            if (level == boardLevel && Money == boardMoney && target == boardTarget && Combo == boardCombo && second == boardSecond
                && Served == boardServed && orders.Count == boardCount) return;      // nothing new: no string, no garbage
            boardLevel = level; boardMoney = Money; boardTarget = target; boardCombo = Combo; boardSecond = second;
            boardServed = Served; boardCount = orders.Count;
            // Same lines as ever; the money line leads (bigger, bold), combo in the menus' gold.
            if (!level) { chalkboard.text = $"<size=125%><b>฿{Money}</b></size>\nServed {Served}/{orders.Count}"; return; }
            string time = level.shiftSeconds > 0f ? $"\n{second / 60}:{second % 60:00}" : $"\nServed {Served}/{orders.Count}";
            chalkboard.text = $"<size=125%><b>฿{Money}</b> / ฿{level.target}</size>\n<color=#FFC73D>Combo x{Combo}</color>{time}";
        }
    }
}
