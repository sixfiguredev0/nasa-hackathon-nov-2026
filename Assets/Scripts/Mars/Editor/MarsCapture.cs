using System.IO;
using UnityEngine;
using UnityEngine.U2D;

namespace RedFrontier.Mars.EditorTools
{
    public static class MarsCapture
    {
        public static string Render(string path, int w, int h, int zoom)
        {
            var cam = Camera.main;
            var ppc = cam.GetComponent<PixelPerfectCamera>();
            bool ppcWas = ppc != null && ppc.enabled;
            if (ppc != null) ppc.enabled = false;
            float size = cam.orthographicSize;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.orthographicSize = h / (2f * MarsMapGenerator.Ppu * zoom);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            cam.orthographicSize = size;
            if (ppc != null) ppc.enabled = ppcWas;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return path;
        }

        public static string RenderSeed(int seed, string path, int w, int h, int zoom)
        {
            var gen = Object.FindObjectOfType<MarsMapGenerator>();
            gen.Generate(seed);
            return Render(path, w, h, zoom) + " hash " + gen.MapHash + " craters " + gen.Terrain.Craters.Count;
        }
    }
}
