using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliceAndSell
{
    // End-of-shift results (GDD > world-space displays): saves progress, shows earnings vs target and stars.
    // It hangs in front of the order card while shown. Lives on an always-active object; `board` is the canvas.
    public class ResultsBoard : MonoBehaviour
    {
        public OrderManager orders;
        public GameObject board;
        public TMP_Text heading, levelLabel, earnings;
        public Image[] stars;
        public Button nextButton;

        void OnEnable() => GameEvents.ShiftEnded += OnShiftEnded;
        void OnDisable() => GameEvents.ShiftEnded -= OnShiftEnded;
        void Start() => board.SetActive(false);

        // Retry, Next, Menu, the bell and the wrist menu all start or stop a shift, which clears ShiftOver.
        void Update() { if (board.activeSelf && !orders.ShiftOver) board.SetActive(false); }

        void OnShiftEnded(bool won, int starCount)
        {
            int i = orders.levelIndex;
            Progress.Record(i, won, starCount);
            heading.text = won ? "Shift won!" : "Shift over";
            levelLabel.text = $"Level {i + 1}: {orders.Level.title}";
            earnings.text = $"Earned ฿{orders.Money}    Target ฿{orders.Level.target}";
            for (int s = 0; s < stars.Length; s++) stars[s].color = s < starCount ? MainMenu.StarOn : MainMenu.StarOff;
            nextButton.gameObject.SetActive(won && i + 1 < orders.levels.Length);
            board.SetActive(true);
        }
    }
}
