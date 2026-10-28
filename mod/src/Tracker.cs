// Which character of the game the mod follows – for the cape and for the skins. It looks at every skinned model in
// the scene and picks the runner: the one tagged Player, else what is on screen and is no chaser, else the one nearest
// to the camera; it looks again when its character leaves the screen (the menu's figure stays in the scene during
// the run). The menu can pick another by hand.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resurf
{
    // a character the game has
    sealed class Cand
    {
        public Transform Root;                          // the parent of the skinned mesh – gives the direction the character faces
        public Transform Key;                           // the skeleton's root bone: one per character, even when many share a folder (avatars)
        public Renderer Skin;                           // one of its skinned meshes (on screen, when there is one)
        public readonly List<Renderer> Skins = new List<Renderer>();   // all of them
        public Bounds Bounds;                           // of all the skinned meshes of this character
        public bool Visible, Tagged, Foe;
        public float Dist;
    }

    public sealed class Tracker
    {
        public Transform Bone;                          // the best bone of the game's skeleton for a cape
        public float BoneLift;                          // how far above the bone the cape's top is (in character heights)
        public Transform Root;                          // the model: its forward is the direction the character faces
        public float Height = 1.8f;
        public string Status = "";
        public int Version;                             // grows whenever the character changes
        internal Cand Current;
        // what is not the game's: the cape, the skin's model
        public readonly List<Transform> Ignore = new List<Transform>();

        Transform ownAnchor, pinKey;
        float nextFind, nextCheck;
        int unseen;

        public bool Has { get { return Bone != null && Bone.gameObject.activeInHierarchy; } }

        // back to choosing by itself
        public void Forget() { Bone = null; Current = null; Version++; nextFind = 0f; pinKey = null; }

        // the next character that has a skinned model in the game, chosen by hand (the menu)
        public void Next()
        {
            var list = Candidates();
            if (list.Count == 0) { Status = "no character found"; return; }
            int at = -1;
            for (int k = 0; k < list.Count; k++) if (Current != null && list[k].Key == Current.Key) at = k;
            var c = list[(at + 1) % list.Count];
            pinKey = c.Key;
            Attach(c, (at + 1) % list.Count, list.Count);
        }

        public void Dispose() { if (ownAnchor != null) UnityEngine.Object.Destroy(ownAnchor.gameObject); }

        // once per frame, when something wants a character
        public void Tick(bool needed)
        {
            if (!needed) return;
            float now = Time.realtimeSinceStartup;
            if (!Has)
            {
                if (Bone != null || Current != null) { Bone = null; Current = null; Version++; }
                if (now < nextFind) return;
                nextFind = now + 1.5f;
                if (!Find()) { Status = "no character found – it comes when one is in the game"; return; }
                nextCheck = now + 1f;
            }
            // a character that is not on screen (the menu's, still in the scene) – look for the one that is
            if (pinKey == null && now >= nextCheck)
            {
                nextCheck = now + 1f;
                if (Current == null || Current.Skin == null || !Current.Skin.isVisible) { if (++unseen >= 2) { unseen = 0; Find(); } }
                else unseen = 0;
            }
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

        internal static bool IsSkinned(Renderer r) { return r != null && r.GetType().Name == "SkinnedMeshRenderer"; }

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

        // everything with a skinned model: the one tagged Player first, then what is on screen and not a chaser,
        // then the nearest to the camera
        internal List<Cand> Candidates()
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
                bool ours = false;
                foreach (var ig in Ignore) if (ig != null && r.transform.IsChildOf(ig)) ours = true;
                if (ours) continue;
                var root = r.transform.parent != null ? r.transform.parent : r.transform;
                var key = RootBone(r);
                if (key == null) key = root;
                Cand c;
                if (byRoot.TryGetValue(key, out c))
                {
                    c.Skins.Add(r);
                    c.Bounds.Encapsulate(r.bounds);
                    if (r.isVisible && !c.Visible) { c.Visible = true; c.Skin = r; }
                    continue;
                }
                c = new Cand { Root = root, Key = key, Skin = r, Bounds = r.bounds, Visible = r.isVisible, Dist = (r.bounds.center - camPos).sqrMagnitude };
                c.Skins.Add(r);
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

        // how tall the character is: its meshes' bounds, and the span of its skeleton from the lowest bone to the
        // highest (a little more: the head goes over its bone) – the bigger one, since the bounds can be too small
        static float HeightOf(Cand c, out float fromBounds, out float fromBones)
        {
            fromBounds = c.Bounds.size.y;
            fromBones = 0f;
            var scope = c.Key != null && c.Key != c.Root ? c.Key : c.Root;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                float y = t.position.y;
                if (y < lo) lo = y;
                if (y > hi) hi = y;
            }
            if (hi > lo) fromBones = (hi - lo) * 1.15f;
            float h = Mathf.Max(fromBounds < 60f ? fromBounds : 0f, fromBones < 60f ? fromBones : 0f);
            return h < 0.05f ? 1.8f : h;
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
            if (Has && Current != null && list[pick].Key == Current.Key)
            {
                // the same character: only what is on screen of it may have changed
                Current.Skin = list[pick].Skin; Current.Visible = list[pick].Visible;
                return true;
            }
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
                if (ownAnchor == null) { var g = new GameObject("ResurfCapeAnchor"); g.hideFlags = HideFlags.HideInHierarchy; ownAnchor = g.transform; }
                ownAnchor.SetParent(c.Skin.transform, true);
                ownAnchor.position = new Vector3(b.center.x, b.max.y - 0.2f * b.size.y, b.center.z);
                bone = ownAnchor; l = 0f; what = "on the model's back (no known bone)";
            }
            Bone = bone; Root = c.Root; BoneLift = l; Current = c;
            float hb, hk;
            Height = HeightOf(c, out hb, out hk);
            Version++;
            Status = "Character " + (index + 1) + " of " + count + ": " + c.Skin.name + " – " + what + (c.Visible ? "" : " (not on screen)")
                + ". Height " + Height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + " (meshes " + hb.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", bones " + hk.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")";
            Debug.Log("[Resurf] character: " + Status);
        }
    }
}
