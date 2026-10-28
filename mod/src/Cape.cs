// The cape of the Resurf mod: cosmetics for the runner. Two designs are built in – Inferno (a cape of flames) and
// Galaxy (a night sky with a golden crescent) – drawn as textures the mod makes itself, so no file comes with it.
// The cape hangs from the character's neck or back (found by the names of the skeleton's bones), is a small piece of
// cloth that swings, trails when the runner runs and flutters – and is chosen in the mod's menu (F1).
using System;
using System.Collections.Generic;
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
        public string Design = "off";
        public float Size = 1f, Height = 0f;
        public bool Flip;
        public string Status = "";

        const int Cols = 7, Rows = 12;
        static readonly string[] Shaders = { "Sprites/Default", "Unlit/Transparent", "Mobile/Particles/Alpha Blended", "Particles/Alpha Blended", "Unlit/Texture", "Mobile/Diffuse" };

        readonly Dictionary<string, Texture2D[]> art = new Dictionary<string, Texture2D[]>();
        readonly ClothSim sim = new ClothSim(Cols, Rows);
        GameObject go, ownAnchor;
        Mesh mesh;
        Material mat;
        MeshRenderer rend;
        Vector3[] verts;
        Transform anchor, charRoot, charKey, pinKey;
        float charH = 1.8f, lift, nextFind, lastSize, simTime;
        Vector3 lastA, vel;
        bool placed;
        // which character: watch is one of its skinned meshes (is it on screen?), pin the name chosen by hand
        Renderer watch;
        string attached = "";
        float nextCheck;
        int unseen;

        public Texture2D Preview(string design)
        {
            var frames = Frames(design);
            return frames == null ? null : frames[0];
        }

        Texture2D[] Frames(string design)
        {
            Texture2D[] f;
            if (art.TryGetValue(design, out f)) return f;
            try
            {
                f = design == "inferno" ? CapeArt.Inferno(6) : design == "galaxy" ? CapeArt.Galaxy(6) : null;
            }
            catch (Exception e) { Debug.Log("[Resurf] cape: the design " + design + " could not be made: " + e.Message); f = null; }
            art[design] = f;
            return f;
        }

        // back to choosing by itself
        public void Forget() { anchor = null; placed = false; nextFind = 0f; pinKey = null; }

        // the next character that has a skinned model in the game, chosen by hand (the menu)
        public void Next()
        {
            var list = Candidates();
            if (list.Count == 0) { Status = "no character found"; return; }
            int at = -1;
            for (int k = 0; k < list.Count; k++) if (charKey != null && list[k].Key == charKey) at = k;
            var c = list[(at + 1) % list.Count];
            pinKey = c.Key;
            Attach(c, (at + 1) % list.Count, list.Count);
        }

        public void Dispose()
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            if (ownAnchor != null) UnityEngine.Object.Destroy(ownAnchor);
        }

        // once per frame, after the animations: LateUpdate
        public void Tick()
        {
            if (Design == "off" || Frames(Design) == null) { if (go != null && go.activeSelf) go.SetActive(false); Status = Design == "off" ? "" : "this design could not be made"; return; }
            float now = Time.realtimeSinceStartup;
            if (anchor == null || !anchor.gameObject.activeInHierarchy)
            {
                placed = false;
                if (go != null && go.activeSelf) go.SetActive(false);
                if (now < nextFind) return;
                nextFind = now + 1.5f;
                if (!Find()) { Status = "no character found – it comes when one is in the game"; return; }
                nextCheck = now + 1f;
            }
            // a character that is not on screen (the menu's, still in the scene) – look for the one that is
            if (pinKey == null && now >= nextCheck)
            {
                nextCheck = now + 1f;
                if (watch == null || !watch.isVisible) { if (++unseen >= 2) { unseen = 0; Find(); } }
                else unseen = 0;
            }
            if (go == null && !Build()) return;
            if (!go.activeSelf) go.SetActive(true);
            go.layer = watch != null ? watch.gameObject.layer : charRoot != null ? charRoot.gameObject.layer : 0;

            var frames = Frames(Design);
            mat.mainTexture = frames[(int)(now * (Design == "inferno" ? 12f : 5f)) % frames.Length];

            float scale = charH / 1.8f;
            float size = Mathf.Clamp(Size, 0.5f, 3f);
            if (!placed || size != lastSize) { sim.Setup(0.42f * charH * size, 0.75f * charH * size); lastSize = size; }

            var f = charRoot != null ? charRoot.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            var back = -f.normalized;
            if (Flip) back = -back;
            var right = Vector3.Cross(back, Vector3.up);
            float backOff = (lift > 0.01f ? 0.08f : 0.05f) * charH;
            var a = anchor.position + Vector3.up * ((lift + Mathf.Clamp(Height, -1f, 1f) * 0.3f) * charH);

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
            Status = attached;
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

        // ── the character ──

        // the neck and the chest hold a cape best; the lift brings a lower bone up to the shoulders (in character heights)
        static int BoneScore(string name, out float lift)
        {
            lift = 0f;
            var n = name.ToLowerInvariant();
            if (n.IndexOf("neck", StringComparison.Ordinal) < 0 && n.IndexOf("chest", StringComparison.Ordinal) < 0 && n.IndexOf("spine", StringComparison.Ordinal) < 0) return 0;
            foreach (var bad in new[] { "twist", "nub", "end", "helper", "target", "pole", "hair", "tail" }) if (n.IndexOf(bad, StringComparison.Ordinal) >= 0) return 0;
            if (n.IndexOf("neck", StringComparison.Ordinal) >= 0) return 4;
            if (n.IndexOf("chest", StringComparison.Ordinal) >= 0 || n.EndsWith("spine2", StringComparison.Ordinal) || n.EndsWith("spine_02", StringComparison.Ordinal) || n.EndsWith("spine3", StringComparison.Ordinal) || n.EndsWith("spine_03", StringComparison.Ordinal)) { lift = 0.1f; return 3; }
            if (n.EndsWith("spine1", StringComparison.Ordinal) || n.EndsWith("spine_01", StringComparison.Ordinal)) { lift = 0.14f; return 2; }
            // the game's own skeleton: jLowerSpine, jUpperSpine … – the upper one is at the shoulders, the lower one at the hips
            if (n.IndexOf("upper", StringComparison.Ordinal) >= 0 && n.IndexOf("spine", StringComparison.Ordinal) >= 0) { lift = 0.08f; return 3; }
            if (n.IndexOf("lower", StringComparison.Ordinal) >= 0 && n.IndexOf("spine", StringComparison.Ordinal) >= 0) { lift = 0.3f; return 1; }
            if (n.IndexOf("spine", StringComparison.Ordinal) >= 0) { lift = 0.22f; return 1; }
            return 0;
        }

        static bool IsSkinned(Renderer r) { return r != null && r.GetType().Name == "SkinnedMeshRenderer"; }

        // the skeleton's root bone of a skinned mesh (SkinnedMeshRenderer.rootBone, through reflection: no module needed)
        static Transform RootBone(Renderer r)
        {
            try
            {
                var p = r.GetType().GetProperty("rootBone");
                return p == null ? null : p.GetValue(r, null) as Transform;
            }
            catch (Exception) { return null; }
        }

        // a character the game has: its model (the parent of the skinned mesh – the skeleton hangs there too)
        sealed class Cand
        {
            public Transform Root;      // the parent of the skinned mesh – gives the direction the character faces
            public Transform Key;       // the skeleton's root bone: one per character, even when many share a folder (avatars)
            public Renderer Skin;
            public Bounds Bounds;       // of all the skinned meshes of this character
            public bool Visible, Tagged, Foe;
            public float Dist;
        }

        // everything with a skinned model: the one tagged Player first, then what is on screen and not a chaser,
        // then the nearest to the camera
        List<Cand> Candidates()
        {
            var cam = Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            GameObject player = null;
            try { player = GameObject.FindWithTag("Player"); } catch (Exception) { }     // a tag the project does not have throws
            var byRoot = new Dictionary<Transform, Cand>();
            var list = new List<Cand>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!IsSkinned(r) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (go != null && r.transform.IsChildOf(go.transform)) continue;
                var root = r.transform.parent != null ? r.transform.parent : r.transform;
                var key = RootBone(r);
                if (key == null) key = root;
                Cand c;
                if (byRoot.TryGetValue(key, out c)) { c.Bounds.Encapsulate(r.bounds); if (r.isVisible && !c.Visible) { c.Visible = true; c.Skin = r; } continue; }
                c = new Cand { Root = root, Key = key, Skin = r, Bounds = r.bounds, Visible = r.isVisible, Dist = (r.bounds.center - camPos).sqrMagnitude };
                c.Tagged = player != null && (r.transform.IsChildOf(player.transform) || player.transform.IsChildOf(root));
                var n = root.name.ToLowerInvariant() + " " + r.name.ToLowerInvariant();
                foreach (var foe in new[] { "police", "guard", "inspector", "dog", "chaser", "enemy", "cop", "npc" }) if (n.IndexOf(foe, StringComparison.Ordinal) >= 0) c.Foe = true;
                byRoot[key] = c;
                list.Add(c);
            }
            list.Sort((x, y) =>
            {
                if (x.Tagged != y.Tagged) return x.Tagged ? -1 : 1;
                if (x.Foe != y.Foe) return x.Foe ? 1 : -1;
                if (x.Visible != y.Visible) return x.Visible ? -1 : 1;
                return x.Dist.CompareTo(y.Dist);
            });
            return list;
        }

        static float HeightOf(Bounds b)
        {
            float h = b.size.y;
            return h < 0.05f || h > 60f ? 1.8f : h;
        }

        // the best bone for a cape in the character's own skeleton (not in the folder with all the others)
        static Transform BestBone(Cand c, out float lift)
        {
            lift = 0f;
            Transform best = null;
            int bestScore = 0;
            var scope = c.Key != null && c.Key != c.Root ? c.Key : c.Root;
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                float l;
                int sc = BoneScore(t.name, out l);
                if (sc > bestScore) { best = t; bestScore = sc; lift = l; }
            }
            return best;
        }

        bool Find()
        {
            var list = Candidates();
            if (list.Count == 0) return false;
            int pick = 0;
            if (pinKey != null)
                for (int k = 0; k < list.Count; k++) if (list[k].Key == pinKey) { pick = k; break; }
            if (anchor != null && anchor.gameObject.activeInHierarchy && list[pick].Key == charKey) { watch = list[pick].Skin; return true; }
            Attach(list[pick], pick, list.Count);
            return true;
        }

        void Attach(Cand c, int index, int count)
        {
            float l;
            var bone = BestBone(c, out l);
            string what;
            if (bone != null) what = "on " + bone.name;
            else
            {
                // no bone with a name we know: the cape on the model's back
                var b = c.Skin.bounds;
                if (ownAnchor == null) { ownAnchor = new GameObject("ResurfCapeAnchor"); ownAnchor.hideFlags = HideFlags.HideInHierarchy; }
                ownAnchor.transform.SetParent(c.Skin.transform, true);
                ownAnchor.transform.position = new Vector3(b.center.x, b.max.y - 0.2f * b.size.y, b.center.z);
                bone = ownAnchor.transform; l = 0f; what = "on the model's back (no known bone)";
            }
            anchor = bone; charRoot = c.Root; charKey = c.Key; watch = c.Skin; lift = l; charH = HeightOf(c.Bounds); placed = false;
            attached = "Character " + (index + 1) + " of " + count + ": " + c.Skin.name + " – " + what + (c.Visible ? "" : " (not on screen)");
            Debug.Log("[Resurf] cape: " + attached + ", " + charH.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " high");
        }
    }
}
