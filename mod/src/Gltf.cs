// A small loader for .glb files (glTF 2.0, binary) – for the mod's skins. Unity 2019.4 cannot read glTF when the game
// runs, so this reads what a character needs: the skeleton (nodes and the skin), the skinned meshes, their materials'
// colour textures (PNG) – and gives it all as plain arrays. No Unity in it: it runs on a worker thread, and the
// conversion to Unity's left-handed world happens here (x is mirrored, the triangles turn round), so what comes out
// can be put into a Mesh as it is.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Resurf
{
    // ── JSON: what a .glb holds in its first chunk ───────────────────────────────────────

    static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON ends too early");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    var k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("JSON: : expected");
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: , or } expected");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("JSON: , or ] expected");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int j = i;
            while (j < s.Length && "+-0123456789.eE".IndexOf(s[j]) >= 0) j++;
            if (j == i) throw new FormatException("JSON: unexpected " + c);
            var n = double.Parse(s.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture);
            i = j;
            return n;
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: string expected");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                c = s[i++];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(c); break;
                }
            }
            i++;
            return sb.ToString();
        }

        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static double Num(object o, string key, double fallback)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) && v is double ? (double)v : fallback;
        }

        public static string Text(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as string : null;
        }
    }

    // ── the model, as plain arrays ───────────────────────────────────────────────────────

    sealed class GltfPart
    {
        public string Name;
        public float[] Pos, Nrm, Uv;        // xyz, xyz, uv – Unity's handedness, v up
        public ushort[] Joints;             // 4 per vertex: an index into the skin's joints
        public float[] Weights;             // 4 per vertex
        public int[] Tris;
        public int Material;
    }

    sealed class GltfMaterial
    {
        public string Name;
        public float R = 1f, G = 1f, B = 1f, A = 1f;
        public int Image = -1;
        public bool Blend;
    }

    sealed class GltfImage
    {
        public int W, H;
        public byte[] Rgba;                 // rows from the bottom to the top, as Unity wants them
    }

    sealed class GltfNode
    {
        public string Name;
        public int Parent = -1;
        public int[] Children = new int[0];
        public float Tx, Ty, Tz, Qx, Qy, Qz, Qw = 1f, Sx = 1f, Sy = 1f, Sz = 1f;
    }

    sealed class GltfModel
    {
        public List<GltfPart> Parts = new List<GltfPart>();
        public GltfMaterial[] Materials = new GltfMaterial[0];
        public GltfImage[] Images = new GltfImage[0];
        public GltfNode[] Nodes = new GltfNode[0];
        public int[] Roots = new int[0];
        public int[] Joints = new int[0];   // node indices
        public float[] Ibm = new float[0];  // 16 per joint, [row * 4 + column], Unity's handedness
        public float MinY, MaxY;            // of the meshes in the pose the file has them
        public int Vertices;
        public string Warning = "";
    }

    static class Glb
    {
        public static GltfModel Load(string path)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 20 || data[0] != 'g' || data[1] != 'l' || data[2] != 'T' || data[3] != 'F')
                throw new FormatException("not a .glb file (it has to start with glTF)");
            int version = BitConverter.ToInt32(data, 4);
            if (version != 2) throw new FormatException("glTF version " + version + " – only 2 is supported");
            int off = 12, jsonAt = -1, jsonLen = 0, binAt = -1, binLen = 0;
            while (off + 8 <= data.Length)
            {
                int len = BitConverter.ToInt32(data, off), type = BitConverter.ToInt32(data, off + 4);
                if (type == 0x4E4F534A && jsonAt < 0) { jsonAt = off + 8; jsonLen = len; }
                else if (type == 0x004E4942 && binAt < 0) { binAt = off + 8; binLen = len; }
                off += 8 + len;
            }
            if (jsonAt < 0) throw new FormatException("no JSON in the file");
            var root = Json.Parse(Encoding.UTF8.GetString(data, jsonAt, jsonLen)) as Dictionary<string, object>;
            if (root == null) throw new FormatException("the JSON is not an object");
            var m = new GltfModel();
            var used = Json.Arr(root, "extensionsRequired");
            if (used != null)
                foreach (var e in used)
                {
                    var name = e as string ?? "";
                    if (name.StartsWith("KHR_draco", StringComparison.Ordinal) || name.StartsWith("EXT_meshopt", StringComparison.Ordinal))
                        throw new FormatException("the meshes are compressed (" + name + ") – export the .glb without compression");
                }
            var rd = new Reader { Root = root, Data = data, BinAt = binAt, BinLen = binLen };

            // the nodes: converted to Unity's handedness by mirroring x
            var nodes = Json.Arr(root, "nodes");
            if (nodes == null) throw new FormatException("no nodes in the file");
            m.Nodes = new GltfNode[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = new GltfNode { Name = Json.Text(nodes[i], "name") ?? ("node" + i) };
                var ch = Json.Arr(nodes[i], "children");
                if (ch != null) { n.Children = new int[ch.Count]; for (int k = 0; k < ch.Count; k++) n.Children[k] = (int)(double)ch[k]; }
                float[] t = Floats(Json.Arr(nodes[i], "translation"), 3), r = Floats(Json.Arr(nodes[i], "rotation"), 4), s = Floats(Json.Arr(nodes[i], "scale"), 3);
                var mat = Floats(Json.Arr(nodes[i], "matrix"), 16);
                if (mat != null) Decompose(mat, out t, out r, out s);
                if (t != null) { n.Tx = -t[0]; n.Ty = t[1]; n.Tz = t[2]; }
                if (r != null) { n.Qx = r[0]; n.Qy = -r[1]; n.Qz = -r[2]; n.Qw = r[3]; }
                if (s != null) { n.Sx = s[0]; n.Sy = s[1]; n.Sz = s[2]; }
                m.Nodes[i] = n;
            }
            for (int i = 0; i < m.Nodes.Length; i++)
                foreach (var c in m.Nodes[i].Children) if (c >= 0 && c < m.Nodes.Length) m.Nodes[c].Parent = i;
            var rootsList = new List<int>();
            for (int i = 0; i < m.Nodes.Length; i++) if (m.Nodes[i].Parent < 0) rootsList.Add(i);
            m.Roots = rootsList.ToArray();

            // the skin: the one the meshes use
            var skins = Json.Arr(root, "skins");
            if (skins == null || skins.Count == 0) throw new FormatException("the model has no skeleton (no skin) – it has to be rigged");
            int skinIndex = 0;
            for (int i = 0; i < nodes.Count; i++)
                if (Json.Num(nodes[i], "mesh", -1) >= 0 && Json.Num(nodes[i], "skin", -1) >= 0) { skinIndex = (int)Json.Num(nodes[i], "skin", 0); break; }
            var skin = skins[skinIndex];
            var jl = Json.Arr(skin, "joints");
            if (jl == null || jl.Count == 0) throw new FormatException("the skin has no joints");
            m.Joints = new int[jl.Count];
            for (int i = 0; i < jl.Count; i++) m.Joints[i] = (int)(double)jl[i];
            m.Ibm = new float[16 * m.Joints.Length];
            int ibmAcc = (int)Json.Num(skin, "inverseBindMatrices", -1);
            if (ibmAcc >= 0)
            {
                var f = rd.Floats(ibmAcc);
                if (f.Length < m.Ibm.Length) throw new FormatException("the inverse bind matrices are too few");
                for (int j = 0; j < m.Joints.Length; j++)
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++)
                        {
                            // glTF: column-major. Mirror x: M' = S M S with S = diag(-1, 1, 1, 1)
                            float v = f[j * 16 + c * 4 + r];
                            if ((r == 0) != (c == 0)) v = -v;
                            m.Ibm[j * 16 + r * 4 + c] = v;
                        }
            }
            else
            {
                // none given: the joints' own world transforms in the pose the file has them, inverted
                var world = WorldMatrices(m.Nodes);
                for (int j = 0; j < m.Joints.Length; j++) Array.Copy(Invert(world[m.Joints[j]]), 0, m.Ibm, j * 16, 16);
            }

            // the materials, and the colour textures they use
            var mats = Json.Arr(root, "materials");
            var texs = Json.Arr(root, "textures");
            var imgs = Json.Arr(root, "images");
            m.Materials = new GltfMaterial[mats == null ? 0 : mats.Count];
            for (int i = 0; i < m.Materials.Length; i++)
            {
                var gm = new GltfMaterial { Name = Json.Text(mats[i], "name") ?? ("material" + i) };
                var pbr = Json.Obj(mats[i], "pbrMetallicRoughness");
                var color = Floats(Json.Arr(pbr, "baseColorFactor"), 4);
                if (color != null) { gm.R = color[0]; gm.G = color[1]; gm.B = color[2]; gm.A = color[3]; }
                var bt = Json.Obj(pbr, "baseColorTexture");
                int ti = (int)Json.Num(bt, "index", -1);
                if (ti >= 0 && texs != null && ti < texs.Count) gm.Image = (int)Json.Num(texs[ti], "source", -1);
                gm.Blend = Json.Text(mats[i], "alphaMode") == "BLEND";
                m.Materials[i] = gm;
            }
            m.Images = new GltfImage[imgs == null ? 0 : imgs.Count];
            var wanted = new HashSet<int>();
            foreach (var gm in m.Materials) if (gm.Image >= 0) wanted.Add(gm.Image);
            foreach (int i in wanted)
            {
                if (i >= m.Images.Length) continue;
                try
                {
                    int bv = (int)Json.Num(imgs[i], "bufferView", -1);
                    if (bv < 0) { m.Warning += "image " + i + " is not in the file; "; continue; }
                    var bytes = rd.BufferView(bv);
                    var img = Png.Decode(bytes);
                    if (img == null) { m.Warning += "image " + i + " is not a PNG this loader can read; "; continue; }
                    m.Images[i] = Png.Shrink(img, 2048);
                }
                catch (Exception e) { m.Warning += "image " + i + ": " + e.Message + "; "; }
            }

            // the meshes
            var meshes = Json.Arr(root, "meshes");
            if (meshes == null) throw new FormatException("no meshes in the file");
            for (int ni = 0; ni < nodes.Count; ni++)
            {
                int mi = (int)Json.Num(nodes[ni], "mesh", -1);
                if (mi < 0 || mi >= meshes.Count) continue;
                var prims = Json.Arr(meshes[mi], "primitives");
                if (prims == null) continue;
                foreach (var prim in prims)
                {
                    if ((int)Json.Num(prim, "mode", 4) != 4) { m.Warning += "a part that is not made of triangles was left out; "; continue; }
                    var at = Json.Obj(prim, "attributes");
                    int accPos = (int)Json.Num(at, "POSITION", -1), accJ = (int)Json.Num(at, "JOINTS_0", -1), accW = (int)Json.Num(at, "WEIGHTS_0", -1);
                    if (accPos < 0) continue;
                    if (accJ < 0 || accW < 0) { m.Warning += "a part without skin weights was left out; "; continue; }
                    var part = new GltfPart { Name = Json.Text(meshes[mi], "name") ?? ("mesh" + mi), Material = (int)Json.Num(prim, "material", -1) };
                    part.Pos = rd.Floats(accPos);
                    int n = part.Pos.Length / 3;
                    for (int v = 0; v < n; v++) part.Pos[v * 3] = -part.Pos[v * 3];
                    int accN = (int)Json.Num(at, "NORMAL", -1);
                    if (accN >= 0)
                    {
                        part.Nrm = rd.Floats(accN);
                        for (int v = 0; v < n; v++) part.Nrm[v * 3] = -part.Nrm[v * 3];
                    }
                    int accUv = (int)Json.Num(at, "TEXCOORD_0", -1);
                    part.Uv = accUv >= 0 ? rd.Floats(accUv) : new float[n * 2];
                    for (int v = 0; v < n; v++) part.Uv[v * 2 + 1] = 1f - part.Uv[v * 2 + 1];
                    var j4 = rd.Uints(accJ);
                    part.Joints = new ushort[n * 4];
                    for (int v = 0; v < n * 4; v++) part.Joints[v] = (ushort)Math.Min(j4[v], (uint)(m.Joints.Length - 1));
                    part.Weights = rd.Floats(accW);
                    int accI = (int)Json.Num(prim, "indices", -1);
                    uint[] idx;
                    if (accI >= 0) idx = rd.Uints(accI);
                    else { idx = new uint[n]; for (int v = 0; v < n; v++) idx[v] = (uint)v; }
                    int tc = idx.Length / 3;
                    part.Tris = new int[tc * 3];
                    // mirrored: the triangles turn round
                    for (int t = 0; t < tc; t++) { part.Tris[t * 3] = (int)idx[t * 3]; part.Tris[t * 3 + 1] = (int)idx[t * 3 + 2]; part.Tris[t * 3 + 2] = (int)idx[t * 3 + 1]; }
                    m.Parts.Add(part);
                    m.Vertices += n;
                }
            }
            if (m.Parts.Count == 0) throw new FormatException("no skinned meshes in the file");
            Bounds(m);
            return m;
        }

        // the height of the model in the pose it has: the meshes, moved by their joints
        static void Bounds(GltfModel m)
        {
            var world = WorldMatrices(m.Nodes);
            var k = new float[m.Joints.Length][];
            for (int j = 0; j < m.Joints.Length; j++)
            {
                var ibm = new float[16];
                Array.Copy(m.Ibm, j * 16, ibm, 0, 16);
                k[j] = Mul(world[m.Joints[j]], ibm);
            }
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in m.Parts)
            {
                int n = p.Pos.Length / 3;
                for (int v = 0; v < n; v++)
                {
                    float x = p.Pos[v * 3], y = p.Pos[v * 3 + 1], z = p.Pos[v * 3 + 2], ry = 0f, wsum = 0f;
                    for (int q = 0; q < 4; q++)
                    {
                        float w = p.Weights[v * 4 + q];
                        if (w <= 0f) continue;
                        var a = k[p.Joints[v * 4 + q]];
                        ry += w * (a[4] * x + a[5] * y + a[6] * z + a[7]);
                        wsum += w;
                    }
                    if (wsum <= 0f) ry = y; else ry /= wsum;
                    if (ry < lo) lo = ry;
                    if (ry > hi) hi = ry;
                }
            }
            m.MinY = lo; m.MaxY = hi;
        }

        static float[] Floats(List<object> l, int n)
        {
            if (l == null || l.Count != n) return null;
            var f = new float[n];
            for (int i = 0; i < n; i++) f[i] = (float)(double)l[i];
            return f;
        }

        // ── 4x4 matrices, row-major, column vectors: v' = M v ──

        public static float[] Mul(float[] a, float[] b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++) s += a[i * 4 + k] * b[k * 4 + j];
                    r[i * 4 + j] = s;
                }
            return r;
        }

        public static float[] Trs(GltfNode n)
        {
            float x = n.Qx, y = n.Qy, z = n.Qz, w = n.Qw;
            float len = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (len > 0f) { x /= len; y /= len; z /= len; w /= len; }
            float xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            return new[] {
                (1f - 2f * (yy + zz)) * n.Sx, 2f * (xy - wz) * n.Sy, 2f * (xz + wy) * n.Sz, n.Tx,
                2f * (xy + wz) * n.Sx, (1f - 2f * (xx + zz)) * n.Sy, 2f * (yz - wx) * n.Sz, n.Ty,
                2f * (xz - wy) * n.Sx, 2f * (yz + wx) * n.Sy, (1f - 2f * (xx + yy)) * n.Sz, n.Tz,
                0f, 0f, 0f, 1f };
        }

        public static float[][] WorldMatrices(GltfNode[] nodes)
        {
            var world = new float[nodes.Length][];
            for (int i = 0; i < nodes.Length; i++) World(nodes, world, i, 0);
            return world;
        }

        static float[] World(GltfNode[] nodes, float[][] cache, int i, int depth)
        {
            if (cache[i] != null) return cache[i];
            var local = Trs(nodes[i]);
            int p = nodes[i].Parent;
            var w = p < 0 || depth > 1000 ? local : Mul(World(nodes, cache, p, depth + 1), local);
            cache[i] = w;
            return w;
        }

        // the inverse of a matrix that is a rotation, a scale and a translation (what the nodes make)
        public static float[] Invert(float[] m)
        {
            // general 4x4 inverse by Gauss-Jordan
            var a = new double[4, 8];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++) { a[i, j] = m[i * 4 + j]; a[i, 4 + j] = i == j ? 1.0 : 0.0; }
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                if (Math.Abs(a[p, c]) < 1e-20) return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
                if (p != c) for (int j = 0; j < 8; j++) { var t = a[c, j]; a[c, j] = a[p, j]; a[p, j] = t; }
                double d = a[c, c];
                for (int j = 0; j < 8; j++) a[c, j] /= d;
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c];
                    if (f == 0.0) continue;
                    for (int j = 0; j < 8; j++) a[r, j] -= f * a[c, j];
                }
            }
            var o = new float[16];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) o[i * 4 + j] = (float)a[i, 4 + j];
            return o;
        }

        // a glTF matrix (column-major) into translation, rotation (x y z w) and scale
        static void Decompose(float[] m, out float[] t, out float[] r, out float[] s)
        {
            t = new[] { m[12], m[13], m[14] };
            float sx = (float)Math.Sqrt(m[0] * m[0] + m[1] * m[1] + m[2] * m[2]);
            float sy = (float)Math.Sqrt(m[4] * m[4] + m[5] * m[5] + m[6] * m[6]);
            float sz = (float)Math.Sqrt(m[8] * m[8] + m[9] * m[9] + m[10] * m[10]);
            s = new[] { sx, sy, sz };
            if (sx == 0f) sx = 1f; if (sy == 0f) sy = 1f; if (sz == 0f) sz = 1f;
            // rotation matrix R[row, col]; column c of the glTF matrix is m[c*4 + row]
            float r00 = m[0] / sx, r10 = m[1] / sx, r20 = m[2] / sx;
            float r01 = m[4] / sy, r11 = m[5] / sy, r21 = m[6] / sy;
            float r02 = m[8] / sz, r12 = m[9] / sz, r22 = m[10] / sz;
            float tr = r00 + r11 + r22, qx, qy, qz, qw;
            if (tr > 0f)
            {
                float k = (float)Math.Sqrt(tr + 1f) * 2f;
                qw = 0.25f * k; qx = (r21 - r12) / k; qy = (r02 - r20) / k; qz = (r10 - r01) / k;
            }
            else if (r00 > r11 && r00 > r22)
            {
                float k = (float)Math.Sqrt(1f + r00 - r11 - r22) * 2f;
                qw = (r21 - r12) / k; qx = 0.25f * k; qy = (r01 + r10) / k; qz = (r02 + r20) / k;
            }
            else if (r11 > r22)
            {
                float k = (float)Math.Sqrt(1f + r11 - r00 - r22) * 2f;
                qw = (r02 - r20) / k; qx = (r01 + r10) / k; qy = 0.25f * k; qz = (r12 + r21) / k;
            }
            else
            {
                float k = (float)Math.Sqrt(1f + r22 - r00 - r11) * 2f;
                qw = (r10 - r01) / k; qx = (r02 + r20) / k; qy = (r12 + r21) / k; qz = 0.25f * k;
            }
            r = new[] { qx, qy, qz, qw };
        }

        // ── reading accessors ──

        sealed class Reader
        {
            public Dictionary<string, object> Root;
            public byte[] Data;
            public int BinAt, BinLen;

            public byte[] BufferView(int index)
            {
                var bv = Json.Arr(Root, "bufferViews");
                if (bv == null || index < 0 || index >= bv.Count) throw new FormatException("a buffer view is missing");
                int off = (int)Json.Num(bv[index], "byteOffset", 0), len = (int)Json.Num(bv[index], "byteLength", 0);
                if (BinAt < 0 || off + len > BinLen) throw new FormatException("a buffer view is outside the file");
                var b = new byte[len];
                Buffer.BlockCopy(Data, BinAt + off, b, 0, len);
                return b;
            }

            static int Size(int componentType)
            {
                switch (componentType)
                {
                    case 5120: case 5121: return 1;
                    case 5122: case 5123: return 2;
                    case 5125: case 5126: return 4;
                    default: throw new FormatException("an unknown component type " + componentType);
                }
            }

            static int Count(string type)
            {
                switch (type)
                {
                    case "SCALAR": return 1;
                    case "VEC2": return 2;
                    case "VEC3": return 3;
                    case "VEC4": return 4;
                    case "MAT4": return 16;
                    default: throw new FormatException("an unknown accessor type " + type);
                }
            }

            // the numbers of an accessor, as doubles (normalized integers become 0–1 or -1–1)
            double[] Read(int index)
            {
                var accs = Json.Arr(Root, "accessors");
                if (accs == null || index < 0 || index >= accs.Count) throw new FormatException("an accessor is missing");
                var a = accs[index];
                int ct = (int)Json.Num(a, "componentType", 5126), count = (int)Json.Num(a, "count", 0), comps = Count(Json.Text(a, "type") ?? "SCALAR");
                bool norm = a is Dictionary<string, object> && ((Dictionary<string, object>)a).ContainsKey("normalized") && (bool)((Dictionary<string, object>)a)["normalized"];
                var result = new double[count * comps];
                int bvi = (int)Json.Num(a, "bufferView", -1);
                if (bvi < 0) return result;
                var bvs = Json.Arr(Root, "bufferViews");
                var bv = bvs[bvi];
                int viewOff = (int)Json.Num(bv, "byteOffset", 0), stride = (int)Json.Num(bv, "byteStride", 0), accOff = (int)Json.Num(a, "byteOffset", 0);
                int cs = Size(ct);
                if (stride == 0) stride = cs * comps;
                for (int i = 0; i < count; i++)
                {
                    int p = BinAt + viewOff + accOff + i * stride;
                    if (p + cs * comps > BinAt + BinLen) throw new FormatException("an accessor is outside the file");
                    for (int c = 0; c < comps; c++)
                    {
                        int q = p + c * cs;
                        double v;
                        switch (ct)
                        {
                            case 5126: v = BitConverter.ToSingle(Data, q); break;
                            case 5125: v = BitConverter.ToUInt32(Data, q); break;
                            case 5123: v = BitConverter.ToUInt16(Data, q); if (norm) v /= 65535.0; break;
                            case 5122: v = BitConverter.ToInt16(Data, q); if (norm) v = Math.Max(v / 32767.0, -1.0); break;
                            case 5121: v = Data[q]; if (norm) v /= 255.0; break;
                            default: v = (sbyte)Data[q]; if (norm) v = Math.Max(v / 127.0, -1.0); break;
                        }
                        result[i * comps + c] = v;
                    }
                }
                return result;
            }

            public float[] Floats(int index)
            {
                var d = Read(index);
                var f = new float[d.Length];
                for (int i = 0; i < d.Length; i++) f[i] = (float)d[i];
                return f;
            }

            public uint[] Uints(int index)
            {
                var d = Read(index);
                var u = new uint[d.Length];
                for (int i = 0; i < d.Length; i++) u[i] = d[i] < 0.0 ? 0u : (uint)d[i];
                return u;
            }
        }
    }

    // ── PNG: what the textures are ───────────────────────────────────────────────────────

    static class Png
    {
        // 8 or 16 bits, grey / grey+alpha / RGB / RGBA / palette; not interlaced. null for anything else.
        public static GltfImage Decode(byte[] d)
        {
            if (d == null || d.Length < 33 || d[0] != 0x89 || d[1] != 'P' || d[2] != 'N' || d[3] != 'G') return null;
            int pos = 8, w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            while (pos + 8 <= d.Length)
            {
                int len = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
                string type = Encoding.ASCII.GetString(d, pos + 4, 4);
                int body = pos + 8;
                if (len < 0 || body + len > d.Length) break;
                if (type == "IHDR")
                {
                    w = (d[body] << 24) | (d[body + 1] << 16) | (d[body + 2] << 8) | d[body + 3];
                    h = (d[body + 4] << 24) | (d[body + 5] << 16) | (d[body + 6] << 8) | d[body + 7];
                    depth = d[body + 8]; ctype = d[body + 9]; interlace = d[body + 12];
                }
                else if (type == "PLTE") { palette = new byte[len]; Buffer.BlockCopy(d, body, palette, 0, len); }
                else if (type == "tRNS") { trns = new byte[len]; Buffer.BlockCopy(d, body, trns, 0, len); }
                else if (type == "IDAT") idat.Write(d, body, len);
                else if (type == "IEND") break;
                pos = body + len + 4;
            }
            if (w <= 0 || h <= 0 || w > 16384 || h > 16384 || interlace != 0 || (depth != 8 && depth != 16)) return null;
            int channels;
            switch (ctype) { case 0: channels = 1; break; case 2: channels = 3; break; case 3: channels = 1; break; case 4: channels = 2; break; case 6: channels = 4; break; default: return null; }
            if (ctype == 3 && (depth != 8 || palette == null)) return null;
            int bpp = channels * (depth / 8), stride = w * bpp;
            var raw = new byte[(stride + 1) * h];
            idat.Position = 2;                                  // the zlib header
            using (var z = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int got = 0;
                while (got < raw.Length)
                {
                    int n = z.Read(raw, got, raw.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < raw.Length) return null;
            }
            // undo the filters, row by row
            var img = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                int f = raw[y * (stride + 1)], src = y * (stride + 1) + 1, dst = y * stride, up = dst - stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? img[dst + x - bpp] : 0, b = y > 0 ? img[up + x] : 0, c = x >= bpp && y > 0 ? img[up + x - bpp] : 0, v = raw[src + x];
                    switch (f)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4:
                            {
                                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                                v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                                break;
                            }
                    }
                    img[dst + x] = (byte)v;
                }
            }
            // to RGBA, the bottom row first
            var o = new GltfImage { W = w, H = h, Rgba = new byte[w * h * 4] };
            int step = depth / 8;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride, outRow = (h - 1 - y) * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int p = row + x * bpp, q = outRow + x * 4;
                    byte r, g, b, a = 255;
                    switch (ctype)
                    {
                        case 0: r = g = b = img[p]; break;
                        case 2: r = img[p]; g = img[p + step]; b = img[p + 2 * step]; break;
                        case 3:
                            {
                                int pi = img[p];
                                r = pi * 3 + 2 < palette.Length ? palette[pi * 3] : (byte)0;
                                g = pi * 3 + 2 < palette.Length ? palette[pi * 3 + 1] : (byte)0;
                                b = pi * 3 + 2 < palette.Length ? palette[pi * 3 + 2] : (byte)0;
                                if (trns != null && pi < trns.Length) a = trns[pi];
                                break;
                            }
                        case 4: r = g = b = img[p]; a = img[p + step]; break;
                        default: r = img[p]; g = img[p + step]; b = img[p + 2 * step]; a = img[p + 3 * step]; break;
                    }
                    o.Rgba[q] = r; o.Rgba[q + 1] = g; o.Rgba[q + 2] = b; o.Rgba[q + 3] = a;
                }
            }
            return o;
        }

        // halves the picture until it is no bigger than max on any side
        public static GltfImage Shrink(GltfImage img, int max)
        {
            while (img.W > max || img.H > max)
            {
                int nw = Math.Max(1, img.W / 2), nh = Math.Max(1, img.H / 2);
                var o = new GltfImage { W = nw, H = nh, Rgba = new byte[nw * nh * 4] };
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int x0 = Math.Min(img.W - 1, x * 2), x1 = Math.Min(img.W - 1, x * 2 + 1), y0 = Math.Min(img.H - 1, y * 2), y1 = Math.Min(img.H - 1, y * 2 + 1);
                            int s = img.Rgba[(y0 * img.W + x0) * 4 + c] + img.Rgba[(y0 * img.W + x1) * 4 + c] + img.Rgba[(y1 * img.W + x0) * 4 + c] + img.Rgba[(y1 * img.W + x1) * 4 + c];
                            o.Rgba[(y * nw + x) * 4 + c] = (byte)(s / 4);
                        }
                img = o;
            }
            return img;
        }
    }
}
