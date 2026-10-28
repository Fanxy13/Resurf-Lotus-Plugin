// The cape of the Resurf mod: cosmetics for the runner. Two designs are built in – Inferno (a cape of flames) and
// Galaxy (a night sky with a golden crescent) – drawn as textures the mod makes itself, so no file comes with it.
// The cape hangs from the character's neck or back (found by the names of the skeleton's bones), is a small piece of
// cloth that swings, trails when the runner runs and flutters – and is chosen in the mod's menu (F1).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Resurf
{
    // ── the cloth: a grid of points on a few springs, no Unity in it but Vector3 ─────────────

    sealed class ClothSim
    {
        public readonly int Cols, Rows;
        public readonly Vector3[] P, Prev;
        readonly float[] rowW;      // how wide each row is at rest: narrow at the neck, wide at the hem
        float dy, lastH = 1f / 60f;

        public ClothSim(int cols, int rows)
        {
            Cols = cols; Rows = rows;
            P = new Vector3[cols * rows]; Prev = new Vector3[cols * rows];
            rowW = new float[rows];
        }

        public void Setup(float width, float length)
        {
            dy = length / (Rows - 1);
            for (int j = 0; j < Rows; j++)
                rowW[j] = width * (0.5f + 0.5f * Mathf.Pow((float)j / (Rows - 1), 0.7f));
        }

        // where point (i, 0) of the top row is held: on the shoulder line, a little behind the bone
        public Vector3 Pin(int i, Vector3 a, Vector3 right, Vector3 back, float backOff)
        {
            return a + back * backOff + right * ((i - (Cols - 1) * 0.5f) * rowW[0] / (Cols - 1));
        }

        // hanging straight down, as if the runner stood still
        public void Reset(Vector3 a, Vector3 right, Vector3 back, float backOff)
        {
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    var p = Pin(i, a, right, back, backOff) + Vector3.down * (dy * j) + back * (dy * j * 0.12f)
                          + right * ((i - (Cols - 1) * 0.5f) * (rowW[j] - rowW[0]) / (Cols - 1));
                    P[j * Cols + i] = p; Prev[j * Cols + i] = p;
                }
            lastH = 1f / 60f;
        }

        // one step of h seconds. air: the push of the wind against the cloth (m/s²), scale: the character's size / 1.8
        public void Step(float h, Vector3 a, Vector3 right, Vector3 back, float backOff, Vector3 air, float scale, float time)
        {
            float r = h / lastH; lastH = h;
            var g = new Vector3(0f, -9.8f * scale, 0f);
            for (int j = 1; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    int k = j * Cols + i;
                    var p = P[k];
                    var v = (p - Prev[k]) * (r * 0.992f);
                    Prev[k] = p;
                    float ph = time * 6.5f + i * 0.9f + j * 0.55f;
                    // a light breeze behind the runner, with a flutter that grows toward the hem
                    float hem = (float)j / (Rows - 1);
                    var acc = g + air + back * (scale * (1.8f + 1.2f * hem * Mathf.Sin(ph))) + right * (scale * 1.6f * hem * Mathf.Sin(ph * 0.7f + j));
                    P[k] = p + v + acc * (h * h);
                }
            for (int it = 0; it < 6; it++)
            {
                for (int i = 0; i < Cols; i++) P[i] = Pin(i, a, right, back, backOff);
                for (int j = 0; j < Rows; j++)
                    for (int i = 0; i < Cols; i++)
                    {
                        int k = j * Cols + i;
                        if (i < Cols - 1) Spring(k, k + 1, rowW[j] / (Cols - 1), 0.7f, j == 0);
                        if (j < Rows - 1) Spring(k, k + Cols, dy, 1f, j == 0);
                    }
                // the body: nothing of the cape goes in front of the back
                for (int j = 1; j < Rows; j++)
                {
                    float min = backOff * (1f - 0.55f * j / (Rows - 1)) + 0.01f * scale;
                    for (int i = 0; i < Cols; i++)
                    {
                        int k = j * Cols + i;
                        float s = Vector3.Dot(P[k] - a, back);
                        if (s < min) P[k] += back * (min - s);
                    }
                }
            }
            for (int i = 0; i < Cols; i++) P[i] = Pin(i, a, right, back, backOff);
        }

        void Spring(int k1, int k2, float rest, float stiff, bool pinned1)
        {
            var d = P[k2] - P[k1];
            float len = d.magnitude;
            if (len < 1e-6f) return;
            var move = d * ((len - rest) / len * stiff);
            if (pinned1) { P[k2] -= move; }
            else { P[k1] += move * 0.5f; P[k2] -= move * 0.5f; }
        }
    }

    // ── the designs: textures made by the mod ─────────────────────────────────────────────

    static class CapeArt
    {
        public const int W = 128, H = 224;      // the cape is 1.75 times as long as it is wide
        const float Aspect = H / (float)W;

        public static readonly string[] Designs = { "off", "inferno", "galaxy" };

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        static float VNoise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = Smooth(x - xi), fy = Smooth(y - yi);
            float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float x, float y, int s)
        {
            return VNoise(x, y, s) * 0.5f + VNoise(x * 2.03f + 5.1f, y * 2.03f + 1.7f, s + 1) * 0.3f + VNoise(x * 4.1f + 9.3f, y * 4.1f + 3.9f, s + 2) * 0.2f;
        }

        static Color Ramp(float t, Color[] c)
        {
            t = Mathf.Clamp01(t) * (c.Length - 1);
            int i = Mathf.Min((int)t, c.Length - 2);
            return Color.Lerp(c[i], c[i + 1], t - i);
        }

        static readonly Color[] Fire = {
            new Color(0.04f, 0f, 0.02f), new Color(0.42f, 0.02f, 0.03f), new Color(0.88f, 0.22f, 0.04f),
            new Color(1f, 0.58f, 0.08f), new Color(1f, 0.88f, 0.35f), new Color(1f, 1f, 0.85f) };

        // Inferno: embers at the neck, flames that boil toward the hem, tongues of fire as the hem. frames loop.
        public static Texture2D[] Inferno(int frames)
        {
            var result = new Texture2D[frames];
            for (int f = 0; f < frames; f++)
            {
                float th = f * 6.2831853f / frames, ox = Mathf.Cos(th) * 1.3f, oy = Mathf.Sin(th) * 1.3f;
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W, d = 1f - (y + 0.5f) / H;       // d: 0 at the neck, 1 at the hem
                        float streak = Fbm(u * 9f + ox * 0.6f, d * 1.5f + oy * 0.8f, 3);
                        float boil = Fbm(u * 14f - oy, d * 5f + ox, 2);
                        float heat = d * 1.2f + (streak - 0.5f) * 0.95f + (boil - 0.5f) * 0.3f - 0.06f;
                        float side = Mathf.Min(u, 1f - u);
                        heat -= Mathf.Clamp01(1f - side / 0.1f) * 0.28f;
                        var c = Ramp(heat, Fire);
                        // sparks that flicker
                        int cx = x / 3, cy = y / 3;
                        float sp = Hash(cx, cy, 40 + f % 3);
                        if (sp > 0.992f && (x % 3 == 1) && (y % 3 == 1)) c = Color.Lerp(c, new Color(1f, 0.9f, 0.5f), 0.9f);
                        // the hem: tongues of fire, sharp and uneven
                        float tri = 1f - Mathf.Abs((u * 6f) % 1f - 0.5f) * 2f;
                        float tongue = Fbm(u * 8f + ox, oy * 1.2f + 4f, 5);
                        float edge = 0.76f + 0.2f * tri * (0.55f + 0.9f * tongue);
                        float alpha = Mathf.Clamp01((edge - d) / 0.035f);
                        alpha *= Mathf.Clamp01((side - 0.012f * streak) / 0.03f);
                        // the tips glow
                        c = Color.Lerp(c, new Color(1f, 0.95f, 0.6f), Mathf.Clamp01(1f - (edge - d) / 0.07f) * 0.5f * Mathf.Clamp01((d - 0.5f) * 3f));
                        px[y * W + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(alpha * 255f));
                    }
                result[f] = Make(px);
            }
            return result;
        }

        // Galaxy: a nebula, twinkling stars, a golden crescent and a golden trim with a scalloped hem
        public static Texture2D[] Galaxy(int frames)
        {
            var result = new Texture2D[frames];
            var indigo = new Color(0.015f, 0.01f, 0.09f);
            var violet = new Color(0.32f, 0.08f, 0.58f);
            var magenta = new Color(0.78f, 0.12f, 0.52f);
            var teal = new Color(0.04f, 0.58f, 0.8f);
            var gold = new Color(0.98f, 0.76f, 0.24f);
            for (int f = 0; f < frames; f++)
            {
                float th = f * 6.2831853f / frames;
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W, d = 1f - (y + 0.5f) / H;
                        var c = Color.Lerp(indigo, violet, Mathf.Pow(Fbm(u * 3.2f, d * 3.2f * Aspect, 11), 1.5f) * 1.7f);
                        c += magenta * (Mathf.Max(0f, Fbm(u * 5f + 7f, d * 5f * Aspect - 3f, 12) - 0.5f) * 1.9f);
                        c += teal * (Mathf.Max(0f, Fbm(u * 4f - 2f, d * 4f * Aspect + 6f, 13) - 0.55f) * 1.5f + d * d * 0.22f);
                        // stars: one in every few cells of 5 pixels, each with its own rhythm
                        int cx = x / 5, cy = y / 5;
                        for (int ny = -1; ny <= 1; ny++)
                            for (int nx = -1; nx <= 1; nx++)
                            {
                                float hs = Hash(cx + nx, cy + ny, 21);
                                if (hs < 0.9f) continue;
                                float sx = (cx + nx) * 5f + 0.5f + Hash(cx + nx, cy + ny, 22) * 4f, sy = (cy + ny) * 5f + 0.5f + Hash(cx + nx, cy + ny, 23) * 4f;
                                float dx = x + 0.5f - sx, dy = y + 0.5f - sy;
                                float tw = 0.55f + 0.45f * Mathf.Sin(th + hs * 60f);
                                float big = hs > 0.975f ? 1f : 0f;
                                float I = Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy) / (1.3f + big * 1.2f));
                                I += big * 0.7f * (Mathf.Max(0f, 1f - Mathf.Abs(dx) / 6f) * Mathf.Max(0f, 1f - Mathf.Abs(dy) / 0.7f) + Mathf.Max(0f, 1f - Mathf.Abs(dy) / 6f) * Mathf.Max(0f, 1f - Mathf.Abs(dx) / 0.7f));
                                c += Color.Lerp(new Color(0.8f, 0.9f, 1f), new Color(1f, 0.95f, 0.8f), Hash(cx + nx, cy + ny, 24)) * (I * tw * 1.4f);
                            }
                        // the crescent, with a warm halo
                        float px0 = u - 0.5f, py0 = d * Aspect - 0.78f;
                        float d1 = Mathf.Sqrt(px0 * px0 + py0 * py0);
                        float qx = px0 - 0.09f, qy = py0 + 0.035f;
                        float d2 = Mathf.Sqrt(qx * qx + qy * qy);
                        float moon = Smooth((0.23f - d1) / 0.012f) * (1f - Smooth((0.19f - d2) / 0.012f));
                        c += gold * (Mathf.Exp(-Mathf.Max(0f, d1 - 0.23f) * 11f) * 0.28f * (1f - moon));
                        c = Color.Lerp(c, new Color(1f, 0.9f, 0.55f) * (0.95f + 0.1f * Mathf.Sin(th)), moon);
                        // the trim: sides, neck and hem
                        float side = Mathf.Min(u, 1f - u);
                        float scallop = 0.5f - 0.5f * Mathf.Cos(u * 6.2831853f * 5f);
                        float hem = 0.968f - 0.05f * scallop;
                        float trim = Mathf.Max(Mathf.Max(Mathf.Clamp01((0.032f - side) / 0.006f), Mathf.Clamp01((0.026f - d) / 0.006f)), Mathf.Clamp01(1f - (hem - d) / 0.03f));
                        var shine = gold * (0.82f + 0.3f * Mathf.Sin(u * 30f + d * 40f + th));
                        c = Color.Lerp(c, shine, trim);
                        float alpha = Mathf.Clamp01((hem - d) / 0.012f);
                        px[y * W + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(alpha * 255f));
                    }
                result[f] = Make(px);
            }
            return result;
        }

        static Texture2D Make(Color32[] px)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }
    }

    // ── the cape in the game ──────────────────────────────────────────────────────────────

    public class Cape
    {
        // the menu sets these (and the settings file keeps them): CAPE, CAPE_SIZE, CAPE_HEIGHT, CAPE_FLIP
        //   CAPE = off | inferno | galaxy | file:<name of a picture in the capes folder>
        public string Design = "off";
        public float Size = 1f, Height = 0f;
        public bool Flip;
        public string Status = "";
        public Tracker Who;                 // the character to follow
        public SkinModel Skin;              // a skin in the game: the cape hangs from its neck then
        public string Dir = "";             // the folder of the pictures: <the mod's folder>/capes

        const int Cols = 7, Rows = 12;
        static readonly string[] Shaders = { "Sprites/Default", "Unlit/Transparent", "Mobile/Particles/Alpha Blended", "Particles/Alpha Blended", "Unlit/Texture", "Mobile/Diffuse" };

        readonly Dictionary<string, Texture2D[]> art = new Dictionary<string, Texture2D[]>();
        readonly Dictionary<string, long> artTime = new Dictionary<string, long>();
        readonly ClothSim sim = new ClothSim(Cols, Rows);
        GameObject go;
        Mesh mesh;
        Material mat;
        MeshRenderer rend;
        Vector3[] verts;
        Transform lastBone;
        float lastSize, simTime;
        Vector3 lastA, vel;
        bool placed;

        public GameObject Object { get { return go; } }

        public Texture2D Preview(string design)
        {
            var frames = Frames(design);
            return frames == null ? null : frames[0];
        }

        // the pictures a player put into the capes folder (png, jpg), as designs
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

        // a changed or new file is read again
        public void Rescan() { foreach (var k in new List<string>(art.Keys)) if (k.StartsWith("file:", StringComparison.Ordinal)) art.Remove(k); }

        Texture2D[] Frames(string design)
        {
            Texture2D[] f;
            if (design.StartsWith("file:", StringComparison.Ordinal))
            {
                try
                {
                    long stamp = File.GetLastWriteTimeUtc(Path.Combine(Dir, design.Substring(5))).Ticks;
                    long was;
                    if (art.TryGetValue(design, out f) && artTime.TryGetValue(design, out was) && was == stamp) return f;
                    artTime[design] = stamp;
                }
                catch (Exception) { }
            }
            else if (art.TryGetValue(design, out f)) return f;
            try
            {
                if (design == "inferno") f = CapeArt.Inferno(6);
                else if (design == "galaxy") f = CapeArt.Galaxy(6);
                else if (design.StartsWith("file:", StringComparison.Ordinal)) f = LoadFile(Path.Combine(Dir, design.Substring(5)));
                else f = null;
            }
            catch (Exception e) { Debug.Log("[Resurf] cape: the design " + design + " could not be made: " + e.Message); f = null; }
            art[design] = f;
            return f;
        }

        // a picture of the player's: Unity's own reader when this build has it (png, jpg), else the mod's PNG reader
        static Texture2D[] LoadFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            bool ok = false;
            try
            {
                var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var m = t == null ? null : t.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (m != null) ok = (bool)m.Invoke(null, new object[] { tex, bytes });
            }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                var img = Png.Decode(bytes);
                if (img == null) { UnityEngine.Object.Destroy(tex); throw new FormatException("not a picture this build can read (a PNG works everywhere)"); }
                img = Png.Shrink(img, 2048);
                tex.Resize(img.W, img.H);
                tex.LoadRawTextureData(img.Rgba);
                tex.Apply(true);
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return new[] { tex };
        }

        public void Dispose()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        // once per frame, after the animations: LateUpdate
        public void Tick()
        {
            if (Design == "off" || Frames(Design) == null) { if (go != null && go.activeSelf) go.SetActive(false); Status = Design == "off" ? "" : "this design could not be made"; return; }
            // from what does it hang: the skin's neck, or the game's own character
            Transform bone; float lift, height; Transform face;
            if (Skin != null && Skin.CapeBone != null) { bone = Skin.CapeBone; lift = 0f; height = Skin.ModelHeight; face = Skin.Root; Status = "on the skin's neck"; }
            else if (Who != null && Who.Has) { bone = Who.Bone; lift = Who.BoneLift; height = Who.Height; face = Who.Root; Status = Who.Status; }
            else { if (go != null && go.activeSelf) go.SetActive(false); Status = Who != null ? Who.Status : ""; return; }
            if (go == null && !Build()) return;
            if (!go.activeSelf) go.SetActive(true);
            if (bone != lastBone) { lastBone = bone; placed = false; }
            go.layer = bone.gameObject.layer;

            float now = Time.realtimeSinceStartup;
            var frames = Frames(Design);
            mat.mainTexture = frames[(int)(now * (Design == "inferno" ? 12f : 5f)) % frames.Length];

            float scale = height / 1.8f;
            float size = Mathf.Clamp(Size, 0.5f, 5f);
            if (!placed || size != lastSize) { sim.Setup(0.42f * height * size, 0.75f * height * size); lastSize = size; }

            var f = face != null ? face.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            var back = -f.normalized;
            if (Flip) back = -back;
            var right = Vector3.Cross(back, Vector3.up);
            float backOff = (lift > 0.01f ? 0.08f : 0.05f) * height;
            var a = bone.position + Vector3.up * ((lift + Mathf.Clamp(Height, -1f, 1f) * 0.3f) * height);

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (!placed || (a - lastA).magnitude > 5f * Mathf.Max(1f, scale))
            {
                sim.Reset(a, right, back, backOff);
                vel = Vector3.zero; placed = true;
            }
            else if (dt > 0f)
            {
                var v = (a - lastA) / dt;
                vel = Vector3.Lerp(vel, v, 0.25f);
                var air = -new Vector3(vel.x, 0f, vel.z) * 0.8f;
                float mag = air.magnitude, cap = 12f * scale;
                if (mag > cap) air *= cap / mag;
                int n = Mathf.Clamp(Mathf.CeilToInt(dt * 60f), 1, 4);
                float h = dt / n;
                for (int s = 0; s < n; s++) { simTime += h; sim.Step(h, a, right, back, backOff, air, scale, simTime); }
            }
            lastA = a;
            Draw();
        }

        void Draw()
        {
            for (int k = 0; k < sim.P.Length; k++) { verts[k] = sim.P[k]; if (verts.Length > sim.P.Length) verts[k + sim.P.Length] = sim.P[k]; }
            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        // ── the cape's mesh and material ──

        bool Build()
        {
            Shader sh = null;
            foreach (var name in Shaders) { sh = Shader.Find(name); if (sh != null) break; }
            if (sh == null)
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.shader != null && m.HasProperty("_MainTex") && m.shader.name.IndexOf("Particles", StringComparison.Ordinal) >= 0) { sh = m.shader; break; }
            if (sh == null) { Status = "no shader for the cape in this build"; Design = "off"; Debug.Log("[Resurf] cape: " + Status); return false; }
            // Sprites and particles are drawn on both sides already
            bool twoSided = !(sh.name.StartsWith("Sprites/", StringComparison.Ordinal) || sh.name.IndexOf("Particles", StringComparison.Ordinal) >= 0);
            Debug.Log("[Resurf] cape: drawn with " + sh.name);

            go = new GameObject("ResurfCape");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            if (Who != null) Who.Ignore.Add(go.transform);
            mat = new Material(sh);
            mat.name = "Resurf cape";
            mesh = new Mesh();
            mesh.name = "Resurf cape";
            int n = Cols * Rows;
            verts = new Vector3[twoSided ? n * 2 : n];
            var uv = new Vector2[verts.Length];
            var tris = new List<int>();
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    var t = new Vector2((float)i / (Cols - 1), 1f - (float)j / (Rows - 1));
                    uv[j * Cols + i] = t;
                    if (twoSided) uv[n + j * Cols + i] = t;
                    if (i == Cols - 1 || j == Rows - 1) continue;
                    int A = j * Cols + i, B = A + 1, C = A + Cols, D = C + 1;
                    tris.Add(A); tris.Add(B); tris.Add(C); tris.Add(B); tris.Add(D); tris.Add(C);
                    if (twoSided) { tris.Add(n + A); tris.Add(n + C); tris.Add(n + B); tris.Add(n + B); tris.Add(n + C); tris.Add(n + D); }
                }
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return true;
        }
    }
}
