using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace RedFrontier.Core.Tests
{
    /// <summary>
    /// Parses the example JSON with System.Text.Json (public fields, case-sensitive names) as a stand-in
    /// for Unity's JsonUtility, then validates and plays it. JsonUtility itself is unverified until Stage 2.
    /// </summary>
    public class ExampleContentTests
    {
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

        static string Dir { get { return Path.Combine(TestContext.CurrentContext.TestDirectory, "Content", "example"); } }

        static (MissionConfig, List<EventDef>) Load()
        {
            var mission = JsonSerializer.Deserialize<MissionConfig>(File.ReadAllText(Path.Combine(Dir, "mission.json")), Json);
            var events = Directory.GetFiles(Path.Combine(Dir, "events"), "*.json")
                .Select(f => JsonSerializer.Deserialize<EventDef>(File.ReadAllText(f), Json))
                .ToList();
            return (mission, events);
        }

        [Test]
        public void ExampleContent_ParsesValidatesAndPlays()
        {
            var (mission, events) = Load();
            Assert.Greater(events.Count, 0);
            var r = ContentValidator.Validate(mission, events);
            Assert.IsTrue(r.Ok, r.ToString());
            Assert.AreEqual(0, r.warnings.Count, r.ToString());

            var storm = events.Single(e => e.id == "milestone_dust_storm");
            Assert.AreEqual(3, storm.variants.Count);
            Assert.IsTrue(events.Single(e => e.id == "random_rover_fault").trigger.once);

            foreach (int seed in new[] { 1, 2, 3 })
            {
                var s = new GameSession(mission, events, seed);
                Fixtures.PlayToEnd(s);
                Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
                Assert.IsTrue(s.History.Any(e => e.eventId == "milestone_dust_storm" && e.sol == 3));
            }
        }

        [Test]
        public void FileOrder_DoesNotMatter()
        {
            var (mission, events) = Load();
            var a = new GameSession(mission, events.OrderBy(e => e.id), 9);
            var b = new GameSession(mission, events.OrderByDescending(e => e.id), 9);
            Fixtures.PlayToEnd(a); Fixtures.PlayToEnd(b);
            Assert.AreEqual(Fixtures.Fingerprint(a), Fixtures.Fingerprint(b));
        }
    }
}
