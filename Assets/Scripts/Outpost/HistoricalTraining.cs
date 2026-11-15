using System;
using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>One offline historical exercise. Durations and consequences are game rules, not NASA measurements.</summary>
    internal sealed class HistoricalTraining : MonoBehaviour
    {
        internal const string SelectedId = "2024-05-10T13:35:00-SEP-001";
        // Hard mode: shelter costs 25 s of lost building time, working costs a 15-point fitness dip, and not choosing in time means working.
        internal const float ExerciseSeconds = MissionPlan.SepShelterSeconds, FitnessDip = MissionPlan.SepFitnessDip, ChoiceSeconds = MissionPlan.SepChoiceSeconds;
        internal enum Phase { Waiting, Choice, Sheltering, Working, Resolved, Unavailable }

        [Serializable] internal sealed class Instrument { public string displayName; }
        [Serializable] internal sealed class Record
        {
            public string sepID, eventTime, link;
            public Instrument[] instruments;
        }
        [Serializable] sealed class Archive
        {
            public int schemaVersion;
            public string source, dataMode;
            public Record[] records;
        }

        internal Phase State { get; private set; }
        internal Record Observation { get; private set; }
        internal string ObservationDate { get; private set; }
        internal float Remaining { get; private set; }
        internal bool ChoseShelter { get; private set; }
        /// <summary>The player did not decide in time, so the crew kept working.</summary>
        internal bool Hesitated { get; private set; }
        internal string DataError { get; private set; }
        Colony colony;
        Crew crew;
        MissionObjective objective;
        float choiceAge, removedFitness;
        /// <summary>Off only in the integration tests, so scripted steps can sit on the choice screen.</summary>
        internal bool autoDefault = true;
        /// <summary>True once the mission director has raised the warning (it never repeats in one mission).</summary>
        internal bool Triggered => State != Phase.Waiting;
        /// <summary>Seconds left to decide before the crew carries on working by default.</summary>
        internal float ChoiceRemaining => Mathf.Max(0f, ChoiceSeconds - choiceAge);

        /// <summary>Called by <see cref="MissionDirector"/> when the scheduled solar particle event arrives.</summary>
        internal void Trigger()
        {
            if (State != Phase.Waiting) return;
            choiceAge = 0f;
            State = Observation != null ? Phase.Choice : Phase.Unavailable;
        }

        internal static bool TryRead(string json, out Record record, out string date)
        {
            record = null;
            date = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var archive = JsonUtility.FromJson<Archive>(json);
                if (archive == null || archive.schemaVersion != 1 || archive.dataMode != "archived" ||
                    string.IsNullOrWhiteSpace(archive.source) || archive.records == null) return false;
                foreach (var candidate in archive.records)
                {
                    if (candidate == null || candidate.sepID != SelectedId) continue;
                    if (!DateTimeOffset.TryParse(candidate.eventTime, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var observed) ||
                        candidate.instruments == null || candidate.instruments.Length == 0 ||
                        candidate.instruments[0] == null || string.IsNullOrWhiteSpace(candidate.instruments[0].displayName) ||
                        !Uri.TryCreate(candidate.link, UriKind.Absolute, out var link) ||
                        link.Scheme != "https" || link.Host != "ccmc.gsfc.nasa.gov") return false;
                    record = candidate;
                    date = observed.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch (ArgumentException) { }
            return false;
        }

        void Start()
        {
            colony = GetComponent<Colony>();
            crew = GetComponent<Crew>();
            objective = GetComponent<MissionObjective>();
            var fixture = Resources.Load<TextAsset>("Nasa/donki-sep-curated");
            if (TryRead(fixture != null ? fixture.text : null, out var record, out var date))
            {
                Observation = record;
                ObservationDate = date;
            }
            else DataError = "NASA archive missing or invalid. The historical exercise is unavailable; you can keep building.";
        }

        void Update() => Tick(Time.deltaTime);

        internal void Tick(float dt)
        {
            if (dt <= 0f || colony == null || !colony.Ready || objective == null) return;
            if (State == Phase.Waiting) return; // the director decides when the event strikes
            if (State == Phase.Choice)
            {
                choiceAge += dt;
                if (autoDefault && choiceAge >= ChoiceSeconds) { Hesitated = true; Apply(false); } // no decision is a decision: the crew kept working
                return;
            }
            if (State != Phase.Sheltering && State != Phase.Working) return;
            // The shelter exercise clock starts only once the crew have actually entered the lander.
            if (State == Phase.Sheltering && !crew.Sheltered) return;
            Remaining = Mathf.Max(0f, Remaining - dt);
            if (Remaining > 0f) return;
            ClearConsequences();
            State = Phase.Resolved;
        }

        internal void Choose(bool shelter)
        {
            if (State != Phase.Choice || Time.timeScale <= 0f || MissionPauseMenu.BlocksGameplayInput) return;
            Apply(shelter);
        }

        void Apply(bool shelter)
        {
            ChoseShelter = shelter;
            Remaining = ExerciseSeconds;
            if (shelter)
            {
                crew.SetSheltering(true);
                State = Phase.Sheltering;
            }
            else
            {
                removedFitness = Mathf.Min(FitnessDip, colony.economy.fitness);
                colony.economy.fitness -= removedFitness;
                State = Phase.Working;
            }
        }

        void ClearConsequences()
        {
            if (crew != null) crew.SetSheltering(false);
            if (colony != null) colony.economy.fitness = Mathf.Clamp01(colony.economy.fitness + removedFitness);
            removedFitness = 0f;
        }

        void OnDisable()
        {
            ClearConsequences();
            Remaining = choiceAge = 0f;
            State = Phase.Waiting;
        }

        internal string SourceDetails => Observation == null ? DataError :
            "ARCHIVED NASA CCMC DONKI / M2M_CATALOG\n" +
            "Moon to Mars Space Weather Analysis Office\n" +
            Observation.sepID + "\nUTC observation: " + Observation.eventTime + "\n" +
            "Instrument: " + Observation.instruments[0].displayName + "\n" + Observation.link +
            "\nNASA supplies the observation, not a Mars dose or forecast.\n" +
            "Exercise timing, fitness and shelter protection are game rules.\n" +
            "This record is not a measurement at our fictional base.";
    }
}
