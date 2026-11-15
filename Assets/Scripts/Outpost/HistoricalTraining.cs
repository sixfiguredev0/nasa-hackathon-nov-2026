using System;
using System.Globalization;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>One offline historical exercise. Durations and consequences are game rules, not NASA measurements.</summary>
    internal sealed class HistoricalTraining : MonoBehaviour
    {
        internal const string SelectedId = "2024-05-10T13:35:00-SEP-001";
        internal const float ExerciseSeconds = 20f, FitnessDip = .08f;
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
        internal string DataError { get; private set; }
        Colony colony;
        Crew crew;
        MissionObjective objective;
        float readyAge, removedFitness;

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
            if (State == Phase.Waiting)
            {
                if (objective.Stage != MissionObjective.Step.Training) return;
                readyAge += dt;
                if (readyAge >= 4f) State = Observation != null ? Phase.Choice : Phase.Unavailable;
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
            Remaining = readyAge = 0f;
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
