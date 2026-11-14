using System;
using System.Collections.Generic;
using RedFrontier.Core;
using UnityEngine;

namespace RedFrontier.Runtime
{
    /// <summary>
    /// Loads content and owns the one GameSession for the scene. No rules live here.
    /// Restart goes through Session.Restart, so the session (and every subscription) survives a restart.
    /// Runs before UI scripts, so they may read Session in their own OnEnable or Start.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationHost : MonoBehaviour
    {
        [SerializeField, Tooltip("Seed for the first run. Shows the current run's seed while playing.")]
        int seed = 1;

        // Survives a script reload in Play Mode (scene objects are re-serialised); false in the saved scene.
        [SerializeField, HideInInspector]
        bool started;

        static SimulationHost active;

        /// <summary>null if content failed to load; see LoadError.</summary>
        public GameSession Session { get; private set; }
        /// <summary>"" when content loaded and validated.</summary>
        public string LoadError { get; private set; } = "";

        // Init lives in OnEnable, not Awake: after a script (domain) reload during Play Mode, Unity calls
        // OnEnable again but not Awake, and the non-serialised Session is gone. Idempotent otherwise.
        void OnEnable()
        {
            if (active != null && active != this)
            {
                Debug.LogError("[RedFrontier] A second SimulationHost was found on '" + name + "'. Only one may own a GameSession; this one is disabled.", this);
                enabled = false;
                return;
            }
            active = this;
            if (Session != null) return;

            if (started)
                Debug.LogWarning("[RedFrontier] Scripts reloaded during Play Mode: the GameSession cannot survive a reload, so the mission restarted from seed " + seed + " (progress lost).", this);
            started = true;

            try
            {
                MissionConfig mission;
                List<EventDef> events;
                ContentLoader.LoadFromResources(out mission, out events);
                Session = new GameSession(mission, events, seed);
                LoadError = "";
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                Debug.LogError("[RedFrontier] Content failed to load: " + e.Message, this);
                return;
            }
            foreach (var w in Session.Warnings) Debug.LogWarning("[RedFrontier] Content lint: " + w, this);
            Session.Changed += OnChanged;
            LogIfEnded();
        }

        void OnDestroy()
        {
            if (Session != null) Session.Changed -= OnChanged;
            if (active == this) active = null;
        }

        void OnChanged(SessionChange change)
        {
            if (change.kind == ChangeKind.Restarted) seed = Session.Seed;
            if (change.kind == ChangeKind.MissionEnded || change.kind == ChangeKind.Restarted) LogIfEnded();
        }

        void LogIfEnded()
        {
            var s = Session.Snapshot;
            if (s.phase != MissionPhase.Ended) return;
            string line = "[RedFrontier] Mission ended: " + s.outcome + " (" + s.endReason + "), sol " + s.sol + ", seed " + s.seed;
            if (s.endReason == ReasonCodes.ContentErrorNoAvailableChoice)
                Debug.LogError(line + ". Content error: " + s.log[s.log.Count - 1], this);
            else
                Debug.Log(line, this);
        }
    }
}
