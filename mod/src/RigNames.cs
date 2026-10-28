// What a bone is, from its name: the game's skeleton (jLowerSpine, jLeftUpperArm …) and the skeleton of a skin
// (pelvis, spine_01, upperarm_l … or Mixamo's mixamorig:LeftArm …) are told apart by their roles, so the arms of the
// one can move the arms of the other. No Unity in it.
using System;
using System.Text;

namespace Resurf
{
    static class RigNames
    {
        public const string Hips = "hips", Spine = "spine", Neck = "neck", Head = "head", Shoulder = "shoulder",
            UpperArm = "upperarm", ForeArm = "forearm", Hand = "hand", Thigh = "thigh", Calf = "calf", Foot = "foot";

        static readonly string[] Skip = { "thumb", "index", "middle", "ring", "pinky", "finger", "twist", "roll", "corrective", "deform", "dyn", "mstr", "facial", "face", "eye", "jaw",
            "brow", "lip", "tongue", "teeth", "hair", "bandana", "cheek", "nose", "mouth", "attach", "nub", "helper", "target", "pole", "metacarpal", "toe", "ball", "weapon", "prop", "socket" };

        static bool Has(string n, string t) { return n.IndexOf(t, StringComparison.Ordinal) >= 0; }

        // the role of a bone, how well the name fits it (0 is best) and which side it is on (-1 left, 1 right, 0 not known)
        public static string Role(string name, out int side, out int rank)
        {
            side = 0; rank = 0;
            if (string.IsNullOrEmpty(name)) return null;
            var low = name.ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (char c in low) if (char.IsLetter(c)) sb.Append(c);
            var n = sb.ToString();
            foreach (var s in Skip) if (Has(n, s)) return null;
            if (low.StartsWith("ik", StringComparison.Ordinal) || Has(low, "_ik") || Has(low, "ik_") || Has(n, "end") && !Has(n, "bend")) return null;
            side = SideOf(name);

            if (Has(n, "forearm") || Has(n, "lowerarm")) { rank = 0; return ForeArm; }
            if (Has(n, "lowarm")) { rank = 1; return ForeArm; }
            if (Has(n, "elbow")) { rank = 2; return ForeArm; }
            if (Has(n, "hand") && !Has(n, "handle")) { rank = 0; return Hand; }
            if (Has(n, "wrist")) { rank = 1; return Hand; }
            if (Has(n, "clavicle")) { rank = 0; return Shoulder; }
            if (Has(n, "shoulder")) { rank = 1; return Shoulder; }
            if (Has(n, "collar")) { rank = 2; return Shoulder; }
            if (Has(n, "upperarm")) { rank = 0; return UpperArm; }
            if (Has(n, "uparm")) { rank = 1; return UpperArm; }
            if (Has(n, "bicep")) { rank = 2; return UpperArm; }
            if (Has(n, "calf") || Has(n, "lowerleg")) { rank = 0; return Calf; }
            if (Has(n, "shin") || Has(n, "lowleg")) { rank = 1; return Calf; }
            if (Has(n, "knee")) { rank = 2; return Calf; }
            if (Has(n, "foot")) { rank = 0; return Foot; }
            if (Has(n, "ankle")) { rank = 1; return Foot; }
            if (Has(n, "thigh") || Has(n, "upperleg")) { rank = 0; return Thigh; }
            if (Has(n, "upleg")) { rank = 1; return Thigh; }
            if (Has(n, "arm") && !Has(n, "armor") && !Has(n, "farm")) { rank = 3; return UpperArm; }
            if (Has(n, "leg")) { rank = 3; return Thigh; }
            if (Has(n, "neck")) { rank = 0; return Neck; }
            if (Has(n, "head") && !Has(n, "headtop") && !Has(n, "headcover")) { rank = 0; return Head; }
            if (Has(n, "pelvis")) { rank = 0; return Hips; }
            if (Has(n, "hips")) { rank = 1; return Hips; }
            if (Has(n, "hip") && !Has(n, "ship") && !Has(n, "whip")) { rank = 2; return Hips; }
            if (Has(n, "spine")) { rank = 0; return Spine; }
            if (Has(n, "chest")) { rank = 1; return Spine; }
            if (Has(n, "torso") || Has(n, "abdomen") || Has(n, "waist")) { rank = 2; return Spine; }
            return null;
        }

        // left or right from the name: Left/Right, _l/_r, .L/.R, L_/R_ at the start, a capital L/R at the end
        public static int SideOf(string name)
        {
            var low = name.ToLowerInvariant();
            if (Has(low, "left")) return -1;
            if (Has(low, "right")) return 1;
            // without the numbers a tool adds at the end (upperarm_l_0189), and in pieces (Bip001 L UpperArm, Thigh.R)
            int end = low.Length;
            while (end > 0 && (char.IsDigit(low[end - 1]) || low[end - 1] == '_' || low[end - 1] == '.' || low[end - 1] == ' ')) end--;
            var t = low.Substring(0, end);
            foreach (var piece in t.Split(new[] { '_', '.', ' ', '-', ':' }))
            {
                if (piece == "l") return -1;
                if (piece == "r") return 1;
            }
            // UpperArmL: a capital at the end after a small letter
            var orig = name.Substring(0, Math.Min(name.Length, end));
            if (orig.Length >= 2 && (orig[orig.Length - 1] == 'L' || orig[orig.Length - 1] == 'R') && char.IsLower(orig[orig.Length - 2])) return orig[orig.Length - 1] == 'L' ? -1 : 1;
            if (low.Length >= 2 && (low[0] == 'l' || low[0] == 'r') && (low[1] == '_' || low[1] == '.')) return low[0] == 'l' ? -1 : 1;
            return 0;
        }
    }
}
