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

        // Playtest: the bowl showed an orange blob ("soup"). It now holds the fruit's own slices, laid in the bowl at their
        // real size (playtest 4: shrinking them to fit looked wrong); one wider than the bowl rests on the rim.
        [UnityTest]
        public IEnumerator BowlHoldsTheCutFruitsSlices()
        {
            var bowl = BowlWith(2);
            Assert.IsNotNull(bowl.Slices, "slices copied into the bowl");
            var pieces = bowl.Slices.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(3, pieces.Length, "a guava cut twice is three slices");
            foreach (var p in pieces)
            {
                Assert.IsNull(p.GetComponent<Collider>(), "slices are only a look");
                Assert.AreEqual(1f, p.transform.lossyScale.x, 1e-3f, $"{p.name} keeps its real size");
                // Below the rim, every corner of the slice's box is inside the bowl's sloping wall at that height.
                var b = p.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    var local = bowl.transform.InverseTransformPoint(p.transform.TransformPoint(c));
                    float h = local.y - Bowl.FootY;
                    Assert.Greater(h, 0.009f, $"{p.name} is above the inner floor");
                    float r = new Vector2(local.x, local.z).magnitude;
                    if (h < 0.06f) Assert.LessOrEqual(r, Bowl.InnerRadius(h) + 0.002f, $"{p.name} corner {k} pokes through the wall (r {r:F3} at h {h:F3})");
                }
            }
            yield break;
        }

        // Playtest: Pause did nothing you could see. Paused, the card says so, and nothing can be served.
        [UnityTest]
        public IEnumerator PauseShowsOnTheCardAndStopsServing()
        {
            orders.orderCard = new GameObject("Card").AddComponent<TMPro.TextMeshPro>();
            orders.orderCard.transform.SetParent(orders.transform);
            var order = orders.Current;
            try
            {
                GamePause.Set(true);
                Assert.AreEqual(OrderManager.PausedCard, orders.orderCard.text);
                Assert.AreEqual(DeliveryResult.NoCustomer, orders.Deliver(BowlWith(2)), "the tray doesn't serve while paused");
                Assert.AreSame(order, orders.Current, "the customer still waits");
            }
            finally { GamePause.Set(false); }
            Assert.AreEqual(order.CardText, orders.orderCard.text, "the order is back on resume");
            Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(BowlWith(2)));
            yield break;
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
