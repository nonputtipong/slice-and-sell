using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceAndSell.Tests
{
    // M3 level flow on OrderManager with in-memory levels and a plain customer object (no walking).
    public class LevelFlowTests
    {
        OrderManager orders;
        LevelDefinition l1, l2;

        static LevelDefinition Level(string title, float shift, int customers, int target, float patience, bool skewer = false, float chili = 0f)
        {
            var l = ScriptableObject.CreateInstance<LevelDefinition>();
            l.title = title; l.shiftSeconds = shift; l.customers = customers; l.target = target;
            l.patienceSeconds = patience; l.skewer = skewer; l.chiliChance = chili; l.fruits = new[] { "Guava" };
            return l;
        }

        IEnumerator Begin(params LevelDefinition[] levels)
        {
            orders = new GameObject("Orders").AddComponent<OrderManager>();
            orders.customer = new GameObject("Customer");
            orders.menu = new[] { new FruitMenuItem { name = "Guava", cuts = 2, price = 20 } };
            orders.levels = levels;
            orders.autoNextDelay = 0.05f;
            yield return null;
        }

        [SetUp] public void Make() { l1 = Level("Opening Day", 0f, 3, 60, 0f); l2 = Level("Lunch Rush", 180f, 0, 400, 45f, skewer: true); }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(orders.customer); Object.Destroy(orders.gameObject);
            Object.Destroy(l1); Object.Destroy(l2);
        }

        Bag Bag(int cuts, bool skewer = false, bool chili = false) => OrderTests.BagWith(orders.transform, cuts, skewer, chili);

        IEnumerator UntilCustomer(float timeout = 2f)
        {
            for (float t = 0; orders.Current == null && t < timeout; t += Time.deltaTime) yield return null;
            Assert.IsNotNull(orders.Current, "a customer is waiting");
        }

        [UnityTest]
        public IEnumerator Level1EndsAfterThreeCustomersAndBellStartsLevel2()
        {
            yield return Begin(l1, l2);
            for (int i = 0; i < 3; i++)
            {
                yield return UntilCustomer();
                Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(Bag(2)));
            }
            Assert.IsTrue(orders.ShiftOver);
            Assert.IsTrue(orders.ShiftWon, $"won with THB {orders.Money}");
            Assert.GreaterOrEqual(orders.Stars, 1);
            orders.NextCustomer();     // the bell
            Assert.AreEqual(1, orders.levelIndex);
            Assert.AreEqual(0, orders.Money, "a new shift starts from zero");
            Assert.IsFalse(orders.ShiftOver);
        }

        [UnityTest]
        public IEnumerator Level1HasNoTimerAndNoTips()
        {
            yield return Begin(l1, l2);
            yield return UntilCustomer();
            yield return new WaitForSeconds(0.3f);
            var bag = Bag(2);               // cut first: its clean-cut bonus is not what this test measures
            int before = orders.Money;
            orders.Deliver(bag);
            Assert.AreEqual(20, orders.Money - before, "base price only, no tip while patience is paused");
            Assert.IsFalse(orders.ShiftOver);
        }

        [UnityTest]
        public IEnumerator TimedShiftEndsAndBellRetriesWhenTargetMissed()
        {
            var quick = Level("Quick", 0.3f, 0, 400, 45f);
            yield return Begin(quick);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(orders.ShiftOver);
            Assert.IsFalse(orders.ShiftWon);
            Assert.IsNull(orders.Current, "the waiting customer leaves when the shift ends");
            orders.NextCustomer();
            Assert.AreEqual(0, orders.levelIndex, "retry the same level");
            Assert.IsFalse(orders.ShiftOver);
            Object.Destroy(quick);
        }

        [UnityTest]
        public IEnumerator CustomerWalksOutWhenPatienceRunsOutThenNextArrives()
        {
            var impatient = Level("Impatient", 0f, 0, 400, 0.2f);
            int walkouts = 0;
            void OnWalkedOut(Vector3 _) => walkouts++;
            GameEvents.WalkedOut += OnWalkedOut;
            yield return Begin(impatient);
            yield return UntilCustomer();
            var first = orders.Current;
            yield return new WaitForSeconds(0.4f);
            GameEvents.WalkedOut -= OnWalkedOut;
            Assert.GreaterOrEqual(walkouts, 1, "walked out");
            Assert.AreEqual(0, orders.Served);
            yield return UntilCustomer();
            Assert.AreNotSame(first, orders.Current, "the next customer has a new order");
            Object.Destroy(impatient);
        }

        [UnityTest]
        public IEnumerator FastServiceEarnsGreenTip()
        {
            yield return Begin(Level("Tips", 0f, 0, 400, 30f));
            yield return UntilCustomer();
            var bag = Bag(2);
            int before = orders.Money;
            orders.Deliver(bag);
            Assert.AreEqual(20 + 5, orders.Money - before);
        }

        [UnityTest]
        public IEnumerator SkewerIsRequiredWhenTheLevelAsksForIt()
        {
            yield return Begin(l2);
            yield return UntilCustomer();
            Assert.IsTrue(orders.Current.skewer);
            Assert.AreEqual(DeliveryResult.Wrong, orders.Deliver(Bag(2)), "no skewer");
            yield return UntilCustomer();
            Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(Bag(2, skewer: true)));
        }

        [UnityTest]
        public IEnumerator ContinuousShiftKeepsSendingCustomers()
        {
            yield return Begin(Level("Rush", 0f, 0, 400, 30f));
            for (int i = 0; i < 5; i++) { yield return UntilCustomer(); orders.Deliver(Bag(2)); }
            Assert.AreEqual(5, orders.Served);
            Assert.IsFalse(orders.ShiftOver);
        }

        [UnityTest]
        public IEnumerator SameSeedGivesSameOrders()
        {
            var a = Level("Seeded", 0f, 6, 400, 0f, chili: 0.5f); a.fruits = new[] { "Guava", "Mango", "Pineapple" };
            yield return Begin(a);
            var first = string.Join(",", orders.orders.ConvertAll(o => o.fruit + o.chili));
            orders.StartLevel(0);
            Assert.AreEqual(first, string.Join(",", orders.orders.ConvertAll(o => o.fruit + o.chili)));
            Object.Destroy(a);
        }
    }
}
