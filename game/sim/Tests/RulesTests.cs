using System.Collections.Generic;
using NUnit.Framework;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>Cost, requirement, fallback and validation rules.</summary>
    public class RulesTests
    {
        static MissionConfig LowPower(int power)
        {
            var c = Stage1Mission();
            c.resources[0].start = power;
            c.startHealth = 80;
            return c;
        }

        [Test]
        public void UnaffordableCost_IsRejected_NotClampedToZero()
        {
            var events = new List<EventDef>
            {
                Major("costly_repair", 1, Choice("pay", Res("power", -6)), Choice("skip", Health(-5))),
            };
            var s = new GameSession(LowPower(4), events, 1);
            var p = s.PendingEvent;
            Assert.IsFalse(p.choices[0].available);
            Assert.AreEqual(ReasonCodes.CannotAfford, p.choices[0].unavailableReason);
            Assert.AreEqual(-6, p.choices[0].effects[0].amount, "cost shown before choosing");
            Assert.IsTrue(p.choices[1].available);

            string fp = Fingerprint(s);
            var r = s.ChooseEventOption(p.instanceId, "pay");
            Assert.AreEqual(ReasonCodes.CannotAfford, r.reason);
            Assert.AreEqual(fp, Fingerprint(s));
            Assert.AreEqual(4, s.Snapshot.Resource("power").amount);
        }

        [Test]
        public void SplitCosts_OnSameResource_AreSummed()
        {
            var events = new List<EventDef>
            {
                Major("double_cost", 1, Choice("pay", Res("power", -3), Res("power", -3)), Choice("skip", Health(-5))),
            };
            var s = new GameSession(LowPower(5), events, 1);
            Assert.AreEqual(ReasonCodes.CannotAfford, s.ChooseEventOption(s.PendingEvent.instanceId, "pay").reason);
        }

        [Test]
        public void Requirement_IsCheckedButNotSpent()
        {
            var events = new List<EventDef>
            {
                Major("battery_check", 1,
                    Choice("inspect", Health(5)).Requires("power", 5),
                    Choice("skip", Health(-1))),
            };
            var low = new GameSession(LowPower(4), events, 1);
            Assert.AreEqual(ReasonCodes.RequirementNotMet, low.PendingEvent.choices[0].unavailableReason);
            Assert.AreEqual(ReasonCodes.RequirementNotMet, low.ChooseEventOption(low.PendingEvent.instanceId, "inspect").reason);

            var ok = new GameSession(LowPower(6), events, 1);
            Assert.IsTrue(ok.ChooseEventOption(ok.PendingEvent.instanceId, "inspect").ok);
            Assert.AreEqual(6, ok.Snapshot.Resource("power").amount, "requirement did not spend power");
            Assert.AreEqual(85, ok.Snapshot.crewHealth);
        }

        [Test]
        public void Facility_FallsBackToAffordableLevel_StockNeverNegative()
        {
            var c = LowPower(3);
            var s = new GameSession(c, new List<EventDef>(), 1);
            Assert.IsTrue(s.SetFacilityLevel("solar_array", 0).ok);
            Assert.IsTrue(s.AdvanceSol(1).ok);
            var rep = s.LastReport;
            var ls = rep.facilities[1];
            Assert.AreEqual(2, ls.requestedLevel);
            Assert.AreEqual(1, ls.levelRun);
            Assert.AreEqual(ReasonCodes.InsufficientResource, ls.reason);
            Assert.AreEqual(1, s.Snapshot.Resource("power").amount);

            Assert.IsTrue(s.AdvanceSol(2).ok);
            Assert.AreEqual(0, s.LastReport.facilities[1].levelRun, "cannot afford eco either: off, not a free run");
            Assert.AreEqual(1, s.Snapshot.Resource("power").amount);
        }

        [Test]
        public void Production_ClampsToCapacity_AndReportsWaste()
        {
            var c = Stage1Mission();
            c.resources[0].start = 18;
            var s = new GameSession(c, new List<EventDef>(), 1);
            Assert.IsTrue(s.AdvanceSol(1).ok);
            var p = s.LastReport.Resource("power");
            Assert.AreEqual(2, p.made);
            Assert.AreEqual(8, p.wasted);
            Assert.AreEqual(16, p.end);
        }

        [Test]
        public void ChoiceThatZeroesHealth_EndsMissionImmediately()
        {
            var c = Stage1Mission();
            c.startHealth = 10;
            var events = new List<EventDef> { Major("hazard", 1, Choice("risk", Health(-10)), Choice("other", Health(-10))) };
            var s = new GameSession(c, events, 1);
            Assert.IsTrue(s.ChooseEventOption(s.PendingEvent.instanceId, "risk").ok);
            Assert.AreEqual(MissionOutcome.Failure, s.Snapshot.outcome);
            Assert.AreEqual(ReasonCodes.HealthZero, s.Snapshot.endReason);
        }

        [Test]
        public void MissionLength_IsConfigurable()
        {
            var s = new GameSession(Stage1Mission(4), new List<EventDef>(), 1);
            var reports = PlayToEnd(s);
            Assert.AreEqual(4, reports.Count);
            Assert.AreEqual(MissionOutcome.Success, s.Snapshot.outcome);
        }

        [Test]
        public void Validator_RejectsBadContent()
        {
            var events = new List<EventDef>
            {
                Major("Bad_Id", 2, Choice("a", Health(-1))),
                Major("clash_one", 3, Choice("a", Health(-1))),
                Major("clash_two", 3, Choice("a", Health(-1))),
                Major("too_late", 99, Choice("a", Health(-1))),
                Major("bad_effect", 4, Choice("a", new EffectDef { type = "teleport" })),
                Major("unknown_res", 5, Choice("a", Res("water", -1))),
                Random("random_with_variants", 0, 0, 1, false),
            };
            events[6].variants.Add(Variant("v", 1, Choice("a", Health(-1))));
            var r = ContentValidator.Validate(Stage1Mission(), events);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("lowercase", string.Join("\n", r.errors));
            StringAssert.Contains("already has major event", string.Join("\n", r.errors));
            StringAssert.Contains("after the last sol", string.Join("\n", r.errors));
            StringAssert.Contains("unknown effect type", string.Join("\n", r.errors));
            StringAssert.Contains("unknown resource 'water'", string.Join("\n", r.errors));
            StringAssert.Contains("only major events", string.Join("\n", r.errors));
            Assert.Throws<ContentValidationException>(() => new GameSession(Stage1Mission(), events, 1));
        }

        [Test]
        public void Validator_RejectsVariantsThatChangeShape()
        {
            var e = MajorWithVariants("storm", 3,
                Variant("a", 1, Choice("clean", Res("power", -2)), Choice("wait", Health(-5))),
                Variant("b", 1, Choice("clean", Health(-2)), Choice("wait", Health(-5))));
            var r = ContentValidator.Validate(Stage1Mission(), new List<EventDef> { e });
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("variants may change amounts and text only", string.Join("\n", r.errors));
        }

        [Test]
        public void Validator_WarnsOnCostFreeChoice_RequirementIsNotACost()
        {
            var e = Major("free_lunch", 2, Choice("take", Res("power", 2)).Requires("power", 1), Choice("leave", Health(-1)));
            var r = ContentValidator.Validate(Stage1Mission(), new List<EventDef> { e });
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(1, r.warnings.Count);
            StringAssert.Contains("'take'", r.warnings[0]);
        }

        [Test]
        public void Validator_AcceptsFixtures()
        {
            var r1 = ContentValidator.Validate(Stage1Mission(), Stage1Events());
            Assert.IsTrue(r1.Ok, r1.ToString());
            var r2 = ContentValidator.Validate(HybridMission(), HybridEvents());
            Assert.IsTrue(r2.Ok, r2.ToString());
            Assert.AreEqual(0, r2.warnings.Count, r2.ToString());
        }
    }
}
