using System;
using System.Collections.Generic;

namespace RedFrontier.Core
{
    public sealed class ValidationResult
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public bool Ok { get { return errors.Count == 0; } }
        public override string ToString()
        {
            return "errors: " + string.Join("; ", errors) + " | warnings: " + string.Join("; ", warnings);
        }
    }

    public sealed class ContentValidationException : Exception
    {
        public readonly ValidationResult result;
        public ContentValidationException(ValidationResult result) : base("Invalid content: " + string.Join("; ", result.errors))
        {
            this.result = result;
        }
    }

    /// <summary>Checks mission config and events. Never fixes content silently. Messages are for developers, not players.</summary>
    public static class ContentValidator
    {
        public const int SchemaVersion = 1;
        public const int MaxChoices = 3;

        public static ValidationResult Validate(MissionConfig c, IList<EventDef> events)
        {
            var r = new ValidationResult();
            if (c == null) { r.errors.Add("mission: config is null"); return r; }
            var resourceIds = new HashSet<string>();
            var facilityIds = new HashSet<string>();
            ValidateMission(c, r, resourceIds, facilityIds);
            if (events == null) events = new List<EventDef>();

            var eventIds = new HashSet<string>();
            var majorSols = new Dictionary<int, string>();
            foreach (var e in events)
            {
                if (e == null) { r.errors.Add("event: null entry"); continue; }
                ValidateEvent(e, c, r, resourceIds, eventIds, majorSols);
            }
            return r;
        }

        static void ValidateMission(MissionConfig c, ValidationResult r, HashSet<string> resourceIds, HashSet<string> facilityIds)
        {
            if (c.schemaVersion != SchemaVersion) r.errors.Add("mission: unsupported schemaVersion " + c.schemaVersion);
            if (c.missionLengthSols < 1) r.errors.Add("mission: missionLengthSols must be >= 1");
            if (c.crewCount < 1) r.errors.Add("mission: crewCount must be >= 1");
            if (c.startHealth < 1 || c.startHealth > 100) r.errors.Add("mission: startHealth must be 1..100");
            if (c.healthRecoveryPerSol < 0) r.errors.Add("mission: healthRecoveryPerSol must be >= 0");
            if (c.eventChancePercent < 0 || c.eventChancePercent > 100) r.errors.Add("mission: eventChancePercent must be 0..100");
            if (c.randomEventMinGapSols < 0) r.errors.Add("mission: randomEventMinGapSols must be >= 0");

            foreach (var res in c.resources ?? new List<ResourceDef>())
            {
                if (res == null) { r.errors.Add("mission: null resource"); continue; }
                string where = "resource '" + res.id + "'";
                if (!IsId(res.id)) r.errors.Add(where + ": id must be lowercase snake_case");
                else if (!resourceIds.Add(res.id)) r.errors.Add(where + ": duplicate id");
                if (res.capacity < 1) r.errors.Add(where + ": capacity must be >= 1");
                if (res.start < 0 || res.start > res.capacity) r.errors.Add(where + ": start must be 0..capacity");
            }
            if (resourceIds.Count == 0) r.errors.Add("mission: at least one resource is required");

            foreach (var n in c.crewNeeds ?? new List<CrewNeedDef>())
            {
                if (n == null) { r.errors.Add("mission: null crew need"); continue; }
                if (!resourceIds.Contains(n.resource)) r.errors.Add("crew need: unknown resource '" + n.resource + "'");
                if (n.perCrewPerSol < 1) r.errors.Add("crew need '" + n.resource + "': perCrewPerSol must be >= 1");
                if (n.healthPenaltyPerSol < 0) r.errors.Add("crew need '" + n.resource + "': healthPenaltyPerSol must be >= 0");
            }

            foreach (var f in c.facilities ?? new List<FacilityDef>())
            {
                if (f == null) { r.errors.Add("mission: null facility"); continue; }
                string where = "facility '" + f.id + "'";
                if (!IsId(f.id)) r.errors.Add(where + ": id must be lowercase snake_case");
                else if (!facilityIds.Add(f.id)) r.errors.Add(where + ": duplicate id");
                var levels = f.levels ?? new List<FacilityLevelDef>();
                if (levels.Count == 0) { r.errors.Add(where + ": needs at least one level"); continue; }
                if (f.startLevel < 0 || f.startLevel >= levels.Count) r.errors.Add(where + ": startLevel out of range");
                for (int i = 0; i < levels.Count; i++)
                {
                    var l = levels[i];
                    if (l == null) { r.errors.Add(where + ": null level " + i); continue; }
                    string lw = where + " level " + i;
                    if (i == 0 && l.uses != null && l.uses.Count > 0) r.errors.Add(lw + ": level 0 must use nothing (it is the fallback)");
                    ValidateFlows(l.uses, lw + " uses", r, resourceIds);
                    ValidateFlows(l.makes, lw + " makes", r, resourceIds);
                }
            }
        }

        static void ValidateFlows(List<FlowDef> flows, string where, ValidationResult r, HashSet<string> resourceIds)
        {
            if (flows == null) return;
            foreach (var fl in flows)
            {
                if (fl == null) { r.errors.Add(where + ": null flow"); continue; }
                if (!resourceIds.Contains(fl.resource)) r.errors.Add(where + ": unknown resource '" + fl.resource + "'");
                if (fl.amount < 1) r.errors.Add(where + ": amount must be >= 1");
            }
        }

        static void ValidateEvent(EventDef e, MissionConfig c, ValidationResult r, HashSet<string> resourceIds,
            HashSet<string> eventIds, Dictionary<int, string> majorSols)
        {
            string where = "event '" + e.id + "'";
            if (e.schemaVersion != SchemaVersion) r.errors.Add(where + ": unsupported schemaVersion " + e.schemaVersion);
            if (!IsId(e.id)) r.errors.Add(where + ": id must be lowercase snake_case");
            else if (!eventIds.Add(e.id)) r.errors.Add(where + ": duplicate id");
            if (e.dataBasis != "illustrative" && e.dataBasis != "archived_nasa")
                r.errors.Add(where + ": dataBasis must be 'illustrative' or 'archived_nasa'");
            if (e.dataBasis == "archived_nasa" && string.IsNullOrEmpty(e.sourceRef))
                r.errors.Add(where + ": archived_nasa needs a sourceRef");

            var t = e.trigger ?? new EventTrigger();
            var variants = e.variants ?? new List<EventVariantDef>();
            var choices = e.choices ?? new List<ChoiceDef>();

            if (t.sol > 0)
            {
                if (t.sol > c.missionLengthSols) r.errors.Add(where + ": trigger.sol is after the last sol");
                string other;
                if (majorSols.TryGetValue(t.sol, out other)) r.errors.Add(where + ": sol " + t.sol + " already has major event '" + other + "'");
                else majorSols[t.sol] = e.id;
                if (t.minSol != 0 || t.maxSol != 0 || t.weight != 0 || t.once)
                    r.errors.Add(where + ": major events (trigger.sol set) must leave minSol, maxSol, weight and once unset");
            }
            else
            {
                if (t.sol < 0) r.errors.Add(where + ": trigger.sol must be >= 0");
                if (t.minSol < 0 || t.maxSol < 0) r.errors.Add(where + ": minSol and maxSol must be >= 0");
                if (t.minSol > 0 && t.maxSol > 0 && t.minSol > t.maxSol) r.errors.Add(where + ": minSol is after maxSol");
                if (t.weight < 0) r.errors.Add(where + ": weight must be >= 0");
                if (variants.Count > 0) r.errors.Add(where + ": only major events (trigger.sol set) may have variants");
            }

            if (variants.Count > 0)
            {
                if (choices.Count > 0) r.errors.Add(where + ": use either choices or variants, not both");
                var variantIds = new HashSet<string>();
                List<ChoiceDef> reference = null;
                foreach (var v in variants)
                {
                    if (v == null) { r.errors.Add(where + ": null variant"); continue; }
                    string vw = where + " variant '" + v.id + "'";
                    if (!IsId(v.id)) r.errors.Add(vw + ": id must be lowercase snake_case");
                    else if (!variantIds.Add(v.id)) r.errors.Add(vw + ": duplicate id");
                    if (v.weight < 0) r.errors.Add(vw + ": weight must be >= 0");
                    ValidateChoices(v.choices ?? new List<ChoiceDef>(), vw, r, resourceIds);
                    if (reference == null) reference = v.choices;
                    else CheckSameShape(reference, v.choices ?? new List<ChoiceDef>(), vw, r);
                }
            }
            else
            {
                ValidateChoices(choices, where, r, resourceIds);
            }
        }

        static void ValidateChoices(List<ChoiceDef> choices, string where, ValidationResult r, HashSet<string> resourceIds)
        {
            if (choices.Count < 1 || choices.Count > MaxChoices) r.errors.Add(where + ": needs 1 to " + MaxChoices + " choices");
            var ids = new HashSet<string>();
            bool hasFallback = false;
            foreach (var ch in choices)
            {
                if (ch == null) { r.errors.Add(where + ": null choice"); continue; }
                if (EffectApplier.IsFallback(ch)) hasFallback = true;
                string cw = where + " choice '" + ch.id + "'";
                if (!IsId(ch.id)) r.errors.Add(cw + ": id must be lowercase snake_case");
                else if (!ids.Add(ch.id)) r.errors.Add(cw + ": duplicate id");

                bool hasCost = false;
                foreach (var q in ch.requires ?? new List<RequirementDef>())
                {
                    if (q == null) { r.errors.Add(cw + ": null requirement"); continue; }
                    if (!resourceIds.Contains(q.resource)) r.errors.Add(cw + ": requirement on unknown resource '" + q.resource + "'");
                    if (q.min < 1) r.errors.Add(cw + ": requirement min must be >= 1");
                }
                foreach (var ef in ch.effects ?? new List<EffectDef>())
                {
                    if (ef == null) { r.errors.Add(cw + ": null effect"); continue; }
                    if (ef.delaySols != 0) r.errors.Add(cw + ": delaySols is not supported in this build (planned for Stage 5)");
                    switch (ef.type)
                    {
                        case "resource":
                            if (!resourceIds.Contains(ef.target)) r.errors.Add(cw + ": resource effect on unknown resource '" + ef.target + "'");
                            if (ef.amount == 0) r.errors.Add(cw + ": resource effect amount must not be 0");
                            if (ef.amount < 0) hasCost = true;
                            break;
                        case "health":
                            if (!string.IsNullOrEmpty(ef.target)) r.errors.Add(cw + ": health effect takes no target");
                            if (ef.amount == 0) r.errors.Add(cw + ": health effect amount must not be 0");
                            if (ef.amount < 0) hasCost = true;
                            break;
                        case "modifier":
                            r.errors.Add(cw + ": modifier effects are not supported in this build (planned for Stage 5)");
                            break;
                        default:
                            r.errors.Add(cw + ": unknown effect type '" + ef.type + "'");
                            break;
                    }
                }
                // Lint (brief: "a choice with no cost" is a named pitfall). A requirement alone is not a cost.
                if (choices.Count > 1 && !hasCost)
                    r.warnings.Add(cw + ": has no cost (no negative resource or health effect)");
            }
            // Integration policy (decision 0006): every choice list must stay answerable at any stock level.
            if (choices.Count > 0 && !hasFallback)
                r.errors.Add(where + ": needs a fallback choice with no requirements and no resource cost (a health loss is allowed)");
        }

        /// <summary>Variants must offer the same choice IDs, each touching the same effect types and targets.</summary>
        static void CheckSameShape(List<ChoiceDef> reference, List<ChoiceDef> other, string where, ValidationResult r)
        {
            if (Signature(reference) != Signature(other))
                r.errors.Add(where + ": choices or effect types/targets differ from the first variant; variants may change amounts and text only");
        }

        static string Signature(List<ChoiceDef> choices)
        {
            var parts = new List<string>();
            foreach (var ch in choices ?? new List<ChoiceDef>())
            {
                if (ch == null) continue;
                var effs = new List<string>();
                foreach (var ef in ch.effects ?? new List<EffectDef>())
                    if (ef != null) effs.Add(ef.type + ":" + ef.target);
                effs.Sort(string.CompareOrdinal);
                parts.Add(ch.id + "(" + string.Join(",", effs) + ")");
            }
            parts.Sort(string.CompareOrdinal);
            return string.Join("|", parts);
        }

        public static bool IsId(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s[0] < 'a' || s[0] > 'z') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char ch = s[i];
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')) return false;
            }
            return true;
        }
    }
}
