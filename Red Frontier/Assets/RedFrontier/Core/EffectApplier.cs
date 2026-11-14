using System.Collections.Generic;

namespace RedFrontier.Core
{
    /// <summary>The only code that interprets effect types. Called only by GameSession.</summary>
    internal static class EffectApplier
    {
        /// <summary>
        /// "" if the choice can be taken now. Requirements are checks only; negative resource effects
        /// are costs and must be affordable in full (summed per resource). Neither is ever clamped.
        /// </summary>
        public static string CheckChoice(SimState s, ChoiceDef choice)
        {
            foreach (var q in choice.requires)
                if (s.Resource(q.resource).amount < q.min) return ReasonCodes.RequirementNotMet;

            var cost = new Dictionary<string, int>();
            foreach (var ef in choice.effects)
            {
                if (ef.type != "resource" || ef.amount >= 0 || ef.delaySols > 0) continue;
                int sum;
                cost.TryGetValue(ef.target, out sum);
                cost[ef.target] = sum - ef.amount;
            }
            foreach (var kv in cost)
                if (s.Resource(kv.Key).amount < kv.Value) return ReasonCodes.CannotAfford;
            return "";
        }

        /// <summary>
        /// Fallback choice: no requirements and no resource cost, so CheckChoice accepts it in every state.
        /// It may still carry a negative consequence (for example a health loss).
        /// </summary>
        public static bool IsFallback(ChoiceDef choice)
        {
            if (choice.requires != null && choice.requires.Count > 0) return false;
            foreach (var ef in choice.effects ?? new List<EffectDef>())
                if (ef != null && ef.type == "resource" && ef.amount < 0) return false;
            return true;
        }

        /// <summary>Applies a choice's effects (caller checked CheckChoice). delaySols >= 1 queues the effect for sol + delaySols.</summary>
        public static void ApplyChoice(SimState s, List<EffectDef> effects, string eventId, string choiceId)
        {
            foreach (var ef in effects)
            {
                if (ef.delaySols > 0)
                    s.scheduled.Add(new ScheduledEffect { dueSol = s.sol + ef.delaySols, effect = ef.Clone(), sourceEventId = eventId, sourceChoiceId = choiceId });
                else
                    Apply(s, ef, eventId, choiceId);
            }
        }

        /// <summary>Step 5: applies, once and in queue order, every effect due on the (new) current sol, and removes it.</summary>
        public static List<ScheduledEffect> ApplyDue(SimState s)
        {
            var due = new List<ScheduledEffect>();
            for (int i = 0; i < s.scheduled.Count; i++)
                if (s.scheduled[i].dueSol <= s.sol) due.Add(s.scheduled[i]);
            foreach (var d in due)
            {
                s.scheduled.Remove(d);
                Apply(s, d.effect, d.sourceEventId, d.sourceChoiceId);
            }
            return due;
        }

        /// <summary>Sum of modifier percents active on this facility on the state's current sol.</summary>
        public static int ModifierPercent(SimState s, string facilityId)
        {
            int p = 0;
            foreach (var m in s.modifiers)
                if (m.facilityId == facilityId && m.firstSol <= s.sol && s.sol <= m.lastSol) p += m.percent;
            return p;
        }

        static void Apply(SimState s, EffectDef ef, string eventId, string choiceId)
        {
            {
                switch (ef.type)
                {
                    case "resource":
                    {
                        var rs = s.Resource(ef.target);
                        int v = rs.amount + ef.amount;
                        // Gains clamp to capacity (overflow lost). Losses were checked affordable, so never go below 0.
                        rs.amount = v > rs.capacity ? rs.capacity : v;
                        break;
                    }
                    case "health":
                    {
                        int h = s.crewHealth + ef.amount;
                        s.crewHealth = h < 0 ? 0 : (h > 100 ? 100 : h);
                        break;
                    }
                    case "modifier":
                        // Active on sols S..S+d-1, S = the sol it takes effect (choice sol, or due sol if delayed).
                        s.modifiers.Add(new ActiveModifier
                        {
                            facilityId = ef.target, percent = ef.percent, firstSol = s.sol, lastSol = s.sol + ef.durationSols - 1,
                            sourceEventId = eventId, sourceChoiceId = choiceId,
                        });
                        break;
                    case "radiation":
                        s.radiationPending += ef.amount;
                        break;
                }
            }
        }
    }
}
