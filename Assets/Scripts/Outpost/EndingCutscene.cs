using System.Collections;
using System.Collections.Generic;
using RedFrontier.Audio;
using RedFrontier.Mars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace RedFrontier.Outpost
{
    /// <summary>
    /// The two endings, played on the live outpost like the opening: the crew boards the ship, then
    /// Return: countdown, launch and the flight home; Stay: Marlonis says goodbye, the core powers down and Earth answers.
    /// Both finish with epilogue pages and a summary card. Stay can continue as free play with Marlonis switched off.
    /// Hold Esc to skip to the epilogue. Built from scene sprites, flat pixels and synthesized sound, like the opening.
    /// </summary>
    internal sealed class EndingCutscene : MonoBehaviour
    {
        const string TitleScene = "Assets/Scenes/MainMenu.unity";
        const float Ppu = 16f, Bars = .09f, CharsPerSecond = 32f, SkipSeconds = 1f;
        const int MaxPuffs = 420, Rate = 22050;
        static readonly Vector2 Reference = new Vector2(960, 540), PanelSize = new Vector2(480, 96);
        static readonly Color32 BlueFace = new Color32(34, 40, 55, 255), BlueEdge = new Color32(103, 152, 196, 255),
            Cyan = new Color32(131, 213, 235, 255), EarthFace = new Color32(24, 34, 50, 255), EarthEdge = new Color32(214, 232, 255, 255);

        static readonly string[] ReturnPages =
        {
            "Seven months later, you land on Earth.",
            "The cities are calm and bright. Nobody is hungry. But nobody gets to choose, either.",
            "Marlonis keeps its promise and talks with you every day. It is slow work. But for the first time, it is listening.",
        };
        static readonly string[] StayPages =
        {
            "Earth's people run their own world again. Some days they argue. But they choose together.",
            "On Mars the sunset is blue. You check the water, the plants and the air, like every sol.",
            "You are the first person to call Mars home.",
        };

        internal bool Running { get; private set; }

        Colony colony;
        StoryArc story;
        StoryArc.Ending which;
        Crew crew;
        Building rocket;
        Camera cam;
        MarsEnvironmentController env;
        PixelPerfectCamera ppc;
        Vector3 home;
        bool panAllowed, shelteredByUs, skipping;
        float hold;
        readonly List<Behaviour> hidden = new List<Behaviour>();

        Canvas canvas;
        RectTransform layout, barTop, barBottom, panel, card;
        Image black, panelFrame;
        RawImage portrait, earth;
        Text speaker, words, epilogue;
        PixelLabel bigLabel;
        CanvasGroup skipGroup;
        AudioSource bed, sfx;
        AudioClip rumble, beep, go, powerDown, burst;
        AudioClip[] marlonisVoice, earthVoice;

        sealed class Puff
        {
            public SpriteRenderer sr;
            public Vector3 pos, vel;
            public float age, life, from, to;
            public Color start, end;
        }
        readonly List<Puff> puffs = new List<Puff>();
        static Sprite pixel;

        internal static EndingCutscene Play(Colony colony, StoryArc story, StoryArc.Ending which)
        {
            var go = new GameObject("Ending " + which);
            var ending = go.AddComponent<EndingCutscene>();
            ending.colony = colony;
            ending.story = story;
            ending.which = which;
            ending.Running = true;
            return ending;
        }

        void Start()
        {
            crew = colony.GetComponent<Crew>();
            rocket = story.Rocket;
            cam = Camera.main;
            env = FindObjectOfType<MarsEnvironmentController>();
            ppc = cam != null ? cam.GetComponent<PixelPerfectCamera>() : null;
            if (cam != null) home = cam.transform.position;
            if (env != null)
            {
                panAllowed = env.allowPan;
                env.allowPan = false;
            }
            Time.timeScale = 1f;
            HideHud(true);
            BuildUi();
            BuildSounds();
            StartCoroutine(Run());
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!skipping && card != null && !card.gameObject.activeSelf)
            {
                hold = Input.GetKey(KeyCode.Escape) ? hold + dt : Mathf.Max(0f, hold - 3f * dt);
                if (hold >= SkipSeconds) skipping = true;
            }
            Animate(Time.deltaTime);
        }

        // ---- sequence ----

        IEnumerator Run()
        {
            yield return Letterbox(true);
            Aim(rocket.transform.position + new Vector3(0f, 2.5f, 0f));
            if (crew != null && !crew.Sheltering)
            {
                shelteredByUs = true;
                crew.SetSheltering(true);
            }
            for (float t = 0f; crew != null && !crew.Sheltered && t < 15f && !skipping; t += Time.deltaTime) yield return null;

            if (which == StoryArc.Ending.Return) yield return Launch();
            else yield return Shutdown();

            yield return Fade(1f, 1.2f);
            bed.Stop();
            panel.gameObject.SetActive(false);
            bigLabel.gameObject.SetActive(false);
            skipGroup.alpha = 0f;
            yield return Epilogue(which == StoryArc.Ending.Return ? ReturnPages : StayPages);
            ShowCard();
        }

        IEnumerator Launch()
        {
            bool calm = FrontierUi.ReduceMotion;
            Vector3 start = rocket.transform.position, basePoint = start + new Vector3(0f, -1.1f, 0f);
            Aim(start + new Vector3(0f, 2.5f, 0f));
            yield return Say(Speaker.Marlonis, "Everyone is aboard. Thank you for trusting me. Let's go home.");
            panel.gameObject.SetActive(false);
            bigLabel.gameObject.SetActive(true);
            foreach (string count in new[] { "3", "2", "1" })
            {
                if (skipping) yield break;
                bigLabel.Text = count;
                sfx.PlayOneShot(beep, .5f);
                for (float t = 0f; t < .9f && !skipping; t += Time.deltaTime)
                {
                    Stream(ref ventAcc, 12f * Time.deltaTime, Kind.Smoke, basePoint + new Vector3(Random.Range(-1f, 1f), 0f, 0f));
                    yield return null;
                }
            }
            bigLabel.Text = "Liftoff!";
            sfx.PlayOneShot(go, .6f);
            GameAudio.Play(Sfx.LaunchWhoosh);
            bed.clip = rumble;
            bed.loop = true;
            bed.volume = .5f;
            bed.Play();
            for (int i = 0; i < 60; i++) Emit(Kind.Dust, basePoint, 1f);

            float height = 0f, speed = 0f;
            for (float t = 0f; t < 6.5f && !skipping; t += Time.deltaTime)
            {
                if (t > 1.2f) bigLabel.gameObject.SetActive(false);
                speed += (t < .8f ? 2f : 5f) * Time.deltaTime;
                height += speed * Time.deltaTime;
                var at = start + Vector3.up * height;
                rocket.transform.position = Snap(at);
                var nozzle = at + new Vector3(0f, -1.1f, 0f);
                // Per-second rates, so the particle pool lasts at any frame rate.
                Stream(ref flameAcc, 140f * Time.deltaTime, Kind.Flame, nozzle);
                Stream(ref smokeAcc, 45f * Time.deltaTime, Kind.Smoke, nozzle);
                if (height < 4f) Stream(ref dustAcc, 60f * Time.deltaTime, Kind.Dust, basePoint + new Vector3(Random.Range(-2f, 2f), 0f, 0f));
                float shake = calm ? 0f : Mathf.Lerp(3f, .5f, t / 6.5f) / Ppu;
                var focus = start + new Vector3(0f, 2.5f + Mathf.Min(height, 9f), 0f);
                Aim(focus + (Vector3)(Random.insideUnitCircle * shake));
                bed.volume = Mathf.Lerp(.6f, .15f, t / 6.5f);
                yield return null;
            }
            rocket.Renderer.enabled = false;
        }

        IEnumerator Shutdown()
        {
            yield return Say(Speaker.Marlonis, "I understand. I only wanted to keep them safe.");
            yield return Say(Speaker.Marlonis, "I forgot that safe is not the same as free. Goodbye, commander. Take care of them.");
            if (!skipping)
            {
                // The core winds down: static, a falling tone, the screen dims, then quiet.
                sfx.PlayOneShot(burst, .7f);
                sfx.PlayOneShot(powerDown, .7f);
                for (float t = 0f; t < 2f && !skipping; t += Time.deltaTime)
                {
                    portrait.color = new Color(1f, 1f, 1f, Mathf.Lerp(.95f, 0f, t / 1.6f) * (Random.value < .3f ? .4f : 1f));
                    black.color = new Color(0f, 0f, 0f, Mathf.Sin(Mathf.Clamp01(t / 2f) * Mathf.PI) * .65f);
                    yield return null;
                }
                black.color = Color.clear;
                panel.gameObject.SetActive(false);
                for (float t = 0f; t < 1.2f && !skipping; t += Time.deltaTime) yield return null;
            }
            yield return Say(Speaker.Earth, "Commander, the lights are back on, and they are ours. Thank you.");
            yield return Say(Speaker.Earth, "Earth and Mars line up for the shortest trip about every 26 months. We'll send a ship then. Hold on!");
            panel.gameObject.SetActive(false);
            var from = cam != null ? cam.transform.position : Vector3.zero;
            for (float t = 0f; t < 4f && !skipping; t += Time.deltaTime)
            {
                Aim(from + new Vector3(Mathf.SmoothStep(0f, 7f, t / 4f), Mathf.SmoothStep(0f, 1.5f, t / 4f), 0f));
                yield return null;
            }
        }

        enum Speaker { Marlonis, Earth }

        IEnumerator Say(Speaker who, string line)
        {
            if (skipping) yield break;
            bool fromEarth = who == Speaker.Earth;
            panel.gameObject.SetActive(true);
            panelFrame.sprite = fromEarth ? PixelArt.Frame(EarthFace, EarthEdge, new Color32(70, 100, 140, 255), FrontierUi.Shadow)
                : PixelArt.Frame(BlueFace, BlueEdge, new Color32(70, 101, 130, 255), new Color32(41, 57, 81, 255));
            portrait.gameObject.SetActive(!fromEarth);
            earth.gameObject.SetActive(fromEarth);
            speaker.color = fromEarth ? (Color)EarthEdge : (Color)Cyan;
            speaker.text = fromEarth ? "<b>Mission Control</b> <color=#b9c9db>|| Earth</color>" : "<b>Marlonis</b> <color=#9eb9cb>|| Ship computer</color>";
            var voice = fromEarth ? earthVoice : marlonisVoice;
            int shown = 0;
            for (float t = 0f; !skipping; t += Time.deltaTime)
            {
                int n = Mathf.Min(line.Length, Mathf.FloorToInt(t * CharsPerSecond));
                if (n != shown)
                {
                    words.text = line.Substring(0, n);
                    if (n % 2 == 0 && n > 0 && char.IsLetter(line[n - 1])) sfx.PlayOneShot(voice[n / 2 % voice.Length], .4f);
                    shown = n;
                }
                if (!fromEarth) portrait.color = new Color(1f, 1f, 1f, Random.value < .06f ? .5f : .95f);
                if (n >= line.Length && t > line.Length / CharsPerSecond + 1.6f) break;
                yield return null;
            }
        }

        IEnumerator Epilogue(string[] pages)
        {
            GameAudio.EnterTitle();
            epilogue.gameObject.SetActive(true);
            foreach (var page in pages)
            {
                for (float t = 0f; ; t += Time.unscaledDeltaTime)
                {
                    int n = Mathf.Min(page.Length, Mathf.FloorToInt(t * CharsPerSecond));
                    epilogue.text = page.Substring(0, n);
                    bool click = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
                    if (click && n < page.Length) t = page.Length / CharsPerSecond;
                    else if (click || t > page.Length / CharsPerSecond + 2.4f) break;
                    yield return null;
                }
                yield return null;
            }
            epilogue.gameObject.SetActive(false);
        }

        void ShowCard()
        {
            bool stay = which == StoryArc.Ending.Stay;
            card = FrontierUi.PanelFrame("Ending card", layout);
            var size = new Vector2(460, 300);
            FrontierUi.Place(card, Reference, (Reference.x - size.x) / 2, (Reference.y - size.y) / 2, size.x, size.y);
            var title = PixelLabel.Create("Ending", card, "Ending", 2, FrontierUi.CreamDark);
            FrontierUi.Place(title.Rect, size, 0, 14, size.x, 14);
            var name = PixelLabel.Create("Name", card, stay ? "First Martian" : "Homecoming", 4, FrontierUi.CoralLight);
            FrontierUi.Place(name.Rect, size, 0, 34, size.x, 32);
            int built = Mathf.Max(0, colony.economy.buildings.Count - 1);
            int sol = Skywatch.Instance != null ? Skywatch.Instance.clock.Sol : 1;
            var stats = Body("Stats", card, size, 15, FrontierUi.Cream, TextAnchor.UpperLeft, 40, 80, size.x - 80, 140);
            stats.text = "Sols on Mars: " + sol + "\nBuildings built: " + built + "\nClues found: " + story.Found.Count + " of " + story.BeatCount +
                "\nSolar storm drill: " + story.StormChoice +
                "\n\nReal science you used: orbiter radio relays, light delay, making fuel and oxygen on Mars, and launch windows.";
            var menu = FrontierUi.Button("Main menu", card, !stay);
            var keep = stay ? FrontierUi.Button("Keep building", card, true) : null;
            FrontierUi.Place((RectTransform)menu.transform, size, stay ? 40 : (size.x - 180) / 2, size.y - 52, 180, 34);
            menu.onClick.AddListener(ToMenu);
            if (keep != null)
            {
                FrontierUi.Place((RectTransform)keep.transform, size, size.x - 220, size.y - 52, 180, 34);
                keep.onClick.AddListener(KeepBuilding);
                keep.Select();
            }
            else menu.Select();
        }

        void ToMenu()
        {
            Time.timeScale = 1f;
            GameAudio.Play(Sfx.UiConfirm);
            SceneManager.LoadSceneAsync(TitleScene);
        }

        void KeepBuilding()
        {
            GameAudio.Play(Sfx.UiConfirm);
            story.FinishStay();
            if (shelteredByUs && crew != null) crew.SetSheltering(false);
            if (env != null)
            {
                env.ConfigureCamera();
                env.allowPan = panAllowed;
            }
            if (cam != null) cam.transform.position = home;
            HideHud(false);
            GameAudio.BeginGameplayMusic();
            Running = false;
            Destroy(canvas.gameObject);
            Destroy(gameObject);
        }

        /// <summary>The IMGUI HUD draws over every canvas, so the components that draw it are switched off while the ending plays.</summary>
        void HideHud(bool hide)
        {
            if (hide)
            {
                foreach (var b in new Behaviour[]
                         {
                             colony, colony.GetComponent<BuilderMode>(), colony.GetComponent<UpgradeBubbles>(),
                             colony.GetComponent<Skywatch>(), colony.GetComponent<MissionPauseMenu>()
                         })
                    if (b != null && b.enabled)
                    {
                        b.enabled = false;
                        hidden.Add(b);
                    }
                return;
            }
            foreach (var b in hidden)
                if (b != null) b.enabled = true;
            hidden.Clear();
        }

        // ---- camera, screen and particles ----

        void Aim(Vector3 at)
        {
            if (cam == null) return;
            if (ppc != null)
            {
                var reference = env != null ? env.referenceResolution : new Vector2Int(960, 540);
                ppc.refResolutionX = reference.x;
                ppc.refResolutionY = reference.y;
            }
            cam.transform.position = new Vector3(Mathf.Round(at.x * Ppu) / Ppu, Mathf.Round(at.y * Ppu) / Ppu, -10f);
        }

        IEnumerator Letterbox(bool on)
        {
            for (float t = 0f; t <= .6f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / .6f);
                Edge(barTop, Bars * (on ? k : 1f - k), true);
                Edge(barBottom, Bars * (on ? k : 1f - k), false);
                yield return null;
            }
        }

        IEnumerator Fade(float to, float seconds)
        {
            float from = black.color.a;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                black.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            black.color = new Color(0f, 0f, 0f, to);
        }

        enum Kind { Smoke, Flame, Dust }

        float ventAcc, flameAcc, smokeAcc, dustAcc;

        void Stream(ref float acc, float amount, Kind kind, Vector3 at)
        {
            for (acc += amount; acc >= 1f; acc--) Emit(kind, at, 1f);
        }

        void Emit(Kind kind, Vector3 at, float chance)
        {
            if (Random.value > chance) return;
            Puff p = puffs.Find(free => !free.sr.enabled);
            if (p == null)
            {
                if (puffs.Count >= MaxPuffs) return;
                p = new Puff { sr = Pixel("Puff") };
                puffs.Add(p);
            }
            p.age = 0f;
            p.pos = at + new Vector3(Random.Range(-2, 3), Random.Range(-1, 2), 0f) / Ppu;
            switch (kind)
            {
                case Kind.Flame:
                    Set(p, Random.Range(.2f, .4f), new Vector3(Random.Range(-.8f, .8f), Random.Range(-8f, -5f), 0f), 7, 2,
                        new Color32(255, 240, 160, 255), new Color32(240, 110, 40, 0), 30001);
                    break;
                case Kind.Dust:
                    Set(p, Random.Range(.9f, 1.8f), new Vector3(Random.Range(-3f, 3f), Random.Range(.1f, .9f), 0f), 5, 16,
                        new Color32(232, 178, 128, 235), new Color32(214, 150, 108, 0), 30000);
                    break;
                default:
                    Set(p, Random.Range(1.6f, 2.8f), new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(-1f, .4f), 0f), 6, 20,
                        new Color32(236, 220, 208, 230), new Color32(196, 168, 156, 0), 30000);
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

        void Animate(float dt)
        {
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

        SpriteRenderer Pixel(string name)
        {
            if (pixel == null)
            {
                var white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Ending pixel" };
                white.SetPixel(0, 0, Color.white);
                white.Apply(false, true);
                pixel = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), Ppu);
            }
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = pixel;
            sr.enabled = false;
            return sr;
        }

        static Vector3 Snap(Vector3 p) => new Vector3(Mathf.Round(p.x * Ppu) / Ppu, Mathf.Round(p.y * Ppu) / Ppu, p.z);

        void BuildUi()
        {
            var root = new GameObject("Ending Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            var rootRect = (RectTransform)root.transform;
            layout = FrontierUi.FitLayout(rootRect);
            FrontierUi.Resize(rootRect, Reference, layout);
            if (UnityEngine.EventSystems.EventSystem.current == null)
                new GameObject("Event System", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));

            barTop = FrontierUi.Fill("Letterbox", rootRect, Color.black).rectTransform;
            barBottom = FrontierUi.Fill("Letterbox", rootRect, Color.black).rectTransform;
            Edge(barTop, 0f, true);
            Edge(barBottom, 0f, false);

            panel = FrontierUi.Box("Speaker panel", layout);
            FrontierUi.Place(panel, Reference, (Reference.x - PanelSize.x) / 2, Reference.y * (1f - Bars) - PanelSize.y - 10, PanelSize.x, PanelSize.y);
            panelFrame = PixelArt.Sliced("Frame", panel, PixelArt.Frame(BlueFace, BlueEdge, BlueEdge, BlueFace));
            panelFrame.pixelsPerUnitMultiplier = 1f;
            portrait = FrontierUi.Art("Marlonis", panel, Resources.Load<Texture2D>("OutpostHud/Mission Guide Portrait"));
            FrontierUi.Place(portrait.rectTransform, PanelSize, 10, 9, 77, 78);
            earth = FrontierUi.Art("Earth", panel, StoryUi.EarthTexture());
            FrontierUi.Place(earth.rectTransform, PanelSize, 20, 20, 56, 56);
            speaker = Body("Speaker", panel, PanelSize, 13, Cyan, TextAnchor.MiddleLeft, 100, 8, 366, 16);
            speaker.supportRichText = true;
            words = Body("Words", panel, PanelSize, 15, FrontierUi.Cream, TextAnchor.UpperLeft, 100, 28, 366, 60);
            panel.gameObject.SetActive(false);

            bigLabel = PixelLabel.Create("Countdown", layout, "", 8, FrontierUi.Cream, .5f, true);
            FrontierUi.Place(bigLabel.Rect, Reference, 0, 120, Reference.x, 80);
            bigLabel.gameObject.SetActive(false);

            black = FrontierUi.Fill("Black", rootRect, Color.clear);
            epilogue = Body("Epilogue", layout, Reference, 22, FrontierUi.Cream, TextAnchor.MiddleCenter, 160, 190, 640, 160);
            epilogue.gameObject.SetActive(false);

            var prompt = PixelLabel.Create("Skip prompt", barBottom, "Hold Esc to skip", 2, FrontierUi.CreamDark, 1f);
            prompt.Rect.offsetMax = new Vector2(-18f, 0f);
            skipGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            black.transform.SetAsLastSibling();
            epilogue.transform.parent.SetAsLastSibling();
        }

        static void Edge(RectTransform bar, float height, bool top)
        {
            bar.anchorMin = new Vector2(0f, top ? 1f - height : 0f);
            bar.anchorMax = new Vector2(1f, top ? 1f : height);
        }

        static Text Body(string name, RectTransform parent, Vector2 size, int fontSize, Color color, TextAnchor anchor, float x, float y, float w, float h)
        {
            var box = FrontierUi.Box(name, parent);
            var text = box.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            FrontierUi.Place(box, size, x, y, w, h);
            return text;
        }

        void BuildSounds()
        {
            bed = gameObject.AddComponent<AudioSource>();
            sfx = gameObject.AddComponent<AudioSource>();
            bed.playOnAwake = sfx.playOnAwake = false;
            var rng = new System.Random(23);
            float White() => (float)rng.NextDouble() * 2f - 1f;
            float brown = 0f;
            rumble = OpeningCutscene.Synth("Launch rumble", 2f, s =>
            {
                brown = (brown + White() * .07f) * .985f;
                return brown * 4f + Mathf.Sin(s * 2f * Mathf.PI * 42f) * .2f;
            });
            beep = OpeningCutscene.Synth("Countdown beep", .18f, s => Mathf.Sin(s * 2f * Mathf.PI * 880f) * .5f * (1f - s / .18f));
            go = OpeningCutscene.Synth("Liftoff tone", .5f, s => Mathf.Sin(s * 2f * Mathf.PI * 1320f) * .5f * (1f - s / .5f));
            float phase = 0f;
            powerDown = OpeningCutscene.Synth("Power down", 1.8f, s =>
            {
                phase += 2f * Mathf.PI * Mathf.Lerp(420f, 30f, s / 1.8f) / Rate; // integrate so the falling pitch stays smooth
                return Mathf.Sin(phase) * .5f * (1f - s / 1.8f);
            });
            burst = OpeningCutscene.Synth("Static burst", .4f, s => White() * .8f * (1f - s / .4f));
            marlonisVoice = new[] { OpeningCutscene.Blip(560f, .02f, White), OpeningCutscene.Blip(640f, .02f, White), OpeningCutscene.Blip(720f, .02f, White) };
            earthVoice = new[] { OpeningCutscene.Blip(380f, .3f, White), OpeningCutscene.Blip(420f, .3f, White), OpeningCutscene.Blip(460f, .3f, White) };
        }
    }
}
