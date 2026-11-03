using System;
using System.Reflection;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Reads and drives the expression board's private members by name, so the
    // names the tests use are spelled in one place. A method runs through
    // MethodInfo.Invoke, so what it throws arrives as a TargetInvocationException.
    static class BoardHarness
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Type Type = typeof(ExperimentalExpressionCaptureWindow);

        public static ExperimentalExpressionCaptureWindow Create() => ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();

        // Puts the avatar in the board and prepares it, as choosing it does.
        public static void Load(this ExperimentalExpressionCaptureWindow window, GameObject source)
        {
            window.SetField("source", source);
            window.Call("Prepare");
        }

        public static ExperimentalExpressionCaptureSession Session(this ExperimentalExpressionCaptureWindow window) =>
            window.Field<ExperimentalExpressionCaptureSession>("session");

        public static object Field(this ExperimentalExpressionCaptureWindow window, string name) => Type.GetField(name, Flags).GetValue(window);
        public static T Field<T>(this ExperimentalExpressionCaptureWindow window, string name) => (T)window.Field(name);
        public static void SetField(this ExperimentalExpressionCaptureWindow window, string name, object value) =>
            Type.GetField(name, Flags).SetValue(window, value);

        // One overload per argument count: a single array or null is one argument.
        public static object Call(this ExperimentalExpressionCaptureWindow window, string method) =>
            Type.GetMethod(method, Flags).Invoke(window, null);
        public static object Call(this ExperimentalExpressionCaptureWindow window, string method, object a) =>
            Type.GetMethod(method, Flags).Invoke(window, new[] { a });
        public static object Call(this ExperimentalExpressionCaptureWindow window, string method, object a, object b) =>
            Type.GetMethod(method, Flags).Invoke(window, new[] { a, b });
        public static object Call(this ExperimentalExpressionCaptureWindow window, string method, object a, object b, object c) =>
            Type.GetMethod(method, Flags).Invoke(window, new[] { a, b, c });
    }
}
