using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>
    /// Regressions for the two defects confirmed in _review/REVIEW-2026-11-14.md:
    /// duplicate facility costs, and events whose choices are all unavailable (decision 0006).
    /// </summary>
    public class RegressionTests
    {
        // ---- A. Duplicate facility costs ----

        /// <summary>Power stock 5; one level costs power 3 + power 3; nothing produces power; no crew demand.</summary>
        static MissionConfig DuplicateCostMission()
        {
            return new MissionConfig
            {
                schemaVersion = 1, missionLengthSols = 3, crewCount = 1, startHealth = 100,
                resources = new List<ResourceDef> { new ResourceDef { id = "power", start = 5, capacity = 20 } },
                facilities = new List<FacilityDef>
                {
                    new FacilityDef { id = "heater", startLevel = 1, levels = new List<FacilityLevelDef>
                    {
                        Level("off"),
                        Level("on", uses: new[] { Flow("power", 3), Flow("power", 3) }),
                    } },
                },
            };
        }

        [Test]
        public void DuplicateFacilityCosts_AreValid_ButSummedForAffordability()
        {
            var c = DuplicateCostMission();
            var v = ContentValidator.Validate(c, new List<EventDef>());
            Assert.IsTrue(v.Ok, "the configuration stays accepted: " + v);

            var s = new GameSession(c, new List<EventDef>(), 1);
            var f = s.Forecast;
            Assert.AreEqual(0, f.facilities[0].levelRun, "forecast: 6 power needed, 5 in stock");
            Assert.AreEqual(5, f.Resource("power").end);

            Assert.IsTrue(s.AdvanceSol(1).ok);
            var rep = s.LastReport;
            Assert.AreEqual(1, rep.facilities[0].requestedLevel);
            Assert.AreEqual(0, rep.facilities[0].levelRun);
            Assert.AreEqual(ReasonCodes.InsufficientResource, rep.facilities[0].reason);
            Assert.AreEqual(0, rep.Resource("power").usedByFacilities);
            Assert.AreEqual(5, s.Snapshot.Resource("power").amount, "never negative, and not clamped: the level simply did not run");
        }

        [Test]
        public void DuplicateFacilityCosts_RunWhenTheSumIsAffordable()
        {
            var c = DuplicateCostMission();
            c.resources[0].start = 6;
            var s = new GameSession(c, new List<EventDef>(), 1);
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(1, s.LastReport.facilities[0].levelRun);
            Assert.AreEqual(6, s.LastReport.Resource("power").usedByFacilities);
            Assert.AreEqual(0, s.Snapshot.Resource("power").amount);
        }

        // ---- B. Fallback choice: validation ----

        static string Errors(List<EventDef> events)
        {
            return string.Join("\n", ContentValidator.Validate(Stage1Mission(), events).errors);
        }

        [Test]
        public void Validator_RequiresFallbackChoice_ForPlainMajorAndRandomEvents()
        {
            var major = Major("all_paid", 2, Choice("a", Res("power", -1)), Choice("b", Res("power", -2)));
            StringAssert.Contains("event 'all_paid': needs a fallback choice", Errors(new List<EventDef> { major }));

            var random = Random("random_all_paid", 0, 0, 1, false, Choice("a", Res("air", -1)), Choice("b", Health(-2)).Requires("power", 1));
            StringAssert.Contains("event 'random_all_paid': needs a fallback choice", Errors(new List<EventDef> { random }));
        }

        [Test]
        public void Validator_RequiresFallbackChoice_InEveryVariant()
        {
            var e = MajorWithVariants("storm", 3,
                Variant("mild", 1, Choice("clean", Res("power", -1)), Choice("wait", Health(-5))),
                Variant("harsh", 1, Choice("clean", Res("power", -4)), Choice("wait", Health(-10)).Requires("power", 2)));
            var errors = Errors(new List<EventDef> { e });
            StringAssert.Contains("variant 'harsh': needs a fallback choice", errors);
            StringAssert.DoesNotContain("variant 'mild': needs a fallback", errors);
        }

        [Test]
        public void Validator_AcceptsFallbackWithNegativeConsequence_RejectsRequirementOnlyChoice()
        {
            // A health loss is a consequence, not a cost: still a fallback.
            Assert.IsTrue(ContentValidator.Validate(Stage1Mission(), new List<EventDef>
            {
                Major("ok_event", 2, Choice("pay", Res("power", -3)), Choice("endure", Health(-10))),
            }).Ok);
            // A requirement alone is enough to make a choice unavailable, so it is not a fallback.
            StringAssert.Contains("needs a fallback choice", Errors(new List<EventDef>
            {
                Major("gated", 2, Choice("pay", Res("power", -3)), Choice("inspect", Health(-1)).Requires("power", 1)),
            }));
        }

        // ---- B. Fallback choice: runtime behaviour at zero stock ----

        /// <summary>Power 0 forever: solar off, life support off. Air lasts three sols for four crew.</summary>
        static MissionConfig NoPowerMission()
        {
            var c = Stage1Mission(6);
            c.resources[0].start = 0;
            c.facilities[0].startLevel = 0;
            c.facilities[1].startLevel = 0;
            return c;
        }

        [Test]
        public void ValidContent_AtZeroStock_FallbackKeepsEveryEventAnswerable_MajorVariantsAndRandom()
        {
            var c = NoPowerMission();
            c.eventChancePercent = 100;
            var events = new List<EventDef>
            {
                MajorWithVariants("storm", 2,
                    Variant("mild", 1, Choice("clean", Res("power", -1)), Choice("wait", Health(-5))),
                    Variant("harsh", 1, Choice("clean", Res("power", -4)), Choice("wait", Health(-10)))),
                Random("random_leak", 3, 3, 1, true, Choice("patch", Res("power", -2)).Requires("power", 3), Choice("seal_room", Health(-5))),
            };
            var variantsSeen = new HashSet<string>();
            for (int seed = 1; seed <= 40; seed++)
            {
                var s = new GameSession(c, events, seed);
                Assert.IsTrue(s.AdvanceSol(1).ok);
                var storm = s.PendingEvent;
                Assert.AreEqual("storm", storm.eventId);
                variantsSeen.Add(storm.variantId);
                Assert.IsFalse(storm.choices.Single(x => x.id == "clean").available);
                Assert.IsTrue(storm.choices.Single(x => x.id == "wait").available);
                int healthBefore = s.Snapshot.crewHealth;
                Assert.IsTrue(s.ChooseEventOption(storm.instanceId, "wait").ok);
                Assert.Less(s.Snapshot.crewHealth, healthBefore, "the fallback keeps its consequence");
                Assert.AreEqual(0, s.Snapshot.Resource("power").amount, "the paid choice was not granted for free");

                Assert.IsTrue(s.AdvanceSol(2).ok);
                var leak = s.PendingEvent;
                Assert.AreEqual("random_leak", leak.eventId, "chance 100 and a one-sol window");
                Assert.AreEqual(ReasonCodes.RequirementNotMet, leak.choices[0].unavailableReason);
                Assert.IsTrue(s.ChooseEventOption(leak.instanceId, "seal_room").ok);
                Assert.AreEqual(MissionPhase.Planning, s.Snapshot.phase);
                PlayToEnd(s); // asserts that an available choice exists for every later event too
                Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
                Assert.AreNotEqual(ReasonCodes.ContentErrorNoAvailableChoice, s.Snapshot.endReason);
            }
            CollectionAssert.AreEquivalent(new[] { "mild", "harsh" }, variantsSeen.ToArray());
        }

        [Test]
        public void ShippedFixtures_AtZeroPower_NeverTrapTheMission()
        {
            var c = HybridMission();
            c.resources[0].start = 0;
            c.facilities[0].startLevel = 0;
            int randomRaised = 0;
            for (int seed = 1; seed <= 100; seed++)
            {
                var s = new GameSession(c, HybridEvents(), seed);
                PlayToEnd(s);
                Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase, "seed " + seed);
                Assert.AreNotEqual(ReasonCodes.ContentErrorNoAvailableChoice, s.Snapshot.endReason, "seed " + seed);
                randomRaised += s.History.Count(e => e.kind == LogKind.EventRaised && e.eventClass == "random");
            }
            Assert.Greater(randomRaised, 0);
        }

        // ---- B. Runtime guard for content that bypassed validation ----

        static void AssertContentError(GameSession s, string eventId, string variantId)
        {
            var snap = s.Snapshot;
            Assert.AreEqual(MissionPhase.Ended, snap.phase);
            Assert.AreEqual(MissionOutcome.Failure, snap.outcome);
            Assert.AreEqual(ReasonCodes.ContentErrorNoAvailableChoice, snap.endReason);
            Assert.IsNull(s.PendingEvent, "no unanswerable event is left pending");
            var end = s.History.Last();
            Assert.AreEqual(LogKind.MissionEnded, end.kind);
            Assert.AreEqual(ReasonCodes.ContentErrorNoAvailableChoice, end.code);
            Assert.AreEqual(eventId, end.eventId);
            Assert.AreEqual(variantId, end.variantId);
            Assert.AreEqual(ReasonCodes.MissionEnded, s.AdvanceSol(snap.sol).reason);
            Assert.AreEqual(ReasonCodes.MissionEnded, s.SetFacilityLevel("solar_array", 1).reason);
            Assert.AreEqual(0, snap.Resource("power").amount, "no cost was waived");
        }

        [Test]
        public void Guard_UnanswerableMajorOnSol1_EndsWithContentError_AndRestartReproduces()
        {
            var events = new List<EventDef> { Major("bad_briefing", 1, Choice("a", Res("power", -1)), Choice("b", Res("power", -2))) };
            Assert.IsFalse(ContentValidator.Validate(NoPowerMission(), events).Ok, "validation rejects it; the guard is defence in depth");
            var s = new GameSession(NoPowerMission(), events, 5, false);
            AssertContentError(s, "bad_briefing", "");
            string fp = Fingerprint(s);
            Assert.IsTrue(s.Restart().ok);
            AssertContentError(s, "bad_briefing", "");
            Assert.AreEqual(fp, Fingerprint(s));
        }

        [Test]
        public void Guard_UnanswerableMajorVariant_EndsWithContentError_OnAdvance()
        {
            var events = new List<EventDef>
            {
                MajorWithVariants("bad_storm", 2,
                    Variant("only", 1, Choice("clean", Res("power", -1)), Choice("brace", Health(-5)).Requires("power", 1))),
            };
            var s = new GameSession(NoPowerMission(), events, 1, false);
            SessionChange seen = null;
            int count = 0;
            s.Changed += ch => { seen = ch; count++; };
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(1, count);
            Assert.AreEqual(ChangeKind.MissionEnded, seen.kind);
            Assert.AreEqual(ReasonCodes.ContentErrorNoAvailableChoice, s.LastReport.endReason);
            AssertContentError(s, "bad_storm", "only");
        }

        [Test]
        public void Guard_UnanswerableRandomEvent_EndsWithContentError_Deterministically()
        {
            var c = NoPowerMission();
            c.eventChancePercent = 100;
            var events = new List<EventDef> { Random("random_bad", 2, 2, 1, true, Choice("pay", Res("power", -1))) };
            var a = new GameSession(c, events, 9, false);
            var b = new GameSession(c, events, 9, false);
            Assert.IsTrue(a.AdvanceSol(1).ok);
            var unused = b.Forecast; unused = b.Forecast; // reads must not change the run
            Assert.AreEqual(ReasonCodes.StaleSol, b.AdvanceSol(4).reason);
            Assert.IsTrue(b.AdvanceSol(1).ok);
            AssertContentError(a, "random_bad", "");
            Assert.AreEqual(Fingerprint(a), Fingerprint(b));
        }
    }
}
