using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Builds VRChat avatar descriptors through the installed SDK's types, which
    // the tests only know by name.
    static class VrcAvatar
    {
        public static Type ComponentType(string name) => OptionalTools.Find("VRC.SDK3.Avatars.Components." + name);

        public static Type DescriptorType => ComponentType("VRCAvatarDescriptor");

        // Turns on custom playable layers and makes the controller the only
        // base layer, as a non-default FX layer.
        public static void SetFx(Component descriptor, RuntimeAnimatorController controller)
        {
            using var data = new SerializedObject(descriptor);
            data.FindProperty("customizeAnimationLayers").boolValue = true;
            var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
            var layer = layers.GetArrayElementAtIndex(0);
            var type = layer.FindPropertyRelative("type"); type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
            layer.FindPropertyRelative("isDefault").boolValue = false;
            layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetExpressions(Component descriptor, ScriptableObject menu, ScriptableObject parameters)
        {
            using var data = new SerializedObject(descriptor);
            data.FindProperty("customExpressions").boolValue = true;
            data.FindProperty("expressionsMenu").objectReferenceValue = menu;
            data.FindProperty("expressionParameters").objectReferenceValue = parameters;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        // Adds a descriptor with the SDK's default playable layers, as the
        // inspector does for a new avatar.
        internal static Component AddDescriptor(GameObject root)
        {
            var type = OptionalTools.Require("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", "Install the real VRChat SDK.");
            var layerType = type.GetNestedType("AnimLayerType");
            Assert.That(layerType, Is.Not.Null, "The installed VRChat SDK must expose its playable layer types.");
            var descriptor = root.AddComponent(type);
            using var data = new SerializedObject(descriptor);
            void Layers(string name, params string[] types)
            {
                var list = data.FindProperty(name); Assert.That(list, Is.Not.Null, name);
                list.arraySize = types.Length;
                for (var i = 0; i < types.Length; i++)
                {
                    var layer = list.GetArrayElementAtIndex(i);
                    layer.FindPropertyRelative("type").intValue = Convert.ToInt32(Enum.Parse(layerType, types[i]));
                    layer.FindPropertyRelative("isDefault").boolValue = true;
                    layer.FindPropertyRelative("isEnabled").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = null;
                    layer.FindPropertyRelative("mask").objectReferenceValue = null;
                }
            }
            data.FindProperty("customizeAnimationLayers").boolValue = false;
            Layers("baseAnimationLayers", "Base", "Additive", "Gesture", "Action", "FX");
            Layers("specialAnimationLayers", "Sitting", "TPose", "IKPose");
            data.ApplyModifiedPropertiesWithoutUndo();
            return descriptor;
        }

        // Writes a public SDK field; enum fields take the member name.
        public static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name); Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, value.ToString()) : value);
        }

        // Replaces a descriptor's playable-layer array with default layers of the given types.
        public static void SetLayers(Component descriptor, string fieldName, string[] types)
        {
            var field = descriptor.GetType().GetField(fieldName); var element = field.FieldType.GetElementType();
            var array = Array.CreateInstance(element, types.Length);
            for (var i = 0; i < types.Length; i++) { var layer = Activator.CreateInstance(element); Set(layer, "type", types[i]); Set(layer, "isDefault", true); array.SetValue(layer, i); }
            field.SetValue(descriptor, array);
        }
    }
}
