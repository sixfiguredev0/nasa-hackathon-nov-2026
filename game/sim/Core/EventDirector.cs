using System.Collections.Generic;

namespace RedFrontier.Core
{
    /// <summary>
    /// Chooses at most one event for the current sol (step 7) and resolves its variant once.
    /// Hybrid structure: a major milestone scheduled on this sol always wins; only on a sol with no
    /// major event may the seeded random pool be rolled. Never applies effects.
    /// Events must arrive sorted by id (GameSession does this) so file-loading order cannot matter.
    /// </summary>
    internal static class EventDirector
    {
        public static void RaiseFor(SimState s, MissionConfig c, List<EventDef> sortedEvents)
        {
            if (!s.pending.IsNone) return;

            EventDef major = null;
            foreach (var e in sortedEvents)
                if (e.trigger.sol == s.sol) { major = e; break; }

            if (major != null)
            {
                Raise(s, major, PickVariant(s, major), true);
                return;
            }

            if (c.eventChancePercent <= 0) return;
            if (s.lastRandomEventSol > 0 && s.sol - s.lastRandomEventSol < c.randomEventMinGapSols) return;

            var pool = new List<EventDef>();
            int totalWeight = 0;
            foreach (var e in sortedEvents)
            {
                if (e.IsMajor) continue;
                var t = e.trigger;
                if (t.minSol > 0 && s.sol < t.minSol) continue;
                if (t.maxSol > 0 && s.sol > t.maxSol) continue;
                if (t.once && s.usedEventIds.Contains(e.id)) continue;
                pool.Add(e);
                totalWeight += Weight(t.weight);
            }
            // Empty pool: no roll, no RNG draw, the sol continues normally.
            if (pool.Count == 0) return;

            if (Rng.Range(s, 100) >= c.eventChancePercent) return;

            int pick = Rng.Range(s, totalWeight);
            foreach (var e in pool)
            {
                pick -= Weight(e.trigger.weight);
                if (pick < 0)
                {
                    Raise(s, e, "", false);
                    s.lastRandomEventSol = s.sol;
                    return;
                }
            }
        }

        static string PickVariant(SimState s, EventDef e)
        {
            if (e.variants.Count == 0) return "";
            if (e.variants.Count == 1) return e.variants[0].id;
            int total = 0;
            foreach (var v in e.variants) total += Weight(v.weight);
            int pick = Rng.Range(s, total);
            foreach (var v in e.variants)
            {
                pick -= Weight(v.weight);
                if (pick < 0) return v.id;
            }
            return e.variants[e.variants.Count - 1].id;
        }

        static void Raise(SimState s, EventDef e, string variantId, bool major)
        {
            s.pending = new PendingEvent
            {
                instanceId = s.nextEventInstanceId++,
                eventId = e.id,
                variantId = variantId,
                major = major,
                raisedOnSol = s.sol,
            };
            if (!s.usedEventIds.Contains(e.id)) s.usedEventIds.Add(e.id);
            s.phase = MissionPhase.AwaitingDecision;
            s.log.Add(new LogEntry
            {
                sol = s.sol,
                kind = LogKind.EventRaised,
                eventId = e.id,
                variantId = variantId,
                instanceId = s.pending.instanceId,
                eventClass = major ? "major" : "random",
            });
        }

        static int Weight(int w) { return w <= 0 ? 1 : w; }

        /// <summary>The choices of the pending event, from its stored variant. Never rolls.</summary>
        public static List<ChoiceDef> ResolvedChoices(EventDef e, string variantId)
        {
            if (variantId.Length == 0) return e.choices;
            foreach (var v in e.variants) if (v.id == variantId) return v.choices;
            return new List<ChoiceDef>();
        }

        public static EventVariantDef Variant(EventDef e, string variantId)
        {
            foreach (var v in e.variants) if (v.id == variantId) return v;
            return null;
        }
    }
}
