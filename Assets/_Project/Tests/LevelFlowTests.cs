using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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

        Bowl Bowl(int cuts, bool skewer = false, bool chili = false) => OrderTests.BowlWith(orders.transform, cuts, skewer, chili);

        IEnumerator UntilCustomer(float timeout = 2f)
        {
            for (float t = 0; orders.Current == null && t < timeout; t += Time.deltaTime) yield return null;
            Assert.IsNotNull(orders.Current, "a customer is waiting");
        }

        // The chalkboard is refreshed every frame but only rebuilt when what it shows changes: no garbage per frame.
        [UnityTest]
        public IEnumerator ChalkboardRebuildsOnlyWhenItChanges()
        {
            yield return Begin(l1, l2);
            var board = new GameObject("Chalkboard").AddComponent<TMPro.TextMeshPro>();
            orders.chalkboard = board;
            orders.StartLevel(1);       // timed: the clock is on the board
            yield return UntilCustomer();
            var update = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), orders,
                typeof(OrderManager).GetMethod("UpdateBoard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            update();
            string shown = board.text;
            StringAssert.Contains("/ ฿400", shown);
            StringAssert.Contains("Combo x0", shown);
            var noGarbage = UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not);   // Unity's GC-alloc constraint
            Assert.That(() => update(), noGarbage, "same second, same money: nothing rebuilt");
            yield return new WaitForSeconds(1.1f);
            Assert.AreNotEqual(shown, board.text, "the clock on the board still moves on");
            Object.Destroy(board.gameObject);
        }

        [UnityTest]
        public IEnumerator Level1EndsAfterThreeCustomersAndBellStartsLevel2()
        {
            yield return Begin(l1, l2);
            for (int i = 0; i < 3; i++)
            {
                yield return UntilCustomer();
                Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(Bowl(2)));
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
            var bowl = Bowl(2);               // cut first: its clean-cut bonus is not what this test measures
            int before = orders.Money;
            orders.Deliver(bowl);
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
            var bowl = Bowl(2);
            int before = orders.Money;
            orders.Deliver(bowl);
            Assert.AreEqual(20 + 5, orders.Money - before);
        }

        [UnityTest]
        public IEnumerator SkewerIsRequiredWhenTheLevelAsksForIt()
        {
            yield return Begin(l2);
            yield return UntilCustomer();
            Assert.IsTrue(orders.Current.skewer);
            Assert.AreEqual(DeliveryResult.Wrong, orders.Deliver(Bowl(2)), "no skewer");
            yield return UntilCustomer();
            Assert.AreEqual(DeliveryResult.Correct, orders.Deliver(Bowl(2, skewer: true)));
        }

        [UnityTest]
        public IEnumerator ContinuousShiftKeepsSendingCustomers()
        {
            yield return Begin(Level("Rush", 0f, 0, 400, 30f));
            for (int i = 0; i < 5; i++) { yield return UntilCustomer(); orders.Deliver(Bowl(2)); }
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

        // Level 3: a second customer waits at the queue spot (no order yet) and steps up when the counter frees.
        [UnityTest]
        public IEnumerator QueueLevelKeepsASecondCustomerWaitingInLine()
        {
            var q = Level("Night Market", 0f, 0, 700, 30f); q.queue = true;
            orders = new GameObject("Orders").AddComponent<OrderManager>();
            orders.customer = new GameObject("Customer");
            orders.menu = new[] { new FruitMenuItem { name = "Guava", cuts = 2, price = 20 } };
            orders.levels = new[] { q };
            orders.autoNextDelay = 0.05f;
            orders.customerPrefabs = new[] { "Salaryman", "Student" }.Select(n =>
                AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Customers/{n}.prefab").GetComponent<Customer>()).ToArray();
            orders.customerEntry = new GameObject("Entry").transform; orders.customerEntry.position = new Vector3(-2.5f, 0, 1.35f);
            orders.counterSpot = new GameObject("Counter").transform; orders.counterSpot.position = new Vector3(0, 0, 1.35f);
            orders.customerExit = new GameObject("Exit").transform; orders.customerExit.position = new Vector3(2.5f, 0, 1.35f);
            orders.queueSpot = new GameObject("Queue").transform; orders.queueSpot.position = new Vector3(-0.9f, 0, 1.8f);
            yield return null;

            yield return UntilCustomer(8f);
            var second = orders.Queued;
            Assert.IsNotNull(second, "someone is waiting in line");
            Assert.AreEqual(1, orders.orders.Count, "only the customer at the counter has ordered");
            for (float t = 0; (second.transform.position - orders.queueSpot.position).sqrMagnitude > 1e-4f && t < 8f; t += Time.deltaTime) yield return null;
            Assert.Less(Vector3.Distance(second.transform.position, orders.queueSpot.position), 0.01f, "they stand at the queue spot");

            orders.Deliver(Bowl(2));
            yield return UntilCustomer(8f);
            Assert.Less(Vector3.Distance(second.transform.position, orders.counterSpot.position), 0.01f, "the one in line stepped up to the counter");
            Assert.IsNotNull(orders.Queued, "and a new one joined the line");
            Assert.AreNotSame(second, orders.Queued);

            orders.StopShift();
            Assert.IsNull(orders.Queued, "exit to menu sends the line away");
            foreach (var t in new[] { orders.customerEntry, orders.counterSpot, orders.customerExit, orders.queueSpot }) Object.Destroy(t.gameObject);
            Object.Destroy(q);
        }
    }
}
