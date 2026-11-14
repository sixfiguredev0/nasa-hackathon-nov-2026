using System;
using System.Collections.Generic;

namespace RedFrontier.Core
{
    /// <summary>
    /// The single authoritative simulation. Owns the only mutable state. Every command validates
    /// first, works on a copy, and commits only on success, so a rejected command (or an exception)
    /// leaves state and RNG untouched. Getters return fresh copies.
    /// </summary>
    public sealed class GameSession
    {
        readonly MissionConfig config;
        readonly List<EventDef> events; // sorted by id, ordinal
        SimState state;
        SolReport lastReport;

        /// <summary>Exactly once per accepted command; never on rejection.</summary>
        public event Action<SessionChange> Changed;

        public GameSession(MissionConfig config, IEnumerable<EventDef> events, int seed) : this(config, events, seed, true) { }

        /// <summary>validate = false exists only so tests can reach the runtime no-choice guard with content validation would reject.</summary>
        internal GameSession(MissionConfig config, IEnumerable<EventDef> events, int seed, bool validate)
        {
            var list = new List<EventDef>();
            if (events != null) foreach (var e in events) list.Add(e);
            var result = validate ? ContentValidator.Validate(config, list) : new ValidationResult();
            if (!result.Ok) throw new ContentValidationException(result);
            Warnings = result.warnings.AsReadOnly();

            this.config = config.Clone();
            this.events = new List<EventDef>(list.Count);
            foreach (var e in list) this.events.Add(e.Clone());
            this.events.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            state = NewState(seed);
        }

        /// <summary>Content lint warnings found at construction (for example cost-free choices).</summary>
        public IReadOnlyList<string> Warnings { get; private set; }

        // ---- Read ----

        public MissionConfig Config { get { return config.Clone(); } }
        public SimState Snapshot { get { return state.Clone(); } }
        public int Seed { get { return state.seed; } }

        /// <summary>The coming sol at current settings, excluding events. Runs on a copy; the live RNG is not touched.</summary>
        public SolReport Forecast
        {
            get
            {
                if (state.phase == MissionPhase.Ended) return null;
                return SolResolver.RunFlows(state.Clone(), config);
            }
        }

        /// <summary>null before the first sol resolves (and after Restart).</summary>
        public SolReport LastReport { get { return lastReport == null ? null : lastReport.Clone(); } }

        /// <summary>Events raised, choices made, shortages and outcome, in order.</summary>
        public IReadOnlyList<LogEntry> History { get { return Copy.List(state.log, x => x.Clone()).AsReadOnly(); } }

        /// <summary>null when no event is pending. Reading it never rerolls the variant.</summary>
        public PendingEventView PendingEvent
        {
            get
            {
                if (state.pending.IsNone) return null;
                var p = state.pending;
                var def = FindEvent(p.eventId);
                var variant = EventDirector.Variant(def, p.variantId);
                var view = new PendingEventView
                {
                    instanceId = p.instanceId,
                    eventId = def.id,
                    variantId = p.variantId,
                    major = p.major,
                    sol = p.raisedOnSol,
                    category = def.category,
                    severity = def.severity,
                    title = variant != null && variant.title.Length > 0 ? variant.title : def.title,
                    body = variant != null && variant.body.Length > 0 ? variant.body : def.body,
                    scienceNote = def.scienceNote,
                    dataBasis = def.dataBasis,
                    sourceRef = def.sourceRef,
                };
                foreach (var ch in EventDirector.ResolvedChoices(def, p.variantId))
                {
                    string why = EffectApplier.CheckChoice(state, ch);
                    view.choices.Add(new ChoiceView
                    {
                        id = ch.id,
                        label = ch.label,
                        preview = ch.preview,
                        effects = Copy.List(ch.effects, x => x.Clone()),
                        requires = Copy.List(ch.requires, x => x.Clone()),
                        available = why.Length == 0,
                        unavailableReason = why,
                    });
                }
                return view;
            }
        }

        // ---- Queries (same checks as the commands, no side effects) ----

        public CommandResult CanSetFacilityLevel(string facilityId, int level)
        {
            if (state.phase == MissionPhase.Ended) return CommandResult.Fail(ReasonCodes.MissionEnded);
            if (state.phase == MissionPhase.AwaitingDecision) return CommandResult.Fail(ReasonCodes.DecisionPending);
            var def = FindFacility(facilityId);
            if (def == null) return CommandResult.Fail(ReasonCodes.UnknownFacility);
            if (level < 0 || level >= def.levels.Count) return CommandResult.Fail(ReasonCodes.InvalidLevel);
            return CommandResult.Ok;
        }

        public CommandResult CanAdvanceSol()
        {
            if (state.phase == MissionPhase.Ended) return CommandResult.Fail(ReasonCodes.MissionEnded);
            if (state.phase == MissionPhase.AwaitingDecision) return CommandResult.Fail(ReasonCodes.DecisionPending);
            return CommandResult.Ok;
        }

        public CommandResult CanChooseEventOption(int eventInstanceId, string choiceId)
        {
            if (state.phase == MissionPhase.Ended) return CommandResult.Fail(ReasonCodes.MissionEnded);
            if (state.pending.IsNone) return CommandResult.Fail(ReasonCodes.NoPendingEvent);
            if (eventInstanceId != state.pending.instanceId) return CommandResult.Fail(ReasonCodes.StaleEvent);
            var choice = FindChoice(choiceId);
            if (choice == null) return CommandResult.Fail(ReasonCodes.UnknownChoice);
            string why = EffectApplier.CheckChoice(state, choice);
            return why.Length == 0 ? CommandResult.Ok : CommandResult.Fail(why);
        }

        // ---- Commands ----

        /// <summary>Recorded at once; changes flows only when the sol resolves.</summary>
        public CommandResult SetFacilityLevel(string facilityId, int level)
        {
            var check = CanSetFacilityLevel(facilityId, level);
            if (!check.ok) return check;
            var next = state.Clone();
            next.Facility(facilityId).level = level;
            Commit(next, new SessionChange(ChangeKind.CommandApplied, "SetFacilityLevel"));
            return CommandResult.Ok;
        }

        /// <summary>Applies the chosen option's immediate effects once and clears the pending event.</summary>
        public CommandResult ChooseEventOption(int eventInstanceId, string choiceId)
        {
            var check = CanChooseEventOption(eventInstanceId, choiceId);
            if (!check.ok) return check;

            var next = state.Clone();
            var p = next.pending;
            var choice = FindChoice(choiceId);
            EffectApplier.ApplyChoice(next, choice.effects, p.eventId, choiceId);
            next.log.Add(new LogEntry
            {
                sol = next.sol, kind = LogKind.ChoiceMade, eventId = p.eventId, variantId = p.variantId,
                instanceId = p.instanceId, eventClass = p.major ? "major" : "random", choiceId = choiceId,
            });
            next.pending = new PendingEvent();
            next.phase = MissionPhase.Planning;

            var kind = ChangeKind.CommandApplied;
            // Departure from ARCHITECTURE section 4 (recorded in 0003): a choice that takes health to 0
            // ends the mission at once. Otherwise step 3 recovery could revive a crew at 0.
            if (next.crewHealth <= 0)
            {
                End(next, MissionOutcome.Failure, ReasonCodes.HealthZero);
                kind = ChangeKind.MissionEnded;
            }
            Commit(next, new SessionChange(kind, "ChooseEventOption"));
            return CommandResult.Ok;
        }

        /// <summary>Resolves the current sol once. expectedSol must equal the current sol.</summary>
        public CommandResult AdvanceSol(int expectedSol)
        {
            var check = CanAdvanceSol();
            if (!check.ok) return check;
            if (expectedSol != state.sol) return CommandResult.Fail(ReasonCodes.StaleSol);

            var next = state.Clone();
            // Steps 1-3.
            var report = SolResolver.RunFlows(next, config);
            foreach (var rr in report.resources)
                if (rr.shortage > 0)
                    next.log.Add(new LogEntry { sol = next.sol, kind = LogKind.Shortage, resource = rr.id, amount = rr.shortage });

            // Step 4: next sol; modifiers that ended before it expire.
            next.sol++;
            next.modifiers.RemoveAll(m => m.lastSol < next.sol);

            // Step 5: delayed effects due on the new sol land once, in queue order, before the failure check.
            foreach (var d in EffectApplier.ApplyDue(next))
            {
                report.delayedApplied.Add(d.sourceEventId + "/" + d.sourceChoiceId + ": " + d.effect.type + " " + d.effect.target + " " + (d.effect.type == "modifier" ? d.effect.percent + "% x" + d.effect.durationSols : d.effect.amount.ToString()));
                next.log.Add(new LogEntry { sol = next.sol, kind = LogKind.DelayedEffect, eventId = d.sourceEventId, choiceId = d.sourceChoiceId, resource = d.effect.target, amount = d.effect.amount, code = d.effect.type });
            }

            // Step 6: failure first.
            var kind = ChangeKind.SolResolved;
            if (next.crewHealth <= 0) End(next, MissionOutcome.Failure, ReasonCodes.HealthZero);
            else if (next.sol > config.missionLengthSols) End(next, MissionOutcome.Success, ReasonCodes.MissionComplete);

            if (next.phase == MissionPhase.Ended)
            {
                kind = ChangeKind.MissionEnded;
            }
            else
            {
                // Step 7.
                EventDirector.RaiseFor(next, config, events);
                if (EndIfUnanswerable(next)) kind = ChangeKind.MissionEnded;
                else if (!next.pending.IsNone)
                {
                    kind = ChangeKind.EventRaised;
                    report.eventRaisedId = next.pending.eventId;
                    report.eventRaisedVariantId = next.pending.variantId;
                }
            }
            report.outcome = next.outcome;
            report.endReason = next.endReason;

            // Step 8.
            lastReport = report;
            Commit(next, new SessionChange(kind, "AdvanceSol"));
            return CommandResult.Ok;
        }

        /// <summary>Rebuilds the mission from config with the given seed. Always accepted.</summary>
        public CommandResult Restart(int seed)
        {
            lastReport = null;
            Commit(NewState(seed), new SessionChange(ChangeKind.Restarted, "Restart"));
            return CommandResult.Ok;
        }

        /// <summary>Restart with the current seed: replays the same mission.</summary>
        public CommandResult Restart() { return Restart(state.seed); }

        // ---- Internals ----

        SimState NewState(int seed)
        {
            var s = new SimState
            {
                seed = seed,
                rngState = Rng.SeedState(seed),
                sol = 1,
                phase = MissionPhase.Planning,
                outcome = MissionOutcome.None,
                crewCount = config.crewCount,
                crewHealth = config.startHealth,
            };
            foreach (var r in config.resources)
                s.resources.Add(new ResourceState { id = r.id, amount = r.start, capacity = r.capacity });
            foreach (var f in config.facilities)
                s.facilities.Add(new FacilityState { id = f.id, level = f.startLevel });
            // Step 7 for sol 1, so a mission can open with a milestone.
            EventDirector.RaiseFor(s, config, events);
            EndIfUnanswerable(s);
            return s;
        }

        /// <summary>
        /// Defensive guard (decision 0006). Validation requires a fallback choice, so this should never
        /// fire; if it does, end the mission with an explicit content-error reason rather than trap the
        /// player, skip the event or make a paid choice free. Stock cannot change while a decision is
        /// pending, so checking once when the event is raised is enough. Uses no RNG.
        /// </summary>
        bool EndIfUnanswerable(SimState s)
        {
            if (s.pending.IsNone) return false;
            var def = FindEvent(s.pending.eventId);
            foreach (var ch in EventDirector.ResolvedChoices(def, s.pending.variantId))
                if (EffectApplier.CheckChoice(s, ch).Length == 0) return false;
            string eventId = s.pending.eventId, variantId = s.pending.variantId;
            End(s, MissionOutcome.Failure, ReasonCodes.ContentErrorNoAvailableChoice);
            var entry = s.log[s.log.Count - 1];
            entry.eventId = eventId;
            entry.variantId = variantId;
            return true;
        }

        void Commit(SimState next, SessionChange change)
        {
            state = next;
            var handler = Changed;
            if (handler != null) handler(change);
        }

        static void End(SimState s, MissionOutcome outcome, string reason)
        {
            s.phase = MissionPhase.Ended;
            s.outcome = outcome;
            s.endReason = reason;
            s.pending = new PendingEvent();
            s.log.Add(new LogEntry { sol = s.sol, kind = LogKind.MissionEnded, code = reason });
        }

        EventDef FindEvent(string id)
        {
            foreach (var e in events) if (e.id == id) return e;
            return null;
        }

        FacilityDef FindFacility(string id)
        {
            foreach (var f in config.facilities) if (f.id == id) return f;
            return null;
        }

        ChoiceDef FindChoice(string choiceId)
        {
            if (state.pending.IsNone) return null;
            var def = FindEvent(state.pending.eventId);
            foreach (var ch in EventDirector.ResolvedChoices(def, state.pending.variantId))
                if (ch.id == choiceId) return ch;
            return null;
        }
    }
}
