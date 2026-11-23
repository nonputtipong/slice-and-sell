using UnityEngine;

namespace SliceAndSell
{
    // One shift (GDD > Level plan). Data only; OrderManager runs it.
    [CreateAssetMenu(menuName = "Slice & Sell/Level")]
    public class LevelDefinition : ScriptableObject
    {
        public string title = "Opening Day";
        public float shiftSeconds;          // 0 = no shift timer (Level 1)
        public int customers = 3;           // 0 = keep coming until the shift ends
        public string[] fruits = { "Guava" };
        public int target = 60;             // THB to win (Level 1: serving every customer is the goal)
        public float patienceSeconds;       // 0 = patience paused, no tips (Level 1)
        public bool skewer;                 // every bowl needs a skewer
        [Range(0f, 1f)] public float chiliChance;
        public bool queue;                  // a second customer waits in line at OrderManager.queueSpot (Level 3); still one order at a time
        public int seed = 1;                // same order sequence every run (repeatable tests and walkthroughs)
    }

    [System.Serializable]
    public class FruitMenuItem
    {
        public string name = "Guava";
        public int cuts = 2;
        public int price = 20;
    }
}
