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
    public sealed class HumanoidLimitData
    {
        public const string Key = "VRVLOG_humanoidLimits";
        public const int SchemaVersion = 1;
        public const double MaximumDegrees = 180;
        public const double MaximumAxisLength = 1000;

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

        public static Dictionary<string, object> Write(HumanoidLimitData data)
        {
            if (data == null || data.Bones.Count == 0) throw Json.Bad();
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
            var result = new Dictionary<string, object> { ["schemaVersion"] = (long)SchemaVersion, ["bones"] = bones };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static HumanoidLimitData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "bones");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var bones = Json.Obj(root["bones"]);
            if (bones.Count == 0) throw Json.Bad();
            var data = new HumanoidLimitData();
            foreach (var pair in bones)
            {
                if (!Known.Contains(pair.Key)) throw Json.Bad();
                var bone = Json.Obj(pair.Value, "min", "max", "center", "axisLength");
                var limit = new Limit { Min = Degrees(bone["min"]), Max = Degrees(bone["max"]), Center = Degrees(bone["center"]), AxisLength = Json.Number(bone["axisLength"]) };
                if (limit.Min.Any(angle => angle > 0) || limit.Max.Any(angle => angle < 0)) throw Json.Bad();
                if (!(limit.AxisLength >= 0 && limit.AxisLength <= MaximumAxisLength)) throw Json.Bad();
                data.Bones[pair.Key] = limit;
            }
            return data;
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

        // To 1/10000 of a degree, so a float limit is not written with its float noise.
        static double Round(double value) => Math.Round(Json.Number(value), 4);

        static readonly ContractJson Json = new ContractJson("Humanoid limit data is invalid or exceeds the supported limits.");
    }
}
