using UnityEditor;
using UnityEngine;

namespace RedFrontier.Audio.Editor
{
    /// <summary>
    /// Import settings for everything under Assets/Audio, applied on (re)import so they cannot drift.
    /// Music: compressed in memory, not preloaded; GameAudio loads/unloads each movement around playback,
    /// which keeps WebGL (where clips are decoded into browser memory) to roughly two movements at a time.
    /// SFX: small, decompressed on load for instant starts. Ambience: compressed in memory, preloaded.
    /// WebGL transcodes everything to AAC itself; no Streaming load type is used anywhere (unsupported there).
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/Audio/";

        // Bump when the rules change so Unity reimports the clips with the new settings.
        public override uint GetVersion() => 2;

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/"), ambience = assetPath.Contains("/Ambience/");

            importer.forceToMono = false;
            importer.loadInBackground = music || ambience;
            importer.ambisonic = false;

            var standalone = importer.defaultSampleSettings;
            standalone.loadType = music || ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            standalone.compressionFormat = AudioCompressionFormat.Vorbis;
            standalone.quality = music ? 0.7f : 0.6f;
            standalone.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            standalone.preloadAudioData = !music;
            importer.defaultSampleSettings = standalone;
            // No per-platform override: WebGL ignores the format and always transcodes to AAC,
            // and these load types (never Streaming) are valid on every target.
            importer.ClearSampleSettingOverride("WebGL");
        }

        /// <summary>Settings as they should be, for the self-test.</summary>
        public static string Check(string path, AudioImporter importer)
        {
            bool music = path.Contains("/Music/");
            var s = importer.defaultSampleSettings;
            if (s.preloadAudioData == music) return "preload flag";
            if (music && s.loadType != AudioClipLoadType.CompressedInMemory) return "music load type";
            if (!music && !path.Contains("/Ambience/") && s.loadType != AudioClipLoadType.DecompressOnLoad) return "sfx load type";
            if (s.loadType == AudioClipLoadType.Streaming) return "streaming is unsupported on WebGL";
            if (importer.ContainsSampleSettingsOverride("WebGL")) return "unexpected WebGL override";
            return null;
        }
    }
}
