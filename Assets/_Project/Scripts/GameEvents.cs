using System;
using UnityEngine;

namespace SliceAndSell
{
    public struct CutInfo
    {
        public bool power;      // blade faster than Knife.powerCutSpeed (controllers)
        public bool clean;      // within the guide's angle and position tolerance
        public Vector3 point;   // where the blade was when the cut completed (world)
    }

    // One place for gameplay moments that effects, audio and UI react to.
    // Raised by gameplay code only (Fruit, Bowl, OrderManager, Scoring); listeners never change game state.
    public static class GameEvents
    {
        public static event Action<Fruit, CutInfo, int> Cut;                 // fruit, cut, combo after this cut
        public static event Action<Vector3> CutMissed;                       // blade went through but off the guide's angle ("thunk")
        public static event Action<Vector3, int, int, bool> Paid;            // where, amount incl. tip, tip, order correct
        public static event Action<Vector3> Refused;                         // empty bowl handed over
        public static event Action<Vector3> WalkedOut;                       // customer ran out of patience
        public static event Action<Vector3, string> AddedToBowl;             // bowl position, a fruit name or "Chili" / "Skewer"
        public static event Action<bool, int> ShiftEnded;                    // won, stars (0-3)

        public static void RaiseCut(Fruit f, CutInfo c, int combo) => Cut?.Invoke(f, c, combo);
        public static void RaiseCutMissed(Vector3 at) => CutMissed?.Invoke(at);
        public static void RaisePaid(Vector3 at, int amount, int tip, bool correct) => Paid?.Invoke(at, amount, tip, correct);
        public static void RaiseRefused(Vector3 at) => Refused?.Invoke(at);
        public static void RaiseWalkedOut(Vector3 at) => WalkedOut?.Invoke(at);
        public static void RaiseAddedToBowl(Vector3 at, string what) => AddedToBowl?.Invoke(at, what);
        public static void RaiseShiftEnded(bool won, int stars) => ShiftEnded?.Invoke(won, stars);
    }
}
