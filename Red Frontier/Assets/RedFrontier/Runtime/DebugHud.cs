using System.Collections.Generic;
using System.Text;
using RedFrontier.Core;
using UnityEngine;

namespace RedFrontier.Runtime
{
    /// <summary>
    /// Developer 1 debug HUD (IMGUI). Not the production UI. Reads only session queries and calls only
    /// session commands: no resource arithmetic, no event logic. Re-reads everything on Changed.
    /// </summary>
    [RequireComponent(typeof(SimulationHost))]
    public sealed class DebugHud : MonoBehaviour
    {
        SimulationHost host;
        GameSession session;
        MissionConfig config;

        // Cached views, replaced on every Changed.
        SimState snap;
        SolReport forecast;
        SolReport last;
        PendingEventView pending;
        CommandResult canAdvance;
        string lastRejection = "";
        string seedText = "";
        Vector2 scroll;

        /// <summary>Number of Changed notifications handled. Play Mode checks use it to detect duplicate subscriptions.</summary>
        public int RefreshCount { get; private set; }

        void OnEnable()
        {
            host = GetComponent<SimulationHost>();
            Bind();
        }

        void OnDisable()
        {
            if (session != null) session.Changed -= OnChanged;
            session = null;
        }

        /// <summary>Subscribes once to the host's current session; also retried from OnGUI in case the host initialised later.</summary>
        void Bind()
        {
            if (session != null || host == null || host.Session == null) return;
            session = host.Session;
            config = session.Config;
            session.Changed += OnChanged;
            Refresh();
        }

        void OnChanged(SessionChange change)
        {
            RefreshCount++;
            Refresh();
        }

        void Refresh()
        {
            snap = session.Snapshot;
            forecast = session.Forecast;
            last = session.LastReport;
            pending = session.PendingEvent;
            canAdvance = session.CanAdvanceSol();
            if (seedText.Length == 0) seedText = (snap.seed + 1).ToString();
        }

        void Run(CommandResult r) { lastRejection = r.ok ? "" : r.reason; }

        void OnGUI()
        {
            Bind();
            GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(Screen.width - 20, 760), Screen.height - 20), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>RED FRONTIER - Developer 1 debug HUD</b>  (placeholder content, illustrative numbers)", Rich());
            if (session == null)
            {
                GUILayout.Label(host.LoadError.Length > 0 ? "Content failed to load: " + host.LoadError : "No simulation session (SimulationHost is disabled or not initialised; see the Console).");
            }
            else
            {
                DrawStatus();
                if (snap.phase == MissionPhase.Ended) DrawEnd();
                if (pending != null) DrawEvent();
                if (snap.phase != MissionPhase.Ended)
                {
                    DrawFacilities();
                    DrawReport("Forecast for sol " + forecast.sol + " at current settings (events not included)", forecast);
                    DrawAdvance();
                }
                if (last != null) DrawReport("Last resolved sol " + last.sol, last);
                DrawRestart();
                DrawHistory();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStatus()
        {
            GUILayout.Label("Sol " + snap.sol + " of " + config.missionLengthSols + "   phase: " + snap.phase + "   seed: " + snap.seed);
            var line = new StringBuilder();
            foreach (var r in snap.resources) line.Append(Title(r.id)).Append(": ").Append(r.amount).Append(" / ").Append(r.capacity).Append("     ");
            line.Append("Crew Health: ").Append(snap.crewHealth).Append(" / 100");
            GUILayout.Label("<b>" + line + "</b>", Rich());
            if (lastRejection.Length > 0) GUILayout.Label("Last command rejected: " + lastRejection);
        }

        void DrawEvent()
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical(GUI.skin.box);
            string kind = pending.major ? "MILESTONE (fixed sol)" : "RANDOM EVENT";
            string variant = pending.variantId.Length > 0 ? "   variant: " + pending.variantId : "";
            GUILayout.Label("<b>" + kind + ": " + pending.title + "</b>   [" + pending.eventId + variant + "]", Rich());
            GUILayout.Label(pending.body);
            GUILayout.Label("Answer the event to continue.");
            foreach (var c in pending.choices)
            {
                GUI.enabled = c.available;
                if (GUILayout.Button(c.label + "  -  " + c.preview))
                    Run(session.ChooseEventOption(pending.instanceId, c.id));
                GUI.enabled = true;
                GUILayout.Label("    effects: " + Effects(c.effects) + Requires(c.requires));
                if (!c.available) GUILayout.Label("    <color=orange>Unavailable: " + Explain(c) + "</color>", Rich());
            }
            GUILayout.EndVertical();
        }

        void DrawFacilities()
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>Facilities</b> (operating level applies when the sol resolves)", Rich());
            foreach (var f in config.facilities)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Title(f.id), GUILayout.Width(140));
                int current = snap.Facility(f.id).level;
                for (int i = 0; i < f.levels.Count; i++)
                {
                    GUI.enabled = session.CanSetFacilityLevel(f.id, i).ok;
                    string label = (i == current ? "[" : "") + f.levels[i].id + (i == current ? "]" : "");
                    if (GUILayout.Button(label, GUILayout.Width(80))) Run(session.SetFacilityLevel(f.id, i));
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            var why = session.CanSetFacilityLevel(config.facilities.Count > 0 ? config.facilities[0].id : "", 0);
            if (!why.ok) GUILayout.Label("    Levels locked: " + why.reason);
        }

        void DrawAdvance()
        {
            GUILayout.Space(6);
            GUI.enabled = canAdvance.ok;
            if (GUILayout.Button("Advance sol " + snap.sol, GUILayout.Height(30))) Run(session.AdvanceSol(snap.sol));
            GUI.enabled = true;
            if (!canAdvance.ok) GUILayout.Label("    Cannot advance: " + canAdvance.reason + (canAdvance.reason == ReasonCodes.DecisionPending ? " (answer the event first)" : ""));
        }

        void DrawReport(string heading, SolReport r)
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>" + heading + "</b>", Rich());
            foreach (var rr in r.resources)
                GUILayout.Label("    " + Title(rr.id) + ": " + rr.start + " -> " + rr.end + "   made " + rr.made + ", facilities used " + rr.usedByFacilities
                    + ", crew used " + rr.usedByCrew + (rr.wasted > 0 ? ", wasted " + rr.wasted : "")
                    + (rr.shortage > 0 ? "   <color=red>SHORTAGE " + rr.shortage + "</color>" : ""), Rich());
            foreach (var fr in r.facilities)
                if (fr.levelRun != fr.requestedLevel)
                    GUILayout.Label("    " + Title(fr.id) + " runs at level " + fr.levelRun + " instead of " + fr.requestedLevel + " (" + fr.reason + ")");
            var causes = new StringBuilder();
            foreach (var hc in r.healthCauses) causes.Append(hc.code).Append(hc.resource.Length > 0 ? " " + hc.resource : "").Append(" ").Append(hc.amount).Append("; ");
            GUILayout.Label("    Crew Health: " + r.healthBefore + " -> " + r.healthAfter + (causes.Length > 0 ? "   (" + causes + ")" : ""));
            if (r.eventRaisedId.Length > 0) GUILayout.Label("    Event raised for next sol: " + r.eventRaisedId);
        }

        void DrawEnd()
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical(GUI.skin.box);
            string color = snap.outcome == MissionOutcome.Success ? "lime" : "red";
            GUILayout.Label("<b><color=" + color + ">MISSION " + snap.outcome.ToString().ToUpperInvariant() + "</color></b>   (" + snap.endReason + ")", Rich());
            GUILayout.Label(EndText());
            GUILayout.EndVertical();
        }

        void DrawRestart()
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart (same seed " + snap.seed + ")", GUILayout.Width(220))) Run(session.Restart());
            GUILayout.Label("New seed:", GUILayout.Width(70));
            seedText = GUILayout.TextField(seedText, GUILayout.Width(90));
            int newSeed;
            bool valid = int.TryParse(seedText, out newSeed);
            GUI.enabled = valid;
            if (GUILayout.Button("Restart with new seed", GUILayout.Width(180))) Run(session.Restart(newSeed));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        void DrawHistory()
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>History</b> (latest last; include the seed in bug reports)", Rich());
            var log = snap.log;
            for (int i = Mathf.Max(0, log.Count - 12); i < log.Count; i++) GUILayout.Label("    " + log[i]);
        }

        // ---- Display text only ----

        string EndText()
        {
            switch (snap.endReason)
            {
                case ReasonCodes.MissionComplete: return "The crew made it through every sol of the mission.";
                case ReasonCodes.HealthZero: return "Crew Health reached 0.";
                case ReasonCodes.ContentErrorNoAvailableChoice:
                    var e = snap.log[snap.log.Count - 1];
                    return "CONTENT ERROR, not a player failure: event '" + e.eventId + "'" + (e.variantId.Length > 0 ? " (variant '" + e.variantId + "')" : "")
                        + " offered no choice the crew could take. Every event needs a fallback choice with no requirement and no resource cost. See the Console.";
                default: return "";
            }
        }

        string Explain(ChoiceView c)
        {
            if (c.unavailableReason == ReasonCodes.RequirementNotMet) return "a requirement is not met" + Requires(c.requires) + Have(c);
            if (c.unavailableReason == ReasonCodes.CannotAfford) return "its resource cost cannot be paid in full" + Have(c);
            return c.unavailableReason;
        }

        string Have(ChoiceView c)
        {
            var seen = new List<string>();
            var sb = new StringBuilder();
            foreach (var q in c.requires) if (!seen.Contains(q.resource)) seen.Add(q.resource);
            foreach (var ef in c.effects) if (ef.type == "resource" && ef.amount < 0 && !seen.Contains(ef.target)) seen.Add(ef.target);
            foreach (var id in seen) sb.Append(sb.Length == 0 ? " (you have " : ", ").Append(snap.Resource(id).amount).Append(" ").Append(id);
            return sb.Length > 0 ? sb.Append(")").ToString() : "";
        }

        static string Effects(List<EffectDef> effects)
        {
            var sb = new StringBuilder();
            foreach (var ef in effects)
                sb.Append(sb.Length > 0 ? ", " : "").Append(ef.type == "health" ? "Crew Health" : ef.target).Append(ef.amount > 0 ? " +" : " ").Append(ef.amount);
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        static string Requires(List<RequirementDef> requires)
        {
            var sb = new StringBuilder();
            foreach (var q in requires) sb.Append(sb.Length == 0 ? "   needs " : ", ").Append(q.resource).Append(" >= ").Append(q.min);
            return sb.ToString();
        }

        static string Title(string id) { return id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id.Substring(1).Replace('_', ' '); }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }
    }
}
