// Resurf – a small mod for the Mac build of Subway Surfers Resurferd, put into the app by the Lotus plugin resurferd.
// Unity Doorstop loads it when the game starts (Doorstop.Entrypoint.Start below – the app's program is a launcher
// that sets Doorstop up); nothing of the game's own code is changed. It talks with Lotus through files in
// <persistentDataPath>/resurf:
//   inbox/<id>.req   from Lotus: "coins +5000" or "coins =5000" – done, then deleted
//   settings         from Lotus, the F key and the menu: FPS=on|off, GLOW=fix|original, SPEED=<0.25–3>, AUTOSAVE=<seconds, 0 = off>,
//                    CAPE=off|inferno|galaxy, CAPE_SIZE=<0.5–1.6>, CAPE_HEIGHT=<-1–1>, CAPE_FLIP=on|off
//   save.txt         to Lotus: coins, high score, best distance … and LAST=<id of the newest request done>
// What it does:
//   - the coins' glow (and the mystery box's): drawn additively, as a glow is – the project draws it with a
//     spray-paint shader that blends, so the dark pixels around it make a black edge
//   - the game's own save (playerdata) is written every 20 seconds, when the window loses focus and when the game
//     quits – the game itself saves only after a run's coins are counted and when a phone sends it away
//   - an FPS counter, bottom left (F switches it on and off)
//   - a menu (M or F1) to change all of that in the game: FPS counter, glow, game speed, how often it saves, coins
//   - a cape for the runner (Cape.cs): two designs, Inferno and Galaxy, chosen in the menu
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
        public const string Version = "1.3";

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
        // the glow materials the mod changed, with the shader they had – the menu can give it back
        readonly Dictionary<int, KeyValuePair<Material, Shader>> fixedGlows = new Dictionary<int, KeyValuePair<Material, Shader>>();

        // the menu (M or F1)
        static readonly int[] AutosaveChoices = { 0, 10, 20, 60 };
        bool menu, speedDirty, capeDirty;
        readonly Cape cape = new Cape();
        float speed = 1f, speedApplied = 1f;
        int autosave = 20;
        GUIStyle menuTitle, menuNote;

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

        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; cape.Dispose(); }

        // after the animations of the frame: the cape follows the runner
        void LateUpdate()
        {
            try { cape.Tick(); }
            catch (Exception e) { Log("cape: " + e.Message + " – the cape is off"); cape.Design = "off"; }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) { glowUntil = Time.realtimeSinceStartup + 30f; nextGlow = 0f; cape.Forget(); }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            frames++;
            if (now - fpsTime >= 0.5f) { fpsShown = frames / (now - fpsTime); frames = 0; fpsTime = now; }
            if (Input.GetKeyDown(KeyCode.F)) { fps = !fps; WriteSettings(); }
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.F1)) menu = !menu;
            else if (menu && Input.GetKeyDown(KeyCode.Escape)) menu = false;
            ApplySpeed();
            if (now >= nextInbox) { nextInbox = now + 0.5f; DoInbox(); }
            if (now >= nextSettings) { nextSettings = now + 1f; ReadSettings(false); }
            if (autosave > 0 && now >= nextSave) { nextSave = now + autosave; SaveGame("every " + autosave + " seconds"); }
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
                // the stand-ins of the project: Mobile/SprayPaint, and in the release's build Mobile/SprayPaint_TextShadow –
                // which takes its alpha from main.png, a texture without one: an opaque square around every coin
                if (shader == null || !was.StartsWith("Mobile/SprayPaint") || fixedGlows.ContainsKey(m.GetInstanceID())) continue;
                fixedGlows.Add(m.GetInstanceID(), new KeyValuePair<Material, Shader>(m, m.shader));
                m.shader = shader;
                Log("glow: " + m.name + " is drawn with " + Additive + " now");
            }
            // once: what there is – for when nothing above matched
            if (seen.Count > 0 && !glowSaid)
            {
                glowSaid = true;
                Log("glow: found " + string.Join(", ", seen.ToArray()) + (shader == null ? " – there is no " + Additive + " in this build" : ""));
            }
        }

        // the glow as the project draws it: the shaders the mod replaced come back
        void RestoreGlows()
        {
            foreach (var kv in fixedGlows.Values)
                if (kv.Key != null && kv.Value != null) kv.Key.shader = kv.Value;
            fixedGlows.Clear();
            glowSaid = false;
            Log("glow: drawn as the project draws it again");
        }

        void SetGlow(bool on)
        {
            if (on == fixGlow) return;
            fixGlow = on;
            if (on) { glowUntil = Time.realtimeSinceStartup + 30f; nextGlow = 0f; }
            else RestoreGlows();
            WriteSettings();
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
                    if (line.StartsWith("GLOW="))
                    {
                        var want = line.Substring(5) != "original";
                        if (first) fixGlow = want; else SetGlowFromFile(want);
                    }
                    float f;
                    int n;
                    if (line.StartsWith("SPEED=") && float.TryParse(line.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        speed = Mathf.Clamp(f, 0.25f, 3f);
                    if (line.StartsWith("CAPE=")) { var d = line.Substring(5).Trim(); cape.Design = Array.IndexOf(CapeArt.Designs, d) >= 0 ? d : "off"; }
                    if (line.StartsWith("CAPE_SIZE=") && float.TryParse(line.Substring(10), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        cape.Size = Mathf.Clamp(f, 0.5f, 1.6f);
                    if (line.StartsWith("CAPE_HEIGHT=") && float.TryParse(line.Substring(12), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        cape.Height = Mathf.Clamp(f, -1f, 1f);
                    if (line.StartsWith("CAPE_FLIP=")) cape.Flip = line.Substring(10).Trim() == "on";
                    if (line.StartsWith("AUTOSAVE=") && int.TryParse(line.Substring(9), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                        autosave = n;
                }
            }
            catch (Exception) { }
        }

        // a glow switched by Lotus while the game runs: the same as in the menu, but the file is already right
        void SetGlowFromFile(bool on)
        {
            if (on == fixGlow) return;
            fixGlow = on;
            if (on) { glowUntil = Time.realtimeSinceStartup + 30f; nextGlow = 0f; }
            else RestoreGlows();
        }

        // the settings the mod and the menu own – the other lines of the file (Lotus's own) stay as they are
        static readonly string[] Own = { "FPS=", "GLOW=", "SPEED=", "AUTOSAVE=", "CAPE=", "CAPE_SIZE=", "CAPE_HEIGHT=", "CAPE_FLIP=" };

        void WriteSettings()
        {
            try
            {
                var lines = new List<string>();
                if (File.Exists(settingsFile))
                    foreach (var l in File.ReadAllLines(settingsFile))
                    {
                        bool own = false;
                        foreach (var k in Own) if (l.StartsWith(k)) own = true;
                        if (!own && l.Trim().Length > 0) lines.Add(l);
                    }
                lines.Add("FPS=" + (fps ? "on" : "off"));
                lines.Add("GLOW=" + (fixGlow ? "fix" : "original"));
                lines.Add("SPEED=" + speed.ToString("0.##", CultureInfo.InvariantCulture));
                lines.Add("AUTOSAVE=" + autosave.ToString(CultureInfo.InvariantCulture));
                lines.Add("CAPE=" + cape.Design);
                lines.Add("CAPE_SIZE=" + cape.Size.ToString("0.##", CultureInfo.InvariantCulture));
                lines.Add("CAPE_HEIGHT=" + cape.Height.ToString("0.##", CultureInfo.InvariantCulture));
                lines.Add("CAPE_FLIP=" + (cape.Flip ? "on" : "off"));
                File.WriteAllText(settingsFile, string.Join("\n", lines.ToArray()) + "\n");
                settingsTime = File.GetLastWriteTimeUtc(settingsFile);
            }
            catch (Exception) { }
        }

        // ── the menu (M or F1) ───────────────────────────────────

        // the game's speed: Time.timeScale – only while the game is not paused (it sets 0 for that itself)
        void ApplySpeed()
        {
            if (speed == 1f && speedApplied == 1f) return;
            if (Time.timeScale < 0.01f) return;
            Time.timeScale = speed;
            speedApplied = speed;
        }

        static string Num(int n) { return n.ToString("N0", CultureInfo.InvariantCulture); }

        void DrawMenu()
        {
            if (menuTitle == null)
            {
                menuTitle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 20, alignment = TextAnchor.MiddleLeft };
                menuNote = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                menuNote.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
            }
            // drawn on a 720-high screen, scaled to the real one
            float scale = Screen.height / 720f;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = 360f, h = 700f;
            var area = new Rect(20f, 10f, w, h);
            var back = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = back;
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, w - 28f, h - 20f));
            GUILayout.Label("Resurf mod " + Boot.Version, menuTitle);

            bool newFps = GUILayout.Toggle(fps, " FPS counter  (F)");
            if (newFps != fps) { fps = newFps; WriteSettings(); }

            bool newGlow = GUILayout.Toggle(fixGlow, " Coins' glow without the black edge");
            if (newGlow != fixGlow) SetGlow(newGlow);

            DrawCapeMenu();

            GUILayout.Space(8f);
            GUILayout.Label("Game speed: " + speed.ToString("0.00", CultureInfo.InvariantCulture) + "x");
            float newSpeed = Mathf.Round(GUILayout.HorizontalSlider(speed, 0.25f, 3f) * 20f) / 20f;
            if (newSpeed != speed) { speed = newSpeed; speedDirty = true; }
            if (speedDirty && Event.current.type == EventType.MouseUp) { speedDirty = false; WriteSettings(); }   // once, not on every step of the drag
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x")) { speed = 0.5f; WriteSettings(); }
            if (GUILayout.Button("1x")) { speed = 1f; WriteSettings(); }
            if (GUILayout.Button("1.5x")) { speed = 1.5f; WriteSettings(); }
            if (GUILayout.Button("2x")) { speed = 2f; WriteSettings(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Saves the game every …");
            int sel = Array.IndexOf(AutosaveChoices, autosave);
            var names = new[] { "never", "10 s", "20 s", "60 s" };
            int pick = GUILayout.Toolbar(sel, names);
            if (pick != sel && pick >= 0) { autosave = AutosaveChoices[pick]; nextSave = Time.realtimeSinceStartup + autosave; WriteSettings(); }
            if (GUILayout.Button("Save now")) SaveGame("the menu");

            GUILayout.Space(8f);
            GUILayout.Label(Player.There ? "Coins: " + Num(Player.Coins) : "Coins: the game's save is not ready yet");
            if (Player.There)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("+100")) AddCoins(100);
                if (GUILayout.Button("+1,000")) AddCoins(1000);
                if (GUILayout.Button("+10,000")) AddCoins(10000);
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("M or F1 closes the menu. A click here can reach the game too – use it while the game is paused or on the start screen.", menuNote);
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        // the cape: the design (with a picture of it), how big, how high, which way round
        void DrawCapeMenu()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Cape", menuTitle);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            string[] names = { "none", "Inferno", "Galaxy" };
            int sel = Mathf.Max(0, Array.IndexOf(CapeArt.Designs, cape.Design));
            int pick = GUILayout.SelectionGrid(sel, names, 1);
            if (pick != sel) { cape.Design = CapeArt.Designs[pick]; WriteSettings(); }
            GUILayout.EndVertical();
            var pic = cape.Design == "off" ? null : cape.Preview(cape.Design);
            GUILayout.FlexibleSpace();
            var slot = GUILayoutUtility.GetRect(60f, 105f);
            if (pic != null) GUI.DrawTexture(new Rect(slot.x, slot.y, 60f, 105f), pic);
            GUILayout.EndHorizontal();
            if (cape.Design != "off")
            {
                GUILayout.Label("Size: " + cape.Size.ToString("0.00", CultureInfo.InvariantCulture) + "x");
                float size = Mathf.Round(GUILayout.HorizontalSlider(cape.Size, 0.5f, 1.6f) * 20f) / 20f;
                if (size != cape.Size) { cape.Size = size; capeDirty = true; }
                GUILayout.Label("Height: " + cape.Height.ToString("0.00", CultureInfo.InvariantCulture));
                float height = Mathf.Round(GUILayout.HorizontalSlider(cape.Height, -1f, 1f) * 20f) / 20f;
                if (height != cape.Height) { cape.Height = height; capeDirty = true; }
                bool flip = GUILayout.Toggle(cape.Flip, " Other way round (if it hangs at the front)");
                if (flip != cape.Flip) { cape.Flip = flip; WriteSettings(); }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Next character")) cape.Next();
                if (GUILayout.Button("Look again")) cape.Forget();
                GUILayout.EndHorizontal();
                if (cape.Status.Length > 0) GUILayout.Label(cape.Status, menuNote);
            }
            if (capeDirty && Event.current.type == EventType.MouseUp) { capeDirty = false; WriteSettings(); }
        }

        void AddCoins(int n)
        {
            if (!Player.There) return;
            long coins = Math.Min(int.MaxValue, (long)Player.Coins + n);
            Player.Coins = (int)coins;
            Log("coins +" + n + " → " + coins + " (menu)");
            SaveGame("the menu");
        }

        // ── the FPS counter ──────────────────────────────────────

        void OnGUI()
        {
            if (menu) DrawMenu();
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
