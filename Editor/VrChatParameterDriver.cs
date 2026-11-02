using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace VRVlog.LilToonExporter
{
    // Serialized SDK data only: never invoke the SDK's runtime delegates or
    // install a handler on its process-wide static events.
    internal static class VrChatParameterDriver
    {
        internal sealed class Operation
        {
            internal string Kind, Destination, Source;
            internal float Value, SourceMin, SourceMax, DestinationMin, DestinationMax;
            internal bool ConvertRange;
            internal bool UnresolvedRandom;
            internal string Error;
        }

        internal sealed class Program
        {
            internal string Location, Error;
            internal bool LocalOnly;
            internal bool FxControl;
            internal float FxWeight;
            internal SdkLayerWeightControl LayerWeight;
            internal readonly List<Operation> Operations = new List<Operation>();
        }

        internal static readonly HashSet<string> BuiltIn = new HashSet<string>(new[]
        {
            "IsLocal", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
            "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "Upright", "Grounded", "Seated",
            "AFK", "TrackingType", "VRMode", "MuteSelf", "InStation", "Earmuffs", "IsOnFriendsList",
            "AvatarVersion", "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
            "EyeHeightAsPercent", "IsAnimatorEnabled", "PreviewMode"
        }, StringComparer.Ordinal);

        internal static bool IsDriver(StateMachineBehaviour value) => VrChatSdkTypes.Is(value, VrChatSdkTypes.ParameterDriver);

        internal static bool IsTracking(StateMachineBehaviour value) => VrChatSdkTypes.Is(value, VrChatSdkTypes.TrackingControl);

        // Known SDK controls are data only. The body targets cannot change the
        // sampled FX morphs; FX commands require a separate neutral-gate proof.
        // A malformed playable control is not a known one.
        private static SdkLayerWeightControl PlayableControl(StateMachineBehaviour value)
        {
            if (!VrChatSdkTypes.Is(value, VrChatSdkTypes.PlayableLayerControl)) return null;
            SdkLayerWeightControl.TryRead(value, null, 0, out var control);
            return control;
        }

        internal static bool IsNonFxPlayableControl(StateMachineBehaviour value) =>
            PlayableControl(value) is SdkLayerWeightControl control && control.Playable != "FX";

        internal static bool ReadInstantFxControl(StateMachineBehaviour value, string location, out Program program)
        {
            program = null;
            if (!(PlayableControl(value) is SdkLayerWeightControl control) || control.Playable != "FX" || control.BlendDuration != 0) return false;
            program = new Program { Location = location, FxControl = true, FxWeight = control.GoalWeight };
            return true;
        }

        // VRChat's locomotion/capsule/IK control does not write the FX graph
        // or its parameters. Keep the separate body-pose restrictions intact.
        internal static bool IsLocomotionControl(StateMachineBehaviour value)
        {
            if (!VrChatSdkTypes.Is(value, VrChatSdkTypes.LocomotionControl)) return false;
            using (var data = new SerializedObject(value))
            {
                var disabled = data.FindProperty("disableLocomotion");
                return disabled != null && disabled.propertyType == SerializedPropertyType.Boolean;
            }
        }
        // The documented SDK effect changes the wearer's viewpoint, not the
        // facial graph. Do not invoke the SDK timing callbacks/runtime delegates.
        internal static bool IsTemporaryPoseSpace(StateMachineBehaviour value)
        {
            if (!VrChatSdkTypes.Is(value, VrChatSdkTypes.TemporaryPoseSpace)) return false;
            using (var data = new SerializedObject(value))
            {
                var enter = data.FindProperty("enterPoseSpace");
                var fixedDelay = data.FindProperty("fixedDelay");
                var delay = data.FindProperty("delayTime");
                return enter != null && enter.propertyType == SerializedPropertyType.Boolean &&
                    fixedDelay != null && fixedDelay.propertyType == SerializedPropertyType.Boolean &&
                    delay != null && delay.propertyType == SerializedPropertyType.Float &&
                    !float.IsNaN(delay.floatValue) && !float.IsInfinity(delay.floatValue) && delay.floatValue >= 0;
            }
        }

        internal static Program Read(StateMachineBehaviour behaviour, string location)
        {
            var result = new Program { Location = location };
            try
            {
                result.LocalOnly = Required(behaviour, "localOnly") is bool local ? local : throw new InvalidOperationException("localOnlyが不正です。");
                if (!(Required(behaviour, "parameters") is IEnumerable items)) throw new InvalidOperationException("parametersが不正です。");
                foreach (var item in items)
                {
                    var op = new Operation();
                    result.Operations.Add(op);
                    try
                    {
                        op.Destination = Required(item, "name") as string;
                        op.Kind = Required(item, "type").ToString();
                        if (string.IsNullOrEmpty(op.Destination)) throw new InvalidOperationException("書き込み先がありません。");
                        switch (op.Kind)
                        {
                            case "Set": case "Add": op.Value = Number(item, "value"); break;
                            case "Copy":
                                op.Source = Required(item, "source") as string;
                                if (string.IsNullOrEmpty(op.Source)) throw new InvalidOperationException("Copy元がありません。");
                                op.ConvertRange = Required(item, "convertRange") is bool convert ? convert : throw new InvalidOperationException("convertRangeが不正です。");
                                if (op.ConvertRange)
                                {
                                    op.SourceMin = Number(item, "sourceMin"); op.SourceMax = Number(item, "sourceMax");
                                    op.DestinationMin = Number(item, "destMin"); op.DestinationMax = Number(item, "destMax");
                                    if (op.SourceMin == op.SourceMax) throw new InvalidOperationException("Copy元の範囲が0です。");
                                }
                                break;
                            case "Random":
                                // Prepared-rest fallback cannot authorize malformed data.
                                var minimum = Number(item, "valueMin");
                                var maximum = Number(item, "valueMax");
                                var chance = Number(item, "chance");
                                if (minimum > maximum || chance < 0 || chance > 1)
                                    throw new InvalidOperationException("Driverの数値が不正です: Random");
                                op.UnresolvedRandom = true;
                                op.Error = "Randomは固定表情の値を確定できません。";
                                break;
                            default: throw new InvalidOperationException("未対応の操作です。");
                        }
                    }
                    catch (Exception error) when (error is InvalidOperationException || error is FormatException || error is InvalidCastException || error is OverflowException)
                    { op.Error = error.Message; }
                    if (string.IsNullOrEmpty(op.Destination)) result.Error = "Driverの書き込み先を特定できません。";
                }
            }
            catch (InvalidOperationException error) { result.Error = error.Message; }
            return result;
        }

        private static object Required(object value, string name) => VrChatExpressionMenu.Member(value, name) ??
            throw new InvalidOperationException("Driverの設定を読み取れません: " + name);

        private static float Number(object value, string name)
        {
            var number = Convert.ToSingle(Required(value, name), CultureInfo.InvariantCulture);
            if (float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidOperationException("Driverの数値が不正です: " + name);
            return number;
        }

        private sealed class RandomCapability : InvalidOperationException
        {
            internal RandomCapability(string message) : base(message) { }
        }

        internal static bool IsRandomCapability(Exception error)
        {
            for (; error != null; error = error.InnerException)
                if (error is RandomCapability) return true;
            return false;
        }

        // The operations whose destinations are needed, with their labels. A
        // program that could not be read stops before any of them.
        private static IEnumerable<(Operation Operation, string Label)> Needed(Program program, ISet<string> needed)
        {
            if (program.Error != null) throw new InvalidOperationException(program.Location + " / Parameter Driver: " + program.Error);
            for (var index = 0; index < program.Operations.Count; index++)
            {
                var operation = program.Operations[index];
                if (needed.Contains(operation.Destination))
                    yield return (operation, program.Location + " / Parameter Driver " + (index + 1) + " (" + operation.Kind + " → " + operation.Destination + ")");
            }
        }

        private static bool Writable(IDictionary<string, AnimatorControllerParameterType> types, string name, out AnimatorControllerParameterType type) =>
            types.TryGetValue(name, out type) && type != AnimatorControllerParameterType.Trigger;

        internal static void ValidateTargets(Program program, IDictionary<string, AnimatorControllerParameterType> types,
            ISet<string> needed)
        {
            foreach (var (operation, label) in Needed(program, needed))
            {
                if (!operation.UnresolvedRandom && operation.Error != null)
                    throw new InvalidOperationException(label + ": " + operation.Error);
                if (BuiltIn.Contains(operation.Destination) || !Writable(types, operation.Destination, out var type))
                    throw new InvalidOperationException(label + ": Parameter Driver の書き込み先の型が不正です。");
                if (operation.Kind == "Copy" && (string.IsNullOrEmpty(operation.Source) || !Writable(types, operation.Source, out _)))
                    throw new InvalidOperationException(label + ": Parameter Driver Copy の参照先の型が不正です。");
                if (operation.Kind == "Add" && type == AnimatorControllerParameterType.Bool)
                    throw new InvalidOperationException(label + ": Bool への Add は未対応です。");
            }
        }

        internal static void Execute(Program program, IDictionary<string, AnimatorControllerParameterType> types,
            ISet<string> expressionParameters, ISet<string> needed, bool isLocal, Func<string, double> read, Action<string, double> write,
            ISet<string> suppliedInputs = null)
        {
            if (program.LocalOnly && !isLocal) return;
            // Known SDK operations whose destinations cannot reach this
            // expression need not be approximated (including Random).
            foreach (var (op, label) in Needed(program, needed))
            {
                try
                {
                    if (BuiltIn.Contains(op.Destination)) throw new InvalidOperationException("VRChat組み込みパラメーターへの書き込みはできません。");
                    if (!Writable(types, op.Destination, out var type))
                        throw new InvalidOperationException("書き込み先の型を解決できません。");
                    if (op.UnresolvedRandom) throw new RandomCapability(op.Error);
                    if (op.Error != null) throw new InvalidOperationException(op.Error);
                    double value;
                    if (op.Kind == "Copy")
                    {
                        if (BuiltIn.Contains(op.Source) && suppliedInputs?.Contains(op.Source) != true)
                            throw new InvalidOperationException("VRChat組み込みパラメーターをCopy元として再現できません。");
                        if (!Writable(types, op.Source, out _))
                            throw new InvalidOperationException("Copy元の型を解決できません: " + op.Source);
                        value = read(op.Source);
                        if (op.ConvertRange)
                        {
                            var t = Math.Max(0, Math.Min(1, (value - op.SourceMin) / ((double)op.SourceMax - op.SourceMin)));
                            value = op.DestinationMin + t * ((double)op.DestinationMax - op.DestinationMin);
                        }
                    }
                    else if (op.Kind == "Add")
                    {
                        if (type == AnimatorControllerParameterType.Bool) throw new InvalidOperationException("BoolへのAddは未対応です。");
                        value = read(op.Destination) + (double)op.Value;
                    }
                    else if (op.Kind == "Set") value = op.Value;
                    else throw new InvalidOperationException("固定表情に変換できないDriver操作です。");
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("演算結果が不正です。");
                    if (type == AnimatorControllerParameterType.Bool) value = value == 0 ? 0 : 1;
                    if (type == AnimatorControllerParameterType.Int)
                        value = op.Kind == "Copy" ? Math.Floor(value) : Math.Truncate(value);
                    if (expressionParameters.Contains(op.Destination))
                    {
                        if (type == AnimatorControllerParameterType.Int) value = Math.Max(0, Math.Min(255, value));
                        if (type == AnimatorControllerParameterType.Float) value = Math.Max(-1, Math.Min(1, value));
                    }
                    if (type == AnimatorControllerParameterType.Int && (value < int.MinValue || value > int.MaxValue) || Math.Abs(value) > float.MaxValue)
                        throw new InvalidOperationException("演算結果がパラメーターの範囲を超えます。");
                    write(op.Destination, value);
                }
                catch (InvalidOperationException error) { throw new InvalidOperationException(label + ": " + error.Message, error); }
            }
        }
    }
}
