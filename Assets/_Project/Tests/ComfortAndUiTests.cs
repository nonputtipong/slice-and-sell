using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace SliceAndSell.Tests
{
    // M4: the assignment's 3D UI and comfort rules, checked on the built Stall scene and on OrderManager.
    public class ComfortAndUiTests
    {
        const string ScenePath = "Assets/_Project/Scenes/Stall.unity";

        IEnumerator LoadStall()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
        }

        [TearDown] public void Unpause() { GamePause.Set(false); GameSettings.Seated = false; }

        // Every Button in a world-space canvas: at least 22 x 22 mm, at least 12 mm from its neighbours (INSTRUCTIONS §5).
        [UnityTest]
        public IEnumerator EveryButtonMeetsPokeSizeAndSpacing()
        {
            yield return LoadStall();
            // VR menus only: world-space canvases (the editor-only XR Interaction Simulator has a screen-space panel).
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b.GetComponentInParent<Canvas>(true).rootCanvas.renderMode == RenderMode.WorldSpace).ToArray();
            Assert.Greater(buttons.Length, 5, "the scene has menus");
            foreach (var b in buttons)
            {
                var size = WorldSize((RectTransform)b.transform) * 1000f;
                Assert.GreaterOrEqual(Mathf.Min(size.x, size.y), 22f, $"{Path(b.transform)} is {size.x:F0} x {size.y:F0} mm");
            }
            foreach (var group in buttons.GroupBy(b => b.transform.parent))
            {
                var list = group.ToArray();
                for (int i = 0; i < list.Length; i++)
                    for (int j = i + 1; j < list.Length; j++)
                        Assert.GreaterOrEqual(GapMm(list[i].transform as RectTransform, list[j].transform as RectTransform), 12f,
                            $"{Path(list[i].transform)} and {list[j].name} are too close");
            }
        }

        // Menu text (UGUI in world canvases): cap height >= 8 mm, legible at 0.5 m (GDD size rules).
        [UnityTest]
        public IEnumerator MenuTextIsLegibleAtHalfAMetre()
        {
            yield return LoadStall();
            foreach (var t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.GetComponentInParent<Canvas>(true).rootCanvas.renderMode != RenderMode.WorldSpace) continue;
                if (string.IsNullOrWhiteSpace(t.text) && !t.enableAutoSizing) continue;
                float size = t.enableAutoSizing ? t.fontSizeMin : t.fontSize;
                float capMm = size * t.transform.lossyScale.y * 1000f * 0.7f;     // cap height ~0.7 of the point size
                Assert.GreaterOrEqual(capMm, 8f - 0.01f, $"{Path(t.transform)} \"{t.text}\": cap {capMm:F1} mm");
            }
        }

        [UnityTest]
        public IEnumerator PlayerCannotMove()
        {
            yield return LoadStall();
            var moving = Object.FindObjectsByType<LocomotionProvider>(FindObjectsSortMode.None).Where(p => p.isActiveAndEnabled).ToArray();
            Assert.IsEmpty(moving, "stationary game: no move/turn/teleport (" + string.Join(", ", moving.Select(m => m.GetType().Name)) + ")");
        }

        [UnityTest]
        public IEnumerator SeatedLowersTheStall()
        {
            yield return LoadStall();
            var stall = Object.FindAnyObjectByType<StanceHeight>().transform;
            float standing = stall.position.y;
            GameSettings.Seated = true;
            Assert.AreEqual(standing - GameSettings.SeatedDrop, stall.position.y, 1e-4f);
            GameSettings.Seated = false;
            Assert.AreEqual(standing, stall.position.y, 1e-4f);
        }

        // The still stall decor is drawn as combined batches (BatchDecor), and seated mode still lowers it with the stall.
        [UnityTest]
        public IEnumerator StallDecorIsCombinedAndStillDropsWhenSeated()
        {
            yield return LoadStall();
            var batch = Object.FindAnyObjectByType<BatchDecor>();
            Assert.IsNotNull(batch, "the stall root combines its decor");
            Assert.Greater(batch.Combined, 50, "most of the decor is combined");
            var cart = batch.decor.Select(g => g.GetComponent<MeshRenderer>()).First(r => r.GetComponentsInParent<Transform>().Any(t => t.name == "PushCart"));
            Assert.IsTrue(cart.isPartOfStaticBatch, "the cart is in a combined batch");
            float standing = cart.bounds.center.y;
            GameSettings.Seated = true;
            yield return null;
            Assert.AreEqual(standing - GameSettings.SeatedDrop, cart.bounds.center.y, 1e-3f, "the combined cart drops with the stall");
            GameSettings.Seated = false;
            yield return null;
            Assert.AreEqual(standing, cart.bounds.center.y, 1e-3f);
        }

        [UnityTest]
        public IEnumerator SceneOpensOnTheMainMenuNotAShift()
        {
            yield return LoadStall();
            var orders = Object.FindAnyObjectByType<OrderManager>();
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(orders.Running, "no shift until the player presses Start");
            Assert.IsNull(orders.Current);
        }

        // ---------- OrderManager pause / exit (no scene) ----------

        static OrderManager Shift(float shiftSeconds, float patience)
        {
            var l = ScriptableObject.CreateInstance<LevelDefinition>();
            l.shiftSeconds = shiftSeconds; l.customers = 0; l.patienceSeconds = patience; l.target = 400; l.fruits = new[] { "Guava" };
            var o = new GameObject("Orders").AddComponent<OrderManager>();
            o.customer = new GameObject("Customer");
            o.menu = new[] { new FruitMenuItem() };
            o.levels = new[] { l };
            return o;
        }

        [UnityTest]
        public IEnumerator PauseFreezesShiftTimerAndPatience()
        {
            var o = Shift(10f, 5f);
            yield return null; yield return null;
            GamePause.Set(true);
            float time = o.TimeLeft, patience = o.Patience01;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(time, o.TimeLeft, "shift timer stopped");
            Assert.AreEqual(patience, o.Patience01, "patience stopped");
            GamePause.Set(false);
            yield return new WaitForSeconds(0.1f);
            Assert.Less(o.TimeLeft, time, "runs again after resume");
            Object.Destroy(o.customer); Object.Destroy(o.gameObject);
        }

        [UnityTest]
        public IEnumerator ExitToMenuClearsTheCounterAndStartLevelBeginsAgain()
        {
            var o = Shift(10f, 5f);
            o.autoNextDelay = 0.05f;
            yield return null;
            Assert.IsNotNull(o.Current);
            o.StopShift();
            Assert.IsFalse(o.Running);
            Assert.IsNull(o.Current);
            yield return new WaitForSeconds(0.2f);
            Assert.IsNull(o.Current, "no customer comes while in the menu");
            o.StartLevel(0);
            Assert.IsTrue(o.Running);
            Assert.IsNotNull(o.Current);
            Object.Destroy(o.customer); Object.Destroy(o.gameObject);
        }

        // ---------- helpers ----------

        static Vector2 WorldSize(RectTransform r) => Vector2.Scale(r.rect.size, r.lossyScale);

        static float GapMm(RectTransform a, RectTransform b)
        {
            Rect ra = WorldRect(a), rb = WorldRect(b);
            float dx = Mathf.Max(0, Mathf.Max(ra.xMin - rb.xMax, rb.xMin - ra.xMax));
            float dy = Mathf.Max(0, Mathf.Max(ra.yMin - rb.yMax, rb.yMin - ra.yMax));
            return Mathf.Max(dx, dy) * 1000f;
        }

        // In the parent's plane (siblings share it).
        static Rect WorldRect(RectTransform r)
        {
            var s = (Vector2)r.lossyScale;
            var size = Vector2.Scale(r.rect.size, s);
            var c = Vector2.Scale(r.localPosition, (Vector2)r.parent.lossyScale);
            return new Rect(c - size / 2f, size);
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    }
}
