using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // What plain Unity runs on a synced layer, which VRVLOG_avatar_animator
    // follows: the behaviours of the copied state machines, root and child,
    // run there too, outer first; of a state's behaviours only the synced
    // layer's override runs.
    public sealed class SyncedLayerBehaviourTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
            SyncedLayerCallbackProbe.Calls.Clear();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ASyncedLayerRunsTheCopiedMachinesBehaviours(bool overridesState, bool affectsTiming)
        {
            var controller = new AnimatorController { name = "FX" };
            owned.Add(controller);
            controller.AddParameter("Go", AnimatorControllerParameterType.Bool);
            controller.AddLayer("Base");
            var root = controller.layers[0].stateMachine;
            var idle = root.AddState("Idle");
            var child = root.AddStateMachine("Child");
            var inner = child.AddState("Inner");
            child.defaultState = inner;
            var transition = idle.AddTransition(child);
            transition.hasExitTime = false;
            transition.duration = 0;
            transition.AddCondition(AnimatorConditionMode.If, 0, "Go");
            root.AddStateMachineBehaviour<SyncedLayerCallbackProbe>().Label = "root";
            child.AddStateMachineBehaviour<SyncedLayerCallbackProbe>().Label = "child";
            idle.AddStateMachineBehaviour<SyncedLayerCallbackProbe>().Label = "idle";
            inner.AddStateMachineBehaviour<SyncedLayerCallbackProbe>().Label = "inner";
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Synced", syncedLayerIndex = 0, defaultWeight = 1, syncedLayerAffectsTiming = affectsTiming,
                stateMachine = Own(new AnimatorStateMachine()),
            });
            if (overridesState)
            {
                var layers = controller.layers;
                var replacement = Own(ScriptableObject.CreateInstance<SyncedLayerCallbackProbe>());
                replacement.Label = "override";
                layers[1].SetOverrideBehaviours(inner, new StateMachineBehaviour[] { replacement });
                controller.layers = layers;
            }
            var avatar = Own(new GameObject("Avatar"));
            var animator = avatar.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            for (var frame = 0; frame < 3; frame++) animator.Update(0.1f);
            Assert.That(SyncedLayerCallbackProbe.Calls, Is.EqualTo(new[] { "root 0", "idle 0", "root 1" }));

            SyncedLayerCallbackProbe.Calls.Clear();
            animator.SetBool("Go", true);
            for (var frame = 0; frame < 3; frame++) animator.Update(0.1f);
            var synced = overridesState ? new[] { "root 1", "child 1", "override 1" } : new[] { "root 1", "child 1" };
            Assert.That(SyncedLayerCallbackProbe.Calls.Where(call => call.EndsWith(" 1")), Is.EqualTo(synced));
            Assert.That(SyncedLayerCallbackProbe.Calls.Where(call => call.EndsWith(" 0")), Is.EqualTo(new[] { "root 0", "child 0", "inner 0" }));
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
