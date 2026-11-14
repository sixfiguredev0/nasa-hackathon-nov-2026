using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static RedFrontier.Core.Tests.Fixtures;

namespace RedFrontier.Core.Tests
{
    /// <summary>Stage 5 (decision 0007): timed modifiers, delayed effects, radiation, extra resources.</summary>
    public class Stage5Tests
    {
        static EffectDef Mod(string facility, int percent, int duration, int delay = 0)
        { return new EffectDef { type = "modifier", target = facility, percent = percent, durationSols = duration, delaySols = delay }; }
        static EffectDef Rad(int amount) { return new EffectDef { type = "radiation", amount = amount }; }
        static EffectDef Delayed(EffectDef e, int delay) { e.delaySols = delay; return e; }

        static GameSession WithSol1Event(MissionConfig c, params EffectDef[] effects)
        {
            var ev = Major("ev", 1, Choice("take", effects), Choice("skip", Health(-1)));
            var s = new GameSession(c, new List<EventDef> { ev }, 1);
            Assert.IsTrue(s.ChooseEventOption(s.PendingEvent.instanceId, "take").ok);
            return s;
        }

        static int Made(SolReport r, string res) { return r.resources.Single(x => x.id == res).made; }

        [Test]
        public void Modifier_ScalesOutputForItsDuration_ThenExpires_ForecastMatches()
        {
            var s = WithSol1Event(Stage1Mission(), Mod("solar_array", -50, 2));
            Assert.AreEqual(5, Made(s.Forecast, "power"), "forecast includes the active modifier");
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(5, Made(s.LastReport, "power"));
            Assert.AreEqual(-50, s.LastReport.facilities.Single(f => f.id == "solar_array").modifierPercent);
            Assert.IsTrue(s.AdvanceSol(2).ok);
            Assert.AreEqual(5, Made(s.LastReport, "power"), "sol 2 is the second sol of a 2-sol modifier");
            Assert.AreEqual(0, s.Snapshot.modifiers.Count, "expired on entering sol 3");
            Assert.AreEqual(10, Made(s.Forecast, "power"));
        }

        [Test]
        public void Modifiers_StackAdditively_AndClampAtZero()
        {
            var s = WithSol1Event(Stage1Mission(), Mod("solar_array", -50, 1), Mod("solar_array", -30, 1));
            Assert.AreEqual(2, Made(s.Forecast, "power"), "10 * (100 - 80) / 100");
            var t = WithSol1Event(Stage1Mission(), Mod("solar_array", -100, 1), Mod("solar_array", -50, 1));
            Assert.AreEqual(0, Made(t.Forecast, "power"));
        }

        [Test]
        public void DelayedEffect_LandsOnceOnItsSol_AndIsReported()
        {
            var s = WithSol1Event(Stage1Mission(), Delayed(Res("power", 5), 2));
            Assert.AreEqual(1, s.Snapshot.scheduled.Count);
            Assert.IsTrue(s.AdvanceSol(1).ok);
            int before = s.Snapshot.Resource("power").amount;
            Assert.AreEqual(0, s.LastReport.delayedApplied.Count);
            Assert.IsTrue(s.AdvanceSol(2).ok);
            Assert.AreEqual(1, s.LastReport.delayedApplied.Count);
            Assert.AreEqual(0, s.Snapshot.scheduled.Count);
            Assert.AreEqual(1, s.History.Count(e => e.kind == LogKind.DelayedEffect));
            Assert.IsTrue(s.AdvanceSol(3).ok);
            Assert.AreEqual(1, s.History.Count(e => e.kind == LogKind.DelayedEffect), "never applied twice");
            Assert.Greater(s.Snapshot.Resource("power").amount, 0);
            Assert.AreNotEqual(before, 0);
        }

        [Test]
        public void DelayedModifier_StartsOnItsDueSol()
        {
            var s = WithSol1Event(Stage1Mission(), Mod("solar_array", -50, 1, delay: 1));
            Assert.AreEqual(10, Made(s.Forecast, "power"), "not active before its due sol");
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(5, Made(s.Forecast, "power"));
        }

        [Test]
        public void Restart_ClearsModifiersScheduledAndRadiation()
        {
            var c = Stage1Mission(); c.radiationDamagePerUnit = 1;
            var s = WithSol1Event(c, Mod("solar_array", -50, 3), Delayed(Health(-5), 2), Rad(4));
            Assert.IsTrue(s.Restart(7).ok);
            Assert.AreEqual(0, s.Snapshot.modifiers.Count);
            Assert.AreEqual(0, s.Snapshot.scheduled.Count);
            Assert.AreEqual(0, s.Snapshot.radiationPending);
            Assert.AreEqual(0, s.Snapshot.radiationTotal);
        }

        static MissionConfig RadMission()
        {
            var c = Stage1Mission();
            c.startHealth = 80;
            c.radiationPerSol = 1;
            c.radiationDamagePerUnit = 2;
            c.facilities.Add(new FacilityDef { id = "shelter", startLevel = 0, levels = new List<FacilityLevelDef>
            {
                Level("off"),
                new FacilityLevelDef { id = "on", uses = new List<FlowDef> { Flow("power", 1) }, shielding = 3 },
            } });
            return c;
        }

        [Test]
        public void Radiation_DamagesOnce_BlocksRecovery_AndShieldingProtects()
        {
            var s = WithSol1Event(RadMission(), Rad(3));
            var f = s.Forecast;
            Assert.AreEqual(4, f.radiationExposure);
            Assert.AreEqual(4, f.radiationNet);
            Assert.AreEqual(80 - 8, f.healthAfter, "no recovery on a damage sol");
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(72, s.Snapshot.crewHealth);
            Assert.AreEqual(0, s.Snapshot.radiationPending, "event exposure consumed");
            Assert.AreEqual(1, s.Forecast.radiationExposure, "event exposure applied exactly once");

            Assert.IsTrue(s.SetFacilityLevel("shelter", 1).ok);
            var g = s.Forecast;
            Assert.AreEqual(1, g.radiationBlocked);
            Assert.AreEqual(0, g.radiationNet);
            Assert.AreEqual(77, g.healthAfter, "fully shielded sol recovers");
            Assert.IsTrue(s.AdvanceSol(2).ok);
            Assert.AreEqual(77, s.Snapshot.crewHealth);
            Assert.AreEqual(4, s.Snapshot.radiationTotal);
        }

        [Test]
        public void Shortage_OfAnyResource_BlocksRecovery()
        {
            var c = Stage1Mission();
            c.startHealth = 50;
            c.resources.Add(new ResourceDef { id = "water", start = 2, capacity = 10 });
            c.crewNeeds.Add(new CrewNeedDef { resource = "water", perCrewPerSol = 1, healthPenaltyPerSol = 15 });
            var s = new GameSession(c, new List<EventDef>(), 1);
            Assert.IsTrue(s.AdvanceSol(1).ok);
            Assert.AreEqual(2, s.LastReport.resources.Single(r => r.id == "water").shortage);
            Assert.AreEqual(35, s.Snapshot.crewHealth);
        }

        [Test]
        public void Validator_RejectsUnsupportedOrMalformedStage5Content()
        {
            var c = RadMission();
            System.Func<EffectDef, bool> ok = e =>
                ContentValidator.Validate(c, new List<EventDef> { Major("ev", 2, Choice("take", e), Choice("skip", Health(-1))) }).Ok;
            Assert.IsTrue(ok(Mod("solar_array", -50, 2)));
            Assert.IsTrue(ok(Rad(2)));
            Assert.IsTrue(ok(Delayed(Res("air", 3), 2)));
            Assert.IsFalse(ok(Mod("nope", -50, 2)), "unknown facility");
            Assert.IsFalse(ok(Mod("solar_array", -50, 0)), "duration");
            Assert.IsFalse(ok(Mod("solar_array", -150, 1)), "below -100");
            Assert.IsFalse(ok(Rad(0)));
            Assert.IsFalse(ok(Delayed(Res("power", -2), 1)), "costs cannot be delayed");
            c.facilities.Last().levels[1].shielding = -1;
            Assert.IsFalse(ok(Rad(2)), "negative shielding");
        }

        [Test]
        public void Forecast_DoesNotConsumeRngOrMutateState()
        {
            var a = WithSol1Event(RadMission(), Rad(2), Mod("solar_array", 20, 2));
            var b = WithSol1Event(RadMission(), Rad(2), Mod("solar_array", 20, 2));
            for (int i = 0; i < 5; i++) { var _ = a.Forecast; }
            Assert.AreEqual(Fingerprint(b), Fingerprint(a));
        }
    }
}
