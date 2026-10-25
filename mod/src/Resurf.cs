// Resurf – a small mod for the Mac build of Subway Surfers Resurferd, put into the app by the Lotus plugin resurferd.
// Unity Doorstop loads it when the game starts (Doorstop.Entrypoint.Start below – the app's program is a launcher
// that sets Doorstop up); nothing of the game's own code is changed. It talks with Lotus through files in
// <persistentDataPath>/resurf:
//   inbox/<id>.req   from Lotus: "coins +5000" or "coins =5000" – done, then deleted
//   settings         from Lotus (and the F key): FPS=on|off, GLOW=fix|original
//   save.txt         to Lotus: coins, high score, best distance … and LAST=<id of the newest request done>
// What it does:
//   - the coins' glow (and the mystery box's): drawn additively, as a glow is – the project draws it with a
//     spray-paint shader that blends, so the dark pixels around it make a black edge
//   - the game's own save (playerdata) is written every 20 seconds, when the window loses focus and when the game
//     quits – the game itself saves only after a run's coins are counted and when a phone sends it away
//   - an FPS counter, bottom left (F switches it on and off)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Doorstop
{
    // Unity Doorstop calls this as soon as Mono runs – before Unity is ready, so nothing of Unity is touched here:
    // it waits for the game's own code (Unity loads it for the first scene), and Resurf.Boot waits for that scene.
    public static class Entrypoint
    {
        public static void Start()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Assembly-CSharp") { Resurf.Boot.Hook(); return; }
            AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
        }

        static void OnLoad(object sender, AssemblyLoadEventArgs e)
        {
            if (e.LoadedAssembly.GetName().Name != "Assembly-CSharp") return;
            AppDomain.CurrentDomain.AssemblyLoad -= OnLoad;
            Resurf.Boot.Hook();
        }
    }
}

namespace Resurf
{
    public static class Boot
    {
        public const string Version = "1.0";

        // The first loaded scene starts the mod (Unity is ready then)
        public static void Hook()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Start();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Start()
        {
            if (Mod.Instance != null) return;
            var go = new GameObject("Resurf");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Mod>();
        }
    }

    // The game's PlayerInfo, through reflection: the mod keeps working when the game's code changes a little
    static class Player
    {
        static readonly Type T = Type.GetType("PlayerInfo, Assembly-CSharp");

        static object Info
        {
            get
            {
                var p = T == null ? null : T.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                return p == null ? null : p.GetValue(null, null);
            }
        }

        public static bool There { get { return Info != null; } }

        public static object Get(string name)
        {
            try
            {
                var info = Info;
                var p = info == null ? null : T.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p == null ? null : p.GetValue(info, null);
            }
            catch (Exception) { return null; }
        }

        public static int Coins
        {
            get { var v = Get("amountOfCoins"); return v is int ? (int)v : 0; }
            set
            {
                var p = T.GetProperty("amountOfCoins", BindingFlags.Public | BindingFlags.Instance);
                p.SetValue(Info, value, null);
            }
        }

        public static void Save()
        {
            var info = Info;
            var m = info == null ? null : T.GetMethod("SaveIfDirty", BindingFlags.Public | BindingFlags.Instance);
            if (m != null) m.Invoke(info, null);
        }
    }

    public class Mod : MonoBehaviour
    {
        public static Mod Instance;

        // what save.txt holds: its key, PlayerInfo's property
        static readonly string[,] Saved = {
            { "COINS", "amountOfCoins" }, { "HIGHSCORE", "highestScore" }, { "BEST_METERS", "highestMeters" },
            { "CHARACTER", "currentCharacter" }, { "MYSTERY_BOXES", "amountOfMysteryBoxesOpened" },
            { "SUPER_MYSTERY_BOXES", "amountOfSuperMysteryBoxesOpened" }, { "MISSIONS", "missionCompletedSum" },
            { "MULTIPLIER", "scoreMultiplier" }, { "DAILY_WORDS_IN_A_ROW", "dailyWordInRow" },
            { "DOUBLE_COINS", "hasDoubleCoins" }, { "TUTORIAL_DONE", "tutorialCompleted" } };

        // the materials that draw a glow with the spray-paint shader
        static readonly string[] Glows = { "glow gold", "EFX" };
        const string Additive = "Mobile/Particles/Additive";

        string dir, inbox, settingsFile, saveFile;
        DateTime settingsTime;
        bool fps, fixGlow = true, glowSaid, saveSaid;
        string last = "";
        float nextInbox, nextSettings, nextSave, glowUntil, nextGlow;
        readonly HashSet<int> fixedGlows = new HashSet<int>();

        // the FPS counter: frames in the last half second
        int frames;
        float fpsTime, fpsShown;
        GUIStyle style;

        void Awake()
        {
            Instance = this;
            dir = Path.Combine(Application.persistentDataPath, "resurf");
            inbox = Path.Combine(dir, "inbox");
            settingsFile = Path.Combine(dir, "settings");
            saveFile = Path.Combine(dir, "save.txt");
            try { Directory.CreateDirectory(inbox); } catch (Exception e) { Log("no folder " + dir + ": " + e.Message); }
            ReadSettings(true);
            Log("mod " + Boot.Version + " loaded – " + dir + (Player.There ? "" : " (the game's PlayerInfo is missing)"));
            if (Player.There) Log("coins " + Player.Coins + " (from the game's save)");
            SceneManager.sceneLoaded += OnSceneLoaded;
            glowUntil = Time.realtimeSinceStartup + 30f;
            DoInbox();
            WriteSave("start");
        }

        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) { glowUntil = Time.realtimeSinceStartup + 30f; nextGlow = 0f; }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            frames++;
            if (now - fpsTime >= 0.5f) { fpsShown = frames / (now - fpsTime); frames = 0; fpsTime = now; }
            if (Input.GetKeyDown(KeyCode.F)) { fps = !fps; WriteSettings(); }
            if (now >= nextInbox) { nextInbox = now + 0.5f; DoInbox(); }
            if (now >= nextSettings) { nextSettings = now + 1f; ReadSettings(false); }
            if (now >= nextSave) { nextSave = now + 20f; SaveGame("every 20 seconds"); }
            // prefabs come later than the scene (the coins' pool, popups): look again for a while
            if (fixGlow && now < glowUntil && now >= nextGlow) { nextGlow = now + 2f; FixGlows(); }
            if (fixGlow && now >= glowUntil && !glowSaid) { glowSaid = true; Log("glow: no glow material came in 30 seconds"); }
        }

        void OnApplicationFocus(bool focus) { if (!focus) SaveGame("the window lost focus"); }

        void OnApplicationQuit() { SaveGame("quit"); }

        // ── the glow ─────────────────────────────────────────────

        void FixGlows()
        {
            var shader = Shader.Find(Additive);
            var seen = new List<string>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null) continue;
                bool glow = false;
                foreach (var g in Glows) if (m.name == g || m.name.StartsWith(g + " ")) glow = true;
                if (!glow) continue;
                var was = m.shader == null ? "no shader" : m.shader.name;
                seen.Add(m.name + " (" + was + ")");
                if (shader == null || was != "Mobile/SprayPaint" || fixedGlows.Contains(m.GetInstanceID())) continue;
                m.shader = shader;
                fixedGlows.Add(m.GetInstanceID());
                Log("glow: " + m.name + " is drawn with " + Additive + " now");
            }
            // once: what there is – for when nothing above matched
            if (seen.Count > 0 && !glowSaid)
            {
                glowSaid = true;
                Log("glow: found " + string.Join(", ", seen.ToArray()) + (shader == null ? " – there is no " + Additive + " in this build" : ""));
            }
        }

        // ── coins from Lotus ─────────────────────────────────────

        void DoInbox()
        {
            if (!Player.There) return;
            string[] files;
            try { files = Directory.GetFiles(inbox, "*.req"); } catch (Exception) { return; }
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                string line;
                try { line = File.ReadAllText(f).Trim(); File.Delete(f); } catch (Exception) { continue; }
                var id = Path.GetFileNameWithoutExtension(f);
                var w = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                long n;
                if (w.Length == 2 && w[0] == "coins" && w[1].Length > 1 && (w[1][0] == '+' || w[1][0] == '=')
                    && long.TryParse(w[1].Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                {
                    long coins = w[1][0] == '+' ? Player.Coins + n : n;
                    coins = Math.Max(0, Math.Min(int.MaxValue, coins));
                    Player.Coins = (int)coins;
                    Log("coins " + w[1] + " → " + coins);
                }
                else
                {
                    Log("not understood: " + line);
                }
                last = id;
            }
            SaveGame("coins from Lotus");
        }

        // ── the save ─────────────────────────────────────────────

        void SaveGame(string why)
        {
            if (!Player.There) return;
            try { Player.Save(); }
            catch (Exception e) { Log("the game could not save: " + (e.InnerException ?? e).Message); }
            WriteSave(why);
        }

        void WriteSave(string why)
        {
            if (!Player.There) return;
            var sb = new StringBuilder();
            sb.Append("# Subway Surfers Resurferd – written by the Resurf mod (").Append(why).Append(")\n");
            for (int i = 0; i < Saved.GetLength(0); i++)
            {
                var v = Player.Get(Saved[i, 1]);
                if (v == null) continue;
                sb.Append(Saved[i, 0]).Append('=').Append(v is bool ? ((bool)v ? "yes" : "no") : Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');
            }
            sb.Append("SAVED=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("LAST=").Append(last).Append('\n');
            sb.Append("MOD=").Append(Boot.Version).Append('\n');
            try
            {
                var tmp = saveFile + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(saveFile)) File.Delete(saveFile);
                File.Move(tmp, saveFile);
                if (!saveSaid) { saveSaid = true; Log("save: " + saveFile + " (" + why + ")"); }
            }
            catch (Exception e) { Log("could not write " + saveFile + ": " + e.Message); }
        }

        // ── settings ─────────────────────────────────────────────

        void ReadSettings(bool first)
        {
            try
            {
                if (!File.Exists(settingsFile)) return;
                var t = File.GetLastWriteTimeUtc(settingsFile);
                if (!first && t == settingsTime) return;
                settingsTime = t;
                foreach (var raw in File.ReadAllLines(settingsFile))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("FPS=")) fps = line.Substring(4) == "on";
                    if (line.StartsWith("GLOW=")) fixGlow = line.Substring(5) != "original";
                }
            }
            catch (Exception) { }
        }

        void WriteSettings()
        {
            try
            {
                var lines = new List<string>();
                if (File.Exists(settingsFile))
                    foreach (var l in File.ReadAllLines(settingsFile)) if (!l.StartsWith("FPS=") && l.Trim().Length > 0) lines.Add(l);
                lines.Add("FPS=" + (fps ? "on" : "off"));
                File.WriteAllText(settingsFile, string.Join("\n", lines.ToArray()) + "\n");
                settingsTime = File.GetLastWriteTimeUtc(settingsFile);
            }
            catch (Exception) { }
        }

        // ── the FPS counter ──────────────────────────────────────

        void OnGUI()
        {
            if (!fps) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontStyle = FontStyle.Bold;
                style.alignment = TextAnchor.MiddleCenter;
            }
            int size = Mathf.Max(12, Screen.height / 36);
            style.fontSize = size;
            style.normal.textColor = fpsShown >= 50f ? new Color(0.45f, 1f, 0.45f) : fpsShown >= 30f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);
            var text = Mathf.RoundToInt(fpsShown) + " FPS";
            var r = new Rect(size * 0.6f, Screen.height - size * 2.4f, size * 5.2f, size * 1.8f);
            var back = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = back;
            GUI.Label(r, text, style);
        }

        static void Log(string s) { Debug.Log("[Resurf] " + s); }
    }
}
