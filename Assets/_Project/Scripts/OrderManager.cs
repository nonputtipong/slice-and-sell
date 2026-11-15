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
    }

    public enum DeliveryResult { NoCustomer, Empty, Wrong, Correct }

    // Greybox order flow: one customer at a time, served from a fixed list (Level 1 = 3 guavas).
    // Tips, patience and combos come in Milestone 3.
    public class OrderManager : MonoBehaviour
    {
        public List<Order> orders = new List<Order>();
        public GameObject customer;
        public TMP_Text orderCard, chalkboard;
        public float autoNextDelay = 3f;

        public int Money { get; private set; }
        public int Served { get; private set; }
        public Order Current => waiting ? orders[index] : null;
        public bool ShiftOver => !waiting && index + 1 >= orders.Count;

        int index = -1;
        bool waiting;

        void Start() { customer.SetActive(false); NextCustomer(); }

        // Bell, or automatically after a delivery. Does nothing while a customer is waiting.
        public void NextCustomer()
        {
            CancelInvoke(nameof(NextCustomer));
            if (waiting || index + 1 >= orders.Count) return;
            index++;
            waiting = true;
            customer.SetActive(true);
            Show($"{orders[index].fruit}\n{orders[index].cuts} cuts");
        }

        public DeliveryResult Deliver(Bag bag)
        {
            if (!waiting) return DeliveryResult.NoCustomer;
            if (!bag.IsFilled) { Show("It's empty!"); return DeliveryResult.Empty; }

            var order = orders[index];
            bool correct = bag.Contents == order.fruit && bag.Cuts == order.cuts;
            Money += order.price;   // wrong order still pays base price (GDD > Failure cases); tips come later
            Served++;
            waiting = false;
            customer.SetActive(false);
            Show(ShiftOver ? "Shift over!" : correct ? "Thanks!" : "Hmm... not quite.");
            if (!ShiftOver) Invoke(nameof(NextCustomer), autoNextDelay);
            return correct ? DeliveryResult.Correct : DeliveryResult.Wrong;
        }

        void Show(string card)
        {
            if (orderCard) orderCard.text = card;
            if (chalkboard) chalkboard.text = $"THB {Money}\nServed {Served}/{orders.Count}";
        }
    }
}
