using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // The Eto (Thai cleaver): cuts every fruit with one hand, a second hand optional, and is the only tool for the
    // watermelon. Direct interactors hold its handle and are moved down through the fruit, as hands would.
    public class CleaverTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Stall.unity";
        CuttingBoard board; Knife cleaver, knife; Dispenser melons, guavas;
        XRGrabInteractable grab;
        XRDirectInteractor left, right;
        int missed;

        [SetUp] public void FixClock() => Time.captureDeltaTime = 1f / 72f;

        [TearDown]
        public void FreeClock()
        {
            Time.captureDeltaTime = 0f;
            GameEvents.CutMissed -= OnMissed;
        }

        void OnMissed(Vector3 _) => missed++;

        [UnitySetUp]
        public IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            board = Object.FindAnyObjectByType<CuttingBoard>();
            var tools = Object.FindObjectsByType<Knife>(FindObjectsSortMode.None);
            cleaver = tools.Single(k => k.cleaver);
            knife = tools.Single(k => !k.cleaver);
            grab = cleaver.GetComponent<XRGrabInteractable>();
            var bins = Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None).Where(d => d.prefab.GetComponent<Fruit>()).ToArray();
            melons = bins.Single(d => d.prefab.GetComponent<Fruit>().needsCleaver);
            guavas = bins.Single(d => d.prefab.GetComponent<Fruit>().fruitName == "Guava");
            left = Hand("Left test hand"); right = Hand("Right test hand");
            missed = 0;
            GameEvents.CutMissed += OnMissed;
        }

        static XRDirectInteractor Hand(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<SphereCollider>().isTrigger = true;
            return go.AddComponent<XRDirectInteractor>();
        }

        [Test]
        public void EtoNeedsOneHandAndTheMelonNeedsTheEto()
        {
            Assert.AreEqual(InteractableSelectMode.Multiple, grab.selectMode, "a second hand can steady it");
            Assert.AreEqual(1, cleaver.handsRequired);
            Assert.IsTrue(melons.prefab.GetComponent<Fruit>().needsCleaver);
            Assert.AreEqual("Watermelon", melons.prefab.GetComponent<Fruit>().fruitName);
        }

        [UnityTest]
        public IEnumerator TwoHandedSlowChopCutsTheMelon()
        {
            var melon = (Fruit)null;
            yield return PlaceMelon(f => melon = f);
            yield return HoldWith(left, right);
            Assert.AreEqual(2, grab.interactorsSelecting.Count, "held by both hands");
            yield return Chop(melon, metresPerFrame: 0.003f);    // 0.2 m/s, a hand push
            Assert.AreEqual(1, melon.CutsDone, "a slow two-handed chop through the melon cuts it");
            Release(left, right);
        }

        [UnityTest]
        public IEnumerator FastDropIsAPowerCut()
        {
            var melon = (Fruit)null;
            yield return PlaceMelon(f => melon = f);
            CutInfo got = default;
            void OnCut(Fruit f, CutInfo c, int combo) => got = c;
            GameEvents.Cut += OnCut;
            yield return HoldWith(left, right);
            yield return Chop(melon, metresPerFrame: 0.042f);    // 3 m/s
            GameEvents.Cut -= OnCut;
            Assert.AreEqual(1, melon.CutsDone);
            Assert.IsTrue(got.power, "a fast drop is a Power Cut");
            Release(left, right);
        }

        [UnityTest]
        public IEnumerator OneHandedChopCutsTheMelon()
        {
            var melon = (Fruit)null;
            yield return PlaceMelon(f => melon = f);
            yield return HoldWith(left);
            Assert.AreEqual(1, grab.interactorsSelecting.Count, "held by one hand");
            yield return Chop(melon, metresPerFrame: 0.003f, hands: new[] { left });
            Assert.AreEqual(1, melon.CutsDone, "one hand is enough for the Eto");
            Release(left);
        }

        [UnityTest]
        public IEnumerator EtoCutsAGuavaToo()
        {
            var guava = (Fruit)null;
            yield return Place(guavas, f => guava = f);
            yield return HoldWith(left);
            yield return Chop(guava, metresPerFrame: 0.003f, hands: new[] { left });
            Assert.AreEqual(1, guava.CutsDone, "the Eto cuts small fruit as well");
            Assert.AreEqual(0, missed, "no thunk");
            Release(left);
        }

        [UnityTest]
        public IEnumerator ParingKnifeCannotCutTheMelon()
        {
            var melon = (Fruit)null;
            yield return PlaceMelon(f => melon = f);
            knife.GetComponent<Rigidbody>().isKinematic = true;
            var g = melon.NextGuide;
            var n = melon.transform.TransformDirection(g.normal).normalized;
            knife.transform.rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up);
            var top = melon.cutVolume.bounds.max.y;
            var p = melon.transform.TransformPoint(g.point);
            Vector3 from = new Vector3(p.x, top + 0.03f, p.z), to = new Vector3(p.x, top - g.depth, p.z);
            yield return MoveTool(knife, from, from, 1f);
            yield return MoveTool(knife, from, to, 0.003f);
            yield return MoveTool(knife, to, from, 0.02f);
            Assert.AreEqual(0, melon.CutsDone, "the paring knife is too small for a watermelon");
            Assert.GreaterOrEqual(missed, 1, "it thunks instead");
        }

        // ---------- helpers ----------

        IEnumerator PlaceMelon(System.Action<Fruit> got) => Place(melons, got);

        IEnumerator Place(Dispenser bin, System.Action<Fruit> got)
        {
            var melon = bin.Current.GetComponent<Fruit>();
            var rb = melon.GetComponent<Rigidbody>();
            var pos = board.fruitSocket.transform.position + Vector3.up * 0.02f;
            rb.position = pos; melon.transform.position = pos; rb.linearVelocity = Vector3.zero;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreSame(melon, board.Fruit, "the fruit snapped into the board socket");
            got(melon);
        }

        // Pose the Eto over the fruit on the board, blade on the next guide, then close the hands on the handle where it lies.
        IEnumerator HoldWith(params XRDirectInteractor[] hands)
        {
            var melon = board.Fruit;
            var g = melon.NextGuide;
            var n = melon.transform.TransformDirection(g.normal).normalized;
            // Kinematic and not interpolated before posing: an interpolated body can pull the pose back on the next
            // physics step, so the tool would sometimes turn and drift before the hands close (a flaky chop).
            var rb = cleaver.GetComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
            cleaver.transform.rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up), Vector3.up);
            var p = melon.transform.TransformPoint(g.point);
            var start = new Vector3(p.x, melon.cutVolume.bounds.max.y + 0.03f, p.z);
            cleaver.transform.position += start - EdgeMid(cleaver);
            rb.position = cleaver.transform.position; rb.rotation = cleaver.transform.rotation;
            Physics.SyncTransforms();
            float[] along = { -0.05f, 0.045f };     // one or two fists at the ends of the 11.5 cm handle (a short baseline makes the two-hand aim twitchy)
            for (int i = 0; i < hands.Length; i++)
            {
                hands[i].transform.SetPositionAndRotation(cleaver.transform.TransformPoint(0, 0, along[i]), cleaver.transform.rotation);
                yield return null;
                hands[i].StartManualInteraction((IXRSelectInteractable)grab);
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        void Release(params XRDirectInteractor[] hands) { foreach (var h in hands) h.EndManualInteraction(); }

        // Both hands move down together until the edge is a full guide depth below the melon's top, then lift.
        IEnumerator Chop(Fruit melon, float metresPerFrame, XRDirectInteractor[] hands = null)
        {
            hands ??= new[] { left, right };
            var top = melon.cutVolume.bounds.max.y;
            float down = (EdgeMid(cleaver).y - top) + melon.NextGuide.depth;
            yield return MoveHands(hands, Vector3.down * down, metresPerFrame);
            yield return MoveHands(hands, Vector3.up * down, 0.02f);
        }

        static IEnumerator MoveHands(XRDirectInteractor[] hands, Vector3 delta, float metresPerFrame)
        {
            int frames = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / metresPerFrame));
            var starts = hands.Select(h => h.transform.position).ToArray();
            for (int i = 1; i <= frames; i++)
            {
                for (int k = 0; k < hands.Length; k++) hands[k].transform.position = starts[k] + delta * ((float)i / frames);
                yield return null;
            }
        }

        static Vector3 EdgeMid(Knife k) => (k.edgeStart.position + k.edgeEnd.position) * 0.5f;

        static IEnumerator MoveTool(Knife k, Vector3 from, Vector3 to, float metresPerFrame)
        {
            int frames = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / metresPerFrame));
            for (int i = 0; i <= frames; i++)
            {
                k.transform.position += Vector3.Lerp(from, to, (float)i / frames) - EdgeMid(k);
                yield return null;
            }
        }
    }
}
