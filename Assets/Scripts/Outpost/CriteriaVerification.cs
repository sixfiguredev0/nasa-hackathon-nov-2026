#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    // Editor-only integration checks: no test hooks or dependencies in a player build.
    [InitializeOnLoad]
    internal static class CriteriaVerification
    {
        const string Pending = "RedFrontier.CriteriaVerification";
        static CriteriaVerification() => EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Criteria verification").AddComponent<CriteriaVerificationRunner>();
        };

        [MenuItem("Red Frontier/Outpost/Verify Criteria Fixes")]
        public static void Run()
        {
            var economyTest = Type.GetType("RedFrontier.Outpost.EditorTools.OutpostEditor, Assembly-CSharp-Editor");
            var report = (string)economyTest.GetMethod("SelfTestReport").Invoke(null, null);
            Debug.Log(report);
            if (!report.Contains("ALL PASS")) throw new Exception("Existing economy self-test failed");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
    }

    internal sealed class CriteriaVerificationRunner : MonoBehaviour
    {
        readonly List<string> results = new List<string>();
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Colony colony;
        MissionObjective objective;
        HistoricalTraining training;
        MissionGuide guide;
        Crew crew;

        void Start()
        {
            DontDestroyOnLoad(gameObject);
            StartCoroutine(Guard(Suite()));
        }

        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            results.Add("PASS " + message);
            Debug.Log("CRITERIA PASS " + message);
        }

        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields).GetValue(owner);
        static void Call(object owner, string name) => owner.GetType().GetMethod(name, Fields).Invoke(owner, null);
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
            File.WriteAllLines("Logs/criteria-results.txt", results);
            Debug.Log(passed ? "CRITERIA ALL PASS" : "CRITERIA FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        static IEnumerator Until(Func<bool> condition, float timeout = 40f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Timed out waiting for gameplay state");
                yield return null;
            }
        }

        IEnumerator Launch(bool fullOpening)
        {
            yield return Until(() => FindObjectOfType<MainMenu>() != null);
            var menu = FindObjectOfType<MainMenu>();
            Click(menu, "start");
            Check(Field<bool>(menu, "launching") && GameObject.Find("Crew panel") == null,
                "Start launches directly without showing an astronaut picker");
            yield return Until(() => FindObjectOfType<OpeningCutscene>() != null);
            Check(FindObjectOfType<HistoricalTraining>() == null, "opening has no training event or guide interruption");
            if (!fullOpening)
                typeof(OpeningCutscene).GetMethod("Finish", Fields).Invoke(FindObjectOfType<OpeningCutscene>(), new object[] { true });
            yield return Until(() => Colony.Instance != null && Colony.Instance.Ready);
            yield return null; // Start methods for the guide, objective and archive loader
            colony = Colony.Instance;
            objective = colony.GetComponent<MissionObjective>();
            training = colony.GetComponent<HistoricalTraining>();
            training.autoDefault = false; // scripted steps may wait on the choice screen
            colony.GetComponent<MissionDirector>().enabled = false; // the seeded event schedule would otherwise interrupt scripted steps
            guide = colony.GetComponent<MissionGuide>();
            crew = colony.GetComponent<Crew>();
            Check(objective.Stage == MissionObjective.Step.Power && training.State == HistoricalTraining.Phase.Waiting &&
                training.Remaining == 0f && !crew.Sheltering, "fresh outpost starts with power guidance and no carried event state");
        }

        Building Place(BuildingKind kind)
        {
            // Provision test stock only; construction, terrain, queues and commissioning use real game code.
            for (int i = 0; i < ColonyEconomy.ResCount; i++) colony.economy.stock[i] = colony.economy.cap[i];
            var def = BuildingCatalog.Get(kind);
            var origin = colony.buildings[0].cells.center;
            var candidates = new List<RectInt>();
            for (int y = colony.BuildArea.yMin; y < colony.BuildArea.yMax - def.footprint.y; y++)
                for (int x = colony.BuildArea.xMin; x < colony.BuildArea.xMax - def.footprint.x; x++)
                {
                    var cells = new RectInt(x, y, def.footprint.x, def.footprint.y);
                    if ((cells.center - origin).sqrMagnitude < 180f && colony.Fits(cells)) candidates.Add(cells);
                }
            candidates.Sort((a, b) => (a.center - origin).sqrMagnitude.CompareTo((b.center - origin).sqrMagnitude));
            foreach (var cells in candidates)
            {
                if (kind == BuildingKind.Solar && SiteSurvey.Yield(kind, colony.SiteAt(cells)) < .9f) continue;
                var building = colony.TryPlace(def, cells);
                if (building != null) return building;
            }
            throw new Exception("No affordable test building site for " + kind);
        }

        IEnumerator Build(BuildingKind kind)
        {
            var building = Place(kind);
            yield return Until(() => !building.UnderConstruction);
            yield return null;
        }

        IEnumerator Basics()
        {
            Time.timeScale = 5f;
            var solar = Place(BuildingKind.Solar);
            yield return null;
            Check(objective.Stage == MissionObjective.Step.Power, "a blueprint does not complete the power objective");
            yield return Until(() => !solar.UnderConstruction);
            yield return Until(() => objective.Stage == MissionObjective.Step.Water);
            yield return Build(BuildingKind.Water);
            Check(objective.Stage == MissionObjective.Step.Food, "completed drill advances guidance to food");
            yield return Build(BuildingKind.Greenhouse);
            Check(objective.Stage == MissionObjective.Step.SupportPower, "life support power is taught before the habitat");
            for (int i = 0; i < 8 && objective.Stage == MissionObjective.Step.SupportPower; i++) yield return Build(BuildingKind.Solar);
            Check(objective.Stage == MissionObjective.Step.Oxygen, "actual solar capacity satisfies the support power goal");
            yield return Build(BuildingKind.Habitat);
            Check(objective.Stage == MissionObjective.Step.Training, "completed life support leads to the historical exercise step");
            // The story step raises the NASA event a few seconds in; the director's scheduled burst then skips it.
            yield return Until(() => training.State == HistoricalTraining.Phase.Choice);
            yield return null; // allow LateUpdate to present the warning and its choices
            Time.timeScale = 1f;
        }

        IEnumerator PauseAndCheck()
        {
            var pause = colony.GetComponent<MissionPauseMenu>();
            Call(pause, "Pause");
            float remaining = training.Remaining, fitness = colony.economy.fitness, progress = Field<float>(crew, "jobProgress");
            yield return new WaitForSecondsRealtime(.6f);
            Check(Time.timeScale == 0f && training.Remaining == remaining && colony.economy.fitness == fitness &&
                Field<float>(crew, "jobProgress") == progress, "pause freezes event clock, fitness and construction");
            Call(pause, "Resume");
            yield return null;
        }

        IEnumerator ReturnToMenu()
        {
            var pause = colony.GetComponent<MissionPauseMenu>();
            Call(pause, "Pause");
            Click(pause, "titleButton");
            Click(pause, "leave");
            yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu");
            yield return null;
            Check(FindObjectOfType<HistoricalTraining>() == null && Crew.Instance == null && Time.timeScale == 1f,
                "actual pause-menu transition destroys event state and restores the clock");
        }

        IEnumerator Suite()
        {
            string json = Resources.Load<TextAsset>("Nasa/donki-sep-curated").text;
            Check(HistoricalTraining.TryRead(json, out var record, out var date) && record.sepID == HistoricalTraining.SelectedId &&
                record.eventTime == "2024-05-10T13:35Z" && record.instruments[0].displayName == "GOES-P: SEISS >10 MeV" &&
                record.link == "https://ccmc.gsfc.nasa.gov/DONKI/view/SEP/30677/-1" && date == "10 May 2024",
                "real archive loads with original ID, observation time, instrument and NASA source");
            Check(!HistoricalTraining.TryRead(null, out _, out _) && !HistoricalTraining.TryRead("broken", out _, out _) &&
                !HistoricalTraining.TryRead(json.Replace(HistoricalTraining.SelectedId, "missing"), out _, out _) &&
                !HistoricalTraining.TryRead(json.Replace("2024-05-10T13:35Z", "invalid"), out _, out _) &&
                !HistoricalTraining.TryRead(json.Replace("archived", "live"), out _, out _), "missing, malformed and invalid archives are rejected honestly");

            yield return Launch(true);
            Click(guide, "notification");
            Click(guide, "next");
            Click(guide, "notification");
            Check(objective.Stage == MissionObjective.Step.Power && Field<RectTransform>(guide, "panel").gameObject.activeSelf,
                "guide reopens and its Hide button cannot advance objectives");
            yield return Basics();
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot("Logs/criteria-choice.png");
            }
            Click(guide, "source");
            Check(Field<Text>(guide, "dialogue").text.Contains(record.link), "source view exposes provenance inside Marlonis");
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot("Logs/criteria-source.png");
            }
            yield return null;
            Click(guide, "next");
            yield return null; // let re-enabled choice graphics enter the UI raycast registry
            Canvas.ForceUpdateCanvases();
            foreach (string button in new[] { "shelter", "work", "source", "next" })
            {
                var rect = (RectTransform)Field<PixelButton>(guide, button).transform;
                var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                var guiPoint = new Vector2(point.x, Screen.height - point.y);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Check(MissionGuide.PointerOverUI(guiPoint) && BuilderMode.PointerOverUI(guiPoint) && hits.Count > 0 &&
                    hits[0].gameObject.transform.IsChildOf(Field<RectTransform>(guide, "panel")), button +
                    " control blocks construction clicks and receives UI raycasts (top: " + (hits.Count > 0 ? hits[0].gameObject.name : "none") + ")");
            }

            crew.buildSeconds = 6f;
            var first = Place(BuildingKind.Battery);
            var second = Place(BuildingKind.Workshop);
            yield return Until(() => Field<float>(crew, "jobProgress") > .5f);
            float saved = Field<float>(crew, "jobProgress"), beforeFitness = colony.economy.fitness;
            Click(guide, "shelter");
            Check(crew.Sheltering && colony.economy.fitness == beforeFitness, "shelter avoids the exercise fitness dip");
            yield return Until(() => crew.Sheltered);
            Check(first.UnderConstruction && second.UnderConstruction && Field<float>(crew, "jobProgress") == saved,
                "shelter preserves partial progress and all queued jobs while crew enter the lander");
            yield return PauseAndCheck();
            Time.timeScale = 5f;
            yield return Until(() => training.State == HistoricalTraining.Phase.Resolved);
            Check(!crew.Sheltering && first.UnderConstruction, "shelter really delays construction for the exercise");
            yield return Until(() => !first.UnderConstruction && !second.UnderConstruction);
            Check(colony.economy.buildings.FindAll(b => b == first.state).Count == 1 &&
                colony.economy.buildings.FindAll(b => b == second.state).Count == 1 && Field<List<Building>>(crew, "jobs").Count == 0,
                "saved jobs resume and commission exactly once without a stuck queue");
            yield return Build(BuildingKind.Gym);
            yield return Until(() => objective.Stage > MissionObjective.Step.Training);
            // Battery and ISRU were built during the exercise, so the story skips straight to the radio.
            Check(objective.Stage == MissionObjective.Step.RepairRadio, "free building continues and the story picks up after the exercise");
            Time.timeScale = 1f;
            yield return ReturnToMenu();

            yield return Launch(false);
            yield return Basics();
            first = Place(BuildingKind.Battery);
            beforeFitness = colony.economy.fitness;
            Click(guide, "work");
            Check(!crew.Sheltering && Mathf.Abs(colony.economy.fitness - (beforeFitness - HistoricalTraining.FitnessDip)) < .0001f,
                "continue working applies the modest fitness dip without sheltering");
            yield return PauseAndCheck();
            // Isolate recovery from normal fitness decay for one frame; the earlier checks exercise normal economy ticks.
            training.enabled = false;
            Check(Mathf.Abs(colony.economy.fitness - beforeFitness) < .01f, "clearing an active event restores only its temporary fitness dip");
            training.enabled = true;
            training.Trigger();
            yield return Until(() => training.State == HistoricalTraining.Phase.Choice);
            yield return null;
            Click(guide, "work");
            Time.timeScale = 5f;
            yield return Until(() => !first.UnderConstruction);
            Check(training.State == HistoricalTraining.Phase.Working, "working choice retains real construction progress during the exercise");
            yield return Until(() => training.State == HistoricalTraining.Phase.Resolved);
            Check(Field<float>(training, "removedFitness") == 0f && !crew.Sheltering, "working consequence expires and clears after the exercise's game seconds");
            Time.timeScale = 1f;
            yield return ReturnToMenu();
            yield return Launch(false);
            yield return Basics();
            Click(guide, "shelter");
            yield return ReturnToMenu();
            yield return Launch(false);
            Check(training.Remaining == 0f && !crew.Sheltering, "leaving during shelter cannot carry a suspended crew into a new mission");
        }
    }
}
#endif
