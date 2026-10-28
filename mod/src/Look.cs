// The look of the game (Resurf): sharper and a finish over the picture. A Unity 2019 player compiles no shaders, so
// the finish is made of the ones every player has: Hidden/Internal-Colored (Unity's own, any blending) and the game's
// Mobile/Particles/Additive. After the world camera has drawn (OnRenderImage) it lays over a copy of its picture:
//   glow       only what is bright (invert, add the threshold, invert: x − t), blurred at 1/8 and 1/16 of the size,
//              added – coins, lights and the sun shine
//   contrast   around the middle grey: invert, multiply, invert, multiply (blending can multiply, not subtract)
//   warmth     a multiply with a warm (or cool) colour, the lost brightness given back
//   vignette   the corners darker, a grid of black with alpha toward the edges
// Sharper is Unity's quality: 8× multisampling (the game's own level has none or 2×), anisotropic filtering on
// every texture (the track far ahead stays sharp), the full texture size. The interface draws with other cameras
// after the world, so the finish never touches it.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Resurf
{
    public class Look
    {
        // LOOK = off | pretty | vivid | dreamy | custom, the rest from its sliders; SHARP = on | off
        public static readonly string[] Presets = { "off", "pretty", "vivid", "dreamy" };
        public string Preset = "off";
        public float Glow, Contrast = 1f, Warmth, Vignette;
        public bool Sharp;
        public string Status = "";

        // what counts as bright for the glow
        const float Threshold = 0.55f;

        Material add, plus, multiply, multiply2, invert, alpha;
        bool shadersLooked, sharpOn;
        int oldAA, oldTextureLimit;
        AnisotropicFiltering oldAniso;
        Camera cam;
        LookFx fx;

        public bool Active { get { return Preset != "off" && (Glow > 0.005f || Mathf.Abs(Contrast - 1f) > 0.005f || Mathf.Abs(Warmth) > 0.005f || Vignette > 0.005f); } }

        // what a preset means
        public void Use(string preset)
        {
            Preset = preset;
            switch (preset)
            {
                case "pretty": Glow = 0.45f; Contrast = 1.1f;  Warmth = 0.25f; Vignette = 0.3f;  Sharp = true; break;
                case "vivid":  Glow = 0.35f; Contrast = 1.22f; Warmth = 0.1f;  Vignette = 0.2f;  Sharp = true; break;
                case "dreamy": Glow = 0.85f; Contrast = 1.04f; Warmth = 0.35f; Vignette = 0.45f; Sharp = true; break;
                case "off":    Glow = 0f;    Contrast = 1f;    Warmth = 0f;    Vignette = 0f;    Sharp = false; break;
            }
        }

        static Material Blend(Shader s, BlendMode src, BlendMode dst)
        {
            var m = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            m.SetInt("_SrcBlend", (int)src);
            m.SetInt("_DstBlend", (int)dst);
            m.SetInt("_Cull", (int)CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)CompareFunction.Always);
            return m;
        }

        void Shaders()
        {
            if (shadersLooked) return;
            shadersLooked = true;
            var colored = Shader.Find("Hidden/Internal-Colored");
            var additive = Shader.Find("Mobile/Particles/Additive");
            if (colored != null)
            {
                plus = Blend(colored, BlendMode.One, BlendMode.One);                     // dst + src
                multiply = Blend(colored, BlendMode.DstColor, BlendMode.Zero);           // dst · src
                multiply2 = Blend(colored, BlendMode.DstColor, BlendMode.SrcColor);      // 2 · dst · src
                invert = Blend(colored, BlendMode.OneMinusDstColor, BlendMode.Zero);     // 1 − dst (src white)
                alpha = Blend(colored, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            }
            if (additive != null) add = new Material(additive) { hideFlags = HideFlags.HideAndDontSave };
            Status = colored == null ? "this build has no Hidden/Internal-Colored – no finish, only sharper" : additive == null ? "no Mobile/Particles/Additive in this build – no glow" : "";
            Debug.Log("[Resurf] look: Hidden/Internal-Colored " + (colored != null ? "yes" : "no") + ", Mobile/Particles/Additive " + (additive != null ? "yes" : "no"));
        }

        // the camera of the world: the first that clears the picture (the others draw the interface over it)
        static Camera World()
        {
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.enabled || c.targetTexture != null) continue;
                if (c.clearFlags == CameraClearFlags.Depth || c.clearFlags == CameraClearFlags.Nothing) continue;
                if (best == null || c.depth < best.depth) best = c;
            }
            return best ?? Camera.main;
        }

        // every two seconds: quality as wanted, the finish on the world camera (a new scene brings a new one)
        public void Tick()
        {
            Shaders();
            SetSharp(Sharp);
            var want = Active && multiply != null ? World() : null;
            if (want == cam && (fx != null || want == null)) return;
            if (fx != null) UnityEngine.Object.Destroy(fx);
            fx = null;
            cam = want;
            if (cam == null) return;
            fx = cam.gameObject.AddComponent<LookFx>();
            fx.Owner = this;
            Debug.Log("[Resurf] look: " + Preset + " on the camera " + cam.name + " (depth " + cam.depth + ")");
        }

        void SetSharp(bool on)
        {
            if (on == sharpOn)
            {
                // the game can set its quality level again (that brings its own anti-aliasing): sharper once more
                if (on && (QualitySettings.antiAliasing != 8 || QualitySettings.anisotropicFiltering != AnisotropicFiltering.ForceEnable)) Sharpen();
                return;
            }
            sharpOn = on;
            if (on)
            {
                oldAA = QualitySettings.antiAliasing; oldAniso = QualitySettings.anisotropicFiltering; oldTextureLimit = QualitySettings.masterTextureLimit;
                Sharpen();
                Debug.Log("[Resurf] look: sharper – multisampling " + QualitySettings.antiAliasing + "× (was " + oldAA + "×), anisotropic filtering on every texture");
            }
            else
            {
                QualitySettings.antiAliasing = oldAA;
                QualitySettings.anisotropicFiltering = oldAniso;
                QualitySettings.masterTextureLimit = oldTextureLimit;
                Debug.Log("[Resurf] look: the game's own quality again");
            }
        }

        static void Sharpen()
        {
            QualitySettings.antiAliasing = 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(8, 16);
            QualitySettings.masterTextureLimit = 0;
        }

        // the whole of the active texture, one colour, through a blending material
        static void Fill(Material m, Color c)
        {
            m.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.Vertex3(0f, 0f, 0f); GL.Vertex3(0f, 1f, 0f); GL.Vertex3(1f, 1f, 0f); GL.Vertex3(1f, 0f, 0f);
            GL.End();
        }

        // a texture over the whole of the active one, through the additive shader: dst + texture · c
        static void Quad(Material m, Texture t, Color c)
        {
            m.mainTexture = t;
            m.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f); GL.TexCoord2(0f, 1f); GL.Vertex3(0f, 1f, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(1f, 1f, 0f); GL.TexCoord2(1f, 0f); GL.Vertex3(1f, 0f, 0f);
            GL.End();
        }

        // ·k, for k up to 2 (2 · dst · k/2)
        void Scale(float k) { k = Mathf.Clamp(k, 0f, 2f) * 0.5f; Fill(multiply2, new Color(k, k, k, 1f)); }

        static void Into(RenderTexture rt) { RenderTexture.active = rt; GL.PushMatrix(); GL.LoadOrtho(); }

        // 8 bits a channel, no depth buffer: every pass ends between 0 and 1 (an HDR picture would let x − t go below
        // zero), and no depth test can stop one
        static RenderTexture Temp(int w, int h)
        {
            var rt = RenderTexture.GetTemporary(Mathf.Max(1, w), Mathf.Max(1, h), 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            return rt;
        }

        static void Done() { GL.PopMatrix(); }

        public void Render(RenderTexture src, RenderTexture dst)
        {
            var old = RenderTexture.active;
            // the passes on a copy: dst may be the screen
            var work = Temp(src.width, src.height);
            Graphics.Blit(src, work);
            RenderTexture blur = Glow > 0.005f && add != null ? Bright(src) : null;
            Into(work);
            // the blur is two of them added – 0.6 brings that back near one
            if (blur != null) { float g = Mathf.Clamp01(Glow) * 0.6f; Quad(add, blur, new Color(g, g, g, 1f)); }
            if (Mathf.Abs(Contrast - 1f) > 0.005f)
            {
                // C around 0.5 with what blending has: 1 − c·(1 − x), then ·k – c = 2C / (1 + C), k = C / c
                float C = Mathf.Clamp(Contrast, 0.7f, 1.6f), c = 2f * C / (1f + C), k = C / c;
                Fill(invert, Color.white);
                Scale(c);
                Fill(invert, Color.white);
                Scale(k);
            }
            if (Mathf.Abs(Warmth) > 0.005f)
            {
                float t = Mathf.Clamp(Mathf.Abs(Warmth), 0f, 1f);
                var tint = Warmth > 0f ? new Color(1f, 1f - 0.04f * t, 1f - 0.11f * t, 1f) : new Color(1f - 0.11f * t, 1f - 0.03f * t, 1f, 1f);
                Fill(multiply, tint);
                Scale(1f + 0.05f * t);
            }
            if (Vignette > 0.005f) DrawVignette();
            Done();
            Graphics.Blit(work, dst);
            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(work);
            if (blur != null) RenderTexture.ReleaseTemporary(blur);
        }

        // the bright part of the picture, blurred: 1/2 → 1/4 (only above the threshold) → 1/8, and 1/16 added to that –
        // every step halves with one bilinear look, which is the mean of four pixels
        RenderTexture Bright(RenderTexture src)
        {
            int w = src.width, h = src.height;
            var half = Temp(w / 2, h / 2);
            var a = Temp(w / 4, h / 4);
            var b = Temp(w / 8, h / 8);
            var c = Temp(w / 16, h / 16);
            Graphics.Blit(src, half);
            Graphics.Blit(half, a);
            // max(0, x − t) / (1 − t): 1 − x, + t (white where x was dark), 1 − that, larger again – then alpha 1
            // everywhere, the additive shader takes the texture's (the camera's picture may have none)
            Into(a);
            Fill(invert, Color.white);
            Fill(plus, new Color(Threshold, Threshold, Threshold, 0f));
            Fill(invert, Color.white);
            Scale(1f / (1f - Threshold));
            Fill(plus, new Color(0f, 0f, 0f, 1f));
            Done();
            Graphics.Blit(a, b);
            Graphics.Blit(b, c);
            // a near glow and a wide one
            Into(b);
            Quad(add, c, Color.white);
            Done();
            RenderTexture.ReleaseTemporary(half); RenderTexture.ReleaseTemporary(a); RenderTexture.ReleaseTemporary(c);
            return b;
        }

        // black, its alpha growing toward the corners – a grid, so the edge is round and soft
        void DrawVignette()
        {
            const int X = 24, Y = 16;
            alpha.SetPass(0);
            GL.Begin(GL.QUADS);
            for (int j = 0; j < Y; j++)
                for (int i = 0; i < X; i++)
                {
                    Corner(i, j, X, Y); Corner(i, j + 1, X, Y); Corner(i + 1, j + 1, X, Y); Corner(i + 1, j, X, Y);
                }
            GL.End();
        }

        void Corner(int i, int j, int X, int Y)
        {
            float x = i / (float)X, y = j / (float)Y;
            float ex = (x - 0.5f) * 2f, ey = (y - 0.5f) * 2f;
            float r = Mathf.Sqrt(ex * ex * 0.55f + ey * ey * 0.45f);
            float a = Mathf.Clamp01(Vignette) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1.05f, r)) * 0.85f;
            GL.Color(new Color(0f, 0f, 0f, a));
            GL.Vertex3(x, y, 0f);
        }

        public void Dispose()
        {
            if (fx != null) UnityEngine.Object.Destroy(fx);
            SetSharp(false);
            foreach (var m in new[] { add, plus, multiply, multiply2, invert, alpha }) if (m != null) UnityEngine.Object.Destroy(m);
        }
    }

    // on the world camera: Unity hands it the picture the camera drew
    public class LookFx : MonoBehaviour
    {
        public Look Owner;
        bool failed;

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Owner == null || failed || !Owner.Active) { Graphics.Blit(src, dst); return; }
            try { Owner.Render(src, dst); }
            catch (Exception e)
            {
                failed = true;
                Owner.Status = "the look stopped: " + e.Message;
                Debug.Log("[Resurf] look: " + Owner.Status);
                Graphics.Blit(src, dst);
            }
        }
    }
}
