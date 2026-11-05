using System;
using System.Collections.Generic;
using UnityEditor;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    internal static class SerializedReferences
    {
        // Calls visit on every serialized property of value in order, and saves
        // value once, without undo, when a call returned true.
        internal static void Visit(Object value, Func<SerializedProperty, bool> visit)
        {
            using (var serialized = new SerializedObject(value))
            {
                var property = serialized.GetIterator();
                var changed = false;
                while (property.Next(true))
                    if (visit(property)) changed = true;
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Points every object reference of value that is a key of replacements
        // at its replacement.
        internal static void Replace<T>(Object value, IReadOnlyDictionary<T, T> replacements) where T : Object =>
            Visit(value, property =>
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    !(property.objectReferenceValue is T original) || !replacements.TryGetValue(original, out var replacement)) return false;
                property.objectReferenceValue = replacement;
                return true;
            });
    }
}
