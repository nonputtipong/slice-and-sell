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
        CuttingBoard board; Knife knife; OrderManager orders; Dispenser bin, bowlStack; XRSocketInteractor tray;

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
            bowlStack = dispensers.First(d => d.prefab.GetComponent<Bowl>());
            tray = Object.FindAnyObjectByType<HandoffTray>().GetComponent<XRSocketInteractor>();
            var rb = knife.GetComponent<Rigidbody>(); rb.isKinematic = true;   // the test drives it, like a held knife
            orders.StartLevel(0);   // the scene opens on the main menu; this is pressing Start
        }

        [UnityTest]
        public IEnumerator SceneStartsReady()
        {
            Assert.IsNotNull(bin.Current, "fruit bin has a fruit");
            Assert.IsNotNull(bowlStack.Current, "bowl stack has a bowl");
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

            // Playtest 2: a bowl already waits on the scoop spot, and the cut fruit goes in by hand, not by itself.
            var bowl = bowlStack.Current.GetComponent<Bowl>();
            Assert.IsTrue(board.scoopSocket.hasSelection, "a bowl waits on the scoop spot");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(bowl.IsFilled, "the cut fruit doesn't jump into the bowl by itself");
            board.fruitSocket.socketActive = false;         // the hand lifts it off the board
            yield return DropAt(fruit.gameObject, bowl.transform.position + Vector3.up * 0.07f);
            board.fruitSocket.socketActive = true;
            Assert.IsTrue(bowl.IsFilled, "let go over the bowl: the slices are in it");
            Assert.IsTrue(fruit == null, "fruit is consumed");
            Assert.AreSame(bowl, bowlStack.Current.GetComponent<Bowl>(), "the filled bowl is still the one on the spot");

            // A hand holding the bowl stops the scoop socket from taking it back; here the socket is paused instead.
            int before = orders.Money;
            board.scoopSocket.socketActive = false;
            yield return DropAt(bowl.gameObject, tray.transform.position);
            board.scoopSocket.socketActive = true;
            Assert.AreEqual(20, orders.Money - before, "Level 1: base price, combo x1, no tips");
            Assert.AreEqual(4 + 20, orders.Money, "two clean cuts (+2 each) and the guava");
            Assert.AreEqual(1, orders.Served);
            Assert.IsNull(orders.Current, "customer leaves after paying");
            var tutorial = Object.FindAnyObjectByType<Tutorial>();
            Assert.Greater(tutorial.OnboardingSeconds, 0f, "serving customer 1 finishes the tutorial and records the onboarding time");

            yield return new WaitForSeconds(1.2f);          // the tray uses the bowl up after 0.5 s
            Assert.IsTrue(bowl == null, "the handed-over bowl is gone");
            Assert.IsNotNull(bowlStack.Current, "a fresh bowl is back on the spot");
            Assert.IsFalse(bowlStack.Current.GetComponent<Bowl>().IsFilled, "and it is empty");
            Assert.IsTrue(board.scoopSocket.hasSelection, "standing in the scoop spot");
        }

        // Playtest 2: lifting the bowl off its spot (to carry it to the tray) makes no second bowl; only a bowl that is
        // gone (handed over, or dropped and despawned) is replaced.
        [UnityTest]
        public IEnumerator BowlSpotRefillsOnlyWhenTheBowlIsGone()
        {
            var bowl = bowlStack.Current;
            board.scoopSocket.socketActive = false;
            var rb = bowl.GetComponent<Rigidbody>(); rb.isKinematic = true;
            bowl.transform.position += new Vector3(0f, 0.3f, -0.2f);       // carried away
            yield return new WaitForSeconds(1f);
            Assert.AreSame(bowl, bowlStack.Current, "no second bowl while this one is carried");
            Object.Destroy(bowl.gameObject);
            board.scoopSocket.socketActive = true;
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(bowlStack.Current != null && bowlStack.Current != bowl, "a new bowl once it is gone");
            Assert.IsTrue(board.scoopSocket.hasSelection, "on the scoop spot");
        }

        // Item 6 of the Quest playtest: the cut fruit goes into a bowl wherever it stands; nobody has to carry the
        // bowl to the board first. Then it is served as usual.
        [UnityTest]
        public IEnumerator CutFruitLetGoOverABowlGoesIn()
        {
            yield return UntilCustomer();
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            yield return Push(fruit, 1f, 0.003f);
            yield return Push(fruit, 1f, 0.003f);
            Assert.IsTrue(fruit.IsFullyCut);

            var bowl = bowlStack.Current.GetComponent<Bowl>();
            board.fruitSocket.socketActive = false;         // the hand lifts it off the board
            yield return DropAt(fruit.gameObject, bowl.transform.position + Vector3.up * 0.07f);
            board.fruitSocket.socketActive = true;
            Assert.IsTrue(bowl.IsFilled, "let go over the bowl: the slices are in it");
            Assert.AreEqual("Guava", bowl.Contents);
            Assert.AreEqual(2, bowl.Cuts);
            Assert.IsTrue(fruit == null, "the fruit is used up");

            int before = orders.Money;
            board.scoopSocket.socketActive = false;         // the hand carries it off the scoop spot
            yield return DropAt(bowl.gameObject, tray.transform.position);
            board.scoopSocket.socketActive = true;
            Assert.AreEqual(20, orders.Money - before, "served and paid");
        }

        [UnityTest]
        public IEnumerator UncutFruitStaysOutOfTheBowl()
        {
            var bowl = bowlStack.Current.GetComponent<Bowl>();
            var fruit = bin.Current.GetComponent<Fruit>();
            yield return DropAt(fruit.gameObject, bowl.transform.position + Vector3.up * 0.07f);
            Assert.IsFalse(bowl.IsFilled, "a whole fruit is not an order");
            Assert.IsTrue(fruit != null);
        }

        [UnityTest]
        public IEnumerator HeldCutFruitOverTheBowlWaitsForTheRelease()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            yield return Push(fruit, 1f, 0.003f);
            yield return Push(fruit, 1f, 0.003f);
            var hand = new GameObject("Test hand"); hand.AddComponent<SphereCollider>().isTrigger = true;
            var direct = hand.AddComponent<XRDirectInteractor>();
            board.fruitSocket.socketActive = false;
            hand.transform.position = fruit.transform.position;
            yield return null;
            direct.StartManualInteraction((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)fruit.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>());
            yield return null;
            var bowl = bowlStack.Current.GetComponent<Bowl>();
            var over = bowl.transform.position + Vector3.up * 0.07f;
            fruit.GetComponent<Rigidbody>().isKinematic = true;
            for (int i = 0; i < 10; i++) { hand.transform.position = over; fruit.transform.position = over; yield return new WaitForFixedUpdate(); }
            Assert.IsFalse(bowl.IsFilled, "still in the hand");
            fruit.GetComponent<Rigidbody>().isKinematic = false;
            direct.EndManualInteraction();
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            board.fruitSocket.socketActive = true;
            Assert.IsTrue(bowl.IsFilled, "let go: in it goes");
            Object.Destroy(hand);
        }

        // Item 1 of the Quest playtest: every bin makes a new item when the waiting one is grabbed, knocked out of the
        // bin, or destroyed without a grab. And it does not make extra ones while the first just settles.
        [UnityTest]
        public IEnumerator EveryBinKeepsOneItemWaiting()
        {
            var bins = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(bins.Length, 7, "4 fruit crates, bowls, skewers, chilies");
            var first = bins.Select(d => d.Current).ToArray();
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < bins.Length; i++)
                Assert.AreSame(first[i], bins[i].Current, $"{bins[i].name}: the item settled in its bin, no respawn");

            // Knocked out of the bin (not grabbed). The bowl spot is not a bin: it refills only when its bowl is gone
            // (BowlSpotRefillsOnlyWhenTheBowlIsGone), so the knock and grab checks leave it out.
            var all = bins;
            bins = all.Where(d => !d.onlyWhenGone).ToArray();
            first = bins.Select(d => d.Current).ToArray();
            foreach (var d in bins) { var rb = d.Current.GetComponent<Rigidbody>(); rb.isKinematic = true; d.Current.transform.position += new Vector3(0, 0.3f, 0); }
            yield return new WaitForSeconds(0.6f);
            for (int i = 0; i < bins.Length; i++)
            {
                Assert.IsNotNull(bins[i].Current, $"{bins[i].name}: a new item after a knock");
                Assert.AreNotSame(first[i], bins[i].Current, $"{bins[i].name}: a new one, not the knocked one");
                Object.Destroy(first[i].gameObject);
            }

            // Used up or despawned without a grab (the bowl spot too).
            bins = all;
            var second = bins.Select(d => d.Current).ToArray();
            foreach (var g in second) Object.Destroy(g.gameObject);
            yield return new WaitForSeconds(0.6f);
            for (int i = 0; i < bins.Length; i++)
                Assert.IsTrue(bins[i].Current != null && bins[i].Current != second[i], $"{bins[i].name}: a new item after a destroy");

            // Grabbed.
            bins = all.Where(d => !d.onlyWhenGone).ToArray();
            var hand = new GameObject("Test hand"); hand.AddComponent<SphereCollider>().isTrigger = true;
            var direct = hand.AddComponent<XRDirectInteractor>();
            foreach (var d in bins)
            {
                var taken = d.Current;
                hand.transform.position = taken.transform.position;
                yield return null;
                direct.StartManualInteraction((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)taken);
                yield return new WaitForSeconds(0.5f);
                Assert.IsNull(d.Current, $"{d.name}: nothing new spawns inside the item still held over the bin");
                hand.transform.position += new Vector3(0f, 0.3f, -0.2f);       // carried away
                yield return new WaitForSeconds(0.5f);
                Assert.IsTrue(d.Current != null && d.Current != taken, $"{d.name}: a new item once the grabbed one has left");
                direct.EndManualInteraction();
                yield return null;
                Object.Destroy(taken.gameObject);
            }
            Object.Destroy(hand);
        }

        // Item 7 of the Quest playtest: a cut visibly splits the fruit, and it still lifts as one object.
        [UnityTest]
        public IEnumerator CutPiecesMoveApartButStayOneObject()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            var stage = fruit.stages[1].transform;
            float Gap() => Vector3.Distance(Centre(stage.GetChild(0)), Centre(stage.GetChild(1)));
            float closed = Gap();
            yield return Push(fruit, 1f, 0.003f);
            Assert.AreEqual(1, fruit.CutsDone);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Gap() - closed, 0.006f, "the two halves are visibly apart (> 6 mm)");
            Assert.AreEqual(1, fruit.GetComponentsInChildren<Rigidbody>().Length, "one body");
            Assert.IsTrue(stage.GetChild(0).IsChildOf(fruit.transform) && stage.GetChild(1).IsChildOf(fruit.transform), "pieces stay under the fruit");
        }

        static Vector3 Centre(Transform piece) => piece.GetComponentInChildren<Renderer>().bounds.center;

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
