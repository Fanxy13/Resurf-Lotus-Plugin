// Skins: another 3D model for the runner. A .glb file in the skins folder (<the mod's folder>/skins) is read on a
// worker thread (Gltf.cs), built as a skinned model of its own and put on the game's character: the game's model is
// made invisible, and every frame the bones of the skin take the directions of the bones of the game's character –
// hips, spine, neck, arms and legs are matched by the roles of their names (RigNames.cs), so the skin runs, jumps and
// rolls as the game's character does. The skin's size follows the character's, and the cape can hang from its neck.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Resurf
{
    // a skeleton, read by the roles of its bones
    sealed class RigMap
    {
        public Transform Hips, Neck, Head;
        public readonly List<Transform> Spine = new List<Transform>();
        public readonly Dictionary<string, Transform[]> Limbs = new Dictionary<string, Transform[]>();     // role → [left, right]
        public bool NamedSides;
        public Vector3 NamedRight;      // from the left to the right side, where the names tell which is which
        public string Report = "";

        sealed class Item { public Transform T; public string Role; public int Side, Rank, Depth; }

        static readonly string[] LimbRoles = { RigNames.Shoulder, RigNames.UpperArm, RigNames.ForeArm, RigNames.Hand, RigNames.Thigh, RigNames.Calf, RigNames.Foot };

        static int DepthOf(Transform t) { int d = 0; for (var p = t; p != null && d < 64; p = p.parent) d++; return d; }

        public Transform Limb(string role, int side)
        {
            Transform[] a;
            return Limbs.TryGetValue(role, out a) ? a[side < 0 ? 0 : 1] : null;
        }

        static Item Best(List<Item> items, string role)
        {
            Item best = null;
            foreach (var it in items)
                if (it.Role == role && (best == null || it.Rank < best.Rank || it.Rank == best.Rank && it.Depth < best.Depth)) best = it;
            return best;
        }

        // rightAxis: where the character's right hand side is, for bones whose names do not say left or right
        public static RigMap Read(IEnumerable<Transform> bones, Vector3 rightAxis)
        {
            var map = new RigMap();
            var items = new List<Item>();
            var seen = new HashSet<Transform>();
            foreach (var b in bones)
            {
                if (b == null || !seen.Add(b)) continue;
                int side, rank;
                var role = RigNames.Role(b.name, out side, out rank);
                if (role == null) continue;
                items.Add(new Item { T = b, Role = role, Side = side, Rank = rank, Depth = DepthOf(b) });
            }
            var hips = Best(items, RigNames.Hips); map.Hips = hips == null ? null : hips.T;
            var neck = Best(items, RigNames.Neck); map.Neck = neck == null ? null : neck.T;
            var head = Best(items, RigNames.Head); map.Head = head == null ? null : head.T;
            var spine = items.FindAll(i => i.Role == RigNames.Spine);
            spine.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.T.position.y.CompareTo(b.T.position.y));
            foreach (var s in spine) map.Spine.Add(s.T);

            // the sides: from the names, else from where the bone is
            Vector3 center = map.Hips != null ? map.Hips.position : Vector3.zero;
            if (map.Hips == null && items.Count > 0) { foreach (var it in items) center += it.T.position; center /= items.Count; }
            foreach (var it in items)
                if (Array.IndexOf(LimbRoles, it.Role) >= 0 && it.Side == 0)
                    it.Side = Vector3.Dot(it.T.position - center, rightAxis) < 0f ? -1 : 1;
            // a bone named only Leg or Arm: the thigh when the shin has its own name, else the shin when the thigh has
            foreach (int side in new[] { -1, 1 })
            {
                var generic = items.FindAll(i => i.Role == RigNames.Thigh && i.Rank == 3 && i.Side == side);
                if (generic.Count == 0) continue;
                generic.Sort((a, b) => a.Depth.CompareTo(b.Depth));
                bool strictThigh = items.Exists(i => i.Role == RigNames.Thigh && i.Rank < 3 && i.Side == side);
                bool strictCalf = items.Exists(i => i.Role == RigNames.Calf && i.Rank < 3 && i.Side == side);
                if (!strictThigh && !strictCalf) { if (generic.Count > 1) { generic[1].Role = RigNames.Calf; generic[1].Rank = 3; } }
                else if (strictThigh && !strictCalf) { generic[0].Role = RigNames.Calf; generic[0].Rank = 3; for (int g = 1; g < generic.Count; g++) generic[g].Role = "ignored"; }
                else if (!strictThigh) { for (int g = 1; g < generic.Count; g++) generic[g].Role = "ignored"; }
                else foreach (var g in generic) g.Role = "ignored";
            }
            var sb = new StringBuilder();
            foreach (var role in LimbRoles)
            {
                var pair = new Transform[2];
                for (int k = 0; k < 2; k++)
                {
                    int side = k == 0 ? -1 : 1;
                    Item best = null;
                    foreach (var it in items)
                        if (it.Role == role && it.Side == side && (best == null || it.Rank < best.Rank || it.Rank == best.Rank && it.Depth < best.Depth)) best = it;
                    pair[k] = best == null ? null : best.T;
                }
                map.Limbs[role] = pair;
                sb.Append(role).Append(": ").Append(pair[0] == null ? "-" : pair[0].name).Append(" | ").Append(pair[1] == null ? "-" : pair[1].name).Append('\n');
                // the direction left → right, where both names say which side they are
                if (pair[0] != null && pair[1] != null && RigNames.SideOf(pair[0].name) != 0 && RigNames.SideOf(pair[1].name) != 0 && role != RigNames.Hand && role != RigNames.Foot)
                {
                    var d = pair[1].position - pair[0].position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-8f) { map.NamedRight += d.normalized; map.NamedSides = true; }
                }
            }
            map.Report = "hips: " + (map.Hips == null ? "-" : map.Hips.name) + "\nspine: " + (map.Spine.Count == 0 ? "-" : string.Join(", ", map.Spine.ConvertAll(t => t.name).ToArray()))
                + "\nneck: " + (map.Neck == null ? "-" : map.Neck.name) + "\nhead: " + (map.Head == null ? "-" : map.Head.name) + "\n" + sb;
            return map;
        }

        // hips, the spine, the neck, the head: the line the skin's spine follows
        public List<Transform> Chain()
        {
            var c = new List<Transform>();
            if (Hips != null) c.Add(Hips);
            foreach (var s in Spine) if (s != Hips && !c.Contains(s)) c.Add(s);
            if (Neck != null && !c.Contains(Neck)) c.Add(Neck);
            if (Head != null && !c.Contains(Head)) c.Add(Head);
            return c;
        }
    }

    public sealed class SkinModel
    {
        // the menu and the settings file (SKIN, SKIN_SIZE, SKIN_HEIGHT, SKIN_FLIP) set these
        public string Want = "off";             // a file in Dir, or off
        public float Size = 1f, Height = 0f;
        public bool Flip;
        public string Dir = "", ReportFile = "";
        public string Status = "";

        // for the cape: where the skin's neck is, how tall the skin stands in the game, and the model (it faces the way the skin does)
        public Transform CapeBone, Root;
        public float ModelHeight = 1.8f;

        sealed class Load { public volatile bool Done; public GltfModel Model; public string Error; }

        // a part of a chain: the skin's bone, the one it aims at
        sealed class Aim { public Transform G, Gt; public Vector3 RestDir; public Quaternion RestRot; public Transform B, Bt; public string Name; }

        string loaded = "off";
        bool failed;
        Load pending;
        GameObject root;
        Transform[] nodes;
        Tracker tracker;
        float glbHeight = 1.7f, glbMinY, modelFeetBelowHips, gameFeetBelowHips, nextCheck;
        Vector3 pelvisRest;
        Material hidden;
        readonly Dictionary<Renderer, Material[]> saved = new Dictionary<Renderer, Material[]>();
        // the skin's own rig, read once, in the pose the file has it (the root at the origin, nothing scaled)
        RigMap glbMap;
        readonly Dictionary<Transform, Vector3> restPos = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Quaternion> restRot = new Dictionary<Transform, Quaternion>();
        bool reload;
        List<Transform> glbChain = new List<Transform>();
        readonly List<Aim> chainAims = new List<Aim>();      // the spine's bones, each aimed along a part of the game's spine line
        readonly List<float> chainFrac = new List<float>();  // where on the line each of them starts (0–1)
        readonly List<Aim> limbAims = new List<Aim>();
        List<Transform> gameChain = new List<Transform>();
        Cand bound;
        int boundVersion = -1;
        Quaternion yaw = Quaternion.identity;       // where the skin faces when it stands: relative to the game's model
        Quaternion facing = Quaternion.identity;    // where it faces now (in the world)
        Vector3 lastHips, runVel;
        bool haveHips;

        public bool Ready { get { return root != null && root.activeSelf; } }

        public List<string> Files()
        {
            var l = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(Dir) && Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.glb")) l.Add(Path.GetFileName(f));
            }
            catch (Exception) { }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        // the file again, e.g. after it was changed
        public void Reload() { reload = true; }

        public void Dispose() { Unload(); }

        void Unload()
        {
            pending = null;
            Restore();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; nodes = null; CapeBone = null; Root = null; bound = null; boundVersion = -1;
            chainAims.Clear(); chainFrac.Clear(); limbAims.Clear(); glbChain.Clear(); gameChain.Clear();
            restPos.Clear(); restRot.Clear();
        }

        // once per frame, after the animations: LateUpdate
        public void Tick(Tracker t)
        {
            tracker = t;
            string want = string.IsNullOrEmpty(Want) ? "off" : Want;
            if (want != loaded || reload)
            {
                reload = false;
                failed = false;
                Unload();
                loaded = want;
                Status = "";
                if (want != "off") StartLoad(want);
            }
            if (loaded == "off" || failed) return;
            if (pending != null && pending.Done)
            {
                var p = pending;
                if (p.Error != null) { Fail("could not read " + loaded + ": " + p.Error, null); return; }
                if (!t.Has) { Status = "waiting for a character …"; return; }   // the build needs the game's own material
                try { Build(p.Model, t); }
                catch (Exception e) { Fail("could not build " + loaded + ": " + e.Message, e); return; }
                pending = null;
            }
            if (root == null) return;
            if (!t.Has) { if (root.activeSelf) root.SetActive(false); Restore(); bound = null; CapeBone = null; return; }
            if (!root.activeSelf) root.SetActive(true);
            if (bound != t.Current || boundVersion != t.Version) Bind(t);
            if (root == null) return;
            Drive(t);
        }

        // the skin did not work: the game's own model is back, the menu says why, and the choice stays (it is tried again
        // when another is chosen or Rescan is pressed)
        public void Fail(string message, Exception e)
        {
            failed = true;
            Unload();
            Status = message;
            Debug.Log("[Resurf] skin: " + message + (e == null ? "" : "\n" + e));
            try { if (!string.IsNullOrEmpty(ReportFile)) File.WriteAllText(ReportFile, "skin " + loaded + ": " + message + (e == null ? "" : "\n\n" + e) + "\n"); } catch (Exception) { }
        }

        void StartLoad(string file)
        {
            var load = new Load();
            pending = load;
            var path = Path.Combine(Dir, file);
            Status = "reading " + file + " …";
            var th = new Thread(() =>
            {
                try { load.Model = Glb.Load(path); }
                catch (Exception e) { load.Error = e.Message; }
                load.Done = true;
            });
            th.IsBackground = true;
            th.Start();
        }

        // ── the model ──

        static Texture2D TextureOf(GltfModel m, Texture2D[] textures, int i)
        {
            if (i < 0 || i >= m.Images.Length || m.Images[i] == null) return null;
            if (textures[i] != null) return textures[i];
            var tex = ToTexture(m.Images[i], TextureWrapMode.Repeat);
            textures[i] = tex;
            return tex;
        }

        // a picture as a texture with all its mip levels (SetPixels32: LoadRawTextureData would want every level)
        internal static Texture2D ToTexture(GltfImage img, TextureWrapMode wrap)
        {
            var tex = new Texture2D(img.W, img.H, TextureFormat.RGBA32, true);
            var px = new Color32[img.W * img.H];
            for (int k = 0; k < px.Length; k++) px[k] = new Color32(img.Rgba[k * 4], img.Rgba[k * 4 + 1], img.Rgba[k * 4 + 2], img.Rgba[k * 4 + 3]);
            tex.SetPixels32(px);
            tex.wrapMode = wrap;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply(true, true);
            return tex;
        }

        void Build(GltfModel m, Tracker t)
        {
            // the game's own shader for the model when it has one that works with a colour texture
            Shader lit = null;
            var baseMat = t.Current.Skin == null ? null : t.Current.Skin.sharedMaterial;
            if (baseMat != null && baseMat.shader != null && baseMat.HasProperty("_MainTex"))
            {
                var n = baseMat.shader.name;
                if (n.IndexOf("SprayPaint", StringComparison.Ordinal) < 0 && n.IndexOf("Particles", StringComparison.Ordinal) < 0 && n.IndexOf("Additive", StringComparison.Ordinal) < 0 && n.IndexOf("Sprite", StringComparison.Ordinal) < 0)
                    lit = baseMat.shader;
            }
            if (lit == null) foreach (var name in new[] { "Mobile/Diffuse", "Legacy Shaders/Diffuse", "Unlit/Texture", "Mobile/Unlit (Supports Lightmap)", "Sprites/Default" }) { lit = Shader.Find(name); if (lit != null) break; }
            var alpha = Shader.Find("Sprites/Default") ?? lit;
            if (lit == null) throw new Exception("no shader to draw the skin with in this build");
            if (hidden == null)
            {
                var hs = Shader.Find("Sprites/Default");
                if (hs != null) { hidden = new Material(hs); hidden.name = "Resurf hidden"; hidden.color = new Color(0f, 0f, 0f, 0f); }
            }

            var textures = new Texture2D[m.Images.Length];
            var mats = new Material[m.Materials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var gm = m.Materials[i];
                var mat = new Material(gm.Blend ? alpha : lit);
                mat.name = gm.Name;
                var tex = TextureOf(m, textures, gm.Image);
                if (tex != null) mat.mainTexture = tex;
                if (mat.HasProperty("_Color")) mat.color = new Color(gm.R, gm.G, gm.B, gm.A);
                mats[i] = mat;
            }
            var fallback = new Material(lit);
            fallback.name = "Resurf skin";

            root = new GameObject("ResurfSkin");
            root.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(root);
            t.Ignore.Add(root.transform);
            Root = root.transform;
            nodes = new Transform[m.Nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var g = new GameObject(m.Nodes[i].Name);
                g.hideFlags = HideFlags.HideInHierarchy;
                nodes[i] = g.transform;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = m.Nodes[i];
                nodes[i].SetParent(n.Parent >= 0 ? nodes[n.Parent] : root.transform, false);
                nodes[i].localPosition = new Vector3(n.Tx, n.Ty, n.Tz);
                nodes[i].localRotation = new Quaternion(n.Qx, n.Qy, n.Qz, n.Qw);
                nodes[i].localScale = new Vector3(n.Sx, n.Sy, n.Sz);
            }
            var bones = new Transform[m.Joints.Length];
            var bind = new Matrix4x4[m.Joints.Length];
            for (int j = 0; j < bones.Length; j++)
            {
                bones[j] = nodes[m.Joints[j]];
                var mt = new Matrix4x4();
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) mt[r, c] = m.Ibm[j * 16 + r * 4 + c];
                bind[j] = mt;
            }
            foreach (var n in nodes) { restPos[n] = n.position; restRot[n] = n.rotation; }
            // the pelvis (or the first bone) is the root of the skinning
            glbMap = RigMap.Read(bones, Vector3.right);
            var rootBone = glbMap.Hips != null ? glbMap.Hips : bones[0];
            foreach (var part in m.Parts)
            {
                int n = part.Pos.Length / 3;
                var mesh = new Mesh();
                mesh.name = part.Name;
                if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var v = new Vector3[n]; var uv = new Vector2[n]; var w = new BoneWeight[n];
                Vector3[] nrm = part.Nrm != null ? new Vector3[n] : null;
                for (int i = 0; i < n; i++)
                {
                    v[i] = new Vector3(part.Pos[i * 3], part.Pos[i * 3 + 1], part.Pos[i * 3 + 2]);
                    uv[i] = new Vector2(part.Uv[i * 2], part.Uv[i * 2 + 1]);
                    if (nrm != null) nrm[i] = new Vector3(part.Nrm[i * 3], part.Nrm[i * 3 + 1], part.Nrm[i * 3 + 2]);
                    float w0 = part.Weights[i * 4], w1 = part.Weights[i * 4 + 1], w2 = part.Weights[i * 4 + 2], w3 = part.Weights[i * 4 + 3], sum = w0 + w1 + w2 + w3;
                    var bw = new BoneWeight();
                    if (sum <= 1e-6f) { bw.boneIndex0 = 0; bw.weight0 = 1f; }
                    else
                    {
                        bw.boneIndex0 = part.Joints[i * 4]; bw.weight0 = w0 / sum;
                        bw.boneIndex1 = part.Joints[i * 4 + 1]; bw.weight1 = w1 / sum;
                        bw.boneIndex2 = part.Joints[i * 4 + 2]; bw.weight2 = w2 / sum;
                        bw.boneIndex3 = part.Joints[i * 4 + 3]; bw.weight3 = w3 / sum;
                    }
                    w[i] = bw;
                }
                mesh.vertices = v;
                mesh.uv = uv;
                if (nrm != null) mesh.normals = nrm; else mesh.RecalculateNormals();
                mesh.triangles = part.Tris;
                mesh.boneWeights = w;
                mesh.bindposes = bind;
                mesh.RecalculateBounds();
                var go = new GameObject(part.Name);
                go.hideFlags = HideFlags.HideInHierarchy;
                go.transform.SetParent(root.transform, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = rootBone;
                smr.updateWhenOffscreen = true;
                smr.sharedMaterial = part.Material >= 0 && part.Material < mats.Length ? mats[part.Material] : fallback;
            }
            glbHeight = Mathf.Max(0.1f, m.MaxY - m.MinY);
            glbMinY = m.MinY;
            // the rig in the pose the file has: root at the origin, nothing scaled
            glbChain = glbMap.Chain();
            pelvisRest = glbMap.Hips != null ? restPos[glbMap.Hips] : Vector3.zero;
            modelFeetBelowHips = pelvisRest.y - glbMinY;
            CapeBone = glbMap.Neck != null ? glbMap.Neck : glbChain.Count > 1 ? glbChain[glbChain.Count - 2] : null;
            root.SetActive(false);
            Status = "";
            Debug.Log("[Resurf] skin: " + loaded + " built – " + m.Vertices + " vertices, " + m.Joints.Length + " bones, " + glbHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " high"
                + (m.Warning.Length > 0 ? ". " + m.Warning : ""));
        }

        // ── the game's character ──

        void Hide(Cand c)
        {
            if (hidden == null) return;
            foreach (var r in c.Skins)
            {
                if (r == null) continue;
                if (!saved.ContainsKey(r)) saved[r] = r.sharedMaterials;
                var arr = new Material[saved[r].Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = hidden;
                r.sharedMaterials = arr;
            }
        }

        void Restore()
        {
            foreach (var kv in saved) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            saved.Clear();
        }

        // the skin on a character of the game: its bones, matched with the skin's
        void Bind(Tracker t)
        {
            Restore();
            bound = t.Current; boundVersion = t.Version;
            chainAims.Clear(); chainFrac.Clear(); limbAims.Clear(); gameChain.Clear();
            if (bound == null) return;
            Hide(bound);
            foreach (var n in nodes) if (n != null) n.gameObject.layer = bound.Skin != null ? bound.Skin.gameObject.layer : 0;
            for (int i = 0; i < root.transform.childCount; i++) root.transform.GetChild(i).gameObject.layer = bound.Skin != null ? bound.Skin.gameObject.layer : 0;

            var bones = new List<Transform>();
            foreach (var r in bound.Skins)
            {
                var smr = r as SkinnedMeshRenderer;
                if (smr != null && smr.bones != null) bones.AddRange(smr.bones);
            }
            var face = t.Root != null ? t.Root.forward : Vector3.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
            var rightGuess = Vector3.Cross(Vector3.up, face.normalized);
            var map = RigMap.Read(bones, rightGuess);
            string heading = "from the model";
            if (map.NamedSides && map.NamedRight.sqrMagnitude > 1e-6f)
            {
                // the arms and legs give the line from the left to the right side; which way is front is the model's forward –
                // the names left and right can be the other way round in a game's skeleton
                var right = map.NamedRight.normalized;
                var named = Vector3.Cross(right, Vector3.up);
                if (Vector3.Dot(named, face) < 0f) { named = -named; right = -right; }
                face = named;
                heading = "from its arms and legs";
                map = RigMap.Read(bones, right);
            }
            yaw = Quaternion.Inverse(t.Root != null ? t.Root.rotation : Quaternion.identity) * Quaternion.LookRotation(face.normalized, Vector3.up);
            facing = (t.Root != null ? t.Root.rotation : Quaternion.identity) * yaw;
            haveHips = false; runVel = Vector3.zero;

            // the skin's spine and the game's spine
            gameChain = map.Chain();
            var report = new StringBuilder();
            report.Append("skin ").Append(loaded).Append(" on ").Append(bound.Skin != null ? bound.Skin.name : "?").Append(", facing ").Append(heading).Append("\n\nthe game's skeleton (").Append(bones.Count).Append(" bones):\n").Append(map.Report);
            report.Append("\nthe skin's skeleton:\n").Append(glbMap.Report);
            int matched = 0, total = 0;
            var missing = new List<string>();
            if (gameChain.Count >= 2 && glbChain.Count >= 2)
            {
                float total2 = 0f;
                var cum = new float[glbChain.Count];
                for (int i = 1; i < glbChain.Count; i++) { total2 += (restPos[glbChain[i]] - restPos[glbChain[i - 1]]).magnitude; cum[i] = total2; }
                if (total2 > 1e-6f)
                    for (int i = 0; i < glbChain.Count - 1; i++)
                    {
                        var a = new Aim { G = glbChain[i], Gt = glbChain[i + 1], Name = glbChain[i].name };
                        a.RestDir = (restPos[a.Gt] - restPos[a.G]).normalized;
                        a.RestRot = restRot[a.G];
                        chainAims.Add(a);
                        chainFrac.Add(cum[i] / total2);
                    }
                chainFrac.Add(1f);
                matched += chainAims.Count;
            }
            total += Math.Max(0, glbChain.Count - 1);
            if (chainAims.Count == 0) missing.Add("spine");
            // the limbs: shoulder → upper arm → forearm → hand, thigh → calf → foot
            string[][] parts = {
                new[] { RigNames.Shoulder, RigNames.UpperArm }, new[] { RigNames.UpperArm, RigNames.ForeArm }, new[] { RigNames.ForeArm, RigNames.Hand },
                new[] { RigNames.Thigh, RigNames.Calf }, new[] { RigNames.Calf, RigNames.Foot } };
            foreach (var p in parts)
                foreach (int side in new[] { -1, 1 })
                {
                    total++;
                    var g = glbMap.Limb(p[0], side); var gt = glbMap.Limb(p[1], side);
                    var b = map.Limb(p[0], side); var bt = map.Limb(p[1], side);
                    if (g == null || gt == null || b == null || bt == null || g == gt || b == bt)
                    {
                        var why = (b == null ? "game " + p[0] : bt == null ? "game " + p[1] : g == null ? "skin " + p[0] : "skin " + p[1]);
                        missing.Add(p[0] + "→" + p[1] + (side < 0 ? " L" : " R") + " (no " + why + ")");
                        continue;
                    }
                    var a = new Aim { G = g, Gt = gt, B = b, Bt = bt, Name = p[0] + (side < 0 ? " left" : " right") };
                    a.RestDir = (restPos[gt] - restPos[g]).normalized;
                    a.RestRot = restRot[g];
                    limbAims.Add(a);
                    matched++;
                }
            // the lowest foot of the game: the skin's feet are put there
            float lowest = float.MaxValue;
            foreach (int side in new[] { -1, 1 }) { var f = map.Limb(RigNames.Foot, side); if (f != null && f.position.y < lowest) lowest = f.position.y; }
            if (lowest == float.MaxValue) foreach (var b in bones) if (b != null && b.position.y < lowest) lowest = b.position.y;
            var hipsT = map.Hips != null ? map.Hips : gameChain.Count > 0 ? gameChain[0] : null;
            gameFeetBelowHips = hipsT != null ? Mathf.Max(0f, hipsT.position.y - lowest + 0.04f * t.Height) : modelFeetBelowHips * t.Height / glbHeight;
            Status = "on " + (bound.Skin != null ? bound.Skin.name : "the character") + ": " + matched + " of " + total + " parts follow the game's bones"
                + (missing.Count > 0 ? ". Missing: " + string.Join("; ", missing.ToArray()) : "");
            report.Append("\nfollowing: ").Append(matched).Append(" of ").Append(total).Append(" parts\n");
            Debug.Log("[Resurf] skin: " + Status);
            try { if (!string.IsNullOrEmpty(ReportFile)) File.WriteAllText(ReportFile, report.ToString()); } catch (Exception) { }
        }

        // ── every frame ──

        void Drive(Tracker t)
        {
            var rootRot = t.Root != null ? t.Root.rotation : Quaternion.identity;
            var hip = gameChain.Count > 0 ? gameChain[0] : null;
            // the skin faces the way it runs: the hips' speed over the ground says it, whatever the bones are called;
            // standing, it faces the way the game's model does
            float dt = Time.deltaTime;
            var target = rootRot * yaw;
            if (hip != null && dt > 1e-4f)
            {
                var p = hip.position;
                if (haveHips)
                {
                    var v = (p - lastHips) / dt;
                    v.y = 0f;
                    if (v.magnitude < 100f * t.Height) runVel = Vector3.Lerp(runVel, v, Mathf.Clamp01(dt * 8f));
                }
                lastHips = p; haveHips = true;
                if (runVel.magnitude > 1.5f * t.Height) target = Quaternion.LookRotation(runVel.normalized, Vector3.up);
            }
            facing = dt > 1e-4f ? Quaternion.Slerp(facing, target, Mathf.Clamp01(dt * 10f)) : target;
            var heading = facing;
            if (Flip) heading = heading * Quaternion.Euler(0f, 180f, 0f);
            float s = t.Height / glbHeight * Mathf.Clamp(Size, 0.3f, 3f);
            ModelHeight = glbHeight * s;
            root.transform.rotation = heading;
            root.transform.localScale = new Vector3(s, s, s);
            var hips = gameChain.Count > 0 ? gameChain[0] : null;
            var hipsPos = hips != null ? hips.position : t.Bone != null ? t.Bone.position : root.transform.position;
            // the skin's feet where the game's feet are: its hips are a bit higher or lower than the game's
            float offY = Mathf.Clamp(s * modelFeetBelowHips - gameFeetBelowHips, -0.4f * t.Height, 0.4f * t.Height);
            root.transform.position = hipsPos + new Vector3(0f, offY + Mathf.Clamp(Height, -1f, 1f) * 0.25f * t.Height, 0f) - heading * (s * pelvisRest);

            // the spine: each bone aims along its part of the line through the game's hips, spine, neck and head
            if (chainAims.Count > 0 && gameChain.Count >= 2)
            {
                int gc = gameChain.Count;
                var gp = new Vector3[gc]; var cum = new float[gc];
                float len = 0f;
                for (int i = 0; i < gc; i++) { gp[i] = gameChain[i].position; if (i > 0) { len += (gp[i] - gp[i - 1]).magnitude; } cum[i] = len; }
                if (len > 1e-5f)
                    for (int i = 0; i < chainAims.Count; i++)
                    {
                        var d = Sample(gp, cum, chainFrac[i + 1] * len) - Sample(gp, cum, chainFrac[i] * len);
                        if (d.sqrMagnitude < 1e-10f) continue;
                        Point(chainAims[i], heading, d);
                    }
            }
            for (int i = 0; i < limbAims.Count; i++)
            {
                var a = limbAims[i];
                if (a.B == null || a.Bt == null) continue;
                var d = a.Bt.position - a.B.position;
                if (d.sqrMagnitude < 1e-10f) continue;
                Point(a, heading, d);
            }
            // the game may put its own material back (a new run, a power-up)
            float now = Time.realtimeSinceStartup;
            if (now >= nextCheck && bound != null && hidden != null)
            {
                nextCheck = now + 1f;
                foreach (var r in bound.Skins)
                    if (r != null && r.sharedMaterial != hidden) { Hide(bound); break; }
            }
        }

        // the point on a line (the corners gp, the length up to each corner cum) at a length
        static Vector3 Sample(Vector3[] gp, float[] cum, float at)
        {
            for (int i = 1; i < gp.Length; i++)
                if (at <= cum[i] || i == gp.Length - 1)
                {
                    float seg = cum[i] - cum[i - 1];
                    float u = seg > 1e-9f ? Mathf.Clamp01((at - cum[i - 1]) / seg) : 0f;
                    return Vector3.Lerp(gp[i - 1], gp[i], u);
                }
            return gp[gp.Length - 1];
        }

        // the bone turned so that what pointed along its rest direction points along dir
        static void Point(Aim a, Quaternion heading, Vector3 dir)
        {
            var restWorldDir = heading * a.RestDir;
            a.G.rotation = Quaternion.FromToRotation(restWorldDir, dir.normalized) * (heading * a.RestRot);
        }
    }
}
