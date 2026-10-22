using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Reflection keeps the SDK optional. These tests use its real descriptor
    // and menu types when installed and explicitly skip otherwise.
    public sealed class ExpressionMenuSourceFingerprintTests
    {
        [Test]
        public void MenuParametersWrittenByTheDescriptorInspectorDoNotInvalidate()
        {
            using var f = new Fixture();
            var stamp = ExportRecoverySourceStamp.Capture(f.Avatar);
            // The inspector assigns the descriptor's parameters to the menu in
            // memory. Dirtying it here keeps the check from relying on any cache.
            Set(f.Menu, "Parameters", f.Parameters); EditorUtility.SetDirty(f.Menu);
            Assert.That(stamp.Matches(f.Avatar), Is.True, "The menu's Parameters copy never reaches the VRM.");
            Set(f.Menu, "Parameters", f.OtherParameters); EditorUtility.SetDirty(f.Menu);
            Assert.That(stamp.Matches(f.Avatar), Is.True);
        }

        [TestCase("controlName")][TestCase("controlValue")][TestCase("controlAdded")][TestCase("descriptorParameters")]
        public void MenuControlsAndDescriptorParametersStillInvalidate(string change)
        {
            using var f = new Fixture();
            Set(f.Menu, "Parameters", f.Parameters);
            var stamp = ExportRecoverySourceStamp.Capture(f.Avatar);
            var controls = (IList)VrChatExpressionMenu.Member(f.Menu, "controls");
            switch (change)
            {
                case "controlName": Set(controls[0], "name", "Changed face"); break;
                case "controlValue": Set(controls[0], "value", 2f); break;
                case "controlAdded": AddControl(f.Menu, "Second face", "Face", 2); break;
                case "descriptorParameters": Set(f.Descriptor, "expressionParameters", f.OtherParameters); break;
            }
            EditorUtility.SetDirty(f.Menu);
            Assert.That(stamp.Matches(f.Avatar), Is.False, change);
        }

        static Type Sdk(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name); Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, value.ToString()) : value);
        }

        static void AddControl(ScriptableObject menu, string name, string parameterName, float value)
        {
            var controls = (IList)VrChatExpressionMenu.Member(menu, "controls");
            var control = Activator.CreateInstance(controls.GetType().GetGenericArguments()[0]); controls.Add(control);
            Set(control, "name", name); Set(control, "type", "Toggle"); Set(control, "value", value);
            var parameter = Activator.CreateInstance(control.GetType().GetField("parameter").FieldType);
            Set(parameter, "name", parameterName); Set(control, "parameter", parameter);
        }

        sealed class Fixture : IDisposable
        {
            internal readonly GameObject Avatar;
            internal readonly Component Descriptor;
            internal readonly ScriptableObject Menu, Parameters, OtherParameters;

            internal Fixture()
            {
                var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                var menuType = Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var parameterType = Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
                if (descriptorType == null || menuType == null || parameterType == null)
                    Assert.Ignore("Install the real VRChat SDK to run the expression menu fingerprint tests.");
                Avatar = new GameObject("Menu fingerprint source");
                Descriptor = Avatar.AddComponent(descriptorType);
                Set(Descriptor, "customExpressions", true);
                Menu = ScriptableObject.CreateInstance(menuType);
                AddControl(Menu, "Face", "Face", 1);
                Set(Descriptor, "expressionsMenu", Menu);
                Parameters = NewParameters(parameterType, 0f); OtherParameters = NewParameters(parameterType, 1f);
                Set(Descriptor, "expressionParameters", Parameters);
            }

            static ScriptableObject NewParameters(Type parameterType, float defaultValue)
            {
                var parameters = ScriptableObject.CreateInstance(parameterType);
                var field = parameterType.GetField("parameters"); var rowType = field.FieldType.GetElementType();
                var rows = Array.CreateInstance(rowType, 1); var row = Activator.CreateInstance(rowType);
                Set(row, "name", "Face"); Set(row, "valueType", "Int"); Set(row, "defaultValue", defaultValue); rows.SetValue(row, 0);
                field.SetValue(parameters, rows);
                return parameters;
            }

            public void Dispose()
            {
                if (Avatar != null) Object.DestroyImmediate(Avatar);
                foreach (var value in new Object[] { Menu, Parameters, OtherParameters }) if (value != null) Object.DestroyImmediate(value);
            }
        }
    }
}
