using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SliceAndSell.Tests
{
    // Customers stand with their arms relaxed at their sides (Idle), never in the glTF T-pose: as saved in the prefab
    // (what the editor and the first frame show) and while Play mode runs Idle.
    public class CustomerPoseTests
    {
        static readonly string[] Prefabs = { "Backpacker", "Courier", "Salaryman", "Student" };

        // How far an upper arm points down: 1 = straight down, 0 = straight out (T-pose).
        static float ArmDown(Transform model, string side)
        {
            var bones = model.GetComponentsInChildren<Transform>();
            var arm = bones.First(t => t.name == side + "Arm");
            var fore = bones.First(t => t.name == side + "ForeArm");
            return Vector3.Dot((fore.position - arm.position).normalized, Vector3.down);
        }

        [UnityTest]
        public IEnumerator ArmsHangAtTheSidesSavedAndPlaying()
        {
            foreach (var name in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Customers/{name}.prefab");
                Assert.IsNotNull(prefab, name);
                var go = Object.Instantiate(prefab);
                foreach (var side in new[] { "Left", "Right" })
                    Assert.Greater(ArmDown(go.transform, side), 0.7f, $"{name} {side} arm as saved");
                for (int i = 0; i < 30; i++) yield return null;      // Idle playing
                foreach (var side in new[] { "Left", "Right" })
                    Assert.Greater(ArmDown(go.transform, side), 0.7f, $"{name} {side} arm while Idle plays");
                Object.Destroy(go);
            }
        }
    }
}
