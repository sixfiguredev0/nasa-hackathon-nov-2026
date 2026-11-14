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
            // can pay for in full. Costs are never clamped: an unaffordable level is not run.
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
                foreach (var m in level.makes)
                {
                    var rs = s.Resource(m.resource);
                    var rr = report.Resource(m.resource);
                    int room = rs.capacity - rs.amount;
                    int added = m.amount < room ? m.amount : room;
                    rs.amount += added;
                    rr.made += added;
                    rr.wasted += m.amount - added;
                }
                report.facilities.Add(new FacilityReport
                {
                    id = def.id,
                    requestedLevel = requested,
                    levelRun = run,
                    reason = run == requested ? "" : ReasonCodes.InsufficientResource,
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

            // Step 3: health. Penalty per shortage; recovery only on a sol with no shortage. Clamp 0..100.
            int health = s.crewHealth;
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
            else if (c.healthRecoveryPerSol > 0 && health < 100)
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

        static bool CanPay(SimState s, List<FlowDef> uses)
        {
            foreach (var u in uses)
                if (s.Resource(u.resource).amount < u.amount) return false;
            return true;
        }
    }
}
