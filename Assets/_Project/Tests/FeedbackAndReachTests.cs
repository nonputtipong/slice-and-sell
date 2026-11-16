using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SliceAndSell.Tests
{
    // Out-of-reach tools come back to the rack; grabbables and sockets show hover / select / release. No scene: built in code.
    public class FeedbackAndReachTests
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        XRInteractionManager manager;
        Transform stall, board;
        readonly List<GameObject> made = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 72f;
            manager = Make("Interaction manager").AddComponent<XRInteractionManager>();
            stall = Make("Stall").transform;
            stall.position = new Vector3(50f, 0f, 0f);     // away from any Stall scene an earlier test left loaded
            board = new GameObject("Board").transform;
            board.SetParent(stall, false); board.localPosition = new Vector3(0, 0.9f, 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (var go in made) Object.Destroy(go);
            made.Clear();
        }

        // ---------- ReturnToRack ----------

        [UnityTest]
        public IEnumerator ToolOutOfReachReturnsAfterTwoSeconds()
        {
            var (tool, home) = Tool();
            tool.transform.position = board.position + new Vector3(1.5f, 0, 0);
            yield return Seconds(1.9f);
            Assert.Greater(Vector3.Distance(tool.transform.position, home), 1f, "still where it was at 1.9 s");
            yield return Seconds(0.2f);
            Assert.Less(Vector3.Distance(tool.transform.position, home), 0.001f, "back on the rack after 2 s");
        }

        [UnityTest]
        public IEnumerator ToolBelowTheCounterReturns()
        {
            var (tool, home) = Tool();
            tool.transform.position = board.position + new Vector3(0.2f, -0.6f, 0);
            yield return Seconds(2.1f);
            Assert.Less(Vector3.Distance(tool.transform.position, home), 0.001f);
        }

        [UnityTest]
        public IEnumerator ToolOnTheCounterNearTheBoardStays()
        {
            var (tool, _) = Tool();
            var spot = board.position + new Vector3(0.3f, 0.01f, -0.1f);
            tool.transform.position = spot;
            yield return Seconds(2.5f);
            Assert.Less(Vector3.Distance(tool.transform.position, spot), 0.001f);
        }

        [UnityTest]
        public IEnumerator HeldToolDoesNotReturn()
        {
            var (tool, home) = Tool();
            var hand = Hand("Hand", board.position + new Vector3(1.5f, 0, 0));
            yield return null;
            hand.StartManualInteraction((IXRSelectInteractable)tool);   // keeps the select alive with no trigger pressed
            yield return Seconds(2.5f);
            Assert.IsTrue(tool.isSelected, "still held");
            Assert.Greater(Vector3.Distance(tool.transform.position, home), 1f, "held tool is not pulled back");
            hand.EndManualInteraction();
        }

        // ---------- ItemFeedback ----------

        [UnityTest]
        public IEnumerator ItemFeedbackTintsEveryRendererAndMaterial()
        {
            var item = Make("Fruit"); item.SetActive(false);
            item.AddComponent<BoxCollider>();
            item.AddComponent<Rigidbody>().useGravity = false;
            var grab = item.AddComponent<XRGrabInteractable>();
            grab.interactionManager = manager;
            grab.selectMode = InteractableSelectMode.Multiple;     // two-handed cleaver
            grab.throwOnDetach = false;
            var whole = Piece(item.transform, "Whole", true);
            var halves = Piece(item.transform, "Halves", false);   // a later cut stage
            item.AddComponent<ItemFeedback>();
            item.SetActive(true);
            var left = Hand("Left", Vector3.one * 5f); var right = Hand("Right", Vector3.one * 5f);
            yield return null;

            var rends = new[] { whole, halves };
            var originals = Originals(rends);

            manager.HoverEnter((IXRHoverInteractor)left, grab);
            AssertAllTinted(rends, originals, "hover");
            var hoverColour = Block(whole, 0);

            manager.SelectEnter((IXRSelectInteractor)left, grab);
            AssertAllTinted(rends, originals, "select");
            Assert.IsFalse(Near(hoverColour, Block(whole, 0)), "select looks different from hover");
            var selectColour = Block(whole, 0);

            manager.SelectEnter((IXRSelectInteractor)right, grab);
            Assert.AreEqual(2, grab.interactorsSelecting.Count, "both hands hold it");
            manager.SelectExit((IXRSelectInteractor)left, grab);
            Assert.IsTrue(Near(selectColour, Block(whole, 0)), "one hand still holds it: still selected");

            halves.gameObject.SetActive(true);                  // the cut swaps stages mid-hold
            AssertAllTinted(rends, originals, "after the stage swap");

            manager.SelectExit((IXRSelectInteractor)right, grab);
            manager.HoverExit((IXRHoverInteractor)left, grab);
            for (int r = 0; r < rends.Length; r++)
                for (int m = 0; m < 2; m++)
                    Assert.IsTrue(Cleared(rends[r], m), $"{rends[r].name}[{m}] back to its own colour: no override left (SRP Batcher)");
        }

        // ---------- SocketFeedback ----------

        [UnityTest]
        public IEnumerator SocketFeedbackHighlightsFlashesAndRestores()
        {
            var socketGo = Make("Tray socket"); socketGo.SetActive(false);
            socketGo.AddComponent<BoxCollider>().isTrigger = true;
            var socket = socketGo.AddComponent<XRSocketInteractor>();
            socket.interactionManager = manager;
            var tray = Piece(stall, "Tray", true);
            socketGo.AddComponent<SocketFeedback>().targets = new Renderer[] { tray };
            socketGo.SetActive(true);

            var bag = Make("Bag"); bag.transform.position = Vector3.one * 5f;   // far from the trigger: no auto snap
            bag.AddComponent<BoxCollider>();
            bag.AddComponent<Rigidbody>().useGravity = false;
            var grab = bag.AddComponent<XRGrabInteractable>();
            grab.interactionManager = manager;
            yield return null;

            var original = Originals(new[] { tray })[0][0];
            manager.HoverEnter((IXRHoverInteractor)socket, grab);
            Assert.IsFalse(Near(original, Block(tray, 0)), "hover highlights the tray");
            var highlight = Block(tray, 0);

            manager.SelectEnter((IXRSelectInteractor)socket, grab);
            Assert.IsFalse(Near(original, Block(tray, 0)), "snap flashes");
            Assert.IsFalse(Near(highlight, Block(tray, 0)), "the flash is not the hover highlight");

            manager.SelectExit((IXRSelectInteractor)socket, grab);
            manager.HoverExit((IXRHoverInteractor)socket, grab);
            yield return Seconds(0.3f);                         // after the flash
            Assert.IsTrue(Cleared(tray, 0), "back to normal once the bag is taken out: no override left");
        }

        // ---------- helpers ----------

        (XRGrabInteractable, Vector3) Tool()
        {
            var go = new GameObject("Knife"); go.SetActive(false);
            go.transform.SetParent(stall);
            go.transform.position = board.position + new Vector3(0, 0.02f, -0.15f);
            go.AddComponent<BoxCollider>().size = new Vector3(0.03f, 0.03f, 0.2f);   // knife-sized, not the default 1 m cube
            go.AddComponent<Rigidbody>().useGravity = false;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.interactionManager = manager;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            go.AddComponent<ReturnToRack>().board = board;
            go.SetActive(true);
            return (grab, go.transform.position);
        }

        XRDirectInteractor Hand(string name, Vector3 at)
        {
            var go = Make(name); go.SetActive(false);
            go.transform.position = at;
            go.AddComponent<SphereCollider>().isTrigger = true;
            var d = go.AddComponent<XRDirectInteractor>();
            d.interactionManager = manager;
            go.SetActive(true);
            return d;
        }

        // A cube with two submeshes and two URP Lit materials, like a fruit stage (skin + flesh).
        static Renderer Piece(Transform parent, string name, bool active)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            var tris = mesh.triangles; int half = tris.Length / 2;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris[..half], 0);
            mesh.SetTriangles(tris[half..], 1);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterials = new[] { Lit(new Color(0.9f, 0.85f, 0.3f)), Lit(new Color(0.95f, 0.5f, 0.6f)) };
            go.SetActive(active);
            return r;
        }

        static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor(BaseColorId, c);
            return m;
        }

        static Color[][] Originals(Renderer[] rends)
        {
            var o = new Color[rends.Length][];
            for (int r = 0; r < rends.Length; r++)
            {
                o[r] = new Color[rends[r].sharedMaterials.Length];
                for (int m = 0; m < o[r].Length; m++) o[r][m] = rends[r].sharedMaterials[m].GetColor(BaseColorId);
            }
            return o;
        }

        static Color Block(Renderer r, int material)
        {
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b, material);
            return b.GetColor(BaseColorId);
        }

        // No property block on that slot: the renderer draws its shared material's own colour and stays SRP-batchable.
        static bool Cleared(Renderer r, int material)
        {
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b, material);
            return b.isEmpty && !r.HasPropertyBlock();
        }

        static void AssertAllTinted(Renderer[] rends, Color[][] originals, string state)
        {
            for (int r = 0; r < rends.Length; r++)
                for (int m = 0; m < originals[r].Length; m++)
                    Assert.IsFalse(Near(originals[r][m], Block(rends[r], m)), $"{state}: {rends[r].name}[{m}] is tinted");
        }

        // Property blocks round-trip colours through linear space, so compare with a tolerance.
        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.01f;

        GameObject Make(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go;
        }

        static IEnumerator Seconds(float s)
        {
            for (int i = Mathf.RoundToInt(s * 72f); i > 0; i--) yield return null;
        }
    }
}
