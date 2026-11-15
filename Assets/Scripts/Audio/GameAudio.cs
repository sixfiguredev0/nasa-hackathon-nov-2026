using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RedFrontier.Audio
{
    /// <summary>
    /// The one persistent audio manager: pooled one-shots with per-cue cooldowns, voice limits and priorities,
    /// crossfaded loops, ambience beds, and the music section controller. Game code only calls the static API;
    /// every call is a safe no-op outside Play mode or when a clip is missing.
    /// Master volume stays on AudioListener.volume (FrontierUi.MasterVolume); category levels multiply on top.
    /// No AudioMixer or streaming is used, so the same path works in WebGL.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameAudio : MonoBehaviour
    {
        const int PoolSize = 16;
        const float PausedMusicDuck = 0.45f, PausedAmbienceDuck = 0.6f;
        const float TensionAfter = 6f, CalmAfter = 12f, TensionLockout = 45f;

        public static GameAudio Instance { get; private set; }
        static bool quitting;

        enum MusicMode { None, Title, Arrival, Gameplay, Tension }

        sealed class Voice { public AudioSource src; public CueDef def; public float started; }

        sealed class Track { public AudioSource src; public string id; public float env, target, rate; }

        sealed class LoopVoice
        {
            public CueDef def; public Object owner; public bool owned, stopping, gameplay;
            public AudioSource cur, next; public float env, envRate, gain = 1f, targetGain = 1f, overlap, xfade = -1f;
        }

        readonly CueGate gate = new CueGate();
        readonly System.Random rng = new System.Random(unchecked(System.Environment.TickCount * 397 + 11));
        readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, int> lastVariation = new Dictionary<string, int>();
        readonly HashSet<string> warned = new HashSet<string>();
        readonly List<LoopVoice> loops = new List<LoopVoice>();
        readonly float[] bus = new float[3];
        Voice[] pool;
        bool paused, unlocked;
        int claimedFrame = -1;
        float duck = 1f;

        // music
        readonly Track[] tracks = new Track[2];
        Track active;
        MusicMode mode;
        MusicSelector selector;
        bool resting, gameplayRequested, shortage;
        float restUntil, shortageChangedAt, tensionLockUntil = -999f;
        string pendingId; float pendingFade, pendingSince;

        // ---------- lifecycle ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; quitting = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() => Ensure();

        /// <summary>Returns the live manager, creating it on first use in Play mode.</summary>
        public static GameAudio Ensure()
        {
            if (Instance != null) return Instance;
            if (quitting || !Application.isPlaying) return null;
            new GameObject("Game Audio").AddComponent<GameAudio>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            pool = new Voice[PoolSize];
            for (int i = 0; i < PoolSize; i++) pool[i] = new Voice { src = NewSource("SFX " + i) };
            for (int i = 0; i < 2; i++) tracks[i] = new Track { src = NewSource("Music " + i) };
            for (int i = 0; i < 3; i++) bus[i] = AudioPrefs.Get((AudioBus)i);
            selector = new MusicSelector(MusicIds.Gameplay, rng.Next());
#if UNITY_WEBGL && !UNITY_EDITOR
            unlocked = false; // browsers keep audio suspended until a real click or key press
#else
            unlocked = true;
#endif
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Instance = null;
        }

        void OnApplicationQuit() => quitting = true;

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        void OnSceneChanged(Scene from, Scene to)
        {
            // Gameplay loops and beds belong to the scene that started them; music carries across on purpose.
            foreach (var l in loops) Stop(l, l.gameplay ? 0.2f : 1f);
            SetPausedInternal(false);
        }

        // ---------- static API used by game code ----------

        public static void Play(string id) => Ensure()?.PlayCue(id);
        public static void PlayDelayed(string id, float seconds) { var a = Ensure(); if (a != null) a.StartCoroutine(a.Delayed(id, seconds)); }

        /// <summary>Generic button press. Skipped when the button's own action already played a specific cue this frame.</summary>
        public static void UiPress() { var a = Ensure(); if (a != null && a.claimedFrame != Time.frameCount) a.PlayCue(Sfx.UiConfirm); }

        public static void StartLoop(string id, Object owner) => Ensure()?.StartLoopInternal(id, owner, true);
        public static void StopLoop(string id, Object owner) => Ensure()?.StopLoopInternal(id, owner);
        public static void SetAmbience(string id, float level) => Ensure()?.SetAmbienceInternal(id, level);
        public static void SetPaused(bool value) => Ensure()?.SetPausedInternal(value);

        public static float GetLevel(AudioBus b) => AudioPrefs.Get(b);
        public static void SetLevel(AudioBus b, float value)
        {
            AudioPrefs.Set(b, value);
            if (Instance != null) Instance.bus[(int)b] = AudioPrefs.Get(b);
        }

        /// <summary>Title screen: welcome movement, repeated with breathing space.</summary>
        public static void EnterTitle() => Ensure()?.EnterTitleInternal();

        /// <summary>Start pressed: whoosh (skipped with Reduce Motion) and the arrival cue, which carries across the scene load.</summary>
        public static void Launch(bool reducedMotion) => Ensure()?.LaunchInternal(reducedMotion);

        /// <summary>Outpost scene is up: start the surface wind bed.</summary>
        public static void EnterOutpost() => Ensure()?.SetAmbienceInternal(Sfx.AmbWind, 1f);

        /// <summary>Colony is ready: gameplay movements start once the arrival cue has finished.</summary>
        public static void BeginGameplayMusic() => Ensure()?.BeginGameplayInternal();

        /// <summary>Raw life-support state; hysteresis and lockout happen here so fluctuations never thrash the score.</summary>
        public static void SetShortage(bool value)
        {
            var a = Instance;
            if (a == null || a.shortage == value) return;
            a.shortage = value;
            a.shortageChangedAt = Time.unscaledTime;
        }

        // ---------- one-shots ----------

        IEnumerator Delayed(string id, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            PlayCue(id);
        }

        internal bool PlayCue(string id)
        {
            var def = AudioCatalog.Get(id);
            if (def == null) { WarnOnce(id, "unknown sound id"); return false; }
            if (def.Priority > 10) claimedFrame = Time.frameCount;
            if (!unlocked) return false;
            var set = Clips(def);
            if (set == null) return false;

            int playing = 0;
            foreach (var v in pool) if (v.def == def && v.src.isPlaying) playing++;
            if (!gate.Allow(def, Time.unscaledTime, playing)) return false;

            var voice = Acquire(def.Priority);
            if (voice == null) return false;
            voice.def = def;
            voice.started = Time.unscaledTime;
            voice.src.clip = set[PickVariation(def, set.Length)];
            voice.src.loop = false;
            voice.src.pitch = def.PitchJitter > 0f ? 1f + def.PitchJitter * (float)(rng.NextDouble() * 2.0 - 1.0) : 1f;
            voice.src.volume = def.Volume * bus[(int)def.Bus];
            voice.src.Play();
            return true;
        }

        Voice Acquire(int priority)
        {
            Voice steal = null;
            foreach (var v in pool)
            {
                if (v.def == null || !v.src.isPlaying) return v;
                if (v.def.Priority <= priority && (steal == null || v.def.Priority < steal.def.Priority ||
                    (v.def.Priority == steal.def.Priority && v.started < steal.started))) steal = v;
            }
            if (steal != null) steal.src.Stop();
            return steal;
        }

        int PickVariation(CueDef def, int count)
        {
            if (count == 1) return 0;
            lastVariation.TryGetValue(def.Id, out var last);
            int pick = rng.Next(count - 1);
            if (pick >= last) pick++;
            lastVariation[def.Id] = pick;
            return pick;
        }

        AudioClip[] Clips(CueDef def)
        {
            if (clips.TryGetValue(def.Id, out var cached)) return cached.Length > 0 ? cached : null;
            var found = new List<AudioClip>();
            for (int i = 0; i < def.Variations; i++)
            {
                var clip = Resources.Load<AudioClip>(def.ResourcePath(i));
                if (clip != null) found.Add(clip);
            }
            if (found.Count == 0) WarnOnce(def.Id, "no clips found under Resources/" + def.ResourcePath(0));
            clips[def.Id] = found.ToArray();
            return found.Count > 0 ? clips[def.Id] : null;
        }

        void WarnOnce(string id, string why)
        {
            if (warned.Add(id ?? "<null>")) Debug.LogWarning($"GameAudio: {id}: {why}");
        }

        // ---------- loops and ambience ----------

        void StartLoopInternal(string id, Object owner, bool gameplay)
        {
            var existing = FindLoop(id, owner);
            if (existing != null) { existing.stopping = false; existing.envRate = 1f / 0.15f; return; }
            var def = AudioCatalog.Get(id);
            var set = def != null ? Clips(def) : null;
            if (set == null) { if (def == null) WarnOnce(id, "unknown sound id"); return; }
            var clip = set[0];
            var l = new LoopVoice
            {
                def = def, owner = owner, owned = owner != null, gameplay = gameplay,
                cur = NewSource("Loop " + id), next = NewSource("Loop " + id + " (overlap)"),
                env = 0f, envRate = 1f / (gameplay ? 0.15f : 3f),
                overlap = Mathf.Min(gameplay ? 0.35f : 2.5f, clip.length * 0.25f),
            };
            l.cur.clip = l.next.clip = clip;
            l.cur.volume = 0f;
            if (paused && gameplay) { l.cur.Play(); l.cur.Pause(); }
            else l.cur.Play();
            loops.Add(l);
        }

        void StopLoopInternal(string id, Object owner)
        {
            var l = FindLoop(id, owner);
            if (l != null) Stop(l, l.gameplay ? 0.25f : 1.5f);
        }

        void SetAmbienceInternal(string id, float level)
        {
            var l = FindLoop(id, null);
            if (l == null)
            {
                if (level <= 0.001f) return;
                StartLoopInternal(id, null, false);
                l = FindLoop(id, null);
                if (l == null) return;
                l.gain = 0f;
            }
            l.stopping = false;
            l.envRate = 1f / 3f;
            l.targetGain = Mathf.Clamp01(level);
        }

        LoopVoice FindLoop(string id, Object owner)
        {
            foreach (var l in loops)
                if (l.def.Id == id && l.owner == owner && (owner != null || !l.owned)) return l;
            return null;
        }

        static void Stop(LoopVoice l, float seconds)
        {
            l.stopping = true;
            l.envRate = 1f / Mathf.Max(0.01f, seconds);
        }

        void UpdateLoops(float dt)
        {
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                var l = loops[i];
                if (l.owned && l.owner == null) Stop(l, 0.2f); // owner destroyed (scene change, crew removed)
                if (paused && l.gameplay) continue;
                l.env = Mathf.MoveTowards(l.env, l.stopping ? 0f : 1f, l.envRate * dt);
                l.gain = Mathf.MoveTowards(l.gain, l.targetGain, dt / 2f);
                if (l.stopping && l.env <= 0f)
                {
                    Destroy(l.cur.gameObject);
                    Destroy(l.next.gameObject);
                    loops.RemoveAt(i);
                    continue;
                }

                // Two-source crossfade instead of AudioSource.loop: seamless even when a codec pads the clip ends (WebGL AAC).
                float len = l.cur.clip.length;
                if (l.xfade < 0f && l.cur.time >= len - l.overlap)
                {
                    l.next.time = 0f;
                    l.next.Play();
                    l.xfade = 0f;
                }
                float a = 1f, b = 0f;
                if (l.xfade >= 0f)
                {
                    l.xfade += dt / l.overlap;
                    float p = Mathf.Clamp01(l.xfade);
                    a = Mathf.Cos(p * Mathf.PI * 0.5f);
                    b = Mathf.Sin(p * Mathf.PI * 0.5f);
                    if (p >= 1f || !l.cur.isPlaying)
                    {
                        l.cur.Stop();
                        (l.cur, l.next) = (l.next, l.cur);
                        l.xfade = -1f;
                        a = 1f; b = 0f;
                    }
                }
                float duckAmount = !l.gameplay && paused ? PausedAmbienceDuck : 1f;
                float vol = l.def.Volume * bus[(int)l.def.Bus] * l.env * l.gain * duckAmount;
                l.cur.volume = vol * a;
                l.next.volume = vol * b;
            }
        }

        void SetPausedInternal(bool value)
        {
            if (paused == value) return;
            paused = value;
            foreach (var l in loops)
            {
                if (!l.gameplay) continue;
                if (value) { l.cur.Pause(); l.next.Pause(); }
                else { l.cur.UnPause(); if (l.xfade >= 0f) l.next.UnPause(); }
            }
        }

        // ---------- music ----------

        void EnterTitleInternal()
        {
            mode = MusicMode.Title;
            gameplayRequested = false;
            if (active != null && active.id == MusicIds.Title && active.target > 0f) return;
            resting = false;
            StartTrack(MusicIds.Title, 2f, 2f);
        }

        void LaunchInternal(bool reducedMotion)
        {
            if (!reducedMotion) PlayCue(Sfx.LaunchWhoosh);
            mode = MusicMode.Arrival;
            gameplayRequested = false;
            resting = false;
            StartTrack(MusicIds.Arrival, 0.8f, 1.2f);
        }

        void BeginGameplayInternal()
        {
            gameplayRequested = true;
            if (mode == MusicMode.Arrival && (active != null || pendingId != null)) return; // arrival finishes first
            mode = MusicMode.Gameplay;
            if (active == null && pendingId == null) Rest(2f, 4f);
        }

        void Rest(float min, float max)
        {
            resting = true;
            restUntil = Time.unscaledTime + selector.Range(min, max);
        }

        void StartTrack(string id, float fadeIn, float fadeOutPrevious)
        {
            FadeOutActive(fadeOutPrevious);
            pendingId = id;
            pendingFade = fadeIn;
            pendingSince = Time.unscaledTime;
            var clip = MusicClip(id);
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        void FadeOutActive(float seconds)
        {
            if (active == null) return;
            active.target = 0f;
            active.rate = 1f / Mathf.Max(0.05f, seconds);
            active = null;
        }

        AudioClip MusicClip(string id)
        {
            var def = AudioCatalog.Get(id);
            var set = def != null ? Clips(def) : null;
            return set?[0];
        }

        void UpdateMusic(float dt, float now)
        {
            duck = Mathf.MoveTowards(duck, paused ? PausedMusicDuck : 1f, dt / 0.5f);

            foreach (var t in tracks)
            {
                if (t.id == null) continue;
                t.env = Mathf.MoveTowards(t.env, t.target, t.rate * dt);
                var def = AudioCatalog.Get(t.id);
                t.src.volume = t.env * (def != null ? def.Volume : 1f) * bus[(int)AudioBus.Music] * duck;
                bool faded = t.target <= 0f && t.env <= 0f;
                if (faded || (t != active && !t.src.isPlaying)) Release(t);
            }

            if (!unlocked) return;

            if (pendingId != null) TryBeginPending(now);

            if (active != null && !active.src.isPlaying)
            {
                // the movement reached its natural end
                var ended = active;
                active = null;
                Release(ended);
                OnTrackEnded();
            }

            if (resting && now >= restUntil && pendingId == null && active == null)
            {
                resting = false;
                if (mode == MusicMode.Title) StartTrack(MusicIds.Title, 3f, 1f);
                else if (mode == MusicMode.Tension) StartTrack(MusicIds.Tension, 3f, 1f);
                else if (mode == MusicMode.Gameplay) StartTrack(selector.Next(), 1.5f, 1f);
            }

            float sinceChange = now - shortageChangedAt;
            if (mode == MusicMode.Gameplay && shortage && sinceChange >= TensionAfter && now >= tensionLockUntil)
            {
                mode = MusicMode.Tension;
                resting = false;
                StartTrack(MusicIds.Tension, 4f, 4f);
            }
            else if (mode == MusicMode.Tension && !shortage && sinceChange >= CalmAfter)
            {
                mode = MusicMode.Gameplay;
                tensionLockUntil = now + TensionLockout;
                pendingId = null;
                FadeOutActive(6f);
                Rest(10f, 14f);
            }
        }

        void TryBeginPending(float now)
        {
            var clip = MusicClip(pendingId);
            if (clip == null) { pendingId = null; OnTrackEnded(); return; }
            // StartTrack asked for a background load. Give it a moment, then Play() anyway: Unity loads
            // non-preloaded clips on demand, and the WebGL player never advances loadState from LoadAudioData.
            if (clip.loadState != AudioDataLoadState.Loaded && clip.loadState != AudioDataLoadState.Failed &&
                now - pendingSince < 3f) return;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                WarnOnce(pendingId, "music data failed to load");
                pendingId = null;
                OnTrackEnded();
                return;
            }
            var t = tracks[0].id == null ? tracks[0] : tracks[1].id == null ? tracks[1] : null;
            if (t == null) // both busy: drop the quieter one
            {
                t = tracks[0].env <= tracks[1].env ? tracks[0] : tracks[1];
                if (t == active) active = null;
                Release(t);
            }
            t.id = pendingId;
            t.src.clip = clip;
            t.src.loop = false;
            t.src.time = 0f;
            t.env = 0f;
            t.target = 1f;
            t.rate = 1f / Mathf.Max(0.05f, pendingFade);
            t.src.volume = 0f;
            t.src.Play();
            active = t;
            pendingId = null;
        }

        void Release(Track t)
        {
            t.src.Stop();
            var clip = t.src.clip;
            t.src.clip = null;
            t.id = null;
            t.env = t.target = 0f;
            if (clip != null && (active == null || active.src.clip != clip) && clip.preloadAudioData == false)
                clip.UnloadAudioData(); // keeps WebGL memory to the movements actually playing
        }

        void OnTrackEnded()
        {
            switch (mode)
            {
                case MusicMode.Title: Rest(6f, 12f); break;
                case MusicMode.Arrival:
                    if (gameplayRequested) { mode = MusicMode.Gameplay; Rest(3f, 6f); }
                    else mode = MusicMode.None; // still landing; BeginGameplayMusic will continue
                    break;
                case MusicMode.Gameplay: resting = true; restUntil = Time.unscaledTime + selector.RestSeconds(); break;
                case MusicMode.Tension: Rest(3f, 6f); break;
            }
        }

        // ---------- frame ----------

        void Update()
        {
            if (!unlocked && (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.touchCount > 0))
                unlocked = true;
            float dt = Time.unscaledDeltaTime;
            UpdateLoops(dt);
            UpdateMusic(dt, Time.unscaledTime);
        }

        // ---------- inspection for tests and diagnostics ----------

        internal string CurrentMusic => active != null ? active.id : pendingId;
        internal string MusicModeName => mode.ToString();
        internal bool IsResting => resting;
        internal int LoopCount => loops.Count;
        internal bool IsPaused => paused;
        internal int VoicesPlaying(string id)
        {
            int n = 0;
            foreach (var v in pool) if (v.def != null && v.def.Id == id && v.src.isPlaying) n++;
            return n;
        }
        internal void InjectClips(string id, AudioClip[] set) => clips[id] = set;
        internal void ForceUnlock() => unlocked = true;
    }
}
