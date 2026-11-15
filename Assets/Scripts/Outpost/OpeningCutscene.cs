using System.Collections.Generic;
using System.Text;
using RedFrontier.Audio;
using RedFrontier.Mars;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// The 15 s opening, played on the live outpost before the player gets control:
    /// 0-3.5 s the rocket shakes down with alarms going, 3.5-6.5 s black then waking in the wreckage,
    /// 6.6-12.7 s Marlonis on the damaged radio gives the mission (survive the sols, solar stops at night),
    /// 12.9-15 s the camera pulls back to the outpost. Any key or click skips it (after a moment); holding Esc also works.
    /// The original 30 s version is kept in Archive/OpeningCutsceneLegacy.cs (Colony.useLegacyOpening).
    /// There is no cutscene art yet, so it is built from the scene, the rocket and crew sprites, flat pixel shapes and
    /// synthesized sound. This object stays behind afterwards as the smouldering crash site.
    /// </summary>
    internal sealed class OpeningCutscene : MonoBehaviour
    {
        const float Duration = 17f, EngineOut = 1.3f, Touch = 1.9f, Break = 2.35f, Rest = 4.4f, Impact = 5f, Wake = 5.9f, Stand = 7.4f;
        const float RadioOn = 8.1f, PageTwo = 11.2f, RadioOff = 14.2f, RecorderOn = 999f, PullBack = 14.4f, Release = 15.9f; // RecorderOn: beat cut from the short version
        // The wreck sheet (Resources/Buildings/RocketWreck): intact, nose half, tail half, both halves at rest.
        // Frames are 192x128, baked from the rocket art turned so the nose points down the lattice to the bottom left.
        const int WreckW = 192, WreckH = 128;
        static readonly Vector2 WreckPivotPx = new Vector2(96, 48), WreckDoorPx = new Vector2(0, 20), WreckFootPx = new Vector2(8, -12),
            NoseTipPx = new Vector2(-44, -6), TailPx = new Vector2(40, 30);
        static readonly Vector3 Heading = new Vector3(-.894f, -.447f, 0f); // nose direction, along the lattice
        const float SlideLength = 14f, HalfGapPx = 5f, TailLag = 5f;
        const float SkipSeconds = 1f, Bars = .09f, Ppu = 16f, CharsPerSecond = 30f;
        const int South = 0, SouthEast = 1, SouthWest = 7, MaxPuffs = 260, Rate = 22050;

        struct Cue
        {
            public readonly float at;
            public readonly string text;
            public Cue(float at, string text) { this.at = at; this.text = text; }
        }

        static readonly Cue[][] RadioLines =
        {
            new[] { new Cue(8.3f, "You're alive. We crashed on Mars."), new Cue(9.6f, " I'm Marlonis. Earth can't hear us.") },
            new[] { new Cue(11.3f, "Survive " + MissionPlan.DefaultSols + " sols. Solar dies at night,"), new Cue(12.6f, " so build batteries and a Gym.") },
        };
        static readonly Cue[] RecorderLines = { new Cue(26.6f, "\"Check the communications log.\"") };

        // Eyelid openness keyframes (time, 0 shut .. 1 open): two heavy blinks before the eyes stay open.
        static readonly Vector2[] Blink =
        {
            new Vector2(5.9f, 0f), new Vector2(6.35f, .35f), new Vector2(6.55f, .05f),
            new Vector2(7f, .7f), new Vector2(7.1f, .45f), new Vector2(7.7f, 1f)
        };

        // Hull colours picked from Rocket.png so the debris reads as pieces of it.
        static readonly Color32[] Hull =
        {
            new Color32(70, 176, 196, 255), new Color32(204, 70, 78, 255),
            new Color32(156, 160, 176, 255), new Color32(236, 168, 64, 255)
        };
        static readonly Color32 Shade = new Color32(38, 26, 34, 170), LedOn = new Color32(255, 72, 64, 255),
            LedOff = new Color32(90, 30, 34, 255), Cyan = new Color32(131, 213, 235, 255);
        static readonly Vector2 PanelSize = new Vector2(414, 87), WarningSize = new Vector2(330, 70),
            PromptSize = new Vector2(188, 24), RecorderSize = new Vector2(62, 44);

        enum Kind { Smoke, Trail, Fire, Flame, Dust, Spark }

        sealed class Puff
        {
            public SpriteRenderer sr;
            public Vector3 pos, vel;
            public float age, life, from, to;
            public Color start, end;
        }

        sealed class Emitter
        {
            public Vector3 at;
            public Kind kind;
            public float rate, acc, until = float.PositiveInfinity;
        }

        public bool Done { get; private set; }
        /// <summary>Where the player wakes up; the crew takes the astronaut over from here.</summary>
        public Vector3 PlayerPosition { get; private set; }

        Building rocket;
        MarsMapGenerator map;
        Camera cam;
        MarsEnvironmentController env;
        PixelPerfectCamera ppc;
        Vector3 home, touchdown, basePlane, playerPlane, recorderPlane, shotA, shotB, contact, entryFrom, gouged;
        Sprite[] wreckSprites;
        SpriteRenderer intact, noseHalf, tailHalf;
        float dustAcc, sparkAcc;
        bool settled;
        Sprite[] facings;
        SpriteRenderer astronaut, led;
        float t, last = -1f, hold, coverFade = -1f, trailAcc, flameAcc, noiseAt;
        bool panAllowed = true, wrecked;
        int radioShown, recorderShown;
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly List<Puff> puffs = new List<Puff>();
        static Sprite cornerPixel, centrePixel;

        Canvas canvas;
        RectTransform barTop, barBottom, lidTop, lidBottom, warning, radio, recorder, skipFill;
        Image black, flash, vision, recorderLight;
        RawImage alarm, portrait, noise;
        Texture2D noiseTexture, vignetteTexture;
        Color32[] noisePixels;
        readonly StringBuilder typed = new StringBuilder();
        readonly List<UnityEngine.Object> siteAssets = new List<UnityEngine.Object>(); // debris/scorch textures and sprites, freed with the crash site
        int ppcRefX, ppcRefY;
        bool ppcSaved;
        // After the opening, the crash site smokes for this long before its emitters stop and the component idles.
        const float SmoulderSeconds = 60f;
        PixelLabel warningTitle, warningStatus, warningAltitude;
        Text radioText, recorderText;
        CanvasGroup recorderGroup, skipGroup;

        AudioSource alarmSource, bed, sfx;
        AudioClip siren, rumble, ring, hiss, burst, click, crunch;
        AudioClip[] marlonisVoice, playerVoice;

        /// <summary>Starts the opening on a freshly spawned rocket. Wait for <see cref="Done"/> before handing over control.</summary>
        internal static OpeningCutscene Begin(Colony colony, Building rocket)
        {
            var site = new GameObject("Crash site");
            site.transform.SetParent(colony.transform, false);
            var cutscene = site.AddComponent<OpeningCutscene>();
            cutscene.Init(colony, rocket);
            return cutscene;
        }

        void Init(Colony colony, Building landing)
        {
            rocket = landing;
            map = colony.map;
            cam = Camera.main;
            env = FindObjectOfType<MarsEnvironmentController>();
            ppc = cam != null ? cam.GetComponent<PixelPerfectCamera>() : null;
            if (cam != null) home = cam.transform.position;

            touchdown = rocket.transform.position;
            basePlane = new Vector3(touchdown.x, rocket.groundY, 0f);
            // The astronaut was thrown clear in front of the wreck.
            playerPlane = basePlane + new Vector3(-1.4f, -3f, 0f);
            recorderPlane = playerPlane + new Vector3(1.25f, -.65f, 0f);
            PlayerPosition = Ground(playerPlane);
            // Ground contact up-right of the resting place; the approach comes in on a straight line from off screen.
            contact = touchdown - Heading * SlideLength;
            entryFrom = contact - Heading * 24f + Vector3.up * 14f;
            gouged = contact;
            shotA = touchdown + new Vector3(6f, 7f, 0f);
            shotB = PlayerPosition + new Vector3(1.7f, 3f, 0f);

            if (env != null)
            {
                panAllowed = env.allowPan;
                env.allowPan = false;
            }
            else if (ppc != null)
            {
                // Without the environment controller nothing else restores the zoom, so Finish puts these back.
                ppcRefX = ppc.refResolutionX;
                ppcRefY = ppc.refResolutionY;
                ppcSaved = true;
            }
            if (Crew.Picked.Length > 0) facings = Crew.Facings(Crew.Picked[0]);
            rocket.InFlight = true;
            DebrisClearing.Reset();

            BuildWreck();
            BuildUi();
            BuildSounds();
            Loop(alarmSource, siren, .3f);
            Loop(bed, rumble, .3f);
            Direct(0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!Done)
            {
                hold = Input.GetKey(KeyCode.Escape) ? hold + dt : Mathf.Max(0f, hold - 3f * dt);
                bool tap = t > .6f && Input.anyKeyDown && !Input.GetKey(KeyCode.Escape); // any key or click skips; Esc still works by holding
                if (hold >= SkipSeconds || tap) Finish(true);
                else
                {
                    last = t;
                    t += dt;
                    if (t >= Duration) Finish(false);
                    else Direct(dt);
                }
            }
            else if (coverFade >= 0f)
            {
                coverFade += dt;
                black.color = new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, coverFade / .3f));
                if (coverFade >= .3f)
                {
                    DestroyCanvas();
                    coverFade = -1f;
                }
            }
            Smoulder(Time.deltaTime);
            if (Done && coverFade < 0f && Burnt()) enabled = false; // nothing left to simulate
        }

        /// <summary>True once every emitter has stopped and every puff has faded out.</summary>
        bool Burnt()
        {
            foreach (var e in emitters)
                if (Time.time < e.until) return false;
            foreach (var p in puffs)
                if (p.sr != null && p.sr.enabled) return false;
            return true;
        }

        bool Crossed(float moment) => last < moment && t >= moment;
        float Pulse(float hz) => .5f + .5f * Mathf.Sin(t * hz * 2f * Mathf.PI);

        void Direct(float dt)
        {
            bool calm = FrontierUi.ReduceMotion;

            // 0-5 s: in on a straight line, nose-first into the ground, slide, break in two, rest; then a hard cut.
            bool crashing = t < Impact;
            warning.gameObject.SetActive(t < Touch && t > .25f);
            alarm.color = new Color(1f, .16f, .12f, crashing ? (calm ? .1f : .26f) * (.4f + .6f * Pulse(1.25f)) * (t < Rest ? 1f : .5f) : 0f);
            if (!calm && Crossed(EngineOut)) GameAudio.Play(Sfx.RocketDescent);
            if (Crossed(Touch)) Touchdown();
            if (Crossed(Break)) BreakApart();
            if (Crossed(Rest)) Settle();
            if (crashing) CrashIn(calm, dt);
            if (Crossed(Impact)) CutToBlack();
            if (Crossed(Impact + .12f)) WakeShot();
            flash.color = new Color(1f, 1f, 1f, !calm && t >= Touch && t < Touch + .07f ? .85f : 0f);
            black.color = new Color(0f, 0f, 0f, t >= Impact && t < Wake ? 1f : 0f);

            // 3.5-6.5 s: the eyes open on the wreckage and the astronaut gets up.
            float open = Openness();
            bool lids = t >= Wake && open < 1f;
            lidTop.gameObject.SetActive(lids);
            lidBottom.gameObject.SetActive(lids);
            Edge(lidTop, .5f * (1f - open) + .002f, true);
            Edge(lidBottom, .5f * (1f - open) + .002f, false);
            vision.color = new Color(.25f, .05f, .06f, t >= Wake ? .65f * (1f - Mathf.InverseLerp(Wake, 7.8f, t)) : 0f);
            Pose(calm);

            // 6.6-12.7 s: Marlonis on the damaged radio, then back out to the outpost (the recorder beat is cut).
            Radio(calm);
            Recorder();
            if (t >= PullBack)
                Aim(Vector3.Lerp(shotB, home, calm ? 1f : Mathf.SmoothStep(0f, 1f, (t - PullBack) / 1.8f)), 1);

            float bars = Bars * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Release, Release + .7f, t)));
            Edge(barTop, bars, true);
            Edge(barBottom, bars, false);
            skipGroup.alpha = bars / Bars;
            skipFill.anchorMax = new Vector2(Mathf.Clamp01(hold / SkipSeconds), 1f);
        }

        void CrashIn(bool calm, float dt)
        {
            // Straight approach, accelerating; then a decelerating slide along the same line.
            Vector3 at;
            float slide = 0f;
            if (t < Touch) at = Vector3.Lerp(entryFrom, contact, Mathf.Pow(t / Touch, 1.25f));
            else
            {
                slide = Mathf.Clamp01((t - Touch) / (Rest - Touch));
                at = Vector3.Lerp(contact, touchdown, 1f - Mathf.Pow(1f - slide, 2f));
            }

            if (!settled)
            {
                // The ship skips twice off the ground before it digs in and ploughs; the tail bounces a beat later.
                var bounce = Vector3.up * Hop(t - Touch);
                if (t < Break) intact.transform.position = Snap(at + bounce);
                else
                {
                    // The tail half is torn loose and lags behind the nose as it digs in, then slides up into its resting gap.
                    float open = calm ? 1f : Mathf.SmoothStep(0f, 1f, (t - Break) / .3f);
                    float lag = TailLag * 4f * slide * (1f - slide) * (1f - slide) * open;
                    noseHalf.transform.position = Snap(at + bounce - Heading * (HalfGapPx * (1f - open) / Ppu));
                    tailHalf.transform.position = Snap(at - Heading * (lag - HalfGapPx * (1f - open) / Ppu) + Vector3.up * Hop(t - Touch - .14f));
                }
            }

            var tail = at + (Vector3)(TailPx / Ppu);
            if (t < Touch)
            {
                // Thrusters sputter until they cut out; smoke streams off the tail all the way down.
                if (t < EngineOut && Pulse(3.1f) > .25f) Stream(ref flameAcc, 45f * dt, Kind.Flame, tail);
                Stream(ref trailAcc, 26f * dt, Kind.Trail, tail);
                bed.volume = .3f + .5f * t / Touch;

                warningTitle.gameObject.SetActive(calm || Pulse(1.25f) > .3f);
                warningStatus.Text = t < .5f ? "Hull breach" : t < .9f ? "Cabin pressure low" : t < EngineOut ? "Engine failure" : "Brace for impact";
                warningAltitude.Text = "Altitude " + Mathf.RoundToInt(Mathf.Pow(1f - t / Touch, 1.25f) * 180f) * 10 + " m";
            }
            else if (t < Rest)
            {
                // Scraping along nose-first: a dust wall and sparks off the nose, a gouge left in the ground.
                float speed = 1f - slide;
                var nose = Ground(at + (Vector3)(NoseTipPx / Ppu));
                Stream(ref dustAcc, 200f * speed * dt, Kind.Dust, nose);
                Stream(ref sparkAcc, 70f * speed * dt, Kind.Spark, nose);
                Stream(ref trailAcc, 14f * dt, Kind.Trail, tail);
                if (t >= Break) Stream(ref dustAcc, 60f * speed * dt, Kind.Dust, Ground(tailHalf.transform.position + (Vector3)(TailPx / Ppu)));
                Gouge(nose);
                bed.volume = .9f * speed;
            }

            // Camera holds the landing site; it shakes in, kicks hard on impact, then eases in on the wreck.
            float shake = calm ? 0f
                : t < Touch ? (1f + 1.5f * t / Touch) / Ppu
                : (7f * Mathf.Exp(-3f * (t - Touch)) + (t < Rest ? 2.5f * (1f - slide) : 0f)) / Ppu;
            var shot = Vector3.Lerp(shotA, touchdown + new Vector3(2f, 4.5f, 0f),
                calm ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Touch, Impact, t)));
            Aim(shot + (Vector3)(Random.insideUnitCircle * shake), 1);
        }

        /// <summary>Height of the ship's bounce, <paramref name="since"/> seconds after it first touches down: a big hop, then a small one.</summary>
        static float Hop(float since)
        {
            if (since <= 0f) return 0f;
            if (since < .45f) return 1.1f * 4f * (since / .45f) * (1f - since / .45f);
            since -= .45f;
            return since < .3f ? .35f * 4f * (since / .3f) * (1f - since / .3f) : 0f;
        }

        void Touchdown()
        {
            GameAudio.Play(Sfx.RocketTouchdown);
            var nose = Ground(contact + (Vector3)(NoseTipPx / Ppu));
            for (int i = 0; i < 70; i++) Emit(Kind.Dust, nose);
            for (int i = 0; i < 24; i++) Emit(Kind.Spark, nose);
        }

        void BreakApart()
        {
            if (settled) return;
            intact.enabled = false;
            noseHalf.enabled = tailHalf.enabled = true;
            sfx.PlayOneShot(crunch, .7f);
            var gap = intact.transform.position + (Vector3)(WreckDoorPx / Ppu);
            for (int i = 0; i < 18; i++) Emit(Kind.Spark, gap);
            for (int i = 0; i < 16; i++) Emit(Kind.Dust, gap);
        }

        /// <summary>The halves come to rest: from here the Rocket building itself shows the wreck.</summary>
        void Settle()
        {
            if (settled) return;
            settled = true;
            foreach (var piece in new[] { intact, noseHalf, tailHalf }) Destroy(piece.gameObject);
            rocket.transform.SetPositionAndRotation(touchdown, Quaternion.identity);
            rocket.InFlight = false;
            rocket.Renderer.enabled = true;
            rocket.ShowWreck(wreckSprites[3], WreckDoorPx, WreckFootPx);
            Gouge(Ground(touchdown + (Vector3)(NoseTipPx / Ppu)));
            ScatterWreckage();
        }

        void CutToBlack()
        {
            Settle();
            alarmSource.Stop();
            bed.Stop();
            sfx.PlayOneShot(ring, .5f);
        }

        void BuildWreck()
        {
            var sheet = Resources.Load<Texture2D>("Buildings/RocketWreck");
            wreckSprites = new Sprite[4];
            for (int i = 0; i < 4; i++)
                wreckSprites[i] = Sprite.Create(sheet, new Rect(i * WreckW, 0, WreckW, WreckH),
                    new Vector2(WreckPivotPx.x / WreckW, WreckPivotPx.y / WreckH), Ppu);
            rocket.Renderer.enabled = false; // the building shows again, as the wreck, once the pieces stop
            int order = MarsMapGenerator.SortingOrderFor(rocket.groundY);
            intact = Piece("Crashing rocket", wreckSprites[0], order, true);
            noseHalf = Piece("Nose half", wreckSprites[1], order, false);
            tailHalf = Piece("Tail half", wreckSprites[2], order, false);
            intact.transform.position = Snap(entryFrom);
        }

        SpriteRenderer Piece(string name, Sprite sprite, int order, bool shown)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.enabled = shown;
            return sr;
        }


        /// <summary>Extends the dark furrow the nose ploughs, from where it last reached to <paramref name="to"/>.</summary>
        void Gouge(Vector3 to)
        {
            var step = 2f / Ppu;
            for (float d = Vector3.Distance(gouged, to); d >= step; d -= step)
            {
                gouged = Vector3.MoveTowards(gouged, to, step);
                Pixel("Gouge", Snap(gouged) - new Vector3(4f, 1f, 0f) / Ppu, 8, 3, new Color32(70, 32, 30, 170), -998);
            }
        }

        void WakeShot()
        {
            Aim(shotB, 2);
            if (facings == null) return;
            astronaut = new GameObject("Waking astronaut").AddComponent<SpriteRenderer>();
            astronaut.transform.SetParent(transform, false);
            astronaut.transform.position = PlayerPosition;
            astronaut.sortingOrder = MarsMapGenerator.SortingOrderFor(PlayerPosition.y);
            Pose(FrontierUi.ReduceMotion);
        }

        float Openness()
        {
            if (t < Blink[0].x) return 0f;
            for (int i = 0; i < Blink.Length - 1; i++)
                if (t < Blink[i + 1].x)
                    return Mathf.Lerp(Blink[i].y, Blink[i + 1].y, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Blink[i].x, Blink[i + 1].x, t)));
            return 1f;
        }

        void Pose(bool calm)
        {
            if (astronaut == null) return;
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Stand, Stand + .4f, t));
            if (calm) rise = t >= Stand ? 1f : 0f;
            astronaut.transform.rotation = Quaternion.Euler(0f, 0f, 90f * (1f - rise)); // lying on their back, head to the left
            int facing = t < 7.8f ? South : t < 8.1f ? SouthWest : t < 8.4f ? SouthEast : t < RadioOff + .1f ? South : SouthEast;
            astronaut.sprite = facings[facing];
        }

        void Radio(bool calm)
        {
            bool on = t >= RadioOn && t < RadioOff + .25f;
            if (radio.gameObject.activeSelf != on) radio.gameObject.SetActive(on);
            if (Crossed(RadioOn))
            {
                Loop(bed, hiss, .16f);
                sfx.PlayOneShot(click, .7f);
            }
            if (Crossed(RadioOff)) sfx.PlayOneShot(burst, .8f);
            if (Crossed(RadioOff + .25f)) bed.Stop();
            if (!on) return;

            float open = calm ? 1f : Mathf.Clamp01((t - RadioOn) / .15f) * Mathf.Clamp01((RadioOff + .25f - t) / .1f);
            radio.localScale = new Vector3(1f, open, 1f);
            bool cutting = t >= RadioOff, glitch = cutting || Hash(Mathf.FloorToInt(t * 9f)) < .14f;
            portrait.color = new Color(1f, 1f, 1f, glitch ? .45f : .9f);
            var jitter = new Vector2(glitch && !calm ? (Hash(Mathf.FloorToInt(t * 40f)) < .5f ? -2f : 2f) : 0f, 0f);
            portrait.rectTransform.offsetMin = portrait.rectTransform.offsetMax = jitter;
            noise.color = new Color(1f, 1f, 1f, cutting ? .95f : glitch ? .55f : .15f);
            if (!cutting) bed.volume = glitch ? .3f : .16f;
            RefreshNoise();
            Speak(radioText, RadioLines[t < PageTwo ? 0 : 1], ref radioShown, marlonisVoice);
        }

        void Recorder()
        {
            bool blink = t >= RadioOff - .1f && Mathf.Repeat(t * 2f, 1f) < .5f;
            if (led != null) led.color = blink ? LedOn : LedOff;
            bool on = t >= RecorderOn;
            if (recorder.gameObject.activeSelf != on) recorder.gameObject.SetActive(on);
            if (Crossed(RecorderOn))
            {
                sfx.PlayOneShot(click, .8f);
                Loop(bed, hiss, .07f);
            }
            if (!on) return;

            recorderGroup.alpha = Mathf.Clamp01((t - RecorderOn) / .15f) * Mathf.Clamp01((Release + .6f - t) / .4f);
            recorderLight.color = blink ? LedOn : LedOff;
            bed.volume = .07f * recorderGroup.alpha;
            Speak(recorderText, RecorderLines, ref recorderShown, playerVoice);
        }

        /// <summary>Typewriter: shows each cue's characters from its start time, with a voice blip every other letter.</summary>
        void Speak(Text box, Cue[] cues, ref int shown, AudioClip[] voice)
        {
            typed.Clear();
            foreach (var cue in cues)
            {
                if (t < cue.at) break;
                int n = Mathf.Min(cue.text.Length, Mathf.FloorToInt((t - cue.at) * CharsPerSecond));
                typed.Append(cue.text, 0, n);
                if (n < cue.text.Length) break;
            }
            int count = typed.Length;
            if (count == shown) return;
            box.text = typed.ToString();
            if (count > shown && count % 2 == 0 && char.IsLetter(typed[count - 1]))
                sfx.PlayOneShot(voice[count / 2 % voice.Length], .45f);
            shown = count;
        }

        void Finish(bool skipped)
        {
            Done = true;
            Settle();
            if (astronaut != null) Destroy(astronaut.gameObject);
            if (led != null) led.color = LedOff;
            alarmSource.Stop();
            bed.Stop();
            sfx.Stop();
            if (env != null)
            {
                env.ConfigureCamera(); // restores the pixel-perfect zoom
                env.allowPan = panAllowed;
            }
            else if (ppcSaved && ppc != null)
            {
                ppc.refResolutionX = ppcRefX;
                ppc.refResolutionY = ppcRefY;
            }
            if (cam != null) cam.transform.position = home;
            foreach (var e in emitters)
            {
                if (e.kind == Kind.Fire) e.until = Time.time + 8f;
                else
                {
                    e.rate *= .5f;
                    e.until = Mathf.Min(e.until, Time.time + SmoulderSeconds);
                }
            }
            ReleaseSounds(); // every source is stopped and no further cues play

            if (!skipped)
            {
                DestroyCanvas();
                return;
            }
            // A skip cuts through black, like a scene change.
            foreach (Transform child in canvas.transform) child.gameObject.SetActive(child == black.transform);
            black.color = Color.black;
            coverFade = 0f;
        }

        void DestroyCanvas()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            if (noiseTexture != null) Destroy(noiseTexture);
            if (vignetteTexture != null) Destroy(vignetteTexture);
            noiseTexture = vignetteTexture = null;
        }

        void ReleaseSounds()
        {
            foreach (var source in new[] { alarmSource, bed, sfx })
                if (source != null)
                {
                    source.Stop();
                    source.clip = null;
                }
            foreach (var clip in new[] { siren, rumble, ring, hiss, burst, click, crunch })
                if (clip != null) Destroy(clip);
            siren = rumble = ring = hiss = burst = click = crunch = null;
            foreach (var voice in new[] { marlonisVoice, playerVoice })
                if (voice != null)
                    foreach (var clip in voice)
                        if (clip != null) Destroy(clip);
            marlonisVoice = playerVoice = null;
        }

        void OnDestroy()
        {
            DestroyCanvas();
            ReleaseSounds();
            foreach (var asset in siteAssets)
                if (asset != null) Destroy(asset);
            siteAssets.Clear();
        }

        void Aim(Vector3 at, int zoom)
        {
            if (cam == null) return;
            if (ppc != null)
            {
                var reference = env != null ? env.referenceResolution : new Vector2Int(960, 540);
                ppc.refResolutionX = reference.x / zoom;
                ppc.refResolutionY = reference.y / zoom;
            }
            cam.transform.position = new Vector3(Mathf.Round(at.x * Ppu) / Ppu, Mathf.Round(at.y * Ppu) / Ppu, -10f);
        }

        // ---- the crash site ----

        void ScatterWreckage()
        {
            if (wrecked) return;
            wrecked = true;
            var scorch = Pixel("Scorch", Ground(basePlane), 1, 1, Color.white, -999, true);
            scorch.sprite = Track(ScorchSprite());
            scorch.transform.localScale = Vector3.one;

            // Hull plates and two torn-off fins thrown around the rocket, some still burning, plus small bits.
            var rng = new System.Random(7);
            for (int i = 0; i < 26; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0), reach = 2.2f + (float)rng.NextDouble() * 4.6f;
                var plane = basePlane + new Vector3(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach * .55f, 0f);
                var sprite = Track(Chunk(rng, i < 2, i < 14));
                if (Vector2.Distance(plane, playerPlane) < 1.4f || Vector2.Distance(plane, recorderPlane) < .9f) continue;
                var piece = Debris(plane, sprite, i < 14);
                var top = Ground(plane) + Vector3.up * (sprite.rect.height * .6f) / Ppu;
                if (i % 4 == 0 || i % 4 == 1)
                {
                    var burning = new Emitter { at = top, kind = i % 4 == 0 ? Kind.Smoke : Kind.Fire, rate = i % 4 == 0 ? 2f : 16f };
                    emitters.Add(burning);
                    piece.onClear = () => burning.until = 0f; // clearing a piece puts out its fire
                }
            }

            // The torn break smokes and burns, and so does the tail.
            var gap = touchdown + (Vector3)(WreckDoorPx / Ppu);
            emitters.Add(new Emitter { at = gap, kind = Kind.Smoke, rate = 5f });
            emitters.Add(new Emitter { at = gap + new Vector3(0f, -.3f, 0f), kind = Kind.Fire, rate = 22f });
            emitters.Add(new Emitter { at = touchdown + (Vector3)(TailPx / Ppu), kind = Kind.Fire, rate = 12f });
            emitters.Add(new Emitter { at = touchdown + (Vector3)(TailPx / Ppu) + new Vector3(0f, .3f, 0f), kind = Kind.Smoke, rate = 4f });

            // The recorder that plays the player's voice: a little box with a playback light.
            var at = Ground(recorderPlane) - new Vector3(3f / Ppu, 0f, 0f);
            int order = MarsMapGenerator.SortingOrderFor(recorderPlane.y);
            Pixel("Recorder shadow", at - new Vector3(1f / Ppu, 0f, 0f), 8, 1, Shade, order);
            Pixel("Recorder", at + Vector3.up / Ppu, 6, 3, new Color32(58, 56, 68, 255), order + 1);
            Pixel("Recorder top", at + Vector3.up * 4f / Ppu, 6, 1, new Color32(112, 110, 124, 255), order + 1);
            led = Pixel("Recorder light", at + new Vector3(4f, 4f, 0f) / Ppu, 1, 1, LedOff, order + 2);
        }

        /// <summary>Records a generated sprite and its texture so OnDestroy can free them.</summary>
        Sprite Track(Sprite sprite)
        {
            siteAssets.Add(sprite);
            siteAssets.Add(sprite.texture);
            return sprite;
        }

        DebrisPiece Debris(Vector3 plane, Sprite sprite, bool required)
        {
            int order = MarsMapGenerator.SortingOrderFor(plane.y), w = (int)sprite.rect.width;
            var at = Ground(plane);
            var shadow = Pixel("Debris shadow", at - new Vector3(Mathf.Floor(w / 2f + 1f) / Ppu, 0f, 0f), w + 2, 1, Shade, order);
            var piece = Pixel("Debris", at + Vector3.up / Ppu, 1, 1, Color.white, order + 1);
            piece.sprite = sprite;
            piece.transform.localScale = Vector3.one;
            var record = new DebrisPiece { sprite = piece, shadow = shadow, at = at, required = required };
            DebrisClearing.Register(record); // the first tutorial mission is to clear these
            return record;
        }

        /// <summary>A jagged, outlined piece of hull (or a torn fin) in the rocket's colours, pivot at its bottom centre.</summary>
        static Sprite Chunk(System.Random rng, bool fin, bool large)
        {
            int w = fin ? 6 + rng.Next(3) : large ? 7 + rng.Next(8) : 2 + rng.Next(3);
            int h = fin ? 7 + rng.Next(3) : large ? 3 + rng.Next(4) : 1 + rng.Next(2);
            Color32 color = fin ? Hull[1] : Hull[rng.Next(Hull.Length)], stripe = Hull[rng.Next(Hull.Length)];
            Color32 ink = new Color32(34, 22, 30, 255), light = Color32.Lerp(color, new Color32(255, 255, 255, 255), .35f),
                dark = Color32.Lerp(color, ink, .45f);
            var top = new int[w];
            for (int x = 0; x < w; x++)
                top[x] = fin ? Mathf.CeilToInt(h * (1f - x / (float)w)) : h - (large && rng.Next(3) == 0 ? 1 : 0);
            int band = large && rng.Next(2) == 0 ? rng.Next(w) : -1;
            bool Inside(int x, int y) => x >= 0 && x < w && y >= 0 && y < top[x];

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Inside(x, y)) continue;
                    bool edge = large || fin ? !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y + 1) || y == 0 : false;
                    px[y * w + x] = edge ? (y == 0 ? ink : dark) : !Inside(x, y + 2) ? light : Mathf.Abs(x - band) <= 1 ? stripe : color;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Debris" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, 0f), Ppu);
        }

        Vector3 Ground(Vector3 plane)
        {
            var lattice = map.WorldToLattice(plane);
            return Snap(map.SurfaceToWorld(lattice.x, lattice.y));
        }

        void Stream(ref float acc, float amount, Kind kind, Vector3 at)
        {
            for (acc += amount; acc >= 1f; acc--) Emit(kind, at);
        }

        void Emit(Kind kind, Vector3 at)
        {
            Puff p = null;
            foreach (var free in puffs)
                if (!free.sr.enabled) { p = free; break; }
            if (p == null)
            {
                if (puffs.Count >= MaxPuffs) return;
                p = new Puff { sr = Pixel("Puff", at, 1, 1, Color.clear, 0, true) };
                puffs.Add(p);
            }

            p.age = 0f;
            p.pos = at + new Vector3(Random.Range(-2, 3), Random.Range(-1, 2), 0f) / Ppu;
            switch (kind)
            {
                case Kind.Smoke:
                    Set(p, Random.Range(3f, 4.4f), new Vector3(Random.Range(-.15f, .4f), Random.Range(.7f, 1.1f), 0f), 3, 10,
                        new Color32(52, 44, 52, 230), new Color32(104, 86, 90, 0), 20000);
                    break;
                case Kind.Trail:
                    Set(p, Random.Range(1.2f, 1.8f), new Vector3(Random.Range(-.2f, .2f), Random.Range(.2f, .5f), 0f), 3, 7,
                        new Color32(64, 56, 64, 215), new Color32(110, 92, 96, 0), rocket.Renderer.sortingOrder - 1);
                    break;
                case Kind.Fire:
                    Set(p, Random.Range(.35f, .7f), new Vector3(Random.Range(-.2f, .2f), Random.Range(.7f, 1.3f), 0f), 4, 1,
                        new Color32(255, 230, 120, 255), new Color32(226, 70, 40, 60), 20001);
                    break;
                case Kind.Dust:
                    // Kicked-up regolith: rolls outwards low and wide.
                    Set(p, Random.Range(.8f, 1.6f), new Vector3(Random.Range(-2.2f, 1.6f), Random.Range(.2f, 1.4f), 0f), 5, 16,
                        new Color32(232, 178, 128, 235), new Color32(214, 150, 108, 0), 20000);
                    break;
                case Kind.Spark:
                    Set(p, Random.Range(.18f, .4f), new Vector3(Random.Range(-1f, 5f), Random.Range(1f, 5f), 0f), 2, 1,
                        new Color32(255, 244, 170, 255), new Color32(255, 120, 40, 0), 20002);
                    break;
                default:
                    Set(p, Random.Range(.15f, .28f), new Vector3(Random.Range(-.5f, .5f), Random.Range(-6f, -4f), 0f), 3, 1,
                        new Color32(255, 240, 160, 255), new Color32(240, 110, 40, 0), 20001);
                    break;
            }
            p.sr.enabled = true;
        }

        static void Set(Puff p, float life, Vector3 vel, float from, float to, Color start, Color end, int order)
        {
            p.life = life;
            p.vel = vel;
            p.from = from;
            p.to = to;
            p.start = start;
            p.end = end;
            p.sr.sortingOrder = order;
        }

        void Smoulder(float dt)
        {
            foreach (var e in emitters)
                if (Time.time < e.until)
                    Stream(ref e.acc, e.rate * dt, e.kind, e.at);
            foreach (var p in puffs)
            {
                if (!p.sr.enabled) continue;
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.sr.enabled = false;
                    continue;
                }
                float k = p.age / p.life, size = Mathf.Round(Mathf.Lerp(p.from, p.to, k));
                p.pos += p.vel * dt;
                p.sr.transform.position = Snap(p.pos);
                p.sr.transform.localScale = new Vector3(size, size, 1f);
                p.sr.color = Color.Lerp(p.start, p.end, k);
            }
        }

        SpriteRenderer Pixel(string name, Vector3 at, int w, int h, Color color, int order, bool centred = false)
        {
            if (cornerPixel == null)
            {
                var white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Cutscene pixel" };
                white.SetPixel(0, 0, Color.white);
                white.Apply(false, true);
                cornerPixel = Sprite.Create(white, new Rect(0, 0, 1, 1), Vector2.zero, Ppu);
                centrePixel = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), Ppu);
            }
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = centred ? centrePixel : cornerPixel;
            sr.transform.position = at;
            sr.transform.localScale = new Vector3(w, h, 1f);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        static Sprite ScorchSprite()
        {
            const int w = 104, h = 32;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + .5f) / w * 2f - 1f, dy = (y + .5f) / h * 2f - 1f;
                    float a = Mathf.Ceil(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)) * 3f) / 3f; // three hard pixel rings
                    px[y * w + x] = new Color32(30, 18, 22, (byte)(a * 150));
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Scorch" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), Ppu);
        }

        static Vector3 Snap(Vector3 p) => new Vector3(Mathf.Round(p.x * Ppu) / Ppu, Mathf.Round(p.y * Ppu) / Ppu, p.z);
        static float Sqr(float x) => x * x;
        static float Hash(int n) => Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.547f, 1f);

        // ---- screen overlays ----

        void BuildUi()
        {
            var root = new GameObject("Opening Cutscene Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // under MissionArrival's scene cover
            FrontierUi.Resize((RectTransform)root.transform, new Vector2(960, 540));
            var ui = root.transform;

            vignetteTexture = Vignette();
            alarm = FrontierUi.Art("Alarm light", ui, vignetteTexture);
            vision = FrontierUi.Fill("Blurred vision", ui, Color.clear);
            lidTop = FrontierUi.Fill("Eyelid", ui, Color.black).rectTransform;
            lidBottom = FrontierUi.Fill("Eyelid", ui, Color.black).rectTransform;
            black = FrontierUi.Fill("Black", ui, Color.clear);
            flash = FrontierUi.Fill("Impact flash", ui, Color.clear);

            warning = Anchored("Alarm readout", ui, new Vector2(.5f, 1f - Bars), new Vector2(.5f, 1f), new Vector2(0f, -10f), WarningSize);
            PixelArt.Sliced("Frame", warning, PixelArt.Frame(FrontierUi.Shadow, FrontierUi.Coral, FrontierUi.Maroon, FrontierUi.Shadow));
            warningTitle = Label(warning, WarningSize, "!! Warning !!", 3, FrontierUi.Coral, 10, 6, 310, 27);
            warningStatus = Label(warning, WarningSize, "", 2, FrontierUi.Cream, 10, 35, 310, 18);
            warningAltitude = Label(warning, WarningSize, "", 1, FrontierUi.CreamDark, 10, 55, 310, 9);
            warning.gameObject.SetActive(false);

            // Marlonis, styled like the mission guide but breaking up.
            radio = Anchored("Damaged radio", ui, new Vector2(.5f, Bars), new Vector2(.5f, 0f), new Vector2(0f, 10f), PanelSize);
            FrontierUi.Art("Blue frame", radio, Resources.Load<Texture2D>("OutpostHud/Mission Guide Frame"));
            portrait = FrontierUi.Art("Marlonis", radio, Resources.Load<Texture2D>("OutpostHud/Mission Guide Portrait"));
            FrontierUi.Place(portrait.rectTransform, PanelSize, 9, 0, 77, 78);
            noiseTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Radio static" };
            noisePixels = new Color32[32 * 32];
            noise = FrontierUi.Art("Static", portrait.transform, noiseTexture);
            Speaker(radio, Cyan, "<b>Marlonis</b> <color=#9eb9cb>|| Damaged radio</color>");
            radioText = Dialogue(radio);
            radio.gameObject.SetActive(false);

            // The player's own voice on a recorder: Marlonis's panel with a little tape deck where the portrait goes.
            recorder = Anchored("Recorder", ui, new Vector2(.5f, Bars), new Vector2(.5f, 0f), new Vector2(0f, 10f), PanelSize);
            recorderGroup = recorder.gameObject.AddComponent<CanvasGroup>();
            FrontierUi.Art("Blue frame", recorder, Resources.Load<Texture2D>("OutpostHud/Mission Guide Frame")); // same panel as Marlonis
            var deck = Sharp(PixelArt.Sliced("Tape deck", recorder, PixelArt.Frame(FrontierUi.PanelDark, FrontierUi.Mauve, FrontierUi.PanelLight, FrontierUi.Shadow)));
            FrontierUi.Place(deck.rectTransform, PanelSize, 16, 20, RecorderSize.x, RecorderSize.y);
            for (int i = 0; i < 2; i++)
            {
                var reel = Sharp(PixelArt.Sliced("Reel", deck.transform, PixelArt.Frame(FrontierUi.CreamDark, FrontierUi.Shadow, FrontierUi.Cream, FrontierUi.Mauve)));
                FrontierUi.Place(reel.rectTransform, RecorderSize, 9 + i * 28, 10, 16, 16);
            }
            recorderLight = FrontierUi.Fill("Playback light", deck.transform, LedOff);
            FrontierUi.Place(recorderLight.rectTransform, RecorderSize, 50, 32, 5, 5);
            Speaker(recorder, Cyan, "<b>Recorder</b> <color=#9eb9cb>|| Your voice</color>");
            recorderText = Dialogue(recorder);
            recorder.gameObject.SetActive(false);

            barTop = FrontierUi.Fill("Letterbox", ui, Color.black).rectTransform;
            barBottom = FrontierUi.Fill("Letterbox", ui, Color.black).rectTransform;

            // Always on screen: "Hold [Esc] to skip", the key filling up while it is held.
            var prompt = Anchored("Skip prompt", barBottom, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-18f, 0f), PromptSize);
            skipGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            Label(prompt, PromptSize, "Hold", 2, FrontierUi.Cream, 0, 0, 50, 24, 1f);
            var key = PixelArt.Sliced("Esc key", prompt, PixelArt.Frame(FrontierUi.PanelDark, FrontierUi.Mauve, FrontierUi.CreamDark, FrontierUi.Mauve));
            FrontierUi.Place(key.rectTransform, PromptSize, 56, 0, 48, 24);
            var track = FrontierUi.Box("Hold progress", key.transform);
            track.offsetMin = new Vector2(3f, 3f);
            track.offsetMax = new Vector2(-3f, -3f);
            skipFill = FrontierUi.Fill("Fill", track, FrontierUi.Coral).rectTransform;
            skipFill.anchorMax = new Vector2(0f, 1f);
            PixelLabel.Create("Key", key.transform, "Esc", 2, FrontierUi.Cream);
            Label(prompt, PromptSize, "or click", 2, FrontierUi.Cream, 110, 0, 78, 24, 0f);
        }

        static RectTransform Anchored(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = FrontierUi.Box(name, parent);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        static void Edge(RectTransform bar, float height, bool top)
        {
            bar.anchorMin = new Vector2(0f, top ? 1f - height : 0f);
            bar.anchorMax = new Vector2(1f, top ? 1f : height);
        }

        static PixelLabel Label(RectTransform parent, Vector2 size, string text, int scale, Color color,
            float x, float y, float w, float h, float align = .5f)
        {
            var label = PixelLabel.Create(text, parent, text, scale, color, align);
            FrontierUi.Place(label.Rect, size, x, y, w, h);
            return label;
        }

        static Image Sharp(Image image)
        {
            image.pixelsPerUnitMultiplier = 1f; // one canvas unit per texel, like the mission guide frame
            return image;
        }

        static Text Speaker(RectTransform panel, Color color, string caption)
        {
            var text = Body("Speaker", panel, 13, color, TextAnchor.MiddleLeft, 103, 7, 294, 16);
            text.text = caption;
            return text;
        }

        static Text Dialogue(RectTransform panel)
        {
            var text = Body("Dialogue", panel, 15, FrontierUi.Cream, TextAnchor.UpperLeft, 103, 25, 295, 50);
            text.supportRichText = false;
            return text;
        }

        static Text Body(string name, RectTransform panel, int fontSize, Color color, TextAnchor anchor, float x, float y, float w, float h)
        {
            var box = FrontierUi.Box(name, panel);
            var text = box.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            FrontierUi.Place(box, PanelSize, x, y, w, h);
            return text;
        }

        void RefreshNoise()
        {
            if (Time.unscaledTime < noiseAt) return;
            noiseAt = Time.unscaledTime + .05f;
            for (int i = 0; i < noisePixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                noisePixels[i] = new Color32(v, v, v, 255);
            }
            noiseTexture.SetPixels32(noisePixels);
            noiseTexture.Apply(false);
        }

        static Texture2D Vignette()
        {
            const int w = 64, h = 36;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + .5f) / w * 2f - 1f, dy = (y + .5f) / h * 2f - 1f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1.25f, Mathf.Sqrt(dx * dx + dy * dy)));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Alarm vignette" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---- synthesized sound ----

        void BuildSounds()
        {
            alarmSource = Source();
            bed = Source();
            sfx = Source();
            var rng = new System.Random(11);
            float White() => (float)rng.NextDouble() * 2f - 1f;
            float brown = 0f, previous = 0f, pop = 0f;

            siren = Synth("Siren", 1f, s => Mathf.Sin(s * 2f * Mathf.PI * (s < .5f ? 880f : 620f)) * .6f
                + Square(s < .5f ? 880f : 620f, s) * .2f);
            rumble = Synth("Rumble", 2f, s =>
            {
                brown = (brown + White() * .06f) * .985f;
                return brown * 4f + Mathf.Sin(s * 2f * Mathf.PI * 36f) * .25f;
            });
            // The hull snapping in two (the touchdown itself is the recorded SFX_ROCKET_TOUCHDOWN).
            crunch = Synth("Crunch", .9f, s =>
            {
                brown = (brown + White() * .08f) * .98f;
                return brown * 4f * Mathf.Exp(-4f * s) + White() * Mathf.Exp(-18f * s) * .6f;
            });
            ring = Synth("Ringing", 4.5f, s => Mathf.Sin(s * 2f * Mathf.PI * 3150f) * .12f * Mathf.Clamp01(s * 8f) * Sqr(1f - s / 4.5f));
            hiss = Synth("Static", 2f, s =>
            {
                float white = White(), high = (white - previous) * .5f;
                previous = white;
                if (rng.NextDouble() < .0005) pop = 1f;
                pop *= .93f;
                return high * .5f + pop * white * .8f;
            });
            burst = Synth("Static burst", .35f, s => White() * .9f * (1f - s / .35f));
            click = Synth("Click", .03f, s => White() * (1f - s / .03f));
            marlonisVoice = new[] { Blip(560f, .1f, White), Blip(640f, .1f, White), Blip(720f, .1f, White) };
            playerVoice = new[] { Blip(200f, .25f, White), Blip(235f, .25f, White), Blip(270f, .25f, White) };
        }

        AudioSource Source()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }

        static void Loop(AudioSource source, AudioClip clip, float volume)
        {
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.Play();
        }

        static float Square(float hz, float s) => Mathf.Sign(Mathf.Sin(s * 2f * Mathf.PI * hz));

        internal static AudioClip Blip(float hz, float crackle, System.Func<float> white) =>
            Synth("Voice", .05f, s => (Square(hz, s) * .5f + white() * crackle) * (1f - s / .05f));

        internal static AudioClip Synth(string name, float seconds, System.Func<float, float> wave)
        {
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
