using System.Collections.Generic;
using NUnit.Framework;
#if !UNITY_2017_1_OR_NEWER
using System.Text.Json;
#endif

// Shared by the Unity EditMode assembly and the plain .NET test project (included by path).

namespace RedFrontier.Core.Tests
{
    /// <summary>
    /// Illustrative test content. Placeholder game values chosen to exercise the rules;
    /// not final balance, not Developer 3's content, not NASA observations.
    /// </summary>
    internal static class Fixtures
    {
        // ---- Builders ----

        public static FlowDef Flow(string resource, int amount) { return new FlowDef { resource = resource, amount = amount }; }

        public static FacilityLevelDef Level(string id, FlowDef[] uses = null, FlowDef[] makes = null)
        {
            return new FacilityLevelDef
            {
                id = id,
                uses = new List<FlowDef>(uses ?? new FlowDef[0]),
                makes = new List<FlowDef>(makes ?? new FlowDef[0]),
            };
        }

        public static EffectDef Res(string target, int amount) { return new EffectDef { type = "resource", target = target, amount = amount }; }
        public static EffectDef Health(int amount) { return new EffectDef { type = "health", amount = amount }; }

        public static ChoiceDef Choice(string id, params EffectDef[] effects)
        {
            return new ChoiceDef { id = id, label = id, preview = id, effects = new List<EffectDef>(effects) };
        }

        public static ChoiceDef Requires(this ChoiceDef c, string resource, int min)
        {
            c.requires.Add(new RequirementDef { resource = resource, min = min });
            return c;
        }

        public static EventDef Major(string id, int sol, params ChoiceDef[] choices)
        {
            return new EventDef
            {
                schemaVersion = 1, id = id, category = "test", severity = "warning", dataBasis = "illustrative",
                title = id, body = id, trigger = new EventTrigger { sol = sol }, choices = new List<ChoiceDef>(choices),
            };
        }

        public static EventVariantDef Variant(string id, int weight, params ChoiceDef[] choices)
        {
            return new EventVariantDef { id = id, weight = weight, body = id, choices = new List<ChoiceDef>(choices) };
        }

        public static EventDef MajorWithVariants(string id, int sol, params EventVariantDef[] variants)
        {
            var e = Major(id, sol);
            e.variants = new List<EventVariantDef>(variants);
            return e;
        }

        public static EventDef Random(string id, int minSol, int maxSol, int weight, bool once, params ChoiceDef[] choices)
        {
            var e = Major(id, 0, choices);
            e.trigger = new EventTrigger { minSol = minSol, maxSol = maxSol, weight = weight, once = once };
            return e;
        }

        // ---- Stage 1 slice: power, air, Crew Health ----

        public static MissionConfig Stage1Mission(int missionLength = 10)
        {
            return new MissionConfig
            {
                schemaVersion = 1,
                missionLengthSols = missionLength,
                crewCount = 4,
                startHealth = 100,
                healthRecoveryPerSol = 5,
                resources = new List<ResourceDef>
                {
                    new ResourceDef { id = "power", start = 8, capacity = 20 },
                    new ResourceDef { id = "air", start = 12, capacity = 20 },
                },
                crewNeeds = new List<CrewNeedDef>
                {
                    new CrewNeedDef { resource = "air", perCrewPerSol = 1, healthPenaltyPerSol = 25 },
                },
                facilities = new List<FacilityDef>
                {
                    new FacilityDef { id = "solar_array", startLevel = 1, levels = new List<FacilityLevelDef>
                    {
                        Level("off"),
                        Level("on", makes: new[] { Flow("power", 10) }),
                    } },
                    new FacilityDef { id = "life_support", startLevel = 2, levels = new List<FacilityLevelDef>
                    {
                        Level("off"),
                        Level("eco", uses: new[] { Flow("power", 2) }, makes: new[] { Flow("air", 3) }),
                        Level("full", uses: new[] { Flow("power", 4) }, makes: new[] { Flow("air", 5) }),
                    } },
                },
            };
        }

        public static List<EventDef> Stage1Events()
        {
            return new List<EventDef>
            {
                Major("dust_storm_first", 2,
                    Choice("clean_panels", Res("power", -3)),
                    Choice("wait", Health(-10))),
            };
        }

        public static GameSession Stage1(int seed = 1) { return new GameSession(Stage1Mission(), Stage1Events(), seed); }

        // ---- Hybrid structure: two milestones (one with variants) and a small random pool ----

        public const int MilestoneA = 3;
        public const int MilestoneB = 7;

        public static MissionConfig HybridMission()
        {
            var c = Stage1Mission(10);
            c.eventChancePercent = 60;
            c.randomEventMinGapSols = 2;
            c.resources[0].start = 15;
            return c;
        }

        public static List<EventDef> HybridEvents()
        {
            return new List<EventDef>
            {
                MajorWithVariants("milestone_dust_storm", MilestoneA,
                    Variant("light", 1, Choice("clean_panels", Res("power", -2)), Choice("wait", Health(-5))),
                    Variant("heavy", 1, Choice("clean_panels", Res("power", -5)), Choice("wait", Health(-15))),
                    Variant("long", 2, Choice("clean_panels", Res("power", -3)), Choice("wait", Health(-10)))),
                Major("milestone_resupply", MilestoneB,
                    Choice("unload_now", Res("power", -2), Res("air", 5)),
                    Choice("unload_later", Health(-5))),
                Random("random_rover_fault", 2, 6, 1, true,
                    Choice("fix", Res("power", -2)), Choice("ignore", Health(-5))),
                Random("random_meteor_alert", 0, 0, 1, true,
                    Choice("shelter", Res("power", -3)), Choice("stay", Health(-10))),
                Random("random_crew_cold", 4, 0, 2, false,
                    Choice("rest", Res("power", -1)), Choice("push_on", Health(-5))),
                Random("random_late_signal", 9, 10, 1, true,
                    Choice("answer", Res("power", -1)), Choice("skip", Health(-2))),
            };
        }

        public static GameSession Hybrid(int seed) { return new GameSession(HybridMission(), HybridEvents(), seed); }

        // ---- Helpers ----

        /// <summary>Complete, deterministic dump of the gameplay state including the RNG.</summary>
#if UNITY_2017_1_OR_NEWER
        public static string Fingerprint(GameSession s) { return UnityEngine.JsonUtility.ToJson(s.Snapshot); }
#else
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        public static string Fingerprint(GameSession s) { return JsonSerializer.Serialize(s.Snapshot, Json); }
#endif

        /// <summary>Sensible bot: keeps default levels, takes the first available choice. Returns the reports.</summary>
        public static List<SolReport> PlayToEnd(GameSession s, int maxSteps = 100)
        {
            var reports = new List<SolReport>();
            for (int i = 0; i < maxSteps && s.Snapshot.phase != MissionPhase.Ended; i++)
            {
                AnswerPending(s);
                if (s.Snapshot.phase == MissionPhase.Ended) break;
                var r = s.AdvanceSol(s.Snapshot.sol);
                Assert.IsTrue(r.ok, r.ToString());
                reports.Add(s.LastReport);
            }
            return reports;
        }

        public static void AnswerPending(GameSession s)
        {
            var p = s.PendingEvent;
            if (p == null) return;
            foreach (var ch in p.choices)
            {
                if (!ch.available) continue;
                var r = s.ChooseEventOption(p.instanceId, ch.id);
                Assert.IsTrue(r.ok, r.ToString());
                return;
            }
            Assert.Fail("No available choice for " + p.eventId);
        }
    }
}
