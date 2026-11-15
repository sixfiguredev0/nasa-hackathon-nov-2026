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
    /// The 30 s opening, played on the live outpost before the player gets control:
    /// 0-6 s the rocket shakes down with alarms going, 6-12 s black then waking in the wreckage,
    /// 12-26 s Marlonis on the damaged radio, 26-30 s a recorder plays the player's own voice while the camera
    /// pulls back to the outpost. Holding Esc skips it at any point.
    /// There is no cutscene art yet, so it is built from the scene, the rocket and crew sprites, flat pixel shapes and
    /// synthesized sound. This object stays behind afterwards as the smouldering crash site.
    /// </summary>
    internal sealed class OpeningCutscene : MonoBehaviour
    {
        const float Duration = 30f, EngineOut = 4.5f, Impact = 6f, Wake = 8f, Stand = 10.8f;
        const float RadioOn = 12f, PageTwo = 21f, RadioOff = 26f, RecorderOn = 26.3f, PullBack = 27.4f, Release = 29.2f;
        const float StartHeight = 17f, StallHeight = 7f;
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
            new[] { new Cue(12.4f, "You're alive. We crashed on Mars."), new Cue(15.4f, " I'm Marlonis. I can help you survive.") },
            new[] { new Cue(21.1f, "Contact with Earth is lost."), new Cue(22.8f, " Don't repair the radio yet. Power comes first.") },
        };
        static readonly Cue[] RecorderLines = { new Cue(26.6f, "\"Check the communications log.\"") };

        // Eyelid openness keyframes (time, 0 shut .. 1 open): two heavy blinks before the eyes stay open.
        static readonly Vector2[] Blink =
        {
            new Vector2(8f, 0f), new Vector2(8.6f, .35f), new Vector2(8.9f, .05f),
            new Vector2(9.7f, .7f), new Vector2(9.85f, .45f), new Vector2(10.7f, 1f)
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

        enum Kind { Smoke, Trail, Fire, Flame }

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
        Vector3 home, touchdown, basePlane, playerPlane, recorderPlane, shotA, shotB;
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
        Texture2D noiseTexture;
        Color32[] noisePixels;
        PixelLabel warningTitle, warningStatus, warningAltitude;
        Text radioText, recorderText;
        CanvasGroup recorderGroup, skipGroup;

        AudioSource alarmSource, bed, sfx;
        AudioClip siren, rumble, ring, hiss, burst, click;
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
            playerPlane = basePlane + new Vector3(-3.25f, -1.75f, 0f);
            recorderPlane = basePlane + new Vector3(-2f, -2.4f, 0f);
            PlayerPosition = Ground(playerPlane);
            shotA = touchdown + new Vector3(0f, 6.5f, 0f);
            shotB = PlayerPosition + new Vector3(1.7f, 3f, 0f);

            if (env != null)
            {
                panAllowed = env.allowPan;
                env.allowPan = false;
            }
            if (Crew.Picked.Length > 0) facings = Crew.Facings(Crew.Picked[0]);
            rocket.InFlight = true;

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
                if (hold >= SkipSeconds) Finish(true);
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
                    Destroy(canvas.gameObject);
                    coverFade = -1f;
                }
            }
            Smoulder(Time.deltaTime);
        }

        bool Crossed(float moment) => last < moment && t >= moment;
        float Pulse(float hz) => .5f + .5f * Mathf.Sin(t * hz * 2f * Mathf.PI);

        void Direct(float dt)
        {
            bool calm = FrontierUi.ReduceMotion;

            // 0-6 s: the rocket shakes down with the alarms going.
            bool falling = t < Impact;
            warning.gameObject.SetActive(falling && t > .25f);
            alarm.color = new Color(1f, .16f, .12f, falling ? (calm ? .1f : .26f) * (.4f + .6f * Pulse(1.25f)) : 0f);
            if (falling) Fall(calm, dt);
            if (!calm && Crossed(EngineOut)) GameAudio.Play(Sfx.RocketDescent);
            if (Crossed(Impact)) Crash();
            if (Crossed(Impact + .12f)) WakeShot();
            flash.color = new Color(1f, 1f, 1f, !calm && t >= Impact && t < Impact + .12f ? 1f : 0f);
            black.color = new Color(0f, 0f, 0f, t >= Impact && t < Wake && (calm || t >= Impact + .12f) ? 1f : 0f);

            // 6-12 s: the eyes open on the wreckage and the astronaut gets up.
            float open = Openness();
            bool lids = t >= Wake && open < 1f;
            lidTop.gameObject.SetActive(lids);
            lidBottom.gameObject.SetActive(lids);
            Edge(lidTop, .5f * (1f - open) + .002f, true);
            Edge(lidBottom, .5f * (1f - open) + .002f, false);
            vision.color = new Color(.25f, .05f, .06f, t >= Wake ? .65f * (1f - Mathf.InverseLerp(Wake, 11.5f, t)) : 0f);
            Pose(calm);

            // 12-26 s: Marlonis on the damaged radio. 26-30 s: the recorder, then back out to the outpost.
            Radio(calm);
            Recorder();
            if (t >= PullBack)
                Aim(Vector3.Lerp(shotB, home, calm ? 1f : Mathf.SmoothStep(0f, 1f, (t - PullBack) / 2.2f)), 1);

            float bars = Bars * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Release, Release + .7f, t)));
            Edge(barTop, bars, true);
            Edge(barBottom, bars, false);
            skipGroup.alpha = bars / Bars;
            skipFill.anchorMax = new Vector2(Mathf.Clamp01(hold / SkipSeconds), 1f);
        }

        void Fall(bool calm, float dt)
        {
            float h = t < EngineOut ? Mathf.Lerp(StartHeight, StallHeight, t / EngineOut)
                : StallHeight * (1f - Sqr((t - EngineOut) / (Impact - EngineOut)));
            float severity = t / Impact;
            float sway = calm ? 0f : Mathf.Round(Mathf.Sin(t * 31f) * (1f + 2f * severity)) / Ppu;
            rocket.transform.position = Snap(touchdown + new Vector3(sway, h, 0f));
            rocket.transform.rotation = Quaternion.Euler(0f, 0f, calm ? 0f : Mathf.Sin(t * 4.7f) * (1.5f + 4f * severity));

            // Thrusters sputter until they cut out; smoke streams off the hull all the way down.
            var hull = rocket.Renderer.bounds;
            if (t < EngineOut && Pulse(3.1f) > .25f)
                Stream(ref flameAcc, 45f * dt, Kind.Flame, rocket.transform.position + Vector3.up * .3f);
            Stream(ref trailAcc, 22f * dt, Kind.Trail, new Vector3(hull.center.x + .3f, hull.center.y + 1f, 0f));
            bed.volume = .3f + .5f * severity;

            warningTitle.gameObject.SetActive(calm || Pulse(1.25f) > .3f);
            warningStatus.Text = t < 1.6f ? "Hull breach" : t < 3.2f ? "Cabin pressure low" : t < EngineOut ? "Thrusters failing"
                : t < 5.2f ? "Engine failure" : "Brace for impact";
            warningAltitude.Text = "Altitude " + Mathf.RoundToInt(h / StartHeight * 180f) * 10 + " m";

            float shake = calm ? 0f : (1f + 2.5f * severity * severity) / Ppu;
            Aim(shotA + (Vector3)(Random.insideUnitCircle * shake), 1);
        }

        void Crash()
        {
            rocket.transform.SetPositionAndRotation(touchdown, Quaternion.identity);
            rocket.InFlight = false;
            ScatterWreckage();
            alarmSource.Stop();
            bed.Stop();
            GameAudio.Play(Sfx.RocketTouchdown);
            sfx.PlayOneShot(ring, .5f);
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
            int facing = t < 11.5f ? South : t < 11.9f ? SouthWest : t < 12.3f ? SouthEast : t < RadioOff + .1f ? South : SouthEast;
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
            var typed = new StringBuilder();
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
            rocket.transform.SetPositionAndRotation(touchdown, Quaternion.identity);
            rocket.InFlight = false;
            ScatterWreckage();
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
            if (cam != null) cam.transform.position = home;
            foreach (var e in emitters)
            {
                if (e.kind == Kind.Fire) e.until = Time.time + 8f;
                else e.rate *= .5f;
            }

            if (!skipped)
            {
                Destroy(canvas.gameObject);
                return;
            }
            // A skip cuts through black, like a scene change.
            foreach (Transform child in canvas.transform) child.gameObject.SetActive(child == black.transform);
            black.color = Color.black;
            coverFade = 0f;
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
            scorch.sprite = ScorchSprite();
            scorch.transform.localScale = Vector3.one;

            // Hull plates and two torn-off fins thrown around the rocket, some still burning, plus small bits.
            var rng = new System.Random(7);
            for (int i = 0; i < 26; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0), reach = 2.2f + (float)rng.NextDouble() * 4.6f;
                var plane = basePlane + new Vector3(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach * .55f, 0f);
                var sprite = Chunk(rng, i < 2, i < 14);
                if (Vector2.Distance(plane, playerPlane) < 1.4f || Vector2.Distance(plane, recorderPlane) < .9f) continue;
                Debris(plane, sprite);
                var top = Ground(plane) + Vector3.up * (sprite.rect.height * .6f) / Ppu;
                if (i % 4 == 0) emitters.Add(new Emitter { at = top, kind = Kind.Smoke, rate = 2f });
                if (i % 4 == 1) emitters.Add(new Emitter { at = top, kind = Kind.Fire, rate = 16f });
            }

            // The hull smokes from its side and keeps burning at the base.
            var hull = rocket.Renderer.bounds;
            emitters.Add(new Emitter { at = new Vector3(hull.center.x + .6f, hull.max.y - 2.6f, 0f), kind = Kind.Smoke, rate = 5f });
            emitters.Add(new Emitter { at = touchdown + new Vector3(.9f, .3f, 0f), kind = Kind.Fire, rate = 22f });
            emitters.Add(new Emitter { at = touchdown + new Vector3(-.6f, .2f, 0f), kind = Kind.Fire, rate = 14f });
            emitters.Add(new Emitter { at = touchdown + new Vector3(.9f, .6f, 0f), kind = Kind.Smoke, rate = 4f });

            // The recorder that plays the player's voice: a little box with a playback light.
            var at = Ground(recorderPlane) - new Vector3(3f / Ppu, 0f, 0f);
            int order = MarsMapGenerator.SortingOrderFor(recorderPlane.y);
            Pixel("Recorder shadow", at - new Vector3(1f / Ppu, 0f, 0f), 8, 1, Shade, order);
            Pixel("Recorder", at + Vector3.up / Ppu, 6, 3, new Color32(58, 56, 68, 255), order + 1);
            Pixel("Recorder top", at + Vector3.up * 4f / Ppu, 6, 1, new Color32(112, 110, 124, 255), order + 1);
            led = Pixel("Recorder light", at + new Vector3(4f, 4f, 0f) / Ppu, 1, 1, LedOff, order + 2);
        }

        void Debris(Vector3 plane, Sprite sprite)
        {
            int order = MarsMapGenerator.SortingOrderFor(plane.y), w = (int)sprite.rect.width;
            var at = Ground(plane);
            Pixel("Debris shadow", at - new Vector3(Mathf.Floor(w / 2f + 1f) / Ppu, 0f, 0f), w + 2, 1, Shade, order);
            var piece = Pixel("Debris", at + Vector3.up / Ppu, 1, 1, Color.white, order + 1);
            piece.sprite = sprite;
            piece.transform.localScale = Vector3.one;
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
                        new Color32(64, 56, 64, 215), new Color32(110, 92, 96, 0), 20000);
                    break;
                case Kind.Fire:
                    Set(p, Random.Range(.35f, .7f), new Vector3(Random.Range(-.2f, .2f), Random.Range(.7f, 1.3f), 0f), 4, 1,
                        new Color32(255, 230, 120, 255), new Color32(226, 70, 40, 60), 20001);
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

            alarm = FrontierUi.Art("Alarm light", ui, Vignette());
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

            // The player's own voice on a recorder, drawn as a little tape deck.
            recorder = Anchored("Recorder", ui, new Vector2(.5f, Bars), new Vector2(.5f, 0f), new Vector2(0f, 10f), PanelSize);
            recorderGroup = recorder.gameObject.AddComponent<CanvasGroup>();
            Sharp(PixelArt.Sliced("Frame", recorder, PixelArt.Frame(FrontierUi.Background, FrontierUi.Coral, FrontierUi.PanelLight, FrontierUi.Shadow)));
            var deck = Sharp(PixelArt.Sliced("Tape deck", recorder, PixelArt.Frame(FrontierUi.PanelDark, FrontierUi.Mauve, FrontierUi.PanelLight, FrontierUi.Shadow)));
            FrontierUi.Place(deck.rectTransform, PanelSize, 16, 20, RecorderSize.x, RecorderSize.y);
            for (int i = 0; i < 2; i++)
            {
                var reel = Sharp(PixelArt.Sliced("Reel", deck.transform, PixelArt.Frame(FrontierUi.CreamDark, FrontierUi.Shadow, FrontierUi.Cream, FrontierUi.Mauve)));
                FrontierUi.Place(reel.rectTransform, RecorderSize, 9 + i * 28, 10, 16, 16);
            }
            recorderLight = FrontierUi.Fill("Playback light", deck.transform, LedOff);
            FrontierUi.Place(recorderLight.rectTransform, RecorderSize, 50, 32, 5, 5);
            Speaker(recorder, FrontierUi.CoralLight, "<b>Recorder</b> <color=#cfb9b8>|| Your voice</color>");
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
            Label(prompt, PromptSize, "to skip", 2, FrontierUi.Cream, 110, 0, 78, 24, 0f);
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

        static AudioClip Blip(float hz, float crackle, System.Func<float> white) =>
            Synth("Voice", .05f, s => (Square(hz, s) * .5f + white() * crackle) * (1f - s / .05f));

        static AudioClip Synth(string name, float seconds, System.Func<float, float> wave)
        {
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
