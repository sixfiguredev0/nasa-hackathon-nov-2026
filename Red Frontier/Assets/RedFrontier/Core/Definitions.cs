using System;
using System.Collections.Generic;

// Content definitions. Loaded from JSON (JsonUtility in Unity), validated by ContentValidator,
// then copied by GameSession and never mutated. Public fields, no nullables, no dictionaries,
// no polymorphism, so JsonUtility can read them. 0 or "" means "not set".
// All numbers in content are placeholder game values, not scientific data.
namespace RedFrontier.Core
{
    [Serializable]
    public sealed class MissionConfig
    {
        public int schemaVersion;
        public int missionLengthSols;
        public int crewCount;
        public int startHealth;
        public int healthRecoveryPerSol;
        public List<ResourceDef> resources = new List<ResourceDef>();
        public List<CrewNeedDef> crewNeeds = new List<CrewNeedDef>();
        /// <summary>Order is the power priority order: facilities run in this order each sol.</summary>
        public List<FacilityDef> facilities = new List<FacilityDef>();
        /// <summary>Chance (0..100) of a random event on a sol with no major event.</summary>
        public int eventChancePercent;
        /// <summary>Minimum difference in sol numbers between two random events. 0 or 1 allows consecutive sols.</summary>
        public int randomEventMinGapSols;

        public MissionConfig Clone()
        {
            var c = (MissionConfig)MemberwiseClone();
            c.resources = Copy.List(resources, r => r.Clone());
            c.crewNeeds = Copy.List(crewNeeds, n => n.Clone());
            c.facilities = Copy.List(facilities, f => f.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class ResourceDef
    {
        public string id = "";
        public int start;
        public int capacity;
        public ResourceDef Clone() { var c = (ResourceDef)MemberwiseClone(); c.id = Copy.Str(id); return c; }
    }

    [Serializable]
    public sealed class CrewNeedDef
    {
        public string resource = "";
        public int perCrewPerSol;
        public int healthPenaltyPerSol;
        public CrewNeedDef Clone() { var c = (CrewNeedDef)MemberwiseClone(); c.resource = Copy.Str(resource); return c; }
    }

    [Serializable]
    public sealed class FacilityDef
    {
        public string id = "";
        public int startLevel;
        /// <summary>Index 0 is the lowest level and must use nothing (it is the fallback when power runs out).</summary>
        public List<FacilityLevelDef> levels = new List<FacilityLevelDef>();

        public FacilityDef Clone()
        {
            var c = (FacilityDef)MemberwiseClone();
            c.id = Copy.Str(id);
            c.levels = Copy.List(levels, l => l.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class FacilityLevelDef
    {
        public string id = "";
        public List<FlowDef> uses = new List<FlowDef>();
        public List<FlowDef> makes = new List<FlowDef>();

        public FacilityLevelDef Clone()
        {
            var c = (FacilityLevelDef)MemberwiseClone();
            c.id = Copy.Str(id);
            c.uses = Copy.List(uses, f => f.Clone());
            c.makes = Copy.List(makes, f => f.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class FlowDef
    {
        public string resource = "";
        public int amount;
        public FlowDef Clone() { var c = (FlowDef)MemberwiseClone(); c.resource = Copy.Str(resource); return c; }
    }

    /// <summary>
    /// One event, one file. trigger.sol &gt; 0 makes it a major milestone on that sol;
    /// trigger.sol == 0 puts it in the random pool. Major events may carry authored variants.
    /// </summary>
    [Serializable]
    public sealed class EventDef
    {
        public int schemaVersion;
        public string id = "";
        public string category = "";
        public string severity = "";
        public EventTrigger trigger = new EventTrigger();
        public string title = "";
        public string body = "";
        public string scienceNote = "";
        public string dataBasis = "";
        public string sourceRef = "";
        /// <summary>Used when the event has no variants.</summary>
        public List<ChoiceDef> choices = new List<ChoiceDef>();
        /// <summary>Major events only. One is picked with the seeded RNG when the event is raised.</summary>
        public List<EventVariantDef> variants = new List<EventVariantDef>();

        public bool IsMajor { get { return trigger != null && trigger.sol > 0; } }

        public EventDef Clone()
        {
            var c = (EventDef)MemberwiseClone();
            c.id = Copy.Str(id); c.category = Copy.Str(category); c.severity = Copy.Str(severity);
            c.title = Copy.Str(title); c.body = Copy.Str(body); c.scienceNote = Copy.Str(scienceNote);
            c.dataBasis = Copy.Str(dataBasis); c.sourceRef = Copy.Str(sourceRef);
            c.trigger = trigger == null ? new EventTrigger() : trigger.Clone();
            c.choices = Copy.List(choices, x => x.Clone());
            c.variants = Copy.List(variants, x => x.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class EventTrigger
    {
        /// <summary>Major milestone: raised at the start of exactly this sol. 0 = random pool.</summary>
        public int sol;
        /// <summary>Random pool: first eligible sol (0 = no lower bound).</summary>
        public int minSol;
        /// <summary>Random pool: last eligible sol (0 = no upper bound).</summary>
        public int maxSol;
        /// <summary>Random pool: relative weight (0 = default 1).</summary>
        public int weight;
        /// <summary>Random pool: may occur at most once per run.</summary>
        public bool once;
        public EventTrigger Clone() { return (EventTrigger)MemberwiseClone(); }
    }

    /// <summary>
    /// A bounded, authored variation of a major event. It must offer the same choice IDs as the
    /// event's other variants, and each choice must touch the same effect types and targets;
    /// only amounts, requirements and text may differ.
    /// </summary>
    [Serializable]
    public sealed class EventVariantDef
    {
        public string id = "";
        /// <summary>Relative weight (0 = default 1).</summary>
        public int weight;
        /// <summary>Optional override of the event title ("" = use the event's).</summary>
        public string title = "";
        /// <summary>Optional override of the event body ("" = use the event's).</summary>
        public string body = "";
        public List<ChoiceDef> choices = new List<ChoiceDef>();

        public EventVariantDef Clone()
        {
            var c = (EventVariantDef)MemberwiseClone();
            c.id = Copy.Str(id); c.title = Copy.Str(title); c.body = Copy.Str(body);
            c.choices = Copy.List(choices, x => x.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class ChoiceDef
    {
        public string id = "";
        public string label = "";
        /// <summary>One-sentence cost statement shown before confirming. Must match the effects.</summary>
        public string preview = "";
        /// <summary>Minimum stock needed to pick this choice. A requirement is a check, not a cost.</summary>
        public List<RequirementDef> requires = new List<RequirementDef>();
        public List<EffectDef> effects = new List<EffectDef>();

        public ChoiceDef Clone()
        {
            var c = (ChoiceDef)MemberwiseClone();
            c.id = Copy.Str(id); c.label = Copy.Str(label); c.preview = Copy.Str(preview);
            c.requires = Copy.List(requires, x => x.Clone());
            c.effects = Copy.List(effects, x => x.Clone());
            return c;
        }
    }

    [Serializable]
    public sealed class RequirementDef
    {
        public string resource = "";
        public int min;
        public RequirementDef Clone() { var c = (RequirementDef)MemberwiseClone(); c.resource = Copy.Str(resource); return c; }
    }

    /// <summary>
    /// Effect types in this build: "resource" (target = resource id, signed amount; a negative amount
    /// is a cost and must be affordable in full) and "health" (signed amount). "modifier" and
    /// delaySols are part of the proposed schema but rejected by the validator until Stage 5.
    /// </summary>
    [Serializable]
    public sealed class EffectDef
    {
        public string type = "";
        public string target = "";
        public int amount;
        public int percent;
        public int durationSols;
        public int delaySols;
        public EffectDef Clone() { var c = (EffectDef)MemberwiseClone(); c.type = Copy.Str(type); c.target = Copy.Str(target); return c; }
    }

    internal static class Copy
    {
        public static string Str(string s) { return s ?? ""; }

        public static List<T> List<T>(List<T> src, Func<T, T> clone)
        {
            var dst = new List<T>(src == null ? 0 : src.Count);
            if (src == null) return dst;
            for (int i = 0; i < src.Count; i++) dst.Add(src[i] == null ? default(T) : clone(src[i]));
            return dst;
        }
    }
}
