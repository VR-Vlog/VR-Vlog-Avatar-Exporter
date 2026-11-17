using System;
using System.Collections.Generic;
using System.Linq;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.AvatarFormats
{
    // glTF asset.extras[Key]: the muscle limits (Unity's HumanLimit) the
    // avatar's humanoid set for its own bones, by VRM 1.0 humanoid bone name.
    // A player that drives the avatar by muscles builds its humanoid with these
    // limits, so a muscle value turns a bone as far as it did on the avatar.
    // Bones the file leaves out keep Unity's defaults. Angles are in degrees
    // about the bone's muscle axes, as in HumanLimit; Min is 0 or less, Max 0
    // or more. See Documentation~/decisions/2026-11-16-arm-rest-and-limits.md.
    //
    // TPose: the rotation each bone takes in the avatar's own humanoid T-pose
    // where it differs from the VRM's rest, as a VRM 1.0 normalized pose: from
    // the bone's humanoid parent, in glTF axes, quaternion x, y, z, w. A player
    // that drives the avatar by muscles builds its humanoid from this pose, so a
    // muscle value gives the bone the same pose it gave on the avatar while the
    // rest stays a plain T-pose for players that copy rotations. Bones the file
    // leaves out take their rest. See
    // Documentation~/decisions/2026-11-17-arm-t-pose-in-extras.md.
    public sealed class HumanoidLimitData
    {
        public const string Key = "VRVLOG_humanoidLimits";
        public const int SchemaVersion = 1;
        public const double MaximumDegrees = 180;
        public const double MaximumAxisLength = 1000;
        // How far from 1 the length of a T-pose rotation may be.
        public const double RotationLengthTolerance = 1e-3;

        public sealed class Limit
        {
            public double[] Min = new double[3], Max = new double[3], Center = new double[3];
            public double AxisLength;
        }

        // VRM 1.0 humanoid bone names, the keys Bones may use.
        public static readonly IReadOnlyList<string> BoneNames = (
            "hips spine chest upperChest neck head leftEye rightEye jaw " +
            "leftUpperLeg leftLowerLeg leftFoot leftToes rightUpperLeg rightLowerLeg rightFoot rightToes " +
            "leftShoulder leftUpperArm leftLowerArm leftHand rightShoulder rightUpperArm rightLowerArm rightHand " +
            "leftThumbMetacarpal leftThumbProximal leftThumbDistal leftIndexProximal leftIndexIntermediate leftIndexDistal " +
            "leftMiddleProximal leftMiddleIntermediate leftMiddleDistal leftRingProximal leftRingIntermediate leftRingDistal " +
            "leftLittleProximal leftLittleIntermediate leftLittleDistal " +
            "rightThumbMetacarpal rightThumbProximal rightThumbDistal rightIndexProximal rightIndexIntermediate rightIndexDistal " +
            "rightMiddleProximal rightMiddleIntermediate rightMiddleDistal rightRingProximal rightRingIntermediate rightRingDistal " +
            "rightLittleProximal rightLittleIntermediate rightLittleDistal").Split(' ');

        static readonly HashSet<string> Known = new HashSet<string>(BoneNames, StringComparer.Ordinal);

        public readonly Dictionary<string, Limit> Bones = new Dictionary<string, Limit>(StringComparer.Ordinal);
        public readonly Dictionary<string, double[]> TPose = new Dictionary<string, double[]>(StringComparer.Ordinal);

        public bool IsEmpty => Bones.Count == 0 && TPose.Count == 0;

        // Each of bones and tPose is written only when it has a bone, so a file
        // with limits alone is the same as before tPose was added.
        public static Dictionary<string, object> Write(HumanoidLimitData data)
        {
            if (data == null || data.IsEmpty) throw Json.Bad();
            var result = new Dictionary<string, object> { ["schemaVersion"] = (long)SchemaVersion };
            if (data.Bones.Count > 0) result["bones"] = WriteBones(data);
            if (data.TPose.Count > 0)
                result["tPose"] = data.TPose.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => (object)Rotation(pair.Value));
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        static Dictionary<string, object> WriteBones(HumanoidLimitData data)
        {
            var bones = new Dictionary<string, object>();
            foreach (var pair in data.Bones.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var limit = pair.Value ?? throw Json.Bad();
                bones[pair.Key] = new Dictionary<string, object>
                {
                    ["min"] = Vector(limit.Min), ["max"] = Vector(limit.Max), ["center"] = Vector(limit.Center),
                    ["axisLength"] = Round(limit.AxisLength)
                };
            }
            return bones;
        }

        public static HumanoidLimitData Read(object value)
        {
            var root = Json.Obj(value);
            if (!root.ContainsKey("schemaVersion") || root.Keys.Any(key => key != "schemaVersion" && key != "bones" && key != "tPose")) throw Json.Bad();
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new HumanoidLimitData();
            if (root.TryGetValue("bones", out var rawBones)) ReadBones(Json.Obj(rawBones), data);
            if (root.TryGetValue("tPose", out var rawTPose))
            {
                var tPose = Json.Obj(rawTPose);
                if (tPose.Count == 0) throw Json.Bad();
                foreach (var pair in tPose)
                {
                    if (!Known.Contains(pair.Key)) throw Json.Bad();
                    data.TPose[pair.Key] = Quaternion(pair.Value);
                }
            }
            if (data.IsEmpty) throw Json.Bad();
            return data;
        }

        static void ReadBones(Dictionary<string, object> bones, HumanoidLimitData data)
        {
            if (bones.Count == 0) throw Json.Bad();
            foreach (var pair in bones)
            {
                if (!Known.Contains(pair.Key)) throw Json.Bad();
                var bone = Json.Obj(pair.Value, "min", "max", "center", "axisLength");
                var limit = new Limit { Min = Degrees(bone["min"]), Max = Degrees(bone["max"]), Center = Degrees(bone["center"]), AxisLength = Json.Number(bone["axisLength"]) };
                if (limit.Min.Any(angle => angle > 0) || limit.Max.Any(angle => angle < 0)) throw Json.Bad();
                if (!(limit.AxisLength >= 0 && limit.AxisLength <= MaximumAxisLength)) throw Json.Bad();
                data.Bones[pair.Key] = limit;
            }
        }

        // Reads the key from glTF asset.extras; null when the file has none.
        public static HumanoidLimitData FromGltf(Dictionary<string, object> gltf)
        {
            var extras = JsonAccess.Object(JsonAccess.Object(gltf, "asset"), "extras");
            return extras != null && extras.TryGetValue(Key, out var value) ? Read(value) : null;
        }

        static List<object> Vector(double[] values)
        {
            if (values == null || values.Length != 3) throw Json.Bad();
            return values.Select(v => (object)Round(v)).ToList();
        }

        static double[] Degrees(object value)
        {
            var values = Json.Arr(value, 3, 3).Select(Json.Number).ToArray();
            if (values.Any(angle => Math.Abs(angle) > MaximumDegrees)) throw Json.Bad();
            return values;
        }

        static List<object> Rotation(double[] values)
        {
            if (values == null || values.Length != 4) throw Json.Bad();
            return values.Select(v => (object)Math.Round(Json.Number(v), 6)).ToList();
        }

        // A unit quaternion x, y, z, w.
        static double[] Quaternion(object value)
        {
            var values = Json.Arr(value, 4, 4).Select(Json.Number).ToArray();
            var length = Math.Sqrt(values.Sum(v => v * v));
            if (Math.Abs(length - 1) > RotationLengthTolerance) throw Json.Bad();
            return values;
        }

        // To 1/10000 of a degree, so a float limit is not written with its float noise.
        static double Round(double value) => Math.Round(Json.Number(value), 4);

        static readonly ContractJson Json = new ContractJson("Humanoid limit data is invalid or exceeds the supported limits.");
    }
}
