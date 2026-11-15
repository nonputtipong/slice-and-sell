using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceAndSell.Tests
{
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
        Bag BagWith(int cuts)
        {
            var bag = new GameObject("Bag").AddComponent<Bag>();
            bag.transform.SetParent(orders.transform);
            if (cuts < 0) return bag;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Guava.prefab");
            var fruit = Object.Instantiate(prefab, orders.transform).GetComponent<Fruit>();
            fruit.GetComponent<Rigidbody>().isKinematic = true;
            for (int i = 0; i < cuts; i++) fruit.ApplyCut(false);
            bag.Fill(fruit);
            return bag;
        }

        [UnityTest]
        public IEnumerator CorrectOrderPays()
        {
            Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(BagWith(2)));
            Assert.AreEqual(20, orders.Money);
            Assert.IsNull(orders.Current);
            yield break;
        }

        [UnityTest]
        public IEnumerator WrongCutCountPaysBaseOnly()
        {
            Assert.AreEqual(DeliveryResult.Wrong, orders.Deliver(BagWith(1)));
            Assert.AreEqual(20, orders.Money);
            yield break;
        }

        [UnityTest]
        public IEnumerator EmptyBagIsRefusedAndCustomerWaits()
        {
            Assert.AreEqual(DeliveryResult.Empty, orders.Deliver(BagWith(-1)));
            Assert.AreEqual(0, orders.Money);
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
    }
}
