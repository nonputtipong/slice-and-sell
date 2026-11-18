using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SliceAndSell.Tests
{
    // Dwell to press (GameSettings.Dwell): resting a pointer on a button for DwellPress.Seconds presses it once.
    public class DwellTests
    {
        bool saved;
        GameObject go;
        int clicks;

        [SetUp]
        public void SetUp()
        {
            saved = GameSettings.Dwell;     // a PlayerPrefs value: put the player's own back afterwards
            clicks = 0;
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.Dwell = saved;
            if (go) Object.Destroy(go);
        }

        DwellPress UiButton()
        {
            go = new GameObject("Dwell button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.GetComponent<Button>().onClick.AddListener(() => clicks++);
            return go.AddComponent<DwellPress>();
        }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        [UnityTest]
        public IEnumerator OffByDefaultSettingDoesNothing()
        {
            GameSettings.Dwell = false;
            var d = UiButton();
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds + 0.3f);
            Assert.AreEqual(0, clicks, "dwell is off: hovering never presses");
        }

        [UnityTest]
        public IEnumerator HoverPressesOncePerVisit()
        {
            GameSettings.Dwell = true;
            var d = UiButton();
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds * 0.5f);
            Assert.AreEqual(0, clicks, "not yet");
            Assert.Greater(d.Progress, 0.3f, "the bar is filling");
            yield return Wait(DwellPress.Seconds * 0.5f + 0.2f);
            Assert.AreEqual(1, clicks, "pressed after a second");
            yield return Wait(DwellPress.Seconds + 0.3f);
            Assert.AreEqual(1, clicks, "staying on it does not press again");

            d.OnPointerExit(null);
            yield return null;
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds + 0.2f);
            Assert.AreEqual(2, clicks, "off and back on: pressed again");
        }

        [UnityTest]
        public IEnumerator LeavingEarlyCancels()
        {
            GameSettings.Dwell = true;
            var d = UiButton();
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds * 0.6f);
            d.OnPointerExit(null);
            yield return null;
            Assert.AreEqual(0f, d.Progress, "the bar empties");
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds * 0.6f);
            Assert.AreEqual(0, clicks, "two short visits do not add up");
        }

        [UnityTest]
        public IEnumerator ObjectWithoutAButtonCallsOnDwell()
        {
            GameSettings.Dwell = true;
            go = new GameObject("Bell");
            var d = go.AddComponent<DwellPress>();
            d.onDwell.AddListener(() => clicks++);
            d.OnPointerEnter(null);
            yield return Wait(DwellPress.Seconds + 0.2f);
            Assert.AreEqual(1, clicks);
        }
    }
}
