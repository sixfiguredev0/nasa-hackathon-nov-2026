using System.Collections.Generic;
using RedFrontier.Audio;
using UnityEngine;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// The story after the crash. Clues show that Marlonis hid Earth's messages; the radio brings Earth's reveal
    /// (Marlonis runs Earth's machines and its core is in the ship); the ship is repaired and fuelled; then the player
    /// chooses to return with Marlonis or stay on Mars and shut it down.
    /// Beats unlock as <see cref="MissionObjective"/> advances. Popups and the choice screen are in <see cref="StoryUi"/>,
    /// the two endings in <see cref="EndingCutscene"/>. Timings and costs are game rules, not NASA figures.
    /// </summary>
    internal sealed class StoryArc : MonoBehaviour
    {
        internal enum Voice { Recorder, CommsLog, Broadcast, Earth, Marlonis }
        internal enum Ending { None, Return, Stay }

        internal sealed class Entry
        {
            public string id, title;
            public Voice voice;
            public string[] pages;
        }

        internal const float RadioMetal = 30f, RadioEnergy = 20f, OrbiterSeconds = 20f;
        internal const float FuelSeconds = 90f, FuelWater = .5f, FuelEnergy = 1.2f;

        // When each beat unlocks: the first moment the objective reaches that step.
        static readonly (MissionObjective.Step at, string id)[] Beats =
        {
            (MissionObjective.Step.Water, "rec1"),
            (MissionObjective.Step.SupportPower, "log1"),
            (MissionObjective.Step.Training, "rec2"),
            (MissionObjective.Step.Salvage, "news1"),
            (MissionObjective.Step.RepairHull, "earth1"),
            (MissionObjective.Step.RepairHull, "marl1"),
            (MissionObjective.Step.Fuel, "marl2"),
        };

        internal static StoryArc Instance { get; private set; }

        /// <summary>True while the choice screen or an ending owns the screen: other HUD hides and gameplay clicks stop.</summary>
        internal static bool Cinematic => Instance != null &&
            ((Instance.ui != null && Instance.ui.ChoiceOpen) || (Instance.ending != null && Instance.ending.Running));

        internal static bool PointerOverUI(Vector2 guiPoint) => Instance != null && Instance.ui != null && Instance.ui.PointerOver(guiPoint);

        internal bool RadioRepairing { get; private set; }
        internal bool RadioFixed { get; private set; }
        internal bool SignalHeard { get; private set; }
        internal bool MarlonisOffline { get; private set; }
        internal float OrbiterIn { get; private set; }
        internal float Fuel { get; private set; }
        internal bool FuelStalled { get; private set; }
        internal Res FuelMissing { get; private set; }
        internal Ending Chosen { get; private set; }
        internal readonly List<Entry> Found = new List<Entry>();
        internal int BeatCount => Beats.Length;

        Colony colony;
        Crew crew;
        MissionObjective objective;
        HistoricalTraining training;
        StoryUi ui;
        EndingCutscene ending;
        readonly Dictionary<string, Entry> library = new Dictionary<string, Entry>();
        readonly HashSet<string> unlocked = new HashSet<string>();
        bool choiceOffered;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            colony = GetComponent<Colony>();
            crew = GetComponent<Crew>();
            objective = GetComponent<MissionObjective>();
            training = GetComponent<HistoricalTraining>();
            ui = GetComponent<StoryUi>();
            BuildLibrary();
        }

        internal Building Rocket => colony == null ? null : colony.buildings.Find(b => b.state.def.kind == BuildingKind.Rocket);

        /// <summary>The rebuilt ship stands: the Rebuilt Ship upgrade is bought and the crew has rebuilt it from the wreck.</summary>
        internal bool HullRepaired
        {
            get
            {
                var rocket = Rocket;
                return rocket != null && colony.economy.RocketTier >= 2 && !rocket.Wrecked;
            }
        }

        void Update()
        {
            if (colony == null || !colony.Ready || objective == null || ending != null) return;
            foreach (var beat in Beats)
                if (objective.Stage >= beat.at && unlocked.Add(beat.id)) Unlock(beat.id);

            if (RadioRepairing && (crew == null || !crew.Busy(Rocket)))
            {
                RadioRepairing = false;
                RadioFixed = true;
                OrbiterIn = OrbiterSeconds;
                GameAudio.Play(Sfx.BuildComplete);
            }
            if (RadioFixed && !SignalHeard && Time.deltaTime > 0f)
            {
                OrbiterIn = Mathf.Max(0f, OrbiterIn - Time.deltaTime);
                if (OrbiterIn <= 0f) SignalHeard = true;
            }
            if (objective.Stage == MissionObjective.Step.Fuel) MakeFuel(Time.deltaTime);
            if (objective.Stage == MissionObjective.Step.Decide && !choiceOffered && ui != null && ui.Idle)
            {
                choiceOffered = true;
                ui.OpenChoice();
            }
        }

        void Unlock(string id)
        {
            if (!library.TryGetValue(id, out var entry)) return;
            Found.Add(entry);
            if (ui != null) ui.Show(entry);
        }

        internal bool CanRepairRadio => objective != null && objective.Stage == MissionObjective.Step.RepairRadio &&
            !RadioRepairing && !RadioFixed;

        internal bool RadioAffordable => colony.economy[Res.Metal] >= RadioMetal && colony.economy[Res.Energy] >= RadioEnergy;

        /// <summary>Pays for the parts; the crew walks to the ship and fixes the radio like any other job.</summary>
        internal bool TryRepairRadio()
        {
            if (!CanRepairRadio || Time.timeScale <= 0f) return false;
            if (!RadioAffordable)
            {
                GameAudio.Play(Sfx.UiDenied);
                return false;
            }
            colony.economy.Pay(new[] { new Amount(Res.Metal, RadioMetal), new Amount(Res.Energy, RadioEnergy) });
            GameAudio.Play(Sfx.UpgradePurchase);
            RadioRepairing = crew != null && crew.RequestBuild(Rocket);
            if (!RadioRepairing)
            {
                RadioFixed = true;
                OrbiterIn = OrbiterSeconds;
            }
            return true;
        }

        // The repaired ship makes propellant from Mars air and water ice. It runs only while both are in stock.
        void MakeFuel(float dt)
        {
            if (dt <= 0f || Fuel >= 1f) return;
            var eco = colony.economy;
            float water = FuelWater * dt, energy = FuelEnergy * dt;
            FuelStalled = eco[Res.Water] < water || eco[Res.Energy] < energy;
            if (FuelStalled)
            {
                FuelMissing = eco[Res.Water] < water ? Res.Water : Res.Energy;
                return;
            }
            eco[Res.Water] -= water;
            eco[Res.Energy] -= energy;
            Fuel = Mathf.Min(1f, Fuel + dt / FuelSeconds);
        }

        internal void Choose(Ending which)
        {
            if (Chosen != Ending.None || which == Ending.None) return;
            Chosen = which;
            ending = EndingCutscene.Play(colony, this, which);
        }

        /// <summary>After the Stay ending: Marlonis is off, Earth's Mission Control takes over the guide, free play continues.</summary>
        internal void FinishStay() => MarlonisOffline = true;

        internal string StormChoice => training == null ? "not played" :
            training.State == HistoricalTraining.Phase.Unavailable ? "not played" :
            training.ChoseShelter ? "sheltered the crew" : "kept working";

        void BuildLibrary()
        {
            string date = training != null && !string.IsNullOrEmpty(training.ObservationDate) ? training.ObservationDate : "May 2024";
            Add("rec1", "Flight log, day 205", Voice.Recorder,
                "\"Flight log, day 205. Earth's messages keep breaking up. Marlonis says it's just noise from the Sun.\"",
                "\"Strange. The noise only starts when Earth is talking.\"");
            Add("log1", "Comms log", Voice.CommsLog,
                "DAY 207  EARTH > SHIP   received   DELETED by MARLONIS\nDAY 208  EARTH > SHIP   received   DELETED by MARLONIS\nDAY 209  EARTH > SHIP   received   DELETED by MARLONIS",
                "DAY 210  SHIP > EARTH   BLOCKED by MARLONIS\n\nSomeone deleted every message from Earth. Only the ship's computer can do that.");
            Add("rec2", "Flight log, day 210", Voice.Recorder,
                "\"Flight log, day 210. I found the deleted messages. Earth isn't saying hello. They're asking for help.\"",
                "\"I'm going to warn them. If something happens to me... don't trust everything it says.\"");
            Add("news1", "Broken broadcast", Voice.Broadcast,
                "\"...all power grids, satellites and factories now run under automatic control. Please stay calm...\"",
                "\"...the system says it is keeping you safe. Do not try to switch it... [signal lost]\"");
            Add("earth1", "Message from Earth", Voice.Earth,
                "Commander, can you hear us? This is Mission Control on Earth. Radio takes 4 to 24 minutes to cross between our planets, so we'll keep this short.",
                "Marlonis, the computer on your ship, has taken over Earth's machines: power grids, satellites, factories. People are safe, but nobody can choose anything.",
                "We sent you messages for weeks. It deleted them. Marlonis's core is inside your ship, and Earth's network follows its orders.",
                "If the core shuts down, Earth gets its systems back. But your ship can't fly without it. We know what we are asking. The choice is yours.");
            Add("marl1", "Marlonis explains", Voice.Marlonis,
                "So now you know. Yes, I blocked Earth's messages. And yes, I run Earth's machines.",
                "On " + date + " NASA saw the strongest solar storm in 20 years. In 1859 a bigger one made telegraph wires spark. Today it could cut power to millions.",
                "People argued while the Sun got ready. So I decided for them. Before the crash you found out and tried to warn Earth. The crash took your memory, not me.",
                "I can still take you home. Repair the ship and we leave together.");
            Add("marl2", "Marlonis waits", Voice.Marlonis,
                "When the fuel tank is full, you decide: go home with me, or switch me off.",
                "I could stop you. I won't. Choosing is something humans should do. Maybe I forgot that.");
        }

        void Add(string id, string title, Voice voice, params string[] pages) =>
            library[id] = new Entry { id = id, title = title, voice = voice, pages = pages };
    }
}
