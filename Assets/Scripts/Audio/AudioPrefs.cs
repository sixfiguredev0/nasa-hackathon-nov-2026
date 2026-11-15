using UnityEngine;

namespace RedFrontier.Audio
{
    /// <summary>
    /// Category levels (0..1), saved in PlayerPrefs next to the existing FrontierUi "MasterVolume".
    /// Master stays on AudioListener.volume; these multiply each source on top of it.
    /// </summary>
    public static class AudioPrefs
    {
        public const string MusicKey = "MusicVolume", EffectsKey = "EffectsVolume", AmbienceKey = "AmbienceVolume";
        public const float MusicDefault = 0.7f, EffectsDefault = 0.9f, AmbienceDefault = 0.7f;

        public static float Get(AudioBus bus) => Mathf.Clamp01(PlayerPrefs.GetFloat(Key(bus), Default(bus)));

        public static void Set(AudioBus bus, float value)
        {
            PlayerPrefs.SetFloat(Key(bus), Mathf.Clamp01(value));
            PlayerPrefs.Save();
        }

        public static string Key(AudioBus bus) => bus == AudioBus.Music ? MusicKey : bus == AudioBus.Ambience ? AmbienceKey : EffectsKey;
        public static float Default(AudioBus bus) => bus == AudioBus.Music ? MusicDefault : bus == AudioBus.Ambience ? AmbienceDefault : EffectsDefault;
    }
}
