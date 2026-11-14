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
                if (ef.type != "resource" || ef.amount >= 0) continue;
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

        /// <summary>Applies immediate effects. Caller has already checked CheckChoice.</summary>
        public static void ApplyImmediate(SimState s, List<EffectDef> effects)
        {
            foreach (var ef in effects)
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
                }
            }
        }
    }
}
