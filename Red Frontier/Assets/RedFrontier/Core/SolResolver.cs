using System.Collections.Generic;

namespace RedFrontier.Core
{
    /// <summary>
    /// Steps 1 to 3 of sol resolution (ARCHITECTURE section 4): facilities, crew needs, health.
    /// Pure function of the state passed in and the config. Uses no randomness, so running it on a
    /// copy gives the forecast without touching the live RNG.
    /// </summary>
    internal static class SolResolver
    {
        public static SolReport RunFlows(SimState s, MissionConfig c)
        {
            var report = new SolReport { sol = s.sol, healthBefore = s.crewHealth };
            foreach (var res in s.resources)
                report.resources.Add(new ResourceReport { id = res.id, start = res.amount });

            // Step 1: facilities in config order. Each runs at the highest level <= requested that it
            // can pay for in full, costs summed per resource. Costs are never clamped: an unaffordable level is not run.
            int shielding = 0;
            for (int i = 0; i < c.facilities.Count; i++)
            {
                var def = c.facilities[i];
                var fs = s.Facility(def.id);
                int requested = fs.level;
                int run = requested;
                while (run > 0 && !CanPay(s, def.levels[run].uses)) run--;

                var level = def.levels[run];
                foreach (var u in level.uses)
                {
                    s.Resource(u.resource).amount -= u.amount;
                    report.Resource(u.resource).usedByFacilities += u.amount;
                }
                // Modifiers scale output only (not costs): floor(amount * (100 + sum) / 100), minimum 0.
                int pct = EffectApplier.ModifierPercent(s, def.id);
                foreach (var m in level.makes)
                {
                    var rs = s.Resource(m.resource);
                    var rr = report.Resource(m.resource);
                    int scaled = 100 + pct <= 0 ? 0 : m.amount * (100 + pct) / 100;
                    int room = rs.capacity - rs.amount;
                    int added = scaled < room ? scaled : room;
                    rs.amount += added;
                    rr.made += added;
                    rr.wasted += scaled - added;
                }
                shielding += level.shielding;
                report.facilities.Add(new FacilityReport
                {
                    id = def.id,
                    requestedLevel = requested,
                    levelRun = run,
                    reason = run == requested ? "" : ReasonCodes.InsufficientResource,
                    modifierPercent = pct,
                    shielding = level.shielding,
                });
            }

            // Step 2: crew needs. Unmet need is a shortage, not a negative stock.
            var shortages = new List<KeyValuePair<CrewNeedDef, int>>();
            foreach (var need in c.crewNeeds)
            {
                var rs = s.Resource(need.resource);
                int required = need.perCrewPerSol * s.crewCount;
                int consumed = rs.amount < required ? rs.amount : required;
                rs.amount -= consumed;
                var rr = report.Resource(need.resource);
                rr.usedByCrew += consumed;
                if (required > consumed)
                {
                    rr.shortage += required - consumed;
                    shortages.Add(new KeyValuePair<CrewNeedDef, int>(need, required - consumed));
                }
            }

            // Step 3a: radiation. Exposure = baseline + event units queued for this sol; shielding from levels that
            // actually ran blocks up to its total. Pending units are consumed here, so each applies exactly once.
            report.radiationExposure = c.radiationPerSol + s.radiationPending;
            report.radiationBlocked = shielding < report.radiationExposure ? shielding : report.radiationExposure;
            report.radiationNet = report.radiationExposure - report.radiationBlocked;
            s.radiationPending = 0;
            s.radiationTotal += report.radiationNet;

            // Step 3b: health. Penalty per shortage and per unshielded radiation unit; recovery only on a sol with
            // neither, so recovery never cancels damage. Clamp 0..100.
            int health = s.crewHealth;
            int radDamage = report.radiationNet * c.radiationDamagePerUnit;
            if (radDamage > 0)
            {
                int before = health;
                health -= radDamage;
                if (health < 0) health = 0;
                report.healthCauses.Add(new HealthCause { code = "radiation", amount = health - before });
            }
            if (shortages.Count > 0)
            {
                foreach (var kv in shortages)
                {
                    int before = health;
                    health -= kv.Key.healthPenaltyPerSol;
                    if (health < 0) health = 0;
                    report.healthCauses.Add(new HealthCause { code = "shortage", resource = kv.Key.resource, amount = health - before });
                }
            }
            else if (radDamage == 0 && c.healthRecoveryPerSol > 0 && health < 100)
            {
                int before = health;
                health += c.healthRecoveryPerSol;
                if (health > 100) health = 100;
                report.healthCauses.Add(new HealthCause { code = "recovery", amount = health - before });
            }
            s.crewHealth = health;
            report.healthAfter = health;

            foreach (var rr in report.resources) rr.end = s.Resource(rr.id).amount;
            return report;
        }

        /// <summary>Costs are summed per resource (same rule as choice costs), so two entries of 3 need 6.</summary>
        static bool CanPay(SimState s, List<FlowDef> uses)
        {
            var total = new Dictionary<string, int>();
            foreach (var u in uses)
            {
                int sum;
                total.TryGetValue(u.resource, out sum);
                total[u.resource] = sum + u.amount;
            }
            foreach (var kv in total)
                if (s.Resource(kv.Key).amount < kv.Value) return false;
            return true;
        }
    }
}
