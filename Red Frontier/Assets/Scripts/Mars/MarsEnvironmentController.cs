using UnityEngine;
using UnityEngine.U2D;

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

        void LateUpdate()
        {
            if (!allowPan || cam == null || !Application.isPlaying) return;
            var p = cam.transform.position;
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f);
            p += move * (panSpeed / Ppu) * Time.unscaledDeltaTime;
            if (Input.GetMouseButtonDown(1)) lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(1))
            {
                var d = cam.ScreenToWorldPoint(lastMouse) - cam.ScreenToWorldPoint(Input.mousePosition);
                p += new Vector3(d.x, d.y, 0f);
                lastMouse = Input.mousePosition;
            }
            cam.transform.position = Clamp(p);
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
