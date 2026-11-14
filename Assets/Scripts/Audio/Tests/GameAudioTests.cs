using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RedFrontier.Audio.Tests
{
    public sealed class GameAudioTests
    {
        const string TestCue = "TEST_CUE", TestLoop = "TEST_LOOP";

        static AudioClip Tone(string name, float seconds)
        {
            int n = Mathf.CeilToInt(44100 * seconds);
            var clip = AudioClip.Create(name, n, 1, 44100, false);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = 0.1f * Mathf.Sin(i * 0.05f);
            clip.SetData(data, 0);
            return clip;
        }

        static GameAudio Fresh()
        {
            var a = GameAudio.Ensure();
            a.ForceUnlock();
            return a;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SecondManagerDestroysItself()
        {
            var first = Fresh();
            var extra = new GameObject("Duplicate").AddComponent<GameAudio>();
            yield return null;
            Assert.IsTrue(extra == null, "duplicate should be destroyed");
            Assert.AreSame(first, GameAudio.Instance);
            Assert.AreSame(first, GameAudio.Ensure());
            Assert.AreEqual(1, Object.FindObjectsOfType<GameAudio>().Length);
        }

        [UnityTest]
        public IEnumerator CooldownBlocksRapidRepeats()
        {
            var a = Fresh();
            AudioCatalog.Register(new CueDef(TestCue, AudioBus.Effects, 1, 0f, cooldown: 0.25f, maxVoices: 4));
            a.InjectClips(TestCue, new[] { Tone("cd", 1f) });
            Assert.IsTrue(a.PlayCue(TestCue));
            Assert.IsFalse(a.PlayCue(TestCue), "inside cooldown");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(a.PlayCue(TestCue), "after cooldown");
            Assert.AreEqual(2, a.VoicesPlaying(TestCue));
        }

        [UnityTest]
        public IEnumerator VoiceLimitCapsSimultaneousCopies()
        {
            var a = Fresh();
            AudioCatalog.Register(new CueDef(TestCue, AudioBus.Effects, 1, 0f, cooldown: 0f, maxVoices: 2));
            a.InjectClips(TestCue, new[] { Tone("vl", 2f) });
            for (int i = 0; i < 5; i++) a.PlayCue(TestCue);
            yield return null;
            Assert.AreEqual(2, a.VoicesPlaying(TestCue));
        }

        [Test]
        public void GateUnit()
        {
            var gate = new CueGate();
            var def = new CueDef("G", AudioBus.Effects, 1, 0f, cooldown: 0.1f, maxVoices: 1);
            Assert.IsTrue(gate.Allow(def, 0f, 0));
            Assert.IsFalse(gate.Allow(def, 0.05f, 0));
            Assert.IsTrue(gate.Allow(def, 0.2f, 0));
            Assert.IsFalse(gate.Allow(def, 1f, 1), "voice limit");
        }

        [Test]
        public void LevelsPersistInPlayerPrefs()
        {
            float before = AudioPrefs.Get(AudioBus.Music);
            try
            {
                GameAudio.SetLevel(AudioBus.Music, 0.3f);
                Assert.AreEqual(0.3f, PlayerPrefs.GetFloat(AudioPrefs.MusicKey), 1e-4f);
                Assert.AreEqual(0.3f, AudioPrefs.Get(AudioBus.Music), 1e-4f);
                GameAudio.SetLevel(AudioBus.Effects, 7f);
                Assert.AreEqual(1f, AudioPrefs.Get(AudioBus.Effects), "clamped");
                Assert.AreNotEqual(AudioPrefs.MusicKey, "MasterVolume", "master volume key is untouched");
            }
            finally
            {
                AudioPrefs.Set(AudioBus.Music, before);
                PlayerPrefs.DeleteKey(AudioPrefs.EffectsKey);
            }
        }

        [UnityTest]
        public IEnumerator LoopStopsWhenOwnerIsDestroyed()
        {
            var a = Fresh();
            AudioCatalog.Register(new CueDef(TestLoop, AudioBus.Effects, 1, 0f));
            a.InjectClips(TestLoop, new[] { Tone("loop", 1f) });
            var owner = new GameObject("Loop owner");
            GameAudio.StartLoop(TestLoop, owner);
            GameAudio.StartLoop(TestLoop, owner); // repeated start must not stack
            Assert.AreEqual(1, a.LoopCount);
            Object.Destroy(owner);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(0, a.LoopCount);
        }

        [UnityTest]
        public IEnumerator SceneChangeClearsLoopsAndPause()
        {
            var a = Fresh();
            AudioCatalog.Register(new CueDef(TestLoop, AudioBus.Effects, 1, 0f));
            a.InjectClips(TestLoop, new[] { Tone("loop2", 1f) });
            GameAudio.StartLoop(TestLoop, a);
            GameAudio.SetPaused(true);
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.CreateScene("AudioTestScene");
            SceneManager.SetActiveScene(scene);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsFalse(a.IsPaused);
            Assert.AreEqual(0, a.LoopCount);
            Assert.IsTrue(GameAudio.Instance == a, "manager survives scene changes");
            SceneManager.SetActiveScene(previous);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator PausePausesGameplayLoopsOnly()
        {
            var a = Fresh();
            AudioCatalog.Register(new CueDef(TestLoop, AudioBus.Effects, 1, 0f));
            a.InjectClips(TestLoop, new[] { Tone("loop3", 2f) });
            var owner = new GameObject("Loop owner 2");
            GameAudio.StartLoop(TestLoop, owner);
            yield return null;
            GameAudio.SetPaused(true);
            GameAudio.SetPaused(true); // idempotent
            Assert.IsTrue(a.IsPaused);
            AudioCatalog.Register(new CueDef(TestCue, AudioBus.Effects, 1, 0f, cooldown: 0f));
            a.InjectClips(TestCue, new[] { Tone("ui", 0.5f) });
            Assert.IsTrue(a.PlayCue(TestCue), "UI sounds still play while paused");
            GameAudio.SetPaused(false);
            Assert.IsFalse(a.IsPaused);
            Assert.AreEqual(1, a.LoopCount, "resume does not stack loops");
            Object.Destroy(owner);
        }

        [Test]
        public void SelectorNeverRepeatsRecentAndCoversAll()
        {
            var pool = new[] { "A", "B", "C", "D", "E" };
            var s = new MusicSelector(pool, 1234);
            var seen = new HashSet<string>();
            string prev = null, prev2 = null;
            for (int i = 0; i < 500; i++)
            {
                var next = s.Next();
                Assert.AreNotEqual(prev, next);
                Assert.AreNotEqual(prev2, next);
                seen.Add(next);
                prev2 = prev;
                prev = next;
                float rest = s.RestSeconds();
                Assert.That(rest, Is.InRange(8f, 50f));
            }
            Assert.AreEqual(pool.Length, seen.Count);
        }

        [Test]
        public void SelectorHandlesTinyPools()
        {
            Assert.AreEqual("X", new MusicSelector(new[] { "X" }, 1).Next());
            var two = new MusicSelector(new[] { "X", "Y" }, 2);
            string a = two.Next(), b = two.Next(), c = two.Next();
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(b, c);
        }

        [UnityTest]
        public IEnumerator MissingClipsAndUnknownIdsAreSafe()
        {
            var a = Fresh();
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(a.PlayCue("NO_SUCH_SOUND"));
            AudioCatalog.Register(new CueDef("TEST_MISSING", AudioBus.Effects, 2, 0f));
            Assert.IsFalse(a.PlayCue("TEST_MISSING"));
            GameAudio.StartLoop("TEST_MISSING", a);
            GameAudio.SetAmbience("NO_SUCH_BED", 1f);
            Assert.AreEqual(0, a.LoopCount);
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator GenericPressSkippedAfterSpecificCue()
        {
            var a = Fresh();
            yield return null; // new frame
            AudioCatalog.Register(new CueDef(TestCue, AudioBus.Effects, 1, 0f, cooldown: 0f));
            a.InjectClips(TestCue, new[] { Tone("spec", 0.5f) });
            a.InjectClips(Sfx.UiConfirm, new[] { Tone("generic", 0.5f) });
            a.PlayCue(TestCue);
            GameAudio.UiPress();
            Assert.AreEqual(0, a.VoicesPlaying(Sfx.UiConfirm), "same-frame specific cue suppresses the generic one");
            yield return null;
            GameAudio.UiPress();
            Assert.AreEqual(1, a.VoicesPlaying(Sfx.UiConfirm));
        }

        [UnityTest]
        public IEnumerator MusicStartsTitleAndRotatesGameplay()
        {
            var a = Fresh();
            foreach (var id in new List<string>(MusicIds.Gameplay) { MusicIds.Title, MusicIds.Arrival, MusicIds.Tension })
                a.InjectClips(id, new[] { Tone(id, 0.4f) });
            GameAudio.EnterTitle();
            yield return null;
            yield return null;
            Assert.AreEqual(MusicIds.Title, a.CurrentMusic);
            GameAudio.Launch(true);
            yield return null;
            yield return null;
            Assert.AreEqual(MusicIds.Arrival, a.CurrentMusic);
            GameAudio.BeginGameplayMusic();
            Assert.AreEqual("Arrival", a.MusicModeName, "arrival finishes before gameplay");
            yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual("Gameplay", a.MusicModeName);
            Assert.IsTrue(a.IsResting, "breathing space before the first movement");
        }
    }
}
