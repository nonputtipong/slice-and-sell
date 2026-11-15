using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // Play-mode tests in the real Stall scene (built by Slice & Sell > Build Stall Scene).
    // They move the knife the way a hand or a controller would and check the whole order loop.
    public class StallPlayTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Stall.unity";
        CuttingBoard board; Knife knife; OrderManager orders; Dispenser bin, bagStack; XRSocketInteractor tray;

        [UnitySetUp]
        public IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            board = Object.FindAnyObjectByType<CuttingBoard>();
            knife = Object.FindAnyObjectByType<Knife>();
            orders = Object.FindAnyObjectByType<OrderManager>();
            var dispensers = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None);
            bin = dispensers.First(d => d.prefab.GetComponent<Fruit>());
            bagStack = dispensers.First(d => d.prefab.GetComponent<Bag>());
            tray = Object.FindAnyObjectByType<HandoffTray>().GetComponent<XRSocketInteractor>();
            var rb = knife.GetComponent<Rigidbody>(); rb.isKinematic = true;   // the test drives it, like a held knife
        }

        [UnityTest]
        public IEnumerator SceneStartsReady()
        {
            Assert.IsNotNull(bin.Current, "fruit bin has a fruit");
            Assert.IsNotNull(bagStack.Current, "bag stack has a bag");
            Assert.AreSame(board, knife.board);
            Assert.IsNotNull(orders.Current, "first customer is waiting");
            yield break;
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
        public IEnumerator FastSwingCuts()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            var c = fruit.transform.position;
            yield return MoveEdge(new Vector3(c.x - 0.3f, c.y, c.z), new Vector3(c.x + 0.3f, c.y, c.z), 0.042f);   // 3 m/s at 72 fps
            Assert.AreEqual(1, fruit.CutsDone);
        }

        [UnityTest]
        public IEnumerator FullOrderLoop()
        {
            var fruit = (Fruit)null;
            yield return PlaceFruit(f => fruit = f);
            yield return Push(fruit, 1f, 0.003f);
            yield return Push(fruit, 1f, 0.003f);
            Assert.IsTrue(fruit.IsFullyCut);

            var bag = bagStack.Current.GetComponent<Bag>();
            yield return DropAt(bag.gameObject, board.scoopSocket.transform.position);
            Assert.IsTrue(bag.IsFilled, "slices go into the bag on the scoop spot");
            Assert.IsTrue(fruit == null, "fruit is consumed");

            // A hand holding the bag stops the scoop socket from taking it back; here the socket is paused instead.
            board.scoopSocket.socketActive = false;
            yield return DropAt(bag.gameObject, tray.transform.position);
            board.scoopSocket.socketActive = true;
            Assert.AreEqual(20, orders.Money,
                $"bag at {(bag ? bag.transform.position.ToString("F3") : "destroyed")}, tray {tray.transform.position:F3}, " +
                $"scoop holds bag: {board.scoopSocket.hasSelection}, tray holds: {tray.hasSelection}, card: {orders.orderCard?.text}");
            Assert.AreEqual(1, orders.Served);
            Assert.IsNull(orders.Current, "customer leaves after paying");
        }

        // ---------- helpers ----------

        IEnumerator PlaceFruit(System.Action<Fruit> got)
        {
            var fruit = bin.Current.GetComponent<Fruit>();
            yield return DropAt(fruit.gameObject, board.fruitSocket.transform.position);
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

        // Blade across the fruit (edge along X, facing down), from 3 cm above it down by a fraction of its thickness.
        IEnumerator Push(Fruit fruit, float depthFraction, float metresPerFrame)
        {
            knife.transform.rotation = Quaternion.Euler(0, 90, 0);
            var c = fruit.transform.position;
            float top = c.y + fruit.thickness / 2f;
            var from = new Vector3(c.x, top + 0.03f, c.z);
            var to = new Vector3(c.x, top - fruit.thickness * depthFraction, c.z);
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
