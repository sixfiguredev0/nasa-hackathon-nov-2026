using System.Collections.Generic;
using UnityEngine;

namespace RedFrontier.Audio
{
    public enum AudioBus { Music, Effects, Ambience }

    /// <summary>One sound ID: where its clips live and how it may play. Volumes are linear gains.</summary>
    public sealed class CueDef
    {
        public readonly string Id;
        public readonly AudioBus Bus;
        public readonly int Variations;     // clips are Resources/Audio/<folder>/<Id>_01 .. _NN
        public readonly float Volume;
        public readonly float Cooldown;      // seconds (unscaled) between starts of this cue
        public readonly int MaxVoices;       // simultaneous one-shots of this cue
        public readonly int Priority;        // higher steals pool sources from lower
        public readonly float PitchJitter;   // +/- fraction; 0 for tonal cues so they stay in tune with the score

        public CueDef(string id, AudioBus bus, int variations, float volumeDb, float cooldown = 0.05f,
            int maxVoices = 2, int priority = 50, float pitchJitter = 0f)
        {
            Id = id; Bus = bus; Variations = Mathf.Max(1, variations);
            Volume = Mathf.Pow(10f, volumeDb / 20f);
            Cooldown = cooldown; MaxVoices = Mathf.Max(1, maxVoices); Priority = priority; PitchJitter = pitchJitter;
        }

        public string Folder => Bus == AudioBus.Music ? "Music" : Bus == AudioBus.Ambience ? "Ambience" : "Sfx";
        public string ResourcePath(int variation) => $"Audio/{Folder}/{Id}_{variation + 1:00}";
    }

    /// <summary>Stable sound IDs used by game code. Each matches a record in MUSIC/manifests/asset_register.json.</summary>
    public static class Sfx
    {
        public const string UiConfirm = "SFX_UI_CONFIRM";
        public const string UiBack = "SFX_UI_BACK";
        public const string UiSelect = "SFX_UI_SELECT";
        public const string UiPanelOpen = "SFX_UI_PANEL_OPEN";
        public const string UiPanelClose = "SFX_UI_PANEL_CLOSE";
        public const string UiDenied = "SFX_UI_DENIED";
        public const string LaunchWhoosh = "SFX_LAUNCH_WHOOSH";
        public const string RocketDescent = "SFX_ROCKET_DESCENT";
        public const string RocketTouchdown = "SFX_ROCKET_TOUCHDOWN";
        public const string Hatch = "SFX_HATCH";
        public const string BuildPlace = "SFX_BUILD_PLACE";
        public const string ConstructLoop = "SFX_CONSTRUCT_LOOP";
        public const string BuildComplete = "SFX_BUILD_COMPLETE";
        public const string UpgradePurchase = "SFX_UPGRADE_PURCHASE";
        public const string GuideNotify = "SFX_GUIDE_NOTIFY";
        public const string GuideNext = "SFX_GUIDE_NEXT";
        public const string AlertShortage = "SFX_ALERT_SHORTAGE";
        public const string AlertRecovered = "SFX_ALERT_RECOVERED";
        public const string AlertStall = "SFX_ALERT_STALL";
        public const string AlertFitness = "SFX_ALERT_FITNESS";
        public const string Footstep = "SFX_FOOTSTEP";
        public const string GymLoop = "SFX_GYM_LOOP";
        public const string AmbWind = "AMB_MARS_WIND";
        public const string AmbColonyHum = "AMB_COLONY_HUM";
    }

    public static class MusicIds
    {
        public const string Title = "MUS_TITLE";
        public const string Arrival = "MUS_ARRIVAL";
        public const string Tension = "MUS_TENSION";
        public const string TensionAlternate = "MUS_SURFACE_E"; // played between tension cues during a long shortage
        public static readonly string[] Gameplay = { "MUS_SURFACE_A", "MUS_SURFACE_B", "MUS_SURFACE_C", "MUS_SURFACE_D", "MUS_SURFACE_E" };
    }

    public static class AudioCatalog
    {
        static readonly Dictionary<string, CueDef> cues = new Dictionary<string, CueDef>();

        static AudioCatalog()
        {
            // UI: quiet and short; generic confirm is suppressed when an action plays its own cue.
            Add(new CueDef(Sfx.UiConfirm, AudioBus.Effects, 3, -15f, 0.05f, 2, 40));
            Add(new CueDef(Sfx.UiBack, AudioBus.Effects, 2, -15f, 0.05f, 2, 40));
            Add(new CueDef(Sfx.UiSelect, AudioBus.Effects, 2, -18f, 0.07f, 2, 30));
            Add(new CueDef(Sfx.UiPanelOpen, AudioBus.Effects, 2, -16f, 0.12f, 1, 40));
            Add(new CueDef(Sfx.UiPanelClose, AudioBus.Effects, 2, -16f, 0.12f, 1, 40));
            Add(new CueDef(Sfx.UiDenied, AudioBus.Effects, 2, -14f, 0.3f, 1, 60));
            // Launch and landing
            Add(new CueDef(Sfx.LaunchWhoosh, AudioBus.Effects, 1, -9f, 1f, 1, 80));
            Add(new CueDef(Sfx.RocketDescent, AudioBus.Effects, 1, -7f, 1f, 1, 85));
            Add(new CueDef(Sfx.RocketTouchdown, AudioBus.Effects, 1, -7f, 1f, 1, 85));
            Add(new CueDef(Sfx.Hatch, AudioBus.Effects, 2, -15f, 1f, 1, 45));
            // Building
            Add(new CueDef(Sfx.BuildPlace, AudioBus.Effects, 3, -11f, 0.08f, 2, 60, 0.04f));
            Add(new CueDef(Sfx.ConstructLoop, AudioBus.Effects, 1, -19f, 0f, 1, 35));
            Add(new CueDef(Sfx.BuildComplete, AudioBus.Effects, 2, -11f, 0.5f, 1, 70));
            Add(new CueDef(Sfx.UpgradePurchase, AudioBus.Effects, 2, -11f, 0.15f, 1, 65));
            // Marlonis
            Add(new CueDef(Sfx.GuideNotify, AudioBus.Effects, 1, -13f, 2f, 1, 55));
            Add(new CueDef(Sfx.GuideNext, AudioBus.Effects, 2, -17f, 0.08f, 1, 40));
            // Alerts: state changes only, gated again by OutpostAudio
            Add(new CueDef(Sfx.AlertShortage, AudioBus.Effects, 1, -9f, 12f, 1, 95));
            Add(new CueDef(Sfx.AlertRecovered, AudioBus.Effects, 1, -11f, 6f, 1, 90));
            Add(new CueDef(Sfx.AlertStall, AudioBus.Effects, 2, -13f, 8f, 1, 75));
            Add(new CueDef(Sfx.AlertFitness, AudioBus.Effects, 1, -12f, 30f, 1, 80));
            // Crew detail
            Add(new CueDef(Sfx.Footstep, AudioBus.Effects, 4, -27f, 0.12f, 2, 10, 0.06f));
            Add(new CueDef(Sfx.GymLoop, AudioBus.Effects, 1, -22f, 0f, 1, 20));
            // Ambience beds (looped by GameAudio)
            Add(new CueDef(Sfx.AmbWind, AudioBus.Ambience, 1, -15f, 0f, 1, 100));
            Add(new CueDef(Sfx.AmbColonyHum, AudioBus.Ambience, 1, -20f, 0f, 1, 100));
            // Music movements: masters are loudness-matched, so one gain for all
            foreach (var id in MusicIds.Gameplay) Add(new CueDef(id, AudioBus.Music, 1, -4f, 0f, 1, 100));
            Add(new CueDef(MusicIds.Title, AudioBus.Music, 1, -4f, 0f, 1, 100));
            Add(new CueDef(MusicIds.Arrival, AudioBus.Music, 1, -4f, 0f, 1, 100));
            Add(new CueDef(MusicIds.Tension, AudioBus.Music, 1, -4f, 0f, 1, 100));
        }

        static void Add(CueDef def) => cues[def.Id] = def;

        public static CueDef Get(string id) => id != null && cues.TryGetValue(id, out var def) ? def : null;
        public static IEnumerable<CueDef> All => cues.Values;

        /// <summary>Test hook: define or replace a cue at runtime.</summary>
        internal static void Register(CueDef def) => Add(def);
    }
}
