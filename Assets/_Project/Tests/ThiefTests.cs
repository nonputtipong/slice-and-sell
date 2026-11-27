using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceAndSell.Tests
{
    // Playtest 3-5: the fruit thief, the pistol and knife that stop him, and thrown things hitting people. No scene: an OrderManager with one level and the
    // real customer prefab, a real knife and guava thrown at them.
    public class ThiefTests
    {
        OrderManager orders;
        LevelDefinition level;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.shiftSeconds = 0f; level.customers = 0; level.patienceSeconds = 0f; level.target = 100; level.fruits = new[] { "Guava" };
            orders = new GameObject("Orders").AddComponent<OrderManager>();
            orders.levels = new[] { level }; orders.levelIndex = 0; orders.autoStart = false;
            orders.menu = new[] { new FruitMenuItem() };
            orders.customerPrefabs = new[] { Prefab("Customers/Salaryman").GetComponent<Customer>() };
            foreach (var (n, p) in new[] { ("Entry", new Vector3(5, 0, 20)), ("Counter", new Vector3(5, 0, 19)), ("Exit", new Vector3(5, 0, 21)) })
            {
                var t = new GameObject(n).transform; t.SetParent(orders.transform); t.position = p;
                if (n == "Entry") orders.customerEntry = t; else if (n == "Counter") orders.counterSpot = t; else orders.customerExit = t;
            }
            yield return null;
            orders.StartLevel(0);
        }

        [TearDown]
        public void TearDown()
        {
            GamePause.Set(false);
            Object.Destroy(orders.gameObject); Object.Destroy(level);
            foreach (var k in Object.FindObjectsByType<Knife>(FindObjectsSortMode.None)) Object.Destroy(k.gameObject);
            foreach (var f in Object.FindObjectsByType<Fruit>(FindObjectsSortMode.None)) Object.Destroy(f.gameObject);
            foreach (var p in Object.FindObjectsByType<Pistol>(FindObjectsSortMode.None)) Object.Destroy(p.gameObject);
            foreach (var b in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None)) Object.Destroy(b.gameObject);
        }

        static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/{name}.prefab");

        Thief StandingThief(bool tough = false)
        {
            var thief = orders.GetComponent<ThiefSpawner>().Spawn(Thief.Escape.LeftStreet, tough);
            thief.Body.SetSpeedScale(0f);           // stands where he is, so the throw is deterministic
            return thief;
        }

        static Vector3 HeadOf(Customer c) => c.GetComponentsInChildren<Hitbox>().First(h => h.IsHead).transform.position;

        // Thrown from 1 m in front of `target`, at 6 m/s.
        static Rigidbody Throw(string prefab, Vector3 target)
        {
            var go = Object.Instantiate(Prefab(prefab), target + new Vector3(0f, 0f, -1f), Quaternion.identity);
            var rack = go.GetComponent<ReturnToRack>(); if (rack) Object.DestroyImmediate(rack);   // no rack here
            var rb = go.GetComponent<Rigidbody>();
            rb.isKinematic = false; rb.useGravity = false;
            rb.linearVelocity = Vector3.forward * 6f;
            return rb;
        }

        static IEnumerator Steps(float seconds = 0.4f) { for (float t = 0; t < seconds; t += Time.fixedDeltaTime) yield return new WaitForFixedUpdate(); }

        // ---------- pistol ----------

        // A pistol held still at `at`, its barrel turned `offDegrees` to the right of the line to `target`.
        static Pistol PistolAt(Vector3 at, Vector3 target, float offDegrees = 0f)
        {
            var go = Object.Instantiate(Prefab("Pistol"), at, Quaternion.Euler(0f, offDegrees, 0f) * Quaternion.LookRotation(target - at));
            var rack = go.GetComponent<ReturnToRack>(); if (rack) Object.DestroyImmediate(rack);
            go.GetComponent<Rigidbody>().isKinematic = true;     // like a held one
            return go.GetComponent<Pistol>();
        }

        // Playtest 5: no magazine to load; it fires straight away, with a short cooldown between shots.
        [UnityTest]
        public IEnumerator PistolFiresWithoutAMagazine()
        {
            var p = PistolAt(new Vector3(20, 1.2f, 0), new Vector3(20, 1.2f, 10));
            yield return null;
            Assert.IsTrue(p.Fire(), "it just fires");
            Assert.AreEqual(Pistol.ShotAmplitude, p.LastHaptic, 1e-4f, "a kick on every shot");
            Assert.IsFalse(p.Fire(), "not again within the cooldown");
            yield return new WaitForSeconds(Pistol.Cooldown + 0.05f);
            Assert.IsTrue(p.Fire(), "and again after it");
            Assert.AreEqual(2, p.Shots);
            Assert.IsNull(p.transform.Find("Magazine socket"), "no magazine socket left on the gun");
            Object.Destroy(p.gameObject);
        }

        // Hands: keeping a thief in the aim cone for AimSeconds fires once, then it re-arms.
        [UnityTest]
        public IEnumerator HandAimAtTheThiefFires()
        {
            yield return null;
            var thief = StandingThief(tough: true);
            yield return null; yield return null;
            var chest = Bullet.AimPoint(thief);
            var p = PistolAt(chest + new Vector3(0f, 0f, -4f), chest, offDegrees: 10f);
            yield return null;
            const float dt = 0.125f;                        // sums to AimSeconds exactly
            for (float t = dt; t < Pistol.AimSeconds - 0.01f; t += dt) Assert.IsFalse(p.AimToShoot(dt), "not before the dwell");
            Assert.IsTrue(p.AimToShoot(dt), "held on him for the dwell: it fires");
            Assert.IsFalse(p.AimToShoot(dt), "then it re-arms first");
            var away = PistolAt(new Vector3(20, 1.2f, 0), new Vector3(20, 1.2f, 10));
            yield return null;
            for (int i = 0; i < 40; i++) Assert.IsFalse(away.AimToShoot(dt), "no thief in the cone: nothing");
            Object.Destroy(p.gameObject); Object.Destroy(away.gameObject);
        }

        [Test]
        public void IndexBendIsMeasuredAtTheKnuckles()
        {
            Assert.AreEqual(0f, Pistol.IndexBend(Vector3.zero, Vector3.forward, Vector3.forward * 2, Vector3.forward * 3), 1e-3f, "straight");
            var bent = Pistol.IndexBend(Vector3.zero, Vector3.forward, Vector3.forward + Vector3.down * 0.7f + Vector3.forward * 0.7f, Vector3.forward + Vector3.down * 1.4f);
            Assert.Greater(bent, Pistol.CurlFireDegrees, "a curled index fires");
        }

        [UnityTest]
        public IEnumerator ShotTracksTheThiefAndKnocksHimDown()
        {
            yield return null;
            var thief = StandingThief();
            yield return null; yield return null;
            var chest = Bullet.AimPoint(thief);
            var p = PistolAt(chest + new Vector3(0f, 0f, -4f), chest, offDegrees: 15f);     // a sloppy aim, inside the cone
            yield return null;
            int before = orders.Money;
            Assert.AreSame(thief, p.FindTarget(), "aim assist picks him");
            Assert.IsTrue(p.Fire());
            yield return Steps(0.6f);
            Assert.AreEqual(Thief.Phase.Caught, thief.State, "the round tracked him");
            AssertKnockedOut(thief);
            Assert.AreEqual(before + OrderManager.ThiefReward, orders.Money, "the reward, once");
            Assert.AreEqual(1, p.Takedowns);
            Assert.AreEqual(Pistol.TakedownAmplitude, p.LastHaptic, 1e-4f, "the hard buzz when he goes down");
            Assert.Greater(Pistol.TakedownAmplitude, Pistol.ShotAmplitude);
            yield return new WaitForSeconds(Customer.KnockOutSeconds + Customer.VanishSeconds);
            Assert.IsTrue(thief == null, "then he is gone");
            Assert.AreEqual(before + OrderManager.ThiefReward, orders.Money, "paid once");
            Object.Destroy(p.gameObject);
        }

        // Playtest 5 and review (a 13+ game): a cartoon knock-out, not a ragdoll. He tips over, stars circle, "KO!".
        static void AssertKnockedOut(Thief thief)
        {
            Assert.IsTrue(thief.Body.Dead, "he goes down");
            Assert.IsFalse(thief.Body.anim.enabled, "the clip stops");
            Assert.AreEqual(0, thief.GetComponentsInChildren<Rigidbody>().Count(r => !r.isKinematic), "no ragdoll");
            Assert.AreEqual(3, thief.Body.StarCount, "three stars circle over his head");
            Assert.AreEqual("KO!", thief.Label);
            Assert.Greater(Vector3.Angle(thief.transform.up, Vector3.up), 45f, "tipped over");
            Assert.IsTrue(thief.GetComponentsInChildren<Hitbox>().All(h => !h.GetComponent<Collider>().enabled), "no more hits");
        }

        // Playtest 4: a tough thief ("THIEF!!") needs two shots; the first staggers him (a medium buzz), the second puts
        // him down (the hard buzz) and pays once.
        [UnityTest]
        public IEnumerator ToughThiefTakesTwoShots()
        {
            yield return null;
            var thief = StandingThief(tough: true);
            yield return null; yield return null;
            Assert.IsTrue(thief.Tough); Assert.AreEqual(2, thief.Health);
            var chest = Bullet.AimPoint(thief);
            var p = PistolAt(chest + new Vector3(0f, 0f, -4f), chest);
            yield return null;
            int before = orders.Money;
            Assert.IsTrue(p.Fire());
            yield return Steps(0.5f);
            Assert.IsFalse(thief.Body.Dead, "one shot only staggers him");
            Assert.AreEqual(1, thief.Health);
            Assert.AreEqual(Pistol.WoundAmplitude, p.LastHaptic, 1e-4f, "a medium buzz for the hit");
            Assert.AreEqual(before, orders.Money, "no reward yet");
            Assert.IsTrue(p.Fire());
            yield return Steps(0.5f);
            Assert.IsTrue(thief.Body.Dead, "the second shot puts him down");
            Assert.AreEqual(Pistol.TakedownAmplitude, p.LastHaptic, 1e-4f);
            Assert.AreEqual(before + OrderManager.ThiefReward, orders.Money, "paid once");
            Assert.AreEqual(2, p.Hits); Assert.AreEqual(1, p.Takedowns);
            Object.Destroy(p.gameObject);
        }

        [Test]
        public void ThievesHaveThreeEscapeRoutes()
        {
            var ends = new[] { Thief.Escape.LeftStreet, Thief.Escape.CrossRight, Thief.Escape.Zigzag }.Select(e => Thief.RouteOf(e)).ToArray();
            Assert.AreEqual(3, ends.Select(r => string.Join(";", r)).Distinct().Count(), "three different routes");
            Assert.IsTrue(ends.All(r => r[r.Length - 1].z > 10f), "all end up the street");
            Assert.Greater(Thief.RouteOf(Thief.Escape.CrossRight)[1].x, 1f, "one crosses to the right side");
        }

        // Playtest 4: placing the bowl on the tray was fiddly; tossing it to the customer at the counter serves it.
        [UnityTest]
        public IEnumerator BowlTossedAtTheWaitingCustomerIsServed()
        {
            for (float t = 0; orders.Current == null && t < 3f; t += Time.deltaTime) yield return null;
            Assert.IsNotNull(orders.Current, "a customer is waiting at the counter");
            var customer = orders.GetComponentsInChildren<Customer>().First(c => !c.GetComponent<Thief>());
            var bowl = Object.Instantiate(Prefab("Bowl"), customer.transform.position + new Vector3(0f, 1.1f, -1f), Quaternion.identity).GetComponent<Bowl>();
            var fruit = Object.Instantiate(Prefab("Guava")).GetComponent<Fruit>();
            fruit.GetComponent<Rigidbody>().isKinematic = true;
            fruit.ApplyCut(false); fruit.ApplyCut(false);
            bowl.Fill(fruit); Object.Destroy(fruit.gameObject);
            var rack = bowl.GetComponent<DespawnWhenDropped>(); if (rack) Object.Destroy(rack);
            var rb = bowl.GetComponent<Rigidbody>(); rb.useGravity = false; rb.linearVelocity = Vector3.forward * 2f;   // a toss
            int served = orders.Served;
            yield return Steps(1f);
            Assert.AreEqual(served + 1, orders.Served, "the tossed bowl was handed over");
            Assert.IsTrue(bowl == null, "and used up, like on the tray");
        }

        [UnityTest]
        public IEnumerator ShotWellOffTheThiefMisses()
        {
            yield return null;
            var thief = StandingThief();
            yield return null; yield return null;
            var chest = Bullet.AimPoint(thief);
            var p = PistolAt(chest + new Vector3(0f, 0f, -4f), chest, offDegrees: 60f);
            yield return null;
            Assert.IsNull(p.FindTarget(), "outside the aim cone");
            Assert.IsTrue(p.Fire());
            yield return Steps(1f);
            Assert.IsFalse(thief.Body.Dead, "missed");
            Object.Destroy(p.gameObject);
        }

        // Playtest 5: a thrown knife takes the thief down too (a tough one needs two), and pays once.
        [UnityTest]
        public IEnumerator ThrownKnifeTakesTheThiefDown()
        {
            yield return null;
            var thief = StandingThief();
            yield return null; yield return null;
            int before = orders.Money;
            Throw("Knife", thief.transform.position + Vector3.up * 1.1f);
            yield return Steps();
            AssertKnockedOut(thief);
            Assert.AreEqual(before + OrderManager.ThiefReward, orders.Money, "the reward, once");
        }

        // Playtest 5: a fast swing of a held knife through him is a slash; a slow touch is not.
        [UnityTest]
        public IEnumerator SlashWithAHeldKnifeTakesTheThiefDown()
        {
            yield return null;
            var thief = StandingThief(tough: true);
            yield return null; yield return null;
            var go = Object.Instantiate(Prefab("Knife"), thief.transform.position + new Vector3(0f, 1.1f, -1f), Quaternion.identity);
            var rack = go.GetComponent<ReturnToRack>(); if (rack) Object.DestroyImmediate(rack);
            var rb = go.GetComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var handGo = new GameObject("Hand");
            handGo.transform.position = go.transform.position;
            var trigger = handGo.AddComponent<SphereCollider>(); trigger.isTrigger = true; trigger.radius = 0.05f;
            var hand = handGo.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
            yield return null;
            var grab = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            hand.StartManualInteraction((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(grab.isSelected, "held");
            // Slowly into him: nothing.
            yield return MoveHand(hand, thief.transform.position + new Vector3(0f, 1.1f, -0.1f), 0.3f);
            Assert.AreEqual(2, thief.Health, "a slow touch is no slash");
            yield return MoveHand(hand, thief.transform.position + new Vector3(0f, 1.1f, -1f), 0.3f);
            yield return new WaitForSeconds(0.6f);
            // Two fast swings: the first staggers the tough thief, the second takes him down.
            for (int swing = 0; swing < 2 && !thief.Body.Dead; swing++)
            {
                yield return MoveHand(hand, thief.transform.position + new Vector3(0f, 1.1f, 0.3f), 3f);
                if (thief.Body.Dead) break;                              // check him before he vanishes
                // A wound only slows him for Thief.SlowSeconds: hold him still, and bring the knife back over his head
                // (not back through him) quickly, so the next swing lands before he would walk again.
                thief.Body.SetSpeedScale(0f);
                var at = thief.transform.position;
                yield return MoveHand(hand, at + new Vector3(0f, 2.6f, 0.3f), 3f);
                yield return MoveHand(hand, at + new Vector3(0f, 2.6f, -1f), 3f);
                yield return MoveHand(hand, at + new Vector3(0f, 1.1f, -1f), 3f);
                yield return new WaitForSeconds(0.2f);                   // past the one-hit-per-swing gate (0.5 s)
            }
            yield return Steps(Customer.FallSeconds + 0.2f);             // let him tip over (he vanishes at KnockOutSeconds)
            AssertKnockedOut(thief);
            hand.EndManualInteraction();
            Object.Destroy(handGo); Object.Destroy(go);
        }

        // At a steady speed whatever the frame rate (batch-mode test frames can be very short).
        static IEnumerator MoveHand(Component hand, Vector3 to, float metresPerSecond)
        {
            while ((hand.transform.position - to).sqrMagnitude > 1e-6f)
            {
                hand.transform.position = Vector3.MoveTowards(hand.transform.position, to, metresPerSecond * Mathf.Max(Time.deltaTime, 1e-4f));
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ThrownFruitSlowsTheThiefAndSplatsHisFace()
        {
            yield return null;
            var thief = StandingThief();
            yield return null; yield return null;
            var fruit = Throw("Guava", HeadOf(thief.Body));
            yield return Steps();
            Assert.IsTrue(fruit == null, "the fruit burst on him");
            Assert.AreEqual(1, thief.Body.Splats, "his face is splatted");
            Assert.IsFalse(thief.Body.Dead);
            Assert.AreEqual(Thief.SlowScale, thief.Body.SpeedScale, 1e-4f, "slowed");
        }

        [UnityTest]
        public IEnumerator KnifeBouncesOffACustomerButFoodSplatsTheirFace()
        {
            var c = Object.Instantiate(Prefab("Customers/Salaryman"), new Vector3(-5, 0, 5), Quaternion.Euler(0, 180, 0)).GetComponent<Customer>();
            yield return null;
            Throw("Knife", c.transform.position + Vector3.up * 1.1f);
            yield return Steps();
            Assert.IsFalse(c.Dead, "customers aren't targets: a knife bounces off them");
            var fruit = Throw("Guava", HeadOf(c));
            yield return Steps();
            Assert.AreEqual(1, c.Splats, "but food in the face splats");
            Assert.IsTrue(fruit == null);
            Object.Destroy(c.gameObject);
        }

        [UnityTest]
        public IEnumerator SlowTossOrPauseDoesNothing()
        {
            yield return null;
            var thief = StandingThief();
            yield return null;
            var rb = Throw("Guava", HeadOf(thief.Body));
            rb.linearVelocity = Vector3.forward * 0.5f;      // a toss, not a throw
            yield return Steps(2.5f);
            Assert.AreEqual(0, thief.Body.Splats, "too slow to count");
            var chest = Bullet.AimPoint(thief);
            var p = PistolAt(chest + new Vector3(0f, 0f, -4f), chest);
            yield return null;
            GamePause.Set(true);
            Assert.IsFalse(p.Fire(), "the pistol doesn't fire while paused");
            Throw("Guava", HeadOf(thief.Body));
            yield return Steps();
            Assert.AreEqual(0, thief.Body.Splats, "nothing happens while paused");
            Assert.IsFalse(thief.Body.Dead);
            Object.Destroy(p.gameObject);
        }

        [UnityTest]
        public IEnumerator EscapeCostsThePenaltyAndNeverGoesNegative()
        {
            yield return null;
            orders.ThiefCaught(Vector3.zero);
            orders.ThiefEscaped(Vector3.zero);
            Assert.AreEqual(OrderManager.ThiefReward - OrderManager.ThiefPenalty, orders.Money);
            for (int i = 0; i < 5; i++) orders.ThiefEscaped(Vector3.zero);
            Assert.AreEqual(0, orders.Money, "never below zero");
        }
    }
}
