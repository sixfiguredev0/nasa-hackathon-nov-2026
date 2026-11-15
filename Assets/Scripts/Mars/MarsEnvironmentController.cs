using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace RedFrontier.Mars
{
    /// <summary>Pixel-perfect camera setup plus simple pan (WASD / arrows / right-mouse drag) clamped to the baked ground.</summary>
    public class MarsEnvironmentController : MonoBehaviour
    {
        const float Ppu = 16f;

        public Camera cam;
        public Vector2Int referenceResolution = new Vector2Int(960, 540);
        public bool allowPan = true;
        [Tooltip("Pan speed in world pixels per second")]
        public float panSpeed = 220f;
        public Color background = MarsSky.SpaceColor;

        Rect bounds;
        bool hasBounds;
        Vector3 lastMouse;
        bool dragging; // right button went down over the world (not over UI)

        void Awake() => ConfigureCamera();

        public void ConfigureCamera()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            cam.orthographic = true;
            cam.transform.rotation = Quaternion.identity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc == null) ppc = cam.gameObject.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = (int)Ppu;
            ppc.refResolutionX = referenceResolution.x;
            ppc.refResolutionY = referenceResolution.y;
            ppc.upscaleRT = false;
            ppc.pixelSnapping = true;
            ppc.cropFrameX = false;
            ppc.cropFrameY = false;
            ppc.stretchFill = false;
        }

        public void SetBounds(Rect worldRect)
        {
            bounds = worldRect;
            hasBounds = true;
            ConfigureCamera();
            if (cam != null) cam.transform.position = Clamp(new Vector3(0f, 0f, -10f));
        }

        public float ViewHalfHeight => referenceResolution.y * 0.5f / Ppu;

        public void Focus(Vector2 world)
        {
            if (cam != null) cam.transform.position = Clamp(new Vector3(world.x, world.y, -10f));
        }

        void LateUpdate()
        {
            if (RedFrontier.Outpost.MissionPauseMenu.BlocksGameplayInput)
            {
                lastMouse = Input.mousePosition;
                return;
            }
            if (!allowPan || cam == null || !Application.isPlaying) return;
            var p = cam.transform.position;
            if (!TextInputFocused())
            {
                var move = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f);
                p += move * (panSpeed / Ppu) * Time.unscaledDeltaTime;
            }
            // a right-drag only pans when it starts over the world, not over a UI panel
            if (Input.GetMouseButtonDown(1))
            {
                lastMouse = Input.mousePosition;
                dragging = !PointerOverUI();
            }
            if (!Input.GetMouseButton(1)) dragging = false;
            if (dragging)
            {
                var d = cam.ScreenToWorldPoint(lastMouse) - cam.ScreenToWorldPoint(Input.mousePosition);
                p += new Vector3(d.x, d.y, 0f);
                lastMouse = Input.mousePosition;
            }
            cam.transform.position = Clamp(p);
        }

        static bool PointerOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        static bool TextInputFocused()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            var field = sel.GetComponent<InputField>();
            if (field != null && field.isFocused) return true;
            // TextMeshPro input fields, looked up by name so this script doesn't depend on the TMP assembly
            var tmp = sel.GetComponent("TMP_InputField");
            if (tmp != null)
            {
                var prop = tmp.GetType().GetProperty("isFocused");
                return prop == null || prop.GetValue(tmp) is bool b && b;
            }
            return false;
        }

        Vector3 Clamp(Vector3 p)
        {
            if (!hasBounds) return p;
            float halfH = referenceResolution.y * 0.5f / Ppu, halfW = referenceResolution.x * 0.5f / Ppu;
            float x = bounds.width > 2 * halfW ? Mathf.Clamp(p.x, bounds.xMin + halfW, bounds.xMax - halfW) : bounds.center.x;
            float y = bounds.height > 2 * halfH ? Mathf.Clamp(p.y, bounds.yMin + halfH, bounds.yMax - halfH) : bounds.center.y;
            return new Vector3(Mathf.Round(x * Ppu) / Ppu, Mathf.Round(y * Ppu) / Ppu, -10f);
        }
    }
}
