// Adapted from Avatar Optimizer 1.9.20 Editor/APIInternal/ComponentInfos.VRM1.cs
// (MIT, Copyright (c) anatawa12; full text in Documentation~/DependencyPatches/AAO-LICENSE.txt).
// AAO declares these only for the com.vrmc.vrm types it compiles against, so the
// bundled copies need the same declarations to keep the root Animator, spring bones
// and constraints of the export copy.
using System.Linq;
using Anatawa12.AvatarOptimizer.API;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using Humanoid = VRVlog.LilToonExporter.Vendor.UniHumanoid.Humanoid;

namespace VRVlog.LilToonExporter
{
    [ComponentInformation(typeof(Vrm10Instance))]
    internal sealed class ExportVrm10InstanceInformation : ComponentInformation<Vrm10Instance>
    {
        protected override void CollectDependency(Vrm10Instance component, ComponentDependencyCollector collector)
        {
            collector.MarkEntrypoint();
            if (component.TryGetComponent<Animator>(out var animator)) collector.AddDependency(animator);
            foreach (var spring in component.SpringBone.Springs)
            {
                foreach (var joint in spring.Joints) collector.AddDependency(joint);
                foreach (var collider in spring.ColliderGroups) collector.AddDependency(collider);
            }
        }

        protected override void CollectMutations(Vrm10Instance component, ComponentMutationsCollector collector)
        {
            foreach (var joint in component.SpringBone.Springs.SelectMany(spring => spring.Joints).Where(joint => joint))
                collector.TransformPositionAndRotation(joint.transform);
            if (component.Vrm != null && component.Vrm.LookAt != null && component.Vrm.LookAt.LookAtType == LookAtType.bone)
            {
                var leftEye = Bone(component, HumanBodyBones.LeftEye);
                if (leftEye != null) collector.TransformRotation(leftEye);
                var rightEye = Bone(component, HumanBodyBones.RightEye);
                if (rightEye != null) collector.TransformRotation(rightEye);
            }
        }

        static Transform Bone(Vrm10Instance component, HumanBodyBones bone)
        {
            var humanoid = component.GetComponent<Humanoid>();
            if (humanoid != null) return humanoid.GetBoneTransform(bone);
            var animator = component.GetComponent<Animator>();
            return animator != null ? animator.GetBoneTransform(bone) : null;
        }
    }

    [ComponentInformation(typeof(VRM10SpringBoneColliderGroup))]
    internal sealed class ExportVrm10SpringBoneColliderGroupInformation : ComponentInformation<VRM10SpringBoneColliderGroup>
    {
        protected override void CollectDependency(VRM10SpringBoneColliderGroup component, ComponentDependencyCollector collector)
        {
            foreach (var collider in component.Colliders) collector.AddDependency(collider);
        }
    }

    [ComponentInformation(typeof(VRM10SpringBoneJoint))]
    [ComponentInformation(typeof(VRM10SpringBoneCollider))]
    internal sealed class ExportVrm10ReferenceHolderInformation : ComponentInformation<Component>
    {
        protected override void CollectDependency(Component component, ComponentDependencyCollector collector)
        {
        }
    }

    [ComponentInformation(typeof(Vrm10AimConstraint))]
    internal sealed class ExportVrm10AimConstraintInformation : ComponentInformation<Vrm10AimConstraint>
    {
        protected override void CollectDependency(Vrm10AimConstraint component, ComponentDependencyCollector collector)
        {
            collector.MarkHeavyBehaviour();
            collector.AddDependency(component.transform, component.Source);
        }

        protected override void CollectMutations(Vrm10AimConstraint component, ComponentMutationsCollector collector) =>
            collector.TransformRotation(component.transform);
    }

    [ComponentInformation(typeof(Vrm10RollConstraint))]
    internal sealed class ExportVrm10RollConstraintInformation : ComponentInformation<Vrm10RollConstraint>
    {
        protected override void CollectDependency(Vrm10RollConstraint component, ComponentDependencyCollector collector)
        {
            collector.MarkHeavyBehaviour();
            collector.AddDependency(component.transform, component.Source);
        }

        protected override void CollectMutations(Vrm10RollConstraint component, ComponentMutationsCollector collector) =>
            collector.TransformRotation(component.transform);
    }

    [ComponentInformation(typeof(Vrm10RotationConstraint))]
    internal sealed class ExportVrm10RotationConstraintInformation : ComponentInformation<Vrm10RotationConstraint>
    {
        protected override void CollectDependency(Vrm10RotationConstraint component, ComponentDependencyCollector collector)
        {
            collector.MarkHeavyBehaviour();
            collector.AddDependency(component.transform, component.Source);
        }

        protected override void CollectMutations(Vrm10RotationConstraint component, ComponentMutationsCollector collector) =>
            collector.TransformRotation(component.transform);
    }

    [ComponentInformation(typeof(Humanoid))]
    internal sealed class ExportHumanoidInformation : ComponentInformation<Humanoid>
    {
        protected override void CollectDependency(Humanoid component, ComponentDependencyCollector collector)
        {
            // Humanoid overwrites the Animator's avatar when the VRM is written.
            collector.MarkEntrypoint();
            foreach (var (transform, _) in component.BoneMap)
                if (transform) collector.AddDependency(transform);
        }
    }
}
