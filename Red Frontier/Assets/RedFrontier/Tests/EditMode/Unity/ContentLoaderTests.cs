using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedFrontier.Runtime;
using UnityEngine;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>Unity only: the real JsonUtility loader and the placeholder content shipped in Resources.</summary>
    public class ContentLoaderTests
    {
        static GameSession Shipped(int seed)
        {
            MissionConfig m; List<EventDef> e;
            ContentLoader.LoadFromResources(out m, out e);
            return new GameSession(m, e, seed);
        }

        [Test]
        public void ShippedContent_LoadsWithJsonUtility_AndValidatesWithoutWarnings()
        {
            MissionConfig m; List<EventDef> e;
            ContentLoader.LoadFromResources(out m, out e);
            Assert.AreEqual(4, e.Count);
            var r = ContentValidator.Validate(m, e);
            Assert.IsTrue(r.Ok, r.ToString());
            Assert.AreEqual(0, r.warnings.Count, r.ToString());
            Assert.AreEqual(3, e.Single(x => x.id == "milestone_dust_storm").variants.Count);
            Assert.AreEqual(3, e.Single(x => x.id == "milestone_dust_storm").trigger.sol);
            Assert.IsTrue(e.Single(x => x.id == "random_rover_fault").trigger.once);
            Assert.AreEqual(60, m.eventChancePercent);
            Assert.AreEqual(2, m.randomEventMinGapSols);
            CollectionAssert.AreEqual(e.Select(x => x.id).OrderBy(x => x, StringComparer.Ordinal).ToList(), e.Select(x => x.id).ToList(), "parsed in ordinal file-name order");
        }

        [Test]
        public void ShippedContent_SuccessIsReachable_WithMilestoneOnSol3()
        {
            int successes = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var s = Shipped(seed);
                PlayToEnd(s);
                Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
                Assert.IsTrue(s.History.Any(x => x.eventId == "milestone_dust_storm" && x.kind == LogKind.EventRaised && x.sol == 3), "seed " + seed);
                if (s.Snapshot.outcome == MissionOutcome.Success) successes++;
            }
            Assert.Greater(successes, 0);
        }

        [Test]
        public void ShippedContent_FailureIsReachable_ByTurningLifeSupportOff()
        {
            var s = Shipped(1);
            for (int i = 0; i < 20 && s.Snapshot.phase != MissionPhase.Ended; i++)
            {
                AnswerPending(s);
                if (s.Snapshot.phase == MissionPhase.Ended) break;
                Assert.IsTrue(s.SetFacilityLevel("life_support", 0).ok);
                Assert.IsTrue(s.AdvanceSol(s.Snapshot.sol).ok);
            }
            Assert.AreEqual(MissionOutcome.Failure, s.Snapshot.outcome);
            Assert.AreEqual(ReasonCodes.HealthZero, s.Snapshot.endReason);
        }

        [Test]
        public void MissingOptionalFields_GetSafeDefaults_AndValidate()
        {
            // No eventChancePercent, randomEventMinGapSols, crewNeeds, uses/makes on level 0.
            const string mission = "{\"schemaVersion\":1,\"missionLengthSols\":3,\"crewCount\":2,\"startHealth\":90,"
                + "\"resources\":[{\"id\":\"power\",\"start\":4,\"capacity\":10}],"
                + "\"facilities\":[{\"id\":\"solar_array\",\"levels\":[{\"id\":\"off\"},{\"id\":\"on\",\"makes\":[{\"resource\":\"power\",\"amount\":2}]}]}]}";
            // No category, severity, scienceNote, sourceRef, variants, trigger.once/weight/minSol, requires, preview.
            const string ev = "{\"schemaVersion\":1,\"id\":\"random_minimal\",\"dataBasis\":\"illustrative\",\"trigger\":{},"
                + "\"title\":\"t\",\"body\":\"b\",\"choices\":[{\"id\":\"pay\",\"label\":\"Pay\",\"effects\":[{\"type\":\"resource\",\"target\":\"power\",\"amount\":-1}]},"
                + "{\"id\":\"endure\",\"label\":\"Endure\",\"effects\":[{\"type\":\"health\",\"amount\":-3}]}]}";
            // No trigger object at all.
            const string bare = "{\"schemaVersion\":1,\"id\":\"random_bare\",\"dataBasis\":\"illustrative\",\"choices\":[{\"id\":\"ok\",\"effects\":[{\"type\":\"health\",\"amount\":-1}]}]}";

            var m = ContentLoader.ParseMission(mission, "mission");
            Assert.AreEqual(0, m.eventChancePercent);
            Assert.AreEqual(0, m.randomEventMinGapSols);
            Assert.IsNotNull(m.crewNeeds);
            Assert.AreEqual(0, m.facilities[0].startLevel);
            Assert.IsNotNull(m.facilities[0].levels[0].uses);
            Assert.IsNotNull(m.facilities[0].levels[0].makes);

            var events = ContentLoader.ParseEvents(new[]
            {
                new KeyValuePair<string, string>("random_minimal", ev),
                new KeyValuePair<string, string>("random_bare", bare),
            });
            var e = events.Single(x => x.id == "random_minimal");
            Assert.AreEqual("", e.scienceNote);
            Assert.AreEqual("", e.sourceRef);
            Assert.IsNotNull(e.variants);
            Assert.AreEqual(0, e.variants.Count);
            Assert.IsNotNull(e.trigger);
            Assert.IsFalse(e.trigger.once);
            Assert.AreEqual(0, e.trigger.weight);
            Assert.IsNotNull(e.choices[0].requires);
            Assert.AreEqual("", e.choices[0].preview);
            Assert.IsNotNull(events.Single(x => x.id == "random_bare").trigger);

            var r = ContentValidator.Validate(m, events);
            Assert.IsTrue(r.Ok, r.ToString());
            var s = new GameSession(m, events, 3);
            PlayToEnd(s);
            Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
        }

        [Test]
        public void LoadingOrder_IsDeterministic_RegardlessOfInputOrder()
        {
            var files = new List<KeyValuePair<string, string>>();
            foreach (var a in Resources.LoadAll<TextAsset>(ContentLoader.EventsFolder))
                files.Add(new KeyValuePair<string, string>(a.name, a.text));
            var forward = ContentLoader.ParseEvents(files.OrderBy(f => f.Key, StringComparer.Ordinal));
            var reversed = ContentLoader.ParseEvents(files.OrderByDescending(f => f.Key, StringComparer.Ordinal));
            CollectionAssert.AreEqual(forward.Select(x => x.id).ToList(), reversed.Select(x => x.id).ToList());

            MissionConfig m; List<EventDef> unused;
            ContentLoader.LoadFromResources(out m, out unused);
            for (int seed = 1; seed <= 10; seed++)
            {
                var a = new GameSession(m, forward, seed);
                var b = new GameSession(m, Enumerable.Reverse(forward), seed);
                PlayToEnd(a); PlayToEnd(b);
                Assert.AreEqual(Fingerprint(a), Fingerprint(b), "seed " + seed);
            }
        }

        [Test]
        public void MalformedJson_FailsWithFileName()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ContentLoader.ParseEvents(new[] { new KeyValuePair<string, string>("broken_event", "{ not json") }));
            StringAssert.Contains("broken_event", ex.Message);
        }

        [Test]
        public void SimState_RoundTripsThroughJsonUtility_IncludingLargeRngState()
        {
            var s = Shipped(-12345);
            PlayToEnd(s);
            var snap = s.Snapshot;
            snap.rngState = 0xF00DBEEFu; // above int.MaxValue
            var back = JsonUtility.FromJson<SimState>(JsonUtility.ToJson(snap));
            Assert.AreEqual(snap.rngState, back.rngState);
            Assert.AreEqual(JsonUtility.ToJson(snap), JsonUtility.ToJson(back));
        }
    }
}
