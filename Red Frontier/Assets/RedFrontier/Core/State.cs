using System;
using System.Collections.Generic;

// Runtime state. Owned and written only by GameSession. Plain serialisable data, no logic.
namespace RedFrontier.Core
{
    public enum MissionPhase { Planning = 0, AwaitingDecision = 1, Ended = 2 }

    public enum MissionOutcome { None = 0, Success = 1, Failure = 2 }

    [Serializable]
    public sealed class SimState
    {
        public int version = 1;
        public int seed;
        public uint rngState;
        /// <summary>1-based. The sol being planned, not yet resolved.</summary>
        public int sol;
        public MissionPhase phase;
        public MissionOutcome outcome;
        /// <summary>Stable code, e.g. "health_zero", "mission_complete". "" while running.</summary>
        public string endReason = "";
        public int crewCount;
        /// <summary>0..100.</summary>
        public int crewHealth;
        public List<ResourceState> resources = new List<ResourceState>();
        public List<FacilityState> facilities = new List<FacilityState>();
        /// <summary>instanceId 0 means no pending event.</summary>
        public PendingEvent pending = new PendingEvent();
        /// <summary>IDs of events already raised this run (for once-only rules).</summary>
        public List<string> usedEventIds = new List<string>();
        public int nextEventInstanceId = 1;
        /// <summary>Sol on which the last random event was raised; 0 = none yet.</summary>
        public int lastRandomEventSol;
        /// <summary>Reproduction log: events raised (milestone, variant, sol), choices, shortages, outcome.</summary>
        public List<LogEntry> log = new List<LogEntry>();
        /// <summary>Exposure units added by events, consumed by the next sol resolution (applied once).</summary>
        public int radiationPending;
        /// <summary>Total unshielded exposure units this mission.</summary>
        public int radiationTotal;
        public List<ActiveModifier> modifiers = new List<ActiveModifier>();
        public List<ScheduledEffect> scheduled = new List<ScheduledEffect>();

        public SimState Clone()
        {
            var c = (SimState)MemberwiseClone();
            c.resources = Copy.List(resources, x => x.Clone());
            c.facilities = Copy.List(facilities, x => x.Clone());
            c.pending = pending == null ? new PendingEvent() : pending.Clone();
            c.usedEventIds = new List<string>(usedEventIds);
            c.log = Copy.List(log, x => x.Clone());
            c.modifiers = Copy.List(modifiers, x => x.Clone());
            c.scheduled = Copy.List(scheduled, x => x.Clone());
            return c;
        }

        public ResourceState Resource(string id)
        {
            for (int i = 0; i < resources.Count; i++) if (resources[i].id == id) return resources[i];
            return null;
        }

        public FacilityState Facility(string id)
        {
            for (int i = 0; i < facilities.Count; i++) if (facilities[i].id == id) return facilities[i];
            return null;
        }
    }

    [Serializable]
    public sealed class ResourceState
    {
        public string id = "";
        public int amount;
        public int capacity;
        public ResourceState Clone() { return (ResourceState)MemberwiseClone(); }
    }

    /// <summary>Scales one facility's output on sols firstSol..lastSol. Stacks additively with others on the same facility.</summary>
    [Serializable]
    public sealed class ActiveModifier
    {
        public string facilityId = "";
        public int percent;
        public int firstSol;
        public int lastSol;
        public string sourceEventId = "";
        public string sourceChoiceId = "";
        public ActiveModifier Clone() { return (ActiveModifier)MemberwiseClone(); }
    }

    /// <summary>An effect queued by delaySols; applied once in step 5 of the advance that enters dueSol.</summary>
    [Serializable]
    public sealed class ScheduledEffect
    {
        public int dueSol;
        public EffectDef effect = new EffectDef();
        public string sourceEventId = "";
        public string sourceChoiceId = "";
        public ScheduledEffect Clone() { var c = (ScheduledEffect)MemberwiseClone(); c.effect = effect.Clone(); return c; }
    }

    [Serializable]
    public sealed class FacilityState
    {
        public string id = "";
        /// <summary>Requested level: index into the facility definition's levels.</summary>
        public int level;
        public FacilityState Clone() { return (FacilityState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class PendingEvent
    {
        /// <summary>0 = none.</summary>
        public int instanceId;
        public string eventId = "";
        /// <summary>Variant resolved when the event was raised; "" when the event has no variants. Never rerolled.</summary>
        public string variantId = "";
        public bool major;
        public int raisedOnSol;
        public bool IsNone { get { return instanceId == 0; } }
        public PendingEvent Clone() { return (PendingEvent)MemberwiseClone(); }
    }

    /// <summary>Kinds: see LogKind. Fields not relevant to a kind are left empty or 0.</summary>
    [Serializable]
    public sealed class LogEntry
    {
        public int sol;
        public string kind = "";
        public string eventId = "";
        public string variantId = "";
        public int instanceId;
        /// <summary>"major" or "random" for event_raised.</summary>
        public string eventClass = "";
        public string choiceId = "";
        public string resource = "";
        public int amount;
        public string code = "";
        public LogEntry Clone() { return (LogEntry)MemberwiseClone(); }

        public override string ToString()
        {
            return "sol " + sol + " " + kind
                + (eventId.Length > 0 ? " event=" + eventId : "")
                + (eventClass.Length > 0 ? " class=" + eventClass : "")
                + (variantId.Length > 0 ? " variant=" + variantId : "")
                + (instanceId != 0 ? " instance=" + instanceId : "")
                + (choiceId.Length > 0 ? " choice=" + choiceId : "")
                + (resource.Length > 0 ? " resource=" + resource + " amount=" + amount : "")
                + (code.Length > 0 ? " code=" + code : "");
        }
    }

    public static class LogKind
    {
        public const string EventRaised = "event_raised";
        public const string ChoiceMade = "choice_made";
        public const string Shortage = "shortage";
        public const string MissionEnded = "mission_ended";
        public const string DelayedEffect = "delayed_effect";
        public const string Radiation = "radiation";
    }
}
