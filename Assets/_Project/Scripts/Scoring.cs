using UnityEngine;

namespace SliceAndSell
{
    // GDD > Combos and scoring. Plain class so the rules are testable without a scene.
    // All values are starting numbers to tune in playtests.
    public class Scoring
    {
        public int Money { get; private set; }
        public int Combo { get; private set; }

        public void Reset() { Money = 0; Combo = 0; }

        // Clean Cut: +1 combo, +2; Power Cut adds +1 on top of a clean one. Messy cut: combo resets, no bonus.
        public int OnCut(CutInfo cut)
        {
            if (!cut.clean) { Combo = 0; return 0; }
            Combo++;
            int bonus = 2 + (cut.power ? 1 : 0);
            Money += bonus;
            return bonus;
        }

        public static float Multiplier(int combo) => combo >= 20 ? 2f : combo >= 10 ? 1.5f : combo >= 5 ? 1.2f : 1f;

        // Patience ring thirds: green +5, yellow +2, red 0.
        public static int Tip(float patience01) => patience01 > 2f / 3f ? 5 : patience01 > 1f / 3f ? 2 : 0;

        // Correct: base x combo multiplier, plus a tip when tips are on (patience ring running).
        // Wrong order: base price only, no tip, combo resets.
        public (int amount, int tip) OnServed(int basePrice, bool correct, bool tipsOn, float patience01)
        {
            if (!correct) { Combo = 0; Money += basePrice; return (basePrice, 0); }
            int tip = tipsOn ? Tip(patience01) : 0;
            int amount = Mathf.RoundToInt(basePrice * Multiplier(Combo)) + tip;
            Money += amount;
            return (amount, tip);
        }

        public void OnWalkedOut() => Combo = 0;

        // Thief caught (+) or got away with the fruit (-); money never goes below zero.
        public void AddMoney(int amount) => Money = Mathf.Max(0, Money + amount);

        // 1 star at the target, 2 at 125%, 3 at 150%.
        public static int Stars(int money, int target) =>
            target <= 0 ? 3 : money >= target * 1.5f ? 3 : money >= target * 1.25f ? 2 : money >= target ? 1 : 0;
    }
}
