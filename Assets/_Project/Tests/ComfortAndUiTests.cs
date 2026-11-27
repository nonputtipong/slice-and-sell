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

        // Dwell to press reaches every VR button, the service bell and the radio; the setting is on both menus.
        [UnityTest]
        public IEnumerator EveryButtonTakesDwell()
        {
            yield return LoadStall();
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(b => b.GetComponentInParent<Canvas>(true).rootCanvas.renderMode == RenderMode.WorldSpace).ToArray();
            foreach (var b in buttons)
            {
                var d = b.GetComponent<DwellPress>();
                Assert.IsNotNull(d, $"{Path(b.transform)} has no DwellPress");
                Assert.IsNotNull(d.fill, $"{Path(b.transform)} has no dwell bar");
            }
            foreach (var target in new Component[] { Object.FindAnyObjectByType<ServiceBell>(), Object.FindAnyObjectByType<StreetAudio>().radioSwitch })
            {
                var d = target.GetComponent<DwellPress>();
                Assert.IsNotNull(d, $"{target.name} has no DwellPress");
                Assert.AreEqual(1, d.onDwell.GetPersistentEventCount(), $"{target.name}: dwell does what a poke does");
            }
            Assert.IsNotNull(Object.FindAnyObjectByType<WristMenu>(FindObjectsInactive.Include).dwellLabel, "wrist menu has the dwell toggle");
            Assert.IsNotNull(Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include).dwellLabel, "shop sign settings have the dwell toggle");
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

        // #12: grabbables interpolate (50 Hz physics, 72-90 Hz display), and that never drags a teleport or the seated drop
        // over a frame: a tool sent back to its rack is drawn at the rack, and resting tools drop with the stall.
        [UnityTest]
        public IEnumerator GrabbablesInterpolateWithoutLaggingTeleports()
        {
            yield return LoadStall();
            var grabbables = Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(FindObjectsSortMode.None)
                .Select(g => g.gameObject)
                .Concat(Object.FindObjectsByType<Dispenser>(FindObjectsSortMode.None).Select(d => d.prefab)).ToArray();
            Assert.GreaterOrEqual(grabbables.Length, 9);
            foreach (var g in grabbables)
                Assert.AreEqual(RigidbodyInterpolation.Interpolate, g.GetComponent<Rigidbody>().interpolation, g.name);

            var knife = Object.FindAnyObjectByType<KnifeGrab>().GetComponent<ReturnToRack>();
            yield return new WaitForSeconds(0.5f);                          // settled on the rack
            Vector3 home = knife.transform.position;
            knife.transform.position += new Vector3(0.5f, 0.3f, 0f);
            yield return new WaitForFixedUpdate(); yield return null;
            knife.Return();
            for (int f = 0; f < 3; f++)
            {
                yield return null;
                Assert.Less(Vector3.Distance(home, knife.transform.position), 0.01f, $"frame {f}: drawn at the rack, not on the way back");
            }

            yield return new WaitForSeconds(0.5f);
            float standing = knife.transform.position.y;
            GameSettings.Seated = true;
            for (int f = 0; f < 3; f++)
            {
                yield return null;
                Assert.AreEqual(standing - GameSettings.SeatedDrop, knife.transform.position.y, 0.01f, $"frame {f}: the resting knife drops with the stall");
            }
        }

        // The still stall decor is drawn as combined batches (BatchDecor), and seated mode still lowers it with the stall.
        [UnityTest]
        public IEnumerator StallDecorIsCombinedAndStillDropsWhenSeated()
        {
            yield return LoadStall();
            var batch = Object.FindAnyObjectByType<BatchDecor>();
            Assert.IsNotNull(batch, "the stall root combines its decor");
            Assert.GreaterOrEqual(batch.Combined, 45, "most of the decor is combined");   // 50 since the bowl stack (4 crate walls) went
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

        // Baked occlusion (BuildAndBake): looking down the soi, the shophouses hide part of the street from the camera.
        [UnityTest]
        public IEnumerator BakedOcclusionCullsTheHiddenStreet()
        {
            yield return LoadStall();
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None;   // a shadow pass also counts as visible
            var cam = new GameObject("Probe camera").AddComponent<Camera>();
            cam.fieldOfView = 100f; cam.farClipPlane = 80f;
            cam.targetTexture = new RenderTexture(256, 256, 24);
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var visible = new int[2];
            for (int oc = 0; oc < 2; oc++)
            {
                cam.useOcclusionCulling = oc == 1;
                yield return null; yield return null;
                visible[oc] = renderers.Count(r => r.isVisible);
            }
            Assert.Less(visible[1], visible[0] - 10, $"occlusion culling hides street renderers ({visible[0]} -> {visible[1]})");
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

        // Playtest: no wrist gesture. The MENU sign on the cart opens the menu in front of the eyes; Pause keeps it open
        // (Close greyed out) until Resume; customers freeze while paused.
        [UnityTest]
        public IEnumerator MenuSignOpensTheMenuAndPauseKeepsItOpen()
        {
            yield return LoadStall();
            var menu = Object.FindAnyObjectByType<WristMenu>(FindObjectsInactive.Include);
            var sign = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(b => b.name == "MENU button");
            Assert.IsTrue(sign.GetComponentInParent<StanceHeight>(true), "the sign is on the stall (seated mode lowers it)");
            Assert.IsFalse(menu.panel.gameObject.activeSelf, "closed at start");
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(menu.panel.gameObject.activeSelf, "nothing opens it but a button");

            sign.onClick.Invoke();
            Assert.IsTrue(menu.IsOpen && menu.panel.gameObject.activeSelf, "the sign opens it");
            var toPanel = menu.panel.position - menu.head.position;
            Assert.Less(toPanel.magnitude, 0.6f, "in front of the eyes, in poke reach");
            Assert.Greater(Vector3.Dot(toPanel.normalized, menu.head.forward), 0.5f, "where you look");

            menu.TogglePause();
            Assert.IsTrue(GamePause.Paused);
            Assert.AreEqual("Resume", menu.pauseLabel.text);
            Assert.AreEqual("Paused", menu.titleLabel.text);
            Assert.IsFalse(menu.closeButton.interactable, "Close is greyed out while paused");
            menu.Close(); sign.onClick.Invoke();
            Assert.IsTrue(menu.IsOpen, "stays open while paused, so Resume is in reach");

            menu.TogglePause();
            Assert.IsFalse(GamePause.Paused);
            Assert.IsTrue(menu.closeButton.interactable);
            menu.closeButton.onClick.Invoke();
            Assert.IsFalse(menu.IsOpen || menu.panel.gameObject.activeSelf, "Close closes it");
        }

        [UnityTest]
        public IEnumerator PausedCustomersStandStill()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Customers/Salaryman.prefab");
            var c = Object.Instantiate(prefab).GetComponent<Customer>();
            c.Arrive(Vector3.zero, new Vector3(0, 0, 5f), null);
            yield return new WaitForSeconds(0.2f);
            GamePause.Set(true);
            var at = c.transform.position;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(at, c.transform.position, "no walking while paused");
            GamePause.Set(false);
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(c.transform.position.z, at.z, "walks on after resume");
            Object.Destroy(c.gameObject);
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
