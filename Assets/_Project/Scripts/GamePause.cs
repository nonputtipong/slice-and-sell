using System;
using UnityEngine;

namespace SliceAndSell
{
    // Game-level pause: shift timer, patience and customers stop; physics and XR input keep running,
    // so menus stay usable with poke and ray (Time.timeScale is never touched).
    public static class GamePause
    {
        public static bool Paused { get; private set; }
        public static event Action<bool> Changed;

        public static void Set(bool paused)
        {
            if (paused == Paused) return;
            Paused = paused;
            Changed?.Invoke(paused);
        }

        public static void Toggle() => Set(!Paused);

        // Statics survive between play sessions when domain reload is off; start every session unpaused.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() { Paused = false; Changed = null; }
    }
}
