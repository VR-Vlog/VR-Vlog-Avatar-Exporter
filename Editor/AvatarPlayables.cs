using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The FX, Gesture and Action controllers, the Expression Menu and the
    // Expression Parameters a VRChat avatar's descriptor gives VRChat. Read from
    // the export copy once every build step has run (NdmfExportPreparation.Playables),
    // they are what an upload sends. The SDK stays optional: its types are read
    // by name, and the menu and parameters are its ScriptableObjects.
    internal sealed class AvatarPlayables
    {
        // A layer left at the SDK's default has no controller here: VRChat
        // plays its own, which holds no expression.
        internal RuntimeAnimatorController Fx, Gesture, Action;
        // Null when Custom Expressions is off.
        internal ScriptableObject Menu, Parameters;

        // Null when the avatar has no VRChat Avatar Descriptor.
        internal static AvatarPlayables Read(GameObject avatar)
        {
            var descriptor = avatar == null ? null : avatar.GetComponents<Component>().FirstOrDefault(component => component != null &&
                component.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptor == null) return null;
            var result = new AvatarPlayables();
            if (Member(descriptor, "customizeAnimationLayers") is bool custom && custom &&
                Member(descriptor, "baseAnimationLayers") is IEnumerable layers)
                foreach (var layer in layers)
                {
                    if (Member(layer, "isDefault") is bool isDefault && isDefault) continue;
                    var controller = Member(layer, "animatorController") as RuntimeAnimatorController;
                    switch (Member(layer, "type")?.ToString())
                    {
                        case "FX": result.Fx = controller; break;
                        case "Gesture": result.Gesture = controller; break;
                        case "Action": result.Action = controller; break;
                    }
                }
            if (Member(descriptor, "customExpressions") is bool expressions && expressions)
            {
                result.Menu = Member(descriptor, "expressionsMenu") as ScriptableObject;
                result.Parameters = Member(descriptor, "expressionParameters") as ScriptableObject;
            }
            return result;
        }

        private static object Member(object value, string name) =>
            value?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    }
}
