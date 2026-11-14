using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>
    /// Hybrid event structure: fixed major milestones, bounded variants, seeded random events between.
    /// </summary>
    public class HybridEventTests
    {
        const int SeedCount = 200;

        static IEnumerable<int> Seeds() { for (int i = 1; i <= SeedCount; i++) yield return i; }

        static List<LogEntry> Raised(GameSession s)
        {
            return s.History.Where(e => e.kind == LogKind.EventRaised).ToList();
        }

        [Test]
        public void Milestones_OccurOnConfiguredSols_ForEverySeed()
        {
            foreach (int seed in Seeds())
            {
                var s = Hybrid(seed);
                PlayToEnd(s);
                Assert.AreEqual(MissionOutcome.Success, s.Snapshot.outcome, "seed " + seed);
                var majors = Raised(s).Where(e => e.eventClass == "major").ToList();
                Assert.AreEqual(2, majors.Count, "seed " + seed);
                Assert.AreEqual("milestone_dust_storm", majors[0].eventId);
                Assert.AreEqual(MilestoneA, majors[0].sol);
                Assert.AreEqual("milestone_resupply", majors[1].eventId);
                Assert.AreEqual(MilestoneB, majors[1].sol);
            }
        }

        [Test]
        public void RandomEvents_NeverDisplaceMilestones_AtMostOneEventPerSol()
        {
            foreach (int seed in Seeds())
            {
                var s = Hybrid(seed);
                PlayToEnd(s);
                var raised = Raised(s);
                foreach (var g in raised.GroupBy(e => e.sol))
                    Assert.AreEqual(1, g.Count(), "seed " + seed + " sol " + g.Key);
                Assert.IsFalse(raised.Any(e => e.eventClass == "random" && (e.sol == MilestoneA || e.sol == MilestoneB)), "seed " + seed);
            }
        }

        [Test]
        public void RandomEvents_RespectWindows_OnceOnly_AndMinimumGap()
        {
            var defs = HybridEvents().ToDictionary(e => e.id);
            int gap = HybridMission().randomEventMinGapSols;
            int randomTotal = 0;
            foreach (int seed in Seeds())
            {
                var s = Hybrid(seed);
                PlayToEnd(s);
                var randoms = Raised(s).Where(e => e.eventClass == "random").ToList();
                randomTotal += randoms.Count;
                for (int i = 0; i < randoms.Count; i++)
                {
                    var t = defs[randoms[i].eventId].trigger;
                    Assert.AreEqual(0, t.sol);
                    if (t.minSol > 0) Assert.GreaterOrEqual(randoms[i].sol, t.minSol, "seed " + seed);
                    if (t.maxSol > 0) Assert.LessOrEqual(randoms[i].sol, t.maxSol, "seed " + seed);
                    if (i > 0) Assert.GreaterOrEqual(randoms[i].sol - randoms[i - 1].sol, gap, "seed " + seed);
                }
                foreach (var g in randoms.GroupBy(e => e.eventId))
                    if (defs[g.Key].trigger.once) Assert.AreEqual(1, g.Count(), "seed " + seed + " " + g.Key);
            }
            Assert.Greater(randomTotal, SeedCount, "the pool actually fires");
        }

        [Test]
        public void Seeds_ProduceVariation_WhilePreservingMilestones()
        {
            var variants = new HashSet<string>();
            var sequences = new HashSet<string>();
            foreach (int seed in Seeds())
            {
                var s = Hybrid(seed);
                PlayToEnd(s);
                var raised = Raised(s);
                variants.Add(raised.Single(e => e.eventId == "milestone_dust_storm").variantId);
                sequences.Add(string.Join(",", raised.Select(e => e.sol + ":" + e.eventId)));
            }
            CollectionAssert.AreEquivalent(new[] { "light", "heavy", "long" }, variants);
            Assert.Greater(sequences.Count, 10, "random events differ between seeds");
        }

        [Test]
        public void SpecificSeeds_PickDifferentVariants()
        {
            // Documented demonstration seeds: find two seeds that differ, then confirm they are stable.
            var bySeed = new Dictionary<int, string>();
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var s = Hybrid(seed);
                PlayToEnd(s);
                bySeed[seed] = Raised(s).Single(e => e.eventId == "milestone_dust_storm").variantId;
            }
            Assert.Greater(bySeed.Values.Distinct().Count(), 1, string.Join(",", bySeed.Select(kv => kv.Key + "=" + kv.Value)));
            TestContext.WriteLine("seed->variant: " + string.Join(", ", bySeed.Select(kv => kv.Key + "=" + kv.Value)));
        }

        [Test]
        public void EmptyPoolOrZeroChance_ContinuesNormally()
        {
            var c = HybridMission();
            var onlyMajors = HybridEvents().Where(e => e.IsMajor).ToList();
            var s = new GameSession(c, onlyMajors, 3);
            PlayToEnd(s);
            Assert.AreEqual(MissionOutcome.Success, s.Snapshot.outcome);
            Assert.AreEqual(2, Raised(s).Count);

            c.eventChancePercent = 0;
            var z = new GameSession(c, HybridEvents(), 3);
            PlayToEnd(z);
            Assert.AreEqual(2, Raised(z).Count);
        }

        [Test]
        public void ResolvedVariant_IsStableUntilAnswered()
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5 })
            {
                var s = Hybrid(seed);
                while (s.Snapshot.sol < MilestoneA) { AnswerPending(s); s.AdvanceSol(s.Snapshot.sol); }
                Assert.AreEqual(MilestoneA, s.Snapshot.sol);
                var first = s.PendingEvent;
                Assert.AreEqual("milestone_dust_storm", first.eventId);
                uint rng = s.Snapshot.rngState;

                for (int i = 0; i < 5; i++)
                {
                    var _ = s.Snapshot; var __ = s.Forecast; var ___ = s.History;
                    Assert.IsFalse(s.AdvanceSol(MilestoneA).ok);
                    Assert.IsFalse(s.ChooseEventOption(first.instanceId, "nope").ok);
                    Assert.IsFalse(s.ChooseEventOption(first.instanceId + 7, "wait").ok);
                    Assert.IsFalse(s.SetFacilityLevel("life_support", 1).ok);
                    var again = s.PendingEvent;
                    Assert.AreEqual(first.variantId, again.variantId);
                    Assert.AreEqual(first.instanceId, again.instanceId);
                    Assert.AreEqual(first.body, again.body);
                    for (int c = 0; c < first.choices.Count; c++)
                        Assert.AreEqual(first.choices[c].effects[0].amount, again.choices[c].effects[0].amount);
                }
                Assert.AreEqual(rng, s.Snapshot.rngState);

                // The cost the player saw is the cost paid.
                var clean = first.choices.Single(c => c.id == "clean_panels");
                int power = s.Snapshot.Resource("power").amount;
                Assert.IsTrue(s.ChooseEventOption(first.instanceId, "clean_panels").ok);
                Assert.AreEqual(power + clean.effects[0].amount, s.Snapshot.Resource("power").amount);
                var made = s.History.Last();
                Assert.AreEqual(LogKind.ChoiceMade, made.kind);
                Assert.AreEqual(first.variantId, made.variantId);
                Assert.AreEqual("clean_panels", made.choiceId);
            }
        }

        [Test]
        public void ReadsAndRejections_LeaveStateAndRngUnchanged()
        {
            var s = Hybrid(11);
            for (int step = 0; step < 30 && s.Snapshot.phase != MissionPhase.Ended; step++)
            {
                string fp = Fingerprint(s);
                var _ = s.Snapshot; var f = s.Forecast; var p = s.PendingEvent; var h = s.History; var r = s.LastReport;
                s.CanAdvanceSol(); s.CanSetFacilityLevel("solar_array", 1); s.CanChooseEventOption(1, "x");
                s.AdvanceSol(s.Snapshot.sol + 1);
                s.AdvanceSol(s.Snapshot.sol - 1);
                s.SetFacilityLevel("nope", 0);
                s.SetFacilityLevel("solar_array", 5);
                s.ChooseEventOption(-1, "wait");
                if (p != null) s.ChooseEventOption(p.instanceId, "not_a_choice");
                Assert.AreEqual(fp, Fingerprint(s), "step " + step);

                if (p != null) AnswerPending(s); else s.AdvanceSol(s.Snapshot.sol);
            }
            Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
        }

        [Test]
        public void ForecastsAndReads_DoNotChangeTheEventSequence()
        {
            foreach (int seed in new[] { 4, 9, 23, 77 })
            {
                var plain = Hybrid(seed);
                PlayToEnd(plain);

                var noisy = Hybrid(seed);
                for (int i = 0; i < 100 && noisy.Snapshot.phase != MissionPhase.Ended; i++)
                {
                    for (int k = 0; k < 3; k++) { var a = noisy.Forecast; var b = noisy.PendingEvent; var c = noisy.Snapshot; }
                    AnswerPending(noisy);
                    if (noisy.Snapshot.phase == MissionPhase.Ended) break;
                    var x = noisy.Forecast;
                    noisy.AdvanceSol(noisy.Snapshot.sol);
                }
                Assert.AreEqual(Fingerprint(plain), Fingerprint(noisy), "seed " + seed);
            }
        }

        [Test]
        public void DuplicateChoices_AndStaleAdvances_ApplyOnce()
        {
            var s = Hybrid(5);
            while (s.PendingEvent == null || s.PendingEvent.eventId != "milestone_resupply")
            {
                if (s.PendingEvent != null) AnswerPending(s); else Assert.IsTrue(s.AdvanceSol(s.Snapshot.sol).ok);
            }
            var p = s.PendingEvent;
            int power = s.Snapshot.Resource("power").amount;
            int air = s.Snapshot.Resource("air").amount;
            Assert.IsTrue(s.ChooseEventOption(p.instanceId, "unload_now").ok);
            Assert.IsFalse(s.ChooseEventOption(p.instanceId, "unload_now").ok);
            Assert.IsFalse(s.ChooseEventOption(p.instanceId, "unload_later").ok);
            Assert.AreEqual(power - 2, s.Snapshot.Resource("power").amount);
            Assert.AreEqual(System.Math.Min(air + 5, 20), s.Snapshot.Resource("air").amount);
            Assert.AreEqual(1, s.History.Count(e => e.kind == LogKind.ChoiceMade && e.instanceId == p.instanceId));

            int sol = s.Snapshot.sol;
            Assert.IsTrue(s.AdvanceSol(sol).ok);
            string fp = Fingerprint(s);
            if (s.PendingEvent != null) AnswerPending(s);
            fp = Fingerprint(s);
            Assert.AreEqual(ReasonCodes.StaleSol, s.AdvanceSol(sol).reason);
            Assert.AreEqual(fp, Fingerprint(s));
            Assert.AreEqual(sol + 1, s.Snapshot.sol);
        }

        [Test]
        public void EventOrder_IsIndependentOfLoadingOrder()
        {
            foreach (int seed in new[] { 1, 8, 31, 64 })
            {
                var forward = Hybrid(seed);
                var shuffled = HybridEvents();
                shuffled.Reverse();
                var tmp = shuffled[1]; shuffled[1] = shuffled[3]; shuffled[3] = tmp;
                var reversed = new GameSession(HybridMission(), shuffled, seed);
                PlayToEnd(forward); PlayToEnd(reversed);
                Assert.AreEqual(Fingerprint(forward), Fingerprint(reversed), "seed " + seed);
            }
        }

        [Test]
        public void Restart_SameSeedReplays_DifferentSeedVaries()
        {
            var s = Hybrid(21);
            PlayToEnd(s);
            string first = Fingerprint(s);
            Assert.IsTrue(s.Restart().ok);
            PlayToEnd(s);
            Assert.AreEqual(first, Fingerprint(s), "same seed replays the run");

            var logs = new HashSet<string>();
            foreach (int seed in new[] { 22, 23, 24, 25, 26 })
            {
                Assert.IsTrue(s.Restart(seed).ok);
                Assert.AreEqual(seed, s.Seed);
                PlayToEnd(s);
                logs.Add(string.Join(";", s.History.Select(e => e.ToString())));
            }
            Assert.Greater(logs.Count, 1, "an explicitly supplied seed varies the run");
        }

        [Test]
        public void Log_RecordsMilestoneVariantSolAndChoice()
        {
            var s = Hybrid(2);
            PlayToEnd(s);
            var h = s.History;
            var raised = h.First(e => e.eventId == "milestone_dust_storm" && e.kind == LogKind.EventRaised);
            Assert.AreEqual(MilestoneA, raised.sol);
            Assert.AreEqual("major", raised.eventClass);
            Assert.IsNotEmpty(raised.variantId);
            var answered = h.First(e => e.instanceId == raised.instanceId && e.kind == LogKind.ChoiceMade);
            Assert.AreEqual(raised.variantId, answered.variantId);
            Assert.IsNotEmpty(answered.choiceId);
            Assert.AreEqual(LogKind.MissionEnded, h.Last().kind);
            TestContext.WriteLine(string.Join("\n", h.Select(e => e.ToString())));
        }
    }
}
