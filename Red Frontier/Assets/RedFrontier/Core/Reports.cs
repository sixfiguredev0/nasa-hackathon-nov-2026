using System;
using System.Collections.Generic;

// Read-only views handed to UI code. Every getter on GameSession returns a fresh copy.
namespace RedFrontier.Core
{
    /// <summary>Result of a command or a Can* query. reason is a stable code from ReasonCodes ("" when ok).</summary>
    public sealed class CommandResult
    {
        public readonly bool ok;
        public readonly string reason;
        private CommandResult(bool ok, string reason) { this.ok = ok; this.reason = reason; }
        public static readonly CommandResult Ok = new CommandResult(true, "");
        public static CommandResult Fail(string reason) { return new CommandResult(false, reason); }
        public override string ToString() { return ok ? "ok" : "rejected:" + reason; }
    }

    public static class ReasonCodes
    {
        public const string MissionEnded = "mission_ended";
        public const string DecisionPending = "decision_pending";
        public const string UnknownFacility = "unknown_facility";
        public const string InvalidLevel = "invalid_level";
        public const string NoPendingEvent = "no_pending_event";
        public const string StaleEvent = "stale_event";
        public const string UnknownChoice = "unknown_choice";
        public const string RequirementNotMet = "requirement_not_met";
        public const string CannotAfford = "cannot_afford";
        public const string StaleSol = "stale_sol";

        // End reasons and per-facility run reasons.
        public const string HealthZero = "health_zero";
        public const string MissionComplete = "mission_complete";
        /// <summary>Content error: a raised event offered no available choice (decision 0006). Outcome is Failure.</summary>
        public const string ContentErrorNoAvailableChoice = "content_error_no_available_choice";
        public const string InsufficientResource = "insufficient_resource";
    }

    public enum ChangeKind { CommandApplied, SolResolved, EventRaised, MissionEnded, Restarted }

    /// <summary>
    /// Fired exactly once per accepted command. For AdvanceSol the kind is the most significant of:
    /// MissionEnded &gt; EventRaised &gt; SolResolved. UI should re-render everything from the session.
    /// </summary>
    public sealed class SessionChange
    {
        public readonly ChangeKind kind;
        public readonly string command;
        public SessionChange(ChangeKind kind, string command) { this.kind = kind; this.command = command; }
    }

    /// <summary>Report of one resolved sol. Forecast uses the same shape with no event or outcome.</summary>
    [Serializable]
    public sealed class SolReport
    {
        /// <summary>The sol that was resolved (or would be, for a forecast).</summary>
        public int sol;
        public List<ResourceReport> resources = new List<ResourceReport>();
        public List<FacilityReport> facilities = new List<FacilityReport>();
        public int healthBefore;
        public int healthAfter;
        public List<HealthCause> healthCauses = new List<HealthCause>();
        /// <summary>Event raised for the next sol, "" if none.</summary>
        public string eventRaisedId = "";
        public string eventRaisedVariantId = "";
        /// <summary>Radiation: units from baseline + events, units blocked by shielding, unshielded units (health damage = net x damagePerUnit).</summary>
        public int radiationExposure;
        public int radiationBlocked;
        public int radiationNet;
        /// <summary>Delayed effects that landed at the start of the next sol (AdvanceSol reports only).</summary>
        public List<string> delayedApplied = new List<string>();
        public MissionOutcome outcome;
        public string endReason = "";

        public SolReport Clone()
        {
            var c = (SolReport)MemberwiseClone();
            c.resources = Copy.List(resources, x => x.Clone());
            c.facilities = Copy.List(facilities, x => x.Clone());
            c.healthCauses = Copy.List(healthCauses, x => x.Clone());
            c.delayedApplied = new List<string>(delayedApplied);
            return c;
        }

        public ResourceReport Resource(string id)
        {
            for (int i = 0; i < resources.Count; i++) if (resources[i].id == id) return resources[i];
            return null;
        }
    }

    [Serializable]
    public sealed class ResourceReport
    {
        public string id = "";
        public int start;
        public int made;
        public int usedByFacilities;
        public int usedByCrew;
        /// <summary>Production lost because stock was at capacity.</summary>
        public int wasted;
        public int end;
        /// <summary>Crew need that could not be met.</summary>
        public int shortage;
        public ResourceReport Clone() { return (ResourceReport)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class FacilityReport
    {
        public string id = "";
        public int requestedLevel;
        public int levelRun;
        /// <summary>"" when levelRun == requestedLevel; otherwise a code such as insufficient_resource.</summary>
        public string reason = "";
        /// <summary>Sum of active modifier percents applied to this facility's output this sol.</summary>
        public int modifierPercent;
        public int shielding;
        public FacilityReport Clone() { return (FacilityReport)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class HealthCause
    {
        /// <summary>"shortage" (resource set) or "recovery".</summary>
        public string code = "";
        public string resource = "";
        /// <summary>Signed change to Crew Health.</summary>
        public int amount;
        public HealthCause Clone() { return (HealthCause)MemberwiseClone(); }
    }

    /// <summary>The pending event with its resolved variant and per-choice availability.</summary>
    public sealed class PendingEventView
    {
        public int instanceId;
        public string eventId = "";
        public string variantId = "";
        public bool major;
        public int sol;
        public string category = "";
        public string severity = "";
        public string title = "";
        public string body = "";
        public string scienceNote = "";
        public string dataBasis = "";
        public string sourceRef = "";
        public List<ChoiceView> choices = new List<ChoiceView>();
    }

    public sealed class ChoiceView
    {
        public string id = "";
        public string label = "";
        public string preview = "";
        /// <summary>Resolved effects (copies). Negative resource amounts are costs paid in full on confirmation.</summary>
        public List<EffectDef> effects = new List<EffectDef>();
        public List<RequirementDef> requires = new List<RequirementDef>();
        public bool available;
        /// <summary>"" when available; otherwise requirement_not_met or cannot_afford.</summary>
        public string unavailableReason = "";
    }
}
