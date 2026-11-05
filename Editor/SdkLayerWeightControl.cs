using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // Decode documented SDK metadata without invoking runtime callbacks. A
    // recognized but malformed command is distinct from an unknown behaviour
    // and from a valid effect that the exporter cannot reproduce.
    internal sealed class SdkLayerWeightControl
    {
        internal string Location;
        internal bool AnimatorLayer;
        internal string Playable;
        internal int LayerIndex;
        internal float GoalWeight, BlendDuration;
        // Filled by graph inspection, not inferred from the SDK object's name.
        internal AnimatorState SourceState;
        internal bool FixedBaseLayer;

        internal static SdkLayerWeightControl Read(StateMachineBehaviour value, string location, int fxLayerCount)
        {
            var reason = TryRead(value, location, fxLayerCount, out var control);
            if (reason != null)
                throw new InvalidOperationException(ExporterLocalization.T("FXのレイヤー制御に不正な設定があります: ") + location + " / " +
                    value.GetType().Name + " / " + reason);
            return control;
        }

        // Returns why a known layer control is malformed, or null. The control
        // is null for any other behaviour and for a malformed one; the caller
        // chooses whether a malformed control stops the export or is skipped.
        internal static string TryRead(StateMachineBehaviour value, string location, int fxLayerCount, out SdkLayerWeightControl control)
        {
            control = null;
            if (value == null) return null;
            var animatorLayer = VrChatSdkTypes.Is(value, VrChatSdkTypes.AnimatorLayerControl);
            if (!animatorLayer && !VrChatSdkTypes.Is(value, VrChatSdkTypes.PlayableLayerControl)) return null;
            using (var data = new SerializedObject(value))
            {
                SerializedProperty Find(string name, SerializedPropertyType expected)
                {
                    var property = data.FindProperty(name);
                    return property != null && property.propertyType == expected ? property : null;
                }
                string Unreadable(string name) => name + " の設定を読み取れません。";

                var targetName = animatorLayer ? "playable" : "layer";
                var target = Find(targetName, SerializedPropertyType.Enum);
                if (target == null) return Unreadable(targetName);
                var enumIndex = target.enumValueIndex;
                if (enumIndex < 0 || enumIndex >= target.enumNames.Length) return targetName + " の列挙値が不正です。";
                var playable = target.enumNames[enumIndex];
                if (playable != "Action" && playable != "Gesture" && playable != "Additive" && playable != "FX")
                    return targetName + " の対象が不正です: " + playable;

                var goal = Find("goalWeight", SerializedPropertyType.Float);
                if (goal == null) return Unreadable("goalWeight");
                if (!Finite(goal.floatValue) || goal.floatValue < 0 || goal.floatValue > 1)
                    return "goalWeight は 0 から 1 の有限値である必要があります。";
                var duration = Find("blendDuration", SerializedPropertyType.Float);
                if (duration == null) return Unreadable("blendDuration");
                if (!Finite(duration.floatValue) || duration.floatValue < 0)
                    return "blendDuration は 0 以上の有限値である必要があります。";

                var layerIndex = -1;
                if (animatorLayer)
                {
                    var layer = Find("layer", SerializedPropertyType.Integer);
                    if (layer == null) return Unreadable("layer");
                    layerIndex = layer.intValue;
                    // This count describes the prepared FX controller only.
                    // Other playable controllers may have different layer counts.
                    // Their runtime range is not proved here; neutral preparation
                    // retains body pose and never executes these body controls.
                    if (layerIndex < 0 || playable == "FX" && layerIndex >= fxLayerCount)
                        return "layer のインデックスが対象範囲外です: " + layerIndex;
                }
                control = new SdkLayerWeightControl
                {
                    Location = location, AnimatorLayer = animatorLayer, Playable = playable,
                    LayerIndex = layerIndex, GoalWeight = goal.floatValue, BlendDuration = duration.floatValue, FixedBaseLayer = animatorLayer && layerIndex == 0
                };
                return null;
            }
        }
    }
}
