using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceAndSell.Tests
{
    // M1 greybox mode (no levels): fixed order list, one customer object. Cut bonuses go to Money as the
    // fruit is cut, so these tests measure what the delivery itself pays.
    public class OrderTests
    {
        OrderManager orders;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            orders = new GameObject("Orders").AddComponent<OrderManager>();
            orders.customer = new GameObject("Customer");
            orders.orders.Add(new Order { fruit = "Guava", cuts = 2, price = 20 });
            orders.orders.Add(new Order { fruit = "Guava", cuts = 2, price = 20 });
            yield return null;   // Start: first customer steps up
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(orders.customer);
            Object.Destroy(orders.gameObject);
        }

        // Bowl and fruit are children of the OrderManager object, so TearDown removes them too.
        internal static Bowl BowlWith(Transform parent, int cuts, bool skewer = false, bool chili = false)
        {
            var bowl = new GameObject("Bowl").AddComponent<Bowl>();
            bowl.transform.SetParent(parent);
            if (skewer) bowl.AddExtra(ExtraKind.Skewer);
            if (chili) bowl.AddExtra(ExtraKind.Chili);
            if (cuts < 0) return bowl;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Guava.prefab");
            var fruit = Object.Instantiate(prefab, parent).GetComponent<Fruit>();
            fruit.GetComponent<Rigidbody>().isKinematic = true;
            for (int i = 0; i < cuts; i++) fruit.ApplyCut(false);
            bowl.Fill(fruit);
            return bowl;
        }

        Bowl BowlWith(int cuts) => BowlWith(orders.transform, cuts);

        int Paid(Bowl bowl, out DeliveryResult result)
        {
            int before = orders.Money;
            result = orders.Deliver(bowl);
            return orders.Money - before;
        }

        [UnityTest]
        public IEnumerator CorrectOrderPays()
        {
            Assert.AreEqual(20, Paid(BowlWith(2), out var r));
            Assert.AreEqual(DeliveryResult.Correct, r);
            Assert.IsNull(orders.Current);
            yield break;
        }

        [UnityTest]
        public IEnumerator WrongCutCountPaysBaseOnly()
        {
            Assert.AreEqual(20, Paid(BowlWith(1), out var r));
            Assert.AreEqual(DeliveryResult.Wrong, r);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmptyBowlIsRefusedAndCustomerWaits()
        {
            Assert.AreEqual(0, Paid(BowlWith(-1), out var r));
            Assert.AreEqual(DeliveryResult.Empty, r);
            Assert.IsNotNull(orders.Current);
            yield break;
        }

        // Quest playtest bug: after an empty bowl the card stayed on "It's empty!" for good.
        [UnityTest]
        public IEnumerator CardShowsTheOrderAgainAfterAnEmptyBowl()
        {
            orders.orderCard = new GameObject("Card").AddComponent<TMPro.TextMeshPro>();
            orders.orderCard.transform.SetParent(orders.transform);
            var order = orders.Current;
            orders.Deliver(BowlWith(-1));
            Assert.AreEqual("It's empty!", orders.orderCard.text);
            yield return new WaitForSeconds(OrderManager.RefusedSeconds + 0.2f);
            Assert.AreEqual(order.CardText, orders.orderCard.text, "the card says what they want again");
            Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(BowlWith(2)), "and the same order can still be served");
        }

        [UnityTest]
        public IEnumerator BellCallsNextCustomerEarly()
        {
            orders.Deliver(BowlWith(2));
            Assert.IsNull(orders.Current);
            orders.NextCustomer();
            Assert.IsNotNull(orders.Current);
            Assert.IsFalse(orders.ShiftOver);
            orders.Deliver(BowlWith(2));
            Assert.IsTrue(orders.ShiftOver);
            yield break;
        }

        [UnityTest]
        public IEnumerator CleanCutsWhilePreparingEarnBonusAndCombo()
        {
            int before = orders.Money;
            BowlWith(2);                     // two clean cuts
            Assert.AreEqual(before + 4, orders.Money);
            Assert.AreEqual(2, orders.Combo);
            yield break;
        }
    }
}
