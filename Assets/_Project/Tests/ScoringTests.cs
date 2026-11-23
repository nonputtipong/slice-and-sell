using NUnit.Framework;

namespace SliceAndSell.Tests
{
    // GDD > Combos and scoring.
    public class ScoringTests
    {
        static CutInfo Clean(bool power = false) => new CutInfo { clean = true, power = power };
        static readonly CutInfo Messy = new CutInfo { clean = false };

        [Test]
        public void CleanCutAddsComboAndTwoBaht()
        {
            var s = new Scoring();
            Assert.AreEqual(2, s.OnCut(Clean()));
            Assert.AreEqual(1, s.Combo);
            Assert.AreEqual(2, s.Money);
        }

        [Test]
        public void PowerCutAddsOneOnTopOfClean() => Assert.AreEqual(3, new Scoring().OnCut(Clean(power: true)));

        [Test]
        public void MessyCutResetsComboAndPaysNothing()
        {
            var s = new Scoring();
            s.OnCut(Clean()); s.OnCut(Clean());
            Assert.AreEqual(0, s.OnCut(new CutInfo { clean = false, power = true }), "power without clean earns nothing");
            Assert.AreEqual(0, s.Combo);
            Assert.AreEqual(4, s.Money);
        }

        [TestCase(0, 1f), TestCase(4, 1f), TestCase(5, 1.2f), TestCase(9, 1.2f), TestCase(10, 1.5f), TestCase(19, 1.5f), TestCase(20, 2f)]
        public void ComboMultiplierSteps(int combo, float expected) => Assert.AreEqual(expected, Scoring.Multiplier(combo));

        [TestCase(1f, 5), TestCase(0.7f, 5), TestCase(0.5f, 2), TestCase(0.34f, 2), TestCase(0.2f, 0), TestCase(0f, 0)]
        public void TipByPatienceThird(float patience, int tip) => Assert.AreEqual(tip, Scoring.Tip(patience));

        [Test]
        public void CorrectOrderUsesMultiplierAndTip()
        {
            var s = new Scoring();
            for (int i = 0; i < 5; i++) s.OnCut(Clean());   // combo 5 -> x1.2, money 10
            var (amount, tip) = s.OnServed(25, true, tipsOn: true, patience01: 0.9f);
            Assert.AreEqual(5, tip);
            Assert.AreEqual(30 + 5, amount);                 // 25 x 1.2 = 30, + tip
            Assert.AreEqual(10 + 35, s.Money);
            Assert.AreEqual(5, s.Combo, "serving keeps the combo");
        }

        [Test]
        public void WrongOrderPaysBaseOnlyAndBreaksCombo()
        {
            var s = new Scoring();
            for (int i = 0; i < 6; i++) s.OnCut(Clean());
            var (amount, tip) = s.OnServed(25, false, tipsOn: true, patience01: 1f);
            Assert.AreEqual(25, amount);
            Assert.AreEqual(0, tip);
            Assert.AreEqual(0, s.Combo);
        }

        [Test]
        public void NoTipsWhilePatienceIsPaused() => Assert.AreEqual((20, 0), new Scoring().OnServed(20, true, tipsOn: false, patience01: 1f));

        [Test]
        public void WalkOutBreaksCombo()
        {
            var s = new Scoring(); s.OnCut(Clean()); s.OnWalkedOut();
            Assert.AreEqual(0, s.Combo);
        }

        [TestCase(399, 400, 0), TestCase(400, 400, 1), TestCase(500, 400, 2), TestCase(600, 400, 3)]
        public void StarsAtTargetAnd125And150Percent(int money, int target, int stars) => Assert.AreEqual(stars, Scoring.Stars(money, target));
    }
}
