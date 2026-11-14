using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace RedFrontier.Core.Tests
{
    /// <summary>Saved Integration_MapV1 configuration, read as text so the scene is never opened or re-saved.</summary>
    public class IntegrationSceneTests
    {
        static string Scene { get { return File.ReadAllText(Path.Combine(Application.dataPath, "RedFrontier/Scenes/Integration_MapV1.unity")); } }

        [Test]
        public void MapSeedIsFixed_AndRandomRegenerationIsOff()
        {
            var s = Scene;
            StringAssert.Contains("seed: 1337", s);
            StringAssert.Contains("useRandomSeed: 0", s);
        }

        [Test]
        public void ExactlyOneCameraListenerAndHost_PanelModeOn_BridgeWired()
        {
            var s = Scene;
            Assert.AreEqual(1, Regex.Matches(s, "^--- !u!20 ", RegexOptions.Multiline).Count, "cameras");
            Assert.AreEqual(1, Regex.Matches(s, "^--- !u!81 ", RegexOptions.Multiline).Count, "audio listeners");
            string hostGuid = File.ReadAllText(Path.Combine(Application.dataPath, "RedFrontier/Runtime/SimulationHost.cs.meta")).Split('\n')[1].Substring(6).Trim();
            Assert.AreEqual(1, Regex.Matches(s, "guid: " + hostGuid).Count, "simulation hosts");
            StringAssert.Contains("panelMode: 1", s);
            Assert.IsFalse(Regex.IsMatch(s, @"panel: \{fileID: 0\}|map: \{fileID: 0\}"), "bridge references set");
        }
    }
}
