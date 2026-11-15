using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SliceAndSell.Tests
{
    // The fruits come from Blender (Tools/Blender/make_fruits.py) and their guides from fruits.json.
    // These tests check the two agree after Unity's glTF import (axis conventions included): every piece
    // of stage k lies entirely on one side of each of the first k guide planes.
    public class FruitModelTests
    {
        static readonly string[] Names = { "Guava", "Mango", "Pineapple", "Watermelon" };
        const float Tolerance = 0.0015f;   // m

        static Fruit Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/{name}.prefab").GetComponent<Fruit>();

        [Test]
        public void StagesMatchGuides([ValueSource(nameof(Names))] string name)
        {
            var f = Load(name);
            Assert.AreEqual(f.guides.Length + 1, f.stages.Length);
            Assert.IsTrue(f.stages[0].activeSelf && f.stages.Skip(1).All(s => !s.activeSelf), "only the whole fruit shows at first");
        }

        [Test]
        public void EveryPieceSitsOnOneSideOfEachDoneCut([ValueSource(nameof(Names))] string name)
        {
            var f = Load(name);
            var root = f.transform;
            for (int k = 1; k < f.stages.Length; k++)
            {
                foreach (var mf in f.stages[k].GetComponentsInChildren<MeshFilter>(true))
                {
                    var verts = mf.sharedMesh.vertices.Select(v => root.InverseTransformPoint(mf.transform.TransformPoint(v))).ToArray();
                    for (int j = 0; j < k; j++)
                    {
                        var g = f.guides[j];
                        var n = g.normal.normalized;
                        float min = verts.Min(v => Vector3.Dot(v - g.point, n)), max = verts.Max(v => Vector3.Dot(v - g.point, n));
                        Assert.IsTrue(min > -Tolerance || max < Tolerance,
                            $"{name} stage {k} piece {mf.name} straddles cut {j} (signed distance {min:F4} .. {max:F4} m)");
                    }
                }
            }
        }

        [Test]
        public void EachCutAddsOnePiece([ValueSource(nameof(Names))] string name)
        {
            var f = Load(name);
            // Pineapple: the crown comes off at cut 1 (Stage1 = body + crown), then the crown is discarded.
            for (int k = 1; k < f.stages.Length; k++)
            {
                int pieces = f.stages[k].GetComponentsInChildren<MeshFilter>(true).Select(m => m.transform.parent == f.stages[k].transform ? m.transform : m.transform.parent).Distinct().Count();
                int expected = name == "Pineapple" ? (k == 1 ? 2 : k) : k + 1;
                Assert.AreEqual(expected, pieces, $"{name} stage {k}");
            }
        }

        [Test]
        public void FruitRestsOnItsOriginAndFitsTheBoard([ValueSource(nameof(Names))] string name)
        {
            var f = Load(name);
            var b = new Bounds();
            bool first = true;
            foreach (var mf in f.stages[0].GetComponentsInChildren<MeshFilter>(true))
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = f.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            Assert.AreEqual(0f, b.min.y, 0.002f, "bottom at y = 0 (rests on the board)");
            if (name == "Watermelon")  // too long for the fruit socket: lies on the 0.32 x 0.22 m board instead
                Assert.IsTrue(b.size.x < 0.30f && b.size.z < 0.20f, $"fits the board ({b.size.x:F3} x {b.size.z:F3} m)");
            else
                Assert.Less(Mathf.Max(b.size.x, b.size.z), 0.18f, "fits the fruit socket");
        }
    }
}
