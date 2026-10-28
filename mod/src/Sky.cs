// The sky behind the track (Resurf). The game draws it on a flat plane far away (normal_skyPlane, the material "sky",
// shader Custom/Sky): its four corners point at an 8 × 56 pixel strip of the atlas main.png – a plain gradient,
// stretched over the whole sky. A sky of the mod is a texture of its own on that material: one it paints here
// (sunset, night, neon, aurora, day) or a picture of yours from the skies folder. The material's scale and offset
// map the plane's corners onto that texture, cut to the plane's square – a panorama (2:1, like the HDRIs of Poly
// Haven as JPG) to a quarter around its middle, its horizon low as the plane's is.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Resurf
{
    static class SkyArt
    {
        public static readonly string[] Skies = { "original", "sunset", "night", "neon", "aurora", "day" };
        const int W = 512, H = 512;
        const float Horizon = 0.095f;          // where the plane's y = 0 is: (0 + 32) / 340 of its height

        static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }
        static Color[] Hexes(params int[] rgb) { var c = new Color[rgb.Length]; for (int i = 0; i < rgb.Length; i++) c[i] = Hex(rgb[i]); return c; }

        static readonly Color[] SunsetSky = Hexes(0xffc86b, 0xff8a5b, 0xe8507a, 0x8a3a9c, 0x3a1f6e, 0x120a2e);
        static readonly Color[] NightSky = Hexes(0x22355e, 0x142446, 0x0a1430, 0x04081a, 0x020410);
        static readonly Color[] NeonSky = Hexes(0xff3f8f, 0xc2309a, 0x6a1b8a, 0x2b0f5a, 0x0c0420);
        static readonly Color[] AuroraSky = Hexes(0x0d3340, 0x0a2433, 0x071626, 0x040b17, 0x02050c);
        static readonly Color[] DaySky = Hexes(0xe3f4ff, 0xbfe3ff, 0x8cc8ff, 0x4f9be8, 0x2a6fc9);

        // from the horizon (0) to the top (1)
        static float Up(float v) { return Mathf.Clamp01((v - Horizon) / (1f - Horizon)); }

        static float Glow(float u, float v, float cu, float cv, float spread)
        {
            float du = u - cu, dv = v - cv;
            return Mathf.Exp(-(du * du + dv * dv) / spread);
        }

        // a disk with a soft edge: 1 inside, 0 outside
        static float Disk(float u, float v, float cu, float cv, float r)
        {
            float d = Mathf.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv));
            return Mathf.Clamp01((r - d) / 0.004f + 0.5f);
        }

        // stars: a few pixels of the hash light up, more of them high up
        static float Star(int x, int y, float v, float many)
        {
            float h = CapeArt.Hash(x, y, 3);
            float need = 1f - many * (0.25f + 0.75f * Up(v));
            return h > need ? Mathf.Pow((h - need) / (1f - need), 3f) : 0f;
        }

        public static Texture2D Paint(string sky)
        {
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = y / (H - 1f);                 // 0 the bottom, 1 the top
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f);
                    Color c;
                    switch (sky)
                    {
                        case "sunset": c = Sunset(u, v); break;
                        case "night":  c = Night(x, y, u, v); break;
                        case "neon":   c = Neon(x, y, u, v); break;
                        case "aurora": c = Aurora(x, y, u, v); break;
                        default:       c = Day(u, v); break;
                    }
                    px[y * W + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
                }
            }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        // a low sun in orange and pink, a purple night coming from above, thin clouds lit from below
        static Color Sunset(float u, float v)
        {
            var c = CapeArt.Ramp(Up(v), SunsetSky);
            float streak = Mathf.Clamp01((CapeArt.Fbm(u * 5f, v * 28f, 21) - 0.56f) / 0.18f) * Mathf.Clamp01((v - 0.14f) / 0.05f) * Mathf.Clamp01((0.5f - v) / 0.1f);
            c = Color.Lerp(c, Color.Lerp(Hex(0xffb38a), Hex(0x6b2c7a), Up(v) * 1.6f), streak * 0.45f);
            c += Hex(0xff9d5c) * Glow(u, v, 0.5f, 0.17f, 0.03f) * 0.55f;
            return Color.Lerp(c, Hex(0xfff4d6), Disk(u, v, 0.5f, 0.17f, 0.055f));
        }

        // deep blue, stars, a faint band of the Milky Way across, the moon up on the right
        static Color Night(int x, int y, float u, float v)
        {
            var c = CapeArt.Ramp(Up(v), NightSky);
            float band = Mathf.Exp(-Mathf.Pow((u - 0.35f - (v - 0.5f) * 0.7f) / 0.12f, 2f)) * CapeArt.Fbm(u * 9f, v * 9f, 31) * Up(v);
            c += Hex(0x4a5a9a) * band * 0.35f;
            c += Color.white * Star(x, y, v, 0.004f) * 0.95f;
            c += Hex(0xc9d6ff) * Glow(u, v, 0.72f, 0.7f, 0.006f) * 0.3f;
            float crater = 0.88f + 0.12f * CapeArt.Fbm(u * 90f, v * 90f, 41);
            return Color.Lerp(c, Hex(0xf3efe0) * crater, Disk(u, v, 0.72f, 0.7f, 0.035f));
        }

        // synthwave: hot pink at the horizon, a striped sun, a glowing horizon line, a few stars
        static Color Neon(int x, int y, float u, float v)
        {
            var c = CapeArt.Ramp(Up(v), NeonSky);
            c += Color.white * Star(x, y, v, 0.0015f) * 0.6f * Up(v);
            c += Hex(0xff4fa0) * Glow(u, v, 0.5f, 0.2f, 0.035f) * 0.45f;
            const float cu = 0.5f, cv = 0.2f, r = 0.16f;
            float inSun = Disk(u, v, cu, cv, r);
            if (inSun > 0f)
            {
                float k = (cv + r - v) / (2f * r);               // 0 at the sun's top, 1 at its bottom
                bool gap = k > 0.45f && Mathf.Repeat(k * 9f, 1f) < (k - 0.45f) * 1.4f;
                if (!gap) c = Color.Lerp(c, Color.Lerp(Hex(0xffe066), Hex(0xff2e7e), k), inSun);
            }
            c += Hex(0xff7ad9) * Mathf.Exp(-Mathf.Pow((v - Horizon) / 0.008f, 2f)) * 0.7f;
            return c;
        }

        // a curtain of green light that turns violet at its top, its rays moving across, stars behind it
        static Color Aurora(int x, int y, float u, float v)
        {
            var c = CapeArt.Ramp(Up(v), AuroraSky);
            c += Color.white * Star(x, y, v, 0.003f) * 0.8f;
            float edge = 0.55f + 0.07f * Mathf.Sin(u * 9.4f + 0.5f) + 0.035f * Mathf.Sin(u * 26f + 2f);
            float d = v - edge;
            float rays = 0.45f + 0.55f * CapeArt.Fbm(u * 46f, v * 1.6f, 51);
            float below = Mathf.Exp(-Mathf.Pow(d / 0.03f, 2f)) * 0.9f;               // the bright lower edge
            float above = d > 0f ? Mathf.Exp(-d / 0.16f) : 0f;                        // the curtain fading upward
            float light = (below + above * 0.75f) * rays * Mathf.Clamp01((v - 0.25f) / 0.15f);
            c += Color.Lerp(Hex(0x3dffa0), Hex(0x9b5cff), Mathf.Clamp01(d / 0.25f)) * light * 0.8f;
            return c;
        }

        // a clear day: light blue at the horizon, deep blue above, soft clouds, the sun's glow up on the right
        static Color Day(float u, float v)
        {
            var c = CapeArt.Ramp(Up(v), DaySky);
            float n = CapeArt.Fbm(u * 4.2f, v * 7f, 11) * 0.7f + CapeArt.Fbm(u * 11f, v * 18f, 12) * 0.3f;
            float cloud = Mathf.Clamp01((n - 0.52f) / 0.16f) * Mathf.Clamp01((v - 0.14f) / 0.08f) * Mathf.Clamp01((0.9f - v) / 0.15f);
            var white = Color.Lerp(Hex(0xc9d6e6), Color.white, Mathf.Clamp01((n - 0.5f) / 0.2f));
            c = Color.Lerp(c, white, cloud * 0.9f);
            return c + Hex(0xfff6d0) * Glow(u, v, 0.82f, 0.82f, 0.02f) * 0.6f;
        }
    }

    // ── the sky in the game ──────────────────────────────────────────────────────────────

    public class Sky
    {
        // the menu and Lotus set it (the settings file keeps it): SKY = original | sunset | night | neon | aurora | day |
        // file:<name of a picture in the skies folder>
        public string Want = "original";
        public string Dir = "";                // the folder of the pictures: <the mod's folder>/skies
        public string Status = "";

        // the corners of normal_skyPlane in main.png (u from left to right, v from bottom to top)
        const float U0 = 0.35596f, U1 = 0.36345f, V0 = 0.24293f, V1 = 0.29757f;

        Texture2D tex;
        string made = "";
        bool said;
        readonly Dictionary<int, object[]> own = new Dictionary<int, object[]>();   // material → its texture, scale, offset

        public Texture2D Preview { get { return tex; } }

        // the pictures in the skies folder (png, jpg)
        public List<string> Files()
        {
            var l = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(Dir) && Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir))
                    {
                        var e = Path.GetExtension(f).ToLowerInvariant();
                        if (e == ".png" || e == ".jpg" || e == ".jpeg") l.Add("file:" + Path.GetFileName(f));
                    }
            }
            catch (Exception) { }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        // the texture of the sky that is wanted: painted, read from its file (again when the file changed), or none
        Texture2D Texture()
        {
            if (Want == "original" || string.IsNullOrEmpty(Want)) return null;
            string key = Want;
            if (Want.StartsWith("file:", StringComparison.Ordinal))
            {
                try { key = Want + " " + File.GetLastWriteTimeUtc(Path.Combine(Dir, Want.Substring(5))).Ticks; }
                catch (Exception) { }
            }
            if (key == made) return tex;
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = null;
            made = key;
            try
            {
                if (Want.StartsWith("file:", StringComparison.Ordinal)) tex = Cape.LoadFile(Path.Combine(Dir, Want.Substring(5)))[0];
                else if (Array.IndexOf(SkyArt.Skies, Want) > 0) tex = SkyArt.Paint(Want);
                Status = tex == null ? "no sky " + Want : "";
            }
            catch (Exception e) { Status = "the sky " + Want + " could not be made: " + e.Message; }
            if (tex != null) Debug.Log("[Resurf] sky: " + Want + " – " + tex.width + " × " + tex.height);
            else Debug.Log("[Resurf] sky: " + Status + " – the game's own");
            return tex;
        }

        // the part of a w × h texture the square plane shows
        static Rect Cut(int w, int h)
        {
            float a = w / (float)h;
            if (a >= 1.7f)                       // a panorama: 90° of its 360°, from just below its horizon (its middle)
            {
                float cw = 0.25f, ch = Mathf.Min(1f, cw * a);
                return new Rect(0.5f - cw / 2f, Mathf.Min(0.45f, 1f - ch), cw, ch);
            }
            if (a >= 1f) return new Rect((1f - 1f / a) / 2f, 0f, 1f / a, 1f);
            return new Rect(0f, 0f, 1f, a);      // taller than wide: from the bottom, where the horizon is
        }

        static bool IsSky(Material m)
        {
            return m != null && (m.name == "sky" || m.name.StartsWith("sky ", StringComparison.Ordinal) || (m.shader != null && m.shader.name == "Custom/Sky"));
        }

        // every two seconds, and at once after a change: the sky materials there are (a theme brings copies) get the
        // texture wanted – or theirs back
        public void Tick()
        {
            var want = Texture();
            var seen = new List<string>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!IsSky(m)) continue;
                int id = m.GetInstanceID();
                object[] mine;
                if (!own.TryGetValue(id, out mine)) { mine = new object[] { m.mainTexture, m.mainTextureScale, m.mainTextureOffset }; own[id] = mine; }
                if (!said) seen.Add(m.name + " (" + (m.shader == null ? "no shader" : m.shader.name) + ")");
                if (want == null)
                {
                    if (m.mainTexture != (Texture)mine[0])
                    {
                        m.mainTexture = (Texture)mine[0]; m.mainTextureScale = (Vector2)mine[1]; m.mainTextureOffset = (Vector2)mine[2];
                        Debug.Log("[Resurf] sky: " + m.name + " – the game's own again");
                    }
                    continue;
                }
                if (m.mainTexture == want) continue;
                var cut = Cut(want.width, want.height);
                var scale = new Vector2(cut.width / (U1 - U0), cut.height / (V1 - V0));
                m.mainTexture = want;
                m.mainTextureScale = scale;
                m.mainTextureOffset = new Vector2(cut.x - U0 * scale.x, cut.y - V0 * scale.y);
                Debug.Log("[Resurf] sky: " + m.name + " shows " + Want);
            }
            if (!said && seen.Count > 0) { said = true; Debug.Log("[Resurf] sky: found " + string.Join(", ", seen.ToArray())); }
        }

        public void Dispose() { if (tex != null) UnityEngine.Object.Destroy(tex); tex = null; }
    }
}
