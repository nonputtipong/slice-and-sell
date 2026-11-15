using UnityEngine;

namespace SliceAndSell
{
    // Saved progress (PlayerPrefs): best stars per level, and which levels are open.
    // Level 0 is always open; level i+1 opens once level i has been won.
    public static class Progress
    {
        public static int BestStars(int level) => PlayerPrefs.GetInt("bestStars" + level, 0);
        public static bool Won(int level) => PlayerPrefs.GetInt("won" + level, 0) == 1;
        public static bool Unlocked(int level) => level == 0 || Won(level - 1);

        public static void Record(int level, bool won, int stars)
        {
            if (stars > BestStars(level)) PlayerPrefs.SetInt("bestStars" + level, stars);
            if (won) PlayerPrefs.SetInt("won" + level, 1);
            PlayerPrefs.Save();     // the Quest can kill the app from its own menu without OnApplicationQuit
        }

        // What Start plays: the first level not won yet (always unlocked), or the last one once all are won.
        public static int FirstUnfinished(int levelCount)
        {
            for (int i = 0; i < levelCount; i++) if (!Won(i)) return i;
            return levelCount - 1;
        }
    }
}
