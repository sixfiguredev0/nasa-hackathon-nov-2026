#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    // Editor-only play-through of the story arc: clues, radio, orbiter, ship repair, fuel, choice and both endings.
    [InitializeOnLoad]
    internal static class StoryVerification
    {
        const string Pending = "RedFrontier.StoryVerification";
        static StoryVerification() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Story verification").AddComponent<StoryVerificationRunner>();
        };

        [MenuItem("Red Frontier/Outpost/Verify Story")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
    }

    internal sealed class StoryVerificationRunner : MonoBehaviour
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<string> results = new List<string>();
        Colony colony;
        MissionObjective objective;
        HistoricalTraining training;
        MissionGuide guide;
        StoryArc story;
        StoryUi ui;

        void Start()
        {
            DontDestroyOnLoad(gameObject);
            StartCoroutine(Guard(Suite()));
        }

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            results.Add("PASS " + message);
            Debug.Log("STORY PASS " + message);
        }

        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
        static void Click(object owner, string name)
        {
            var button = Field<PixelButton>(owner, name);
            if (!button.isActiveAndEnabled || !button.interactable) throw new Exception("Test tried to click hidden/disabled " + name);
            button.onClick.Invoke();
        }

        IEnumerator Guard(IEnumerator suite)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(suite);
            while (stack.Count > 0)
            {
                object next = null;
                Exception failure = null;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Debug.LogException(failure);
                    results.Add("FAIL " + failure);
                    Finish(false);
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            Finish(true);
        }

        void Finish(bool passed)
        {
            Time.timeScale = 1f;
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/story-results.txt", results);
            Debug.Log(passed ? "STORY ALL PASS" : "STORY FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static IEnumerator Until(Func<bool> condition, float timeout = 60f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Timed out waiting for story state");
                yield return null;
            }
        }

        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Logs/story-" + name + ".png");
            yield return null;
        }

        void Fill()
        {
            for (int i = 0; i < ColonyEconomy.ResCount; i++) colony.economy.stock[i] = colony.economy.cap[i];
            colony.economy.fitness = 1f;
        }

        IEnumerator Launch()
        {
            yield return Until(() => FindObjectOfType<MainMenu>() != null);
            var menu = FindObjectOfType<MainMenu>();
            Click(menu, "start"); // Start launches the mission directly (no astronaut pick)
            yield return Until(() => FindObjectOfType<OpeningCutscene>() != null);
            typeof(OpeningCutscene).GetMethod("Finish", Fields).Invoke(FindObjectOfType<OpeningCutscene>(), new object[] { true });
            yield return Until(() => Colony.Instance != null && Colony.Instance.Ready);
            yield return null;
            colony = Colony.Instance;
            objective = colony.GetComponent<MissionObjective>();
            training = colony.GetComponent<HistoricalTraining>();
            guide = colony.GetComponent<MissionGuide>();
            story = colony.GetComponent<StoryArc>();
            ui = colony.GetComponent<StoryUi>();
            Check(story != null && ui != null && story.Found.Count == 0 && story.Chosen == StoryArc.Ending.None,
                "fresh outpost starts the story with no clues and no choice");
        }

        Building Place(BuildingKind kind)
        {
            Fill();
            var def = BuildingCatalog.Get(kind);
            var origin = colony.buildings[0].cells.center;
            var candidates = new List<RectInt>();
            for (int y = colony.BuildArea.yMin; y < colony.BuildArea.yMax - def.footprint.y; y++)
                for (int x = colony.BuildArea.xMin; x < colony.BuildArea.xMax - def.footprint.x; x++)
                {
                    var cells = new RectInt(x, y, def.footprint.x, def.footprint.y);
                    if ((cells.center - origin).sqrMagnitude < 260f && colony.Fits(cells)) candidates.Add(cells);
                }
            candidates.Sort((a, b) => (a.center - origin).sqrMagnitude.CompareTo((b.center - origin).sqrMagnitude));
            foreach (var cells in candidates)
            {
                if (kind == BuildingKind.Solar && SiteSurvey.Yield(kind, colony.SiteAt(cells)) < .9f) continue;
                var building = colony.TryPlace(def, cells);
                if (building != null) return building;
            }
            throw new Exception("No test building site for " + kind);
        }

        IEnumerator Build(BuildingKind kind)
        {
            var b = Place(kind);
            yield return Until(() => !b.UnderConstruction);
            yield return null;
        }

        IEnumerator DismissPopups()
        {
            for (int i = 0; i < 400 && !ui.Idle; i++)
            {
                yield return null;
                if (Field<StoryArc.Entry>(ui, "current") != null) Click(ui, "next");
            }
        }

        /// <summary>Plays the arc up to the choice screen with provisioned stock and sped-up time.</summary>
        IEnumerator ToChoice(bool shots)
        {
            Time.timeScale = 6f;
            yield return Build(BuildingKind.Solar);
            yield return Until(() => story.Found.Count == 1);
            Check(story.Found[0].id == "rec1", "first recorder clue unlocks after power");
            if (shots)
            {
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("1-recorder");
                Time.timeScale = 6f;
            }
            yield return Build(BuildingKind.Water);
            yield return Build(BuildingKind.Greenhouse);
            for (int i = 0; i < 8 && objective.Stage == MissionObjective.Step.SupportPower; i++) yield return Build(BuildingKind.Solar);
            Check(story.Found.Exists(e => e.id == "log1"), "comms log of deleted messages unlocks mid-survival");
            if (shots)
            {
                Click(ui, "logButton");
                yield return null;
                yield return Shot("2-log");
                Click(ui, "logClose");
            }
            yield return Build(BuildingKind.Habitat);
            yield return Until(() => training.State == HistoricalTraining.Phase.Choice);
            yield return null;
            Click(guide, "work");
            yield return Until(() => objective.Stage == MissionObjective.Step.Salvage, 90f);
            Check(story.Found.Exists(e => e.id == "rec2") && story.Found.Exists(e => e.id == "news1"),
                "recorder warning and the broken broadcast unlock around the exercise");
            yield return Build(BuildingKind.Workshop);
            Check(objective.Stage == MissionObjective.Step.StorePower, "ISRU completes the salvage step");
            yield return Build(BuildingKind.Battery);
            Check(objective.Stage == MissionObjective.Step.RepairRadio && story.CanRepairRadio, "battery leads to the radio repair");
            yield return DismissPopups();
            Fill();
            yield return null;
            if (!Field<RectTransform>(guide, "panel").gameObject.activeSelf) Click(guide, "notification");
            yield return null;
            if (shots) yield return Shot("3-radio");
            float metal = colony.economy[Res.Metal];
            Click(guide, "action");
            Check(story.RadioRepairing && colony.economy[Res.Metal] <= metal - StoryArc.RadioMetal + 1f, "repairing the radio costs parts and sends the crew");
            yield return Until(() => story.RadioFixed);
            yield return null;
            yield return null;
            Check(objective.Stage == MissionObjective.Step.Signal && story.OrbiterIn > 0f, "a fixed radio waits for the orbiter pass");
            yield return Until(() => story.SignalHeard && objective.Stage == MissionObjective.Step.RepairHull);
            Check(story.Found.Exists(e => e.id == "earth1") && story.Found.Exists(e => e.id == "marl1"), "Earth's message and Marlonis's confession arrive");
            if (shots)
            {
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(4f);
                yield return Shot("4-earth");
                Time.timeScale = 6f;
            }
            Fill();
            var rocket = story.Rocket;
            Check(colony.economy.TryUpgrade(rocket.state, "rocket2"), "the Repaired Ship upgrade is buyable");
            yield return Until(() => story.HullRepaired);
            yield return Until(() => objective.Stage == MissionObjective.Step.Fuel);
            Check(story.Found.Exists(e => e.id == "marl2"), "Marlonis's last words unlock with the fuel step");
            colony.enabled = false; // hold production so the drill cannot refill the tank between frames
            colony.economy[Res.Water] = 0f;
            yield return null;
            yield return null;
            Check(story.FuelStalled && story.FuelMissing == Res.Water && objective.Guidance.Contains("water"), "fuel pauses without water and says why");
            colony.enabled = true;
            Time.timeScale = 20f;
            yield return Until(() =>
            {
                Fill();
                return story.Fuel >= 1f;
            }, 120f);
            Time.timeScale = 1f;
            yield return Until(() => objective.Stage == MissionObjective.Step.Decide);
            yield return DismissPopups();
            yield return Until(() => ui.ChoiceOpen);
            Check(StoryArc.Cinematic && StoryArc.PointerOverUI(new Vector2(Screen.width / 2f, Screen.height / 2f)),
                "the choice screen takes over the screen and blocks building clicks");
            if (shots) yield return Shot("5-choice");
        }

        IEnumerator Suite()
        {
            // Stay: shut Marlonis down, then keep playing.
            yield return Launch();
            yield return ToChoice(true);
            Click(ui, "laterButton");
            Check(!ui.ChoiceOpen && objective.Stage == MissionObjective.Step.Decide, "Not yet closes the choice without deciding");
            yield return null;
            if (!Field<RectTransform>(guide, "panel").gameObject.activeSelf) Click(guide, "notification");
            yield return null;
            Click(guide, "action");
            Check(ui.ChoiceOpen, "the guide reopens the choice");
            Click(ui, "stayButton");
            yield return null; // the ending sets itself up in Start
            yield return null;
            var ending = FindObjectOfType<EndingCutscene>();
            Check(ending != null && ending.Running && !colony.enabled, "Stay starts its ending and hides the HUD");
            yield return Until(() => Field<RectTransform>(ending, "panel") != null && Field<RectTransform>(ending, "panel").gameObject.activeSelf, 30f);
            yield return Shot("6-stay-goodbye");
            yield return Until(() => Field<RectTransform>(ending, "card") != null, 90f);
            yield return Shot("7-stay-card");
            var card = Field<RectTransform>(ending, "card");
            var keep = Array.Find(card.GetComponentsInChildren<PixelButton>(), b => b.name == "Keep building");
            keep.onClick.Invoke();
            yield return null;
            yield return null;
            Check(story.MarlonisOffline && colony.enabled && objective.Stage == MissionObjective.Step.Sandbox && !StoryArc.Cinematic &&
                FindObjectOfType<EndingCutscene>() == null, "Keep building restores free play with Marlonis switched off");
            yield return new WaitForSecondsRealtime(1f);
            Check(Field<Text>(guide, "speaker").text.Contains("Mission Control"), "Mission Control takes over the guide");
            yield return Shot("8-after-stay");
            var pause = colony.GetComponent<MissionPauseMenu>();
            typeof(MissionPauseMenu).GetMethod("Pause", Fields).Invoke(pause, null);
            Click(pause, "titleButton");
            Click(pause, "leave");
            yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu");
            yield return null;
            Check(StoryArc.Instance == null && Time.timeScale == 1f, "leaving to the menu clears the story");

            // Return: launch with Marlonis, then back to the title.
            yield return Launch();
            yield return ToChoice(false);
            Click(ui, "returnButton");
            yield return null;
            yield return null;
            ending = FindObjectOfType<EndingCutscene>();
            yield return Until(() => Field<PixelLabel>(ending, "bigLabel").gameObject.activeSelf, 40f);
            yield return Shot("9-countdown");
            yield return Until(() => Field<PixelLabel>(ending, "bigLabel").Text == "Liftoff!", 20f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("10-liftoff");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("10b-flight");
            typeof(EndingCutscene).GetField("skipping", Fields).SetValue(ending, true);
            yield return Until(() => Field<RectTransform>(ending, "card") != null, 90f);
            yield return Shot("11-return-card");
            card = Field<RectTransform>(ending, "card");
            Check(Array.Find(card.GetComponentsInChildren<PixelButton>(), b => b.name == "Keep building") == null, "Return has no free play afterwards");
            Array.Find(card.GetComponentsInChildren<PixelButton>(), b => b.name == "Main menu").onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu");
            Check(StoryArc.Instance == null, "Return ending goes back to the main menu");
        }
    }
}
#endif
