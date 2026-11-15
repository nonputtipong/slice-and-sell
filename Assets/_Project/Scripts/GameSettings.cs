using System;
using UnityEngine;

namespace SliceAndSell
{
    // Player settings shared by the wrist menu and the shop sign, saved between sessions.
    public static class GameSettings
    {
        public const float SeatedDrop = 0.2f;     // m the stall moves down when seated (GDD: counter and bins to arm height)
        const string VolumeKey = "volume", SeatedKey = "seated";

        public static event Action Changed;

        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 1f);
            set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); AudioListener.volume = Volume; Changed?.Invoke(); }
        }

        public static bool Seated
        {
            get => PlayerPrefs.GetInt(SeatedKey, 0) == 1;
            set { PlayerPrefs.SetInt(SeatedKey, value ? 1 : 0); Changed?.Invoke(); }
        }

        public static void VolumeUp() => Volume = Mathf.Round((Volume + 0.1f) * 10f) / 10f;
        public static void VolumeDown() => Volume = Mathf.Round((Volume - 0.1f) * 10f) / 10f;
        public static void ToggleSeated() => Seated = !Seated;

        // Clear listeners before the scene's OnEnable calls subscribe (statics survive with domain reload off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetListeners() => Changed = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply() => AudioListener.volume = Volume;
    }
}
