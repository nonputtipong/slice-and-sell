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

        // Bag and fruit are children of the OrderManager object, so TearDown removes them too.
        internal static Bag BagWith(Transform parent, int cuts, bool skewer = false, bool chili = false)
        {
            var bag = new GameObject("Bag").AddComponent<Bag>();
            bag.transform.SetParent(parent);
            if (skewer) bag.AddExtra(ExtraKind.Skewer);
            if (chili) bag.AddExtra(ExtraKind.Chili);
            if (cuts < 0) return bag;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Guava.prefab");
            var fruit = Object.Instantiate(prefab, parent).GetComponent<Fruit>();
            fruit.GetComponent<Rigidbody>().isKinematic = true;
            for (int i = 0; i < cuts; i++) fruit.ApplyCut(false);
            bag.Fill(fruit);
            return bag;
        }

        Bag BagWith(int cuts) => BagWith(orders.transform, cuts);

        int Paid(Bag bag, out DeliveryResult result)
        {
            int before = orders.Money;
            result = orders.Deliver(bag);
            return orders.Money - before;
        }

        [UnityTest]
        public IEnumerator CorrectOrderPays()
        {
            Assert.AreEqual(20, Paid(BagWith(2), out var r));
            Assert.AreEqual(DeliveryResult.Correct, r);
            Assert.IsNull(orders.Current);
            yield break;
        }

        [UnityTest]
        public IEnumerator WrongCutCountPaysBaseOnly()
        {
            Assert.AreEqual(20, Paid(BagWith(1), out var r));
            Assert.AreEqual(DeliveryResult.Wrong, r);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmptyBagIsRefusedAndCustomerWaits()
        {
            Assert.AreEqual(0, Paid(BagWith(-1), out var r));
            Assert.AreEqual(DeliveryResult.Empty, r);
            Assert.IsNotNull(orders.Current);
            yield break;
        }

        [UnityTest]
        public IEnumerator BellCallsNextCustomerEarly()
        {
            orders.Deliver(BagWith(2));
            Assert.IsNull(orders.Current);
            orders.NextCustomer();
            Assert.IsNotNull(orders.Current);
            Assert.IsFalse(orders.ShiftOver);
            orders.Deliver(BagWith(2));
            Assert.IsTrue(orders.ShiftOver);
            yield break;
        }

        [UnityTest]
        public IEnumerator CleanCutsWhilePreparingEarnBonusAndCombo()
        {
            int before = orders.Money;
            BagWith(2);                     // two clean cuts
            Assert.AreEqual(before + 4, orders.Money);
            Assert.AreEqual(2, orders.Combo);
            yield break;
        }
    }
}
