using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceAndSell
{
    // The painted cart sign (GDD > Shop-sign main menu): up while no shift runs, and it is what starts shifts.
    // Lives on an always-active object; `sign` is the canvas, hidden (not moved) during a shift. Built by MenuBuilder.
    public class MainMenu : MonoBehaviour
    {
        public static readonly Color StarOn = new Color(1f, 0.78f, 0.15f), StarOff = new Color(0.62f, 0.5f, 0.38f, 0.7f);   // empty star: visible on the cream buttons and the wood board

        public OrderManager orders;
        public GameObject sign, mainPage, levelsPage, howToPage, settingsPage;
        public TMP_Text startLabel, stanceLabel, volumeLabel;
        public Button[] levelButtons;
        public Image[] levelStars;          // 3 per level button

        void OnEnable() => GameSettings.Changed += Refresh;
        void OnDisable() => GameSettings.Changed -= Refresh;
        void Start() { if (orders.autoStart) Hide(); else Show(); }

        public void Show() { sign.SetActive(true); ShowMain(); }
        public void Hide() => sign.SetActive(false);

        public void ShowMain() => Page(mainPage);
        public void ShowLevels() => Page(levelsPage);
        public void ShowHowTo() => Page(howToPage);
        public void ShowSettings() => Page(settingsPage);

        public void StartGame() => PlayLevel(Progress.FirstUnfinished(orders.levels.Length));

        public void PlayLevel(int i)
        {
            if (!Progress.Unlocked(i)) return;
            Hide();
            orders.StartLevel(i);
        }

        public void ToggleStance() => GameSettings.ToggleSeated();
        public void VolumeUp() => GameSettings.VolumeUp();
        public void VolumeDown() => GameSettings.VolumeDown();

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Page(GameObject page)
        {
            foreach (var p in new[] { mainPage, levelsPage, howToPage, settingsPage }) p.SetActive(p == page);
            Refresh();
        }

        void Refresh()
        {
            var levels = orders.levels;
            int next = Progress.FirstUnfinished(levels.Length);
            startLabel.text = $"Start\n<size=75%>Level {next + 1}: {levels[next].title}</size>";
            for (int i = 0; i < levelButtons.Length; i++)
            {
                bool open = Progress.Unlocked(i);
                levelButtons[i].interactable = open;
                levelButtons[i].GetComponentInChildren<TMP_Text>().text = $"{i + 1}. {levels[i].title}" + (open ? "" : "\n<size=80%>Locked</size>");
                for (int s = 0; s < 3; s++)
                {
                    var star = levelStars[i * 3 + s];
                    star.enabled = open;
                    star.color = s < Progress.BestStars(i) ? StarOn : StarOff;
                }
            }
            stanceLabel.text = "Stance: " + (GameSettings.Seated ? "Seated" : "Standing");
            volumeLabel.text = $"Volume {Mathf.RoundToInt(GameSettings.Volume * 100)}%";
        }
    }
}
