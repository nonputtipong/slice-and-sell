using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // Play-mode tests in the real Stall scene (built by Slice & Sell > Build Stall Scene).
    // They move the knife the way a hand or a controller would and check the whole order loop.
    public class StallPlayTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Stall.unity";
        CuttingBoard board; Knife knife; OrderManager orders; Dispenser bin, bagStack; XRSocketInteractor tray;

        // Batch mode runs frames as fast as it can; a fixed 72 Hz clock makes the knife speeds real
        // (0.003 m/frame = 0.2 m/s hand push, 0.042 m/frame = 3 m/s chop).
        [SetUp] public void FixClock() => Time.captureDeltaTime = 1f / 72f;
        [TearDown] public void FreeClock() => Time.captureDeltaTime = 0f;

        [UnitySetUp]
        public IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            board = Object.FindAnyObjectByType<CuttingBoard>();
            knife = Object.FindObjectsByType<Knife>(FindObjectsSortMode.None).Single(k => !k.cleaver);   // the paring knife, not the cleaver
            orders = Object.FindAnyObjectByType<OrderManager>();
            var dispensers = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None);
            bin = dispensers.First(d => d.prefab.GetComponent<Fruit>() && d.prefab.GetComponent<Fruit>().fruitName == "Guava");
            bagStack = dispensers.First(d => d.prefab.GetComponent<Bag>());
            tray = Object.FindAnyObjectByType<HandoffTray>().GetComponent<XRSocketInteractor>();
            var rb = knife.GetComponent<Rigidbody>(); rb.isKinematic = true;   // the test drives it, like a held knife
            orders.StartLevel(0);   // the scene opens on the main menu; this is pressing Start
        }

        [UnityTest]
        public IEnumerator SceneStartsReady()
        {
            Assert.IsNotNull(bin.Current, "fruit bin has a fruit");
            Assert.IsNotNull(bagStack.Current, "bag stack has a bag");
            Assert.AreSame(board, knife.board);
            Assert.IsNotNull(orders.Level, "the scene runs levels");
            Assert.AreEqual(0, orders.levelIndex, "starts at Level 1");
            yield return UntilCustomer();
            Assert.AreEqual("Guava", orders.Current.fruit);
        }

        [UnityTest]
        public IEnumerator SlowHandPushCutsAndShallowPushDoesNot()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            yield return Push(fruit, depthFraction: 0.5f, metresPerFrame: 0.0014f);   // 0.1 m/s at 72 fps, half way
            Assert.AreEqual(0, fruit.CutsDone, "shallow push must not cut");
            yield return Push(fruit, depthFraction: 1f, metresPerFrame: 0.0014f);
            Assert.AreEqual(1, fruit.CutsDone, "slow full push cuts once");
        }

        [UnityTest]
        public IEnumerator FastChopCuts()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            CutInfo got = default;
            void OnCut(Fruit f, CutInfo c, int combo) => got = c;
            GameEvents.Cut += OnCut;
            yield return Push(fruit, 1f, 0.042f);   // 3 m/s at 72 fps
            GameEvents.Cut -= OnCut;
            Assert.AreEqual(1, fruit.CutsDone);
            Assert.IsTrue(got.power, "3 m/s is a Power Cut");
        }

        [UnityTest]
        public IEnumerator SlowPushIsNotAPowerCut()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            CutInfo got = default;
            void OnCut(Fruit f, CutInfo c, int combo) => got = c;
            GameEvents.Cut += OnCut;
            yield return Push(fruit, 1f, 0.003f);   // 0.2 m/s
            GameEvents.Cut -= OnCut;
            Assert.AreEqual(1, fruit.CutsDone);
            Assert.IsTrue(got.clean && !got.power);
        }

        [UnityTest]
        public IEnumerator BladeAcrossTheGuideMissesWithAThunk()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            int missed = 0;
            void OnMissed(Vector3 _) => missed++;
            GameEvents.CutMissed += OnMissed;
            yield return Push(fruit, 1f, 0.003f, extraYaw: 90f);
            GameEvents.CutMissed -= OnMissed;
            Assert.AreEqual(0, fruit.CutsDone, "a blade at 90 degrees to the guide does not cut");
            Assert.GreaterOrEqual(missed, 1, "the miss is reported (thunk)");
        }

        [UnityTest]
        public IEnumerator OffGuideCutCountsButIsMessy()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            CutInfo got = default;
            void OnCut(Fruit f, CutInfo c, int combo) => got = c;
            GameEvents.Cut += OnCut;
            yield return Push(fruit, 1f, 0.003f, sideOffset: 0.025f);
            GameEvents.Cut -= OnCut;
            Assert.AreEqual(1, fruit.CutsDone, "still a cut");
            Assert.IsFalse(got.clean, "2.5 cm off the guide is not a Clean Cut");
            Assert.AreEqual(0, orders.Combo);
        }

        [UnityTest]
        public IEnumerator FullOrderLoop()
        {
            yield return UntilCustomer();
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            yield return Push(fruit, 1f, 0.003f);
            yield return Push(fruit, 1f, 0.003f);
            Assert.IsTrue(fruit.IsFullyCut);
            Assert.AreEqual(2, orders.Combo, "both cuts on the guides are clean");

            var bag = bagStack.Current.GetComponent<Bag>();
            yield return DropAt(bag.gameObject, board.scoopSocket.transform.position);
            Assert.IsTrue(bag.IsFilled, "slices go into the bag on the scoop spot");
            Assert.IsTrue(fruit == null, "fruit is consumed");

            // A hand holding the bag stops the scoop socket from taking it back; here the socket is paused instead.
            int before = orders.Money;
            board.scoopSocket.socketActive = false;
            yield return DropAt(bag.gameObject, tray.transform.position);
            board.scoopSocket.socketActive = true;
            Assert.AreEqual(20, orders.Money - before, "Level 1: base price, combo x1, no tips");
            Assert.AreEqual(4 + 20, orders.Money, "two clean cuts (+2 each) and the guava");
            Assert.AreEqual(1, orders.Served);
            Assert.IsNull(orders.Current, "customer leaves after paying");
            var tutorial = Object.FindAnyObjectByType<Tutorial>();
            Assert.Greater(tutorial.OnboardingSeconds, 0f, "serving customer 1 finishes the tutorial and records the onboarding time");
        }

        // ---------- helpers ----------

        IEnumerator UntilCustomer(float timeout = 15f)
        {
            for (float t = 0; orders.Current == null && t < timeout; t += Time.deltaTime) yield return null;
            Assert.IsNotNull(orders.Current, "a customer reached the counter");
        }

        IEnumerator PlaceFruit(System.Action<Fruit> got)
        {
            var fruit = bin.Current.GetComponent<Fruit>();
            yield return DropAt(fruit.gameObject, board.fruitSocket.transform.position + Vector3.up * 0.02f);
            Assert.AreSame(fruit, board.Fruit, "fruit snapped into the board socket");
            got(fruit);
        }

        static IEnumerator DropAt(GameObject go, Vector3 pos)
        {
            var rb = go.GetComponent<Rigidbody>();
            rb.position = pos; go.transform.position = pos; rb.linearVelocity = Vector3.zero;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            yield return null;
        }

        Vector3 EdgeMid => (knife.edgeStart.position + knife.edgeEnd.position) * 0.5f;

        // Blade in the next guide's plane (turned by extraYaw, moved sideways by sideOffset), from 3 cm above the
        // fruit down by a fraction of the guide's depth.
        IEnumerator Push(Fruit fruit, float depthFraction, float metresPerFrame, float extraYaw = 0f, float sideOffset = 0f)
        {
            var g = fruit.NextGuide;
            var n = fruit.transform.TransformDirection(g.normal).normalized;
            knife.transform.rotation = Quaternion.AngleAxis(extraYaw, Vector3.up) * Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up);
            var top = fruit.cutVolume.bounds.max.y;
            var onPlane = fruit.transform.TransformPoint(g.point) + n * sideOffset;
            var from = new Vector3(onPlane.x, top + 0.03f, onPlane.z);
            var to = new Vector3(onPlane.x, top - g.depth * depthFraction, onPlane.z);
            yield return MoveEdge(from, from, 1f);      // settle above the fruit
            yield return MoveEdge(from, to, metresPerFrame);
            yield return MoveEdge(to, from, 0.02f);     // lift out
        }

        IEnumerator MoveEdge(Vector3 from, Vector3 to, float metresPerFrame)
        {
            int frames = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / metresPerFrame));
            for (int i = 0; i <= frames; i++)
            {
                knife.transform.position += Vector3.Lerp(from, to, (float)i / frames) - EdgeMid;
                yield return null;
            }
        }
    }
}
