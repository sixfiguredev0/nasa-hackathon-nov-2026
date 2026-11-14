using System.Collections.Generic;
using NUnit.Framework;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>The ten Stage 1 acceptance tests from IMPLEMENTATION_PLAN.md, plus Stage 0 smoke.</summary>
    public class Stage1Tests
    {
        [Test]
        public void Stage0_Smoke_SessionStartsOnSolOne()
        {
            var s = Stage1();
            Assert.AreEqual(1, s.Snapshot.sol);
            Assert.AreEqual(MissionPhase.Planning, s.Snapshot.phase);
        }

        [Test]
        public void A1_SensiblePlaythrough_EndsInSuccessOnExpectedSol()
        {
            var s = Stage1();
            var reports = PlayToEnd(s);
            var snap = s.Snapshot;
            Assert.AreEqual(MissionOutcome.Success, snap.outcome);
            Assert.AreEqual(ReasonCodes.MissionComplete, snap.endReason);
            Assert.AreEqual(10, reports.Count, "one advance per sol");
            Assert.AreEqual(11, snap.sol, "sol counter passes missionLengthSols");
            Assert.AreEqual(MissionOutcome.Success, reports[9].outcome);
        }

        [Test]
        public void A2_LifeSupportOff_EndsInFailure_WithAirShortageAsCause()
        {
            var s = Stage1();
            Assert.IsTrue(s.SetFacilityLevel("life_support", 0).ok);
            var reports = PlayToEnd(s);
            var snap = s.Snapshot;
            Assert.AreEqual(MissionOutcome.Failure, snap.outcome);
            Assert.AreEqual(ReasonCodes.HealthZero, snap.endReason);

            bool airCause = false;
            foreach (var r in reports)
                foreach (var c in r.healthCauses)
                    if (c.code == "shortage" && c.resource == "air" && c.amount < 0) airCause = true;
            Assert.IsTrue(airCause, "air shortage listed as a health cause");
            var last = reports[reports.Count - 1];
            Assert.Greater(last.Resource("air").shortage, 0);
            Assert.AreEqual(MissionOutcome.Failure, last.outcome);
        }

        [Test]
        public void A3_SetFacilityLevel_ChangesNextSolFlowsOnly()
        {
            var s = Stage1();
            var before = s.Snapshot;
            var forecastFull = s.Forecast;
            Assert.IsTrue(s.SetFacilityLevel("life_support", 1).ok);
            var after = s.Snapshot;
            for (int i = 0; i < before.resources.Count; i++)
                Assert.AreEqual(before.resources[i].amount, after.resources[i].amount, "no stock change before AdvanceSol");
            Assert.AreEqual(before.crewHealth, after.crewHealth);
            Assert.AreEqual(1, after.Facility("life_support").level);

            var forecastEco = s.Forecast;
            Assert.AreNotEqual(forecastFull.Resource("air").made, forecastEco.Resource("air").made);

            Assert.IsTrue(s.AdvanceSol(1).ok);
            var rep = s.LastReport;
            Assert.AreEqual(3, rep.Resource("air").made, "eco level output");
            Assert.AreEqual(2, rep.Resource("power").usedByFacilities, "eco level input");
            Assert.AreEqual(forecastEco.Resource("air").end, rep.Resource("air").end, "forecast matches result when no event intervenes");
        }

        [Test]
        public void A4_StaleAdvance_IsRejected_StateUnchanged()
        {
            var s = Stage1();
            Assert.IsTrue(s.AdvanceSol(1).ok);
            AnswerPending(s); // sol 2 milestone
            string fp = Fingerprint(s);
            var r = s.AdvanceSol(1);
            Assert.IsFalse(r.ok);
            Assert.AreEqual(ReasonCodes.StaleSol, r.reason);
            Assert.AreEqual(fp, Fingerprint(s));
        }

        [Test]
        public void A5_Advance_IsRejectedWhileEventPending()
        {
            var s = Stage1();
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.IsNotNull(s.PendingEvent);
            string fp = Fingerprint(s);
            var r = s.AdvanceSol(2);
            Assert.AreEqual(ReasonCodes.DecisionPending, r.reason);
            Assert.AreEqual(ReasonCodes.DecisionPending, s.CanAdvanceSol().reason);
            Assert.AreEqual(fp, Fingerprint(s));
        }

        [Test]
        public void A6_ImmediateEffects_ApplyOnce_RepeatChoiceRejected()
        {
            var s = Stage1();
            Assert.IsTrue(s.AdvanceSol(1).ok);
            var p = s.PendingEvent;
            int power = s.Snapshot.Resource("power").amount;
            Assert.IsTrue(s.ChooseEventOption(p.instanceId, "clean_panels").ok);
            Assert.AreEqual(power - 3, s.Snapshot.Resource("power").amount);

            string fp = Fingerprint(s);
            var again = s.ChooseEventOption(p.instanceId, "clean_panels");
            Assert.IsFalse(again.ok);
            Assert.AreEqual(ReasonCodes.NoPendingEvent, again.reason);
            Assert.AreEqual(fp, Fingerprint(s));
            Assert.AreEqual(power - 3, s.Snapshot.Resource("power").amount);
        }

        [Test]
        public void A7_WritingToSnapshot_DoesNotAlterLaterSnapshots()
        {
            var s = Stage1();
            var snap = s.Snapshot;
            snap.crewHealth = 1;
            snap.resources[0].amount = 999;
            snap.facilities[0].level = 0;
            snap.log.Add(new LogEntry { kind = "forged" });
            var report = s.Forecast; report.healthAfter = -5;
            var cfg = s.Config; cfg.missionLengthSols = 1;

            var fresh = s.Snapshot;
            Assert.AreEqual(100, fresh.crewHealth);
            Assert.AreEqual(8, fresh.resources[0].amount);
            Assert.AreEqual(1, fresh.facilities[0].level);
            Assert.AreEqual(0, fresh.log.Count);
            Assert.AreEqual(10, s.Config.missionLengthSols);
        }

        [Test]
        public void A8_Changed_FiresOncePerAcceptedCommand_NeverOnRejection()
        {
            var s = Stage1();
            var kinds = new List<ChangeKind>();
            s.Changed += c => kinds.Add(c.kind);

            Assert.IsTrue(s.SetFacilityLevel("life_support", 1).ok);       // 1
            Assert.IsFalse(s.SetFacilityLevel("nope", 1).ok);
            Assert.IsFalse(s.SetFacilityLevel("life_support", 9).ok);
            Assert.IsFalse(s.AdvanceSol(5).ok);
            Assert.IsTrue(s.AdvanceSol(1).ok);                             // 2 (event raised)
            Assert.IsFalse(s.AdvanceSol(2).ok);
            Assert.IsFalse(s.SetFacilityLevel("life_support", 2).ok);
            var p = s.PendingEvent;
            Assert.IsFalse(s.ChooseEventOption(p.instanceId + 1, "wait").ok);
            Assert.IsFalse(s.ChooseEventOption(p.instanceId, "nope").ok);
            Assert.IsTrue(s.ChooseEventOption(p.instanceId, "wait").ok);   // 3
            Assert.IsTrue(s.AdvanceSol(2).ok);                             // 4
            Assert.IsTrue(s.Restart().ok);                                 // 5

            CollectionAssert.AreEqual(new[]
            {
                ChangeKind.CommandApplied, ChangeKind.EventRaised, ChangeKind.CommandApplied,
                ChangeKind.SolResolved, ChangeKind.Restarted,
            }, kinds);
        }

        [Test]
        public void A9_SameSeedAndCommands_GiveIdenticalFinalState()
        {
            var a = Hybrid(42); var b = Hybrid(42);
            PlayToEnd(a); PlayToEnd(b);
            Assert.AreEqual(Fingerprint(a), Fingerprint(b));
            var c = Stage1(7); var d = Stage1(7);
            PlayToEnd(c); PlayToEnd(d);
            Assert.AreEqual(Fingerprint(c), Fingerprint(d));
        }

        [Test]
        public void A10_AfterEnd_OnlyRestartAccepted_RestartReturnsToSolOne()
        {
            var s = Stage1();
            PlayToEnd(s);
            Assert.AreEqual(MissionPhase.Ended, s.Snapshot.phase);
            string fp = Fingerprint(s);
            Assert.AreEqual(ReasonCodes.MissionEnded, s.SetFacilityLevel("life_support", 1).reason);
            Assert.AreEqual(ReasonCodes.MissionEnded, s.AdvanceSol(s.Snapshot.sol).reason);
            Assert.AreEqual(ReasonCodes.MissionEnded, s.ChooseEventOption(1, "wait").reason);
            Assert.IsNull(s.Forecast);
            Assert.AreEqual(fp, Fingerprint(s));

            Assert.IsTrue(s.Restart().ok);
            var snap = s.Snapshot;
            Assert.AreEqual(1, snap.sol);
            Assert.AreEqual(MissionPhase.Planning, snap.phase);
            Assert.AreEqual(MissionOutcome.None, snap.outcome);
            Assert.AreEqual(100, snap.crewHealth);
            Assert.IsNull(s.LastReport);
            Assert.AreEqual(0, snap.log.Count);
        }
    }
}
