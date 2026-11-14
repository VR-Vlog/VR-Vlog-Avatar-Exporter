using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class AnimatorNestingTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        AnimatorController Controller()
        {
            var controller = Own(new AnimatorController { name = "FX" });
            controller.AddLayer("Base");
            return controller;
        }

        // A chain of `depth` BlendTrees below the state's tree, ending in a clip.
        BlendTree Trees(int depth)
        {
            var root = Own(new BlendTree { name = "Tree 0", blendType = BlendTreeType.Direct });
            var tree = root;
            for (var index = 1; index <= depth; index++)
            {
                var child = Own(new BlendTree { name = "Tree " + index, blendType = BlendTreeType.Direct });
                tree.AddChild(child);
                tree = child;
            }
            tree.AddChild(Own(new AnimationClip { name = "Leaf" }));
            return root;
        }

        [TestCase(AnimatorNesting.MaximumDepth, false)]
        [TestCase(AnimatorNesting.MaximumDepth + 1, true)]
        public void BlendTreesNestedPastTheLimitStopTheWalk(int depth, bool stops)
        {
            var controller = Controller();
            controller.layers[0].stateMachine.AddState("Face").motion = Trees(depth);
            if (stops)
                Assert.That(Assert.Throws<InvalidOperationException>(() => ExpressionDependencies.Controller(controller)).Message,
                    Is.EqualTo(AnimatorNesting.TooDeepReason));
            else Assert.That(ExpressionDependencies.Controller(controller), Is.SameAs(controller));
        }

        [TestCase(AnimatorNesting.MaximumDepth, false)]
        [TestCase(AnimatorNesting.MaximumDepth + 1, true)]
        public void StateMachinesNestedPastTheLimitStopTheWalk(int depth, bool stops)
        {
            var controller = Controller();
            var machine = controller.layers[0].stateMachine;
            for (var index = 0; index < depth; index++) machine = machine.AddStateMachine("Machine " + index);
            machine.AddState("Deepest");
            if (stops) Assert.Throws<InvalidOperationException>(() => AnimatorNesting.Check(controller));
            else Assert.DoesNotThrow(() => AnimatorNesting.Check(controller));
        }

        // A synced layer can put its own motion on the source's state.
        [Test]
        public void ASyncedLayersOwnMotionIsChecked()
        {
            var controller = Controller();
            var state = controller.layers[0].stateMachine.AddState("Face");
            controller.AddLayer(new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0, stateMachine = Own(new AnimatorStateMachine()) });
            var layers = controller.layers;
            layers[1].SetOverrideMotion(state, Trees(AnimatorNesting.MaximumDepth + 1));
            controller.layers = layers;
            Assert.Throws<InvalidOperationException>(() => AnimatorNesting.Check(controller));
        }

        // The same tree under two parents is walked again only when reached
        // deeper, so the deeper path still counts.
        [Test]
        public void ASharedTreeCountsItsDeepestPath()
        {
            var controller = Controller();
            var shared = Trees(AnimatorNesting.MaximumDepth - 1);
            var shallow = Own(new BlendTree { name = "Shallow", blendType = BlendTreeType.Direct });
            shallow.AddChild(shared);
            var deep = Own(new BlendTree { name = "Deep", blendType = BlendTreeType.Direct });
            var middle = Own(new BlendTree { name = "Middle", blendType = BlendTreeType.Direct });
            deep.AddChild(middle);
            middle.AddChild(shared);
            var machine = controller.layers[0].stateMachine;
            machine.AddState("Shallow").motion = shallow;
            machine.AddState("Deep").motion = deep;
            Assert.Throws<InvalidOperationException>(() => AnimatorNesting.Check(controller));
        }

        // A loop is not depth: the walks report it in their own words.
        [Test]
        public void ALoopIsLeftToTheWalks()
        {
            var controller = Controller();
            var tree = Own(new BlendTree { name = "Loop", blendType = BlendTreeType.Direct });
            tree.AddChild(tree);
            controller.layers[0].stateMachine.AddState("Face").motion = tree;
            Assert.DoesNotThrow(() => AnimatorNesting.Check(controller));
        }
    }
}
