using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Builds the small FX controllers the tests play: full-weight layers,
    // states, immediate transitions and blend-shape clips on "Body".
    static class FxGraph
    {
        // Adds a layer at full weight and returns its state machine.
        public static AnimatorStateMachine Layer(AnimatorController controller, string name)
        {
            controller.AddLayer(name); var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1; controller.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        public static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion = null, bool writeDefaults = false)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = writeDefaults; return state;
        }

        // Like State, and the first state added to the machine becomes its default.
        public static AnimatorState StartState(AnimatorStateMachine machine, string name, Motion motion = null, bool writeDefaults = false)
        {
            var state = State(machine, name, motion, writeDefaults);
            if (machine.states.Length == 1) machine.defaultState = state;
            return state;
        }

        // A transition without exit time or duration.
        public static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to)
        {
            var transition = from.AddTransition(to); transition.hasExitTime = false; transition.duration = 0; return transition;
        }

        public static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode, float threshold)
        {
            var transition = Transition(from, to); transition.AddCondition(mode, threshold, parameter); return transition;
        }

        public static EditorCurveBinding Binding(string shape, string path = "Body") =>
            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape);

        // A clip of blend-shape curves on "Body", stored inside the controller asset.
        public static AnimationClip CurveClip(AnimatorController owner, string name, params (string Shape, AnimationCurve Curve)[] curves)
        {
            var clip = new AnimationClip { name = name };
            foreach (var item in curves) AnimationUtility.SetEditorCurve(clip, Binding(item.Shape), item.Curve);
            AssetDatabase.AddObjectToAsset(clip, owner); return clip;
        }

        // A clip holding each blend shape at a constant weight for one second.
        public static AnimationClip Clip(AnimatorController owner, string name, params (string Shape, float Weight)[] weights)
        {
            var clip = new AnimationClip { name = name };
            foreach (var item in weights) AnimationUtility.SetEditorCurve(clip, Binding(item.Shape), AnimationCurve.Constant(0, 1, item.Weight));
            AssetDatabase.AddObjectToAsset(clip, owner); return clip;
        }
    }
}
