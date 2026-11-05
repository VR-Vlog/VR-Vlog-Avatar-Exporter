using System;
using System.Collections.Generic;

namespace VRVlog.AvatarFormats
{
    // Reads parsed JSON: objects are Dictionary<string, object>, arrays
    // List<object>. The lenient readers return null for a missing owner or key
    // or a value of another type. The strict ones throw the exception the
    // caller makes, so each format keeps its own exception type and message.
    public static class JsonAccess
    {
        public static object Get(Dictionary<string, object> owner, string key) =>
            owner != null && owner.TryGetValue(key, out var value) ? value : null;

        public static Dictionary<string, object> Object(Dictionary<string, object> owner, string key) => Get(owner, key) as Dictionary<string, object>;

        public static List<object> List(Dictionary<string, object> owner, string key) => Get(owner, key) as List<object>;

        public static Dictionary<string, object> Object(object value, Func<Exception> invalid) =>
            value as Dictionary<string, object> ?? throw invalid();

        public static List<object> List(object value, Func<Exception> invalid) => value as List<object> ?? throw invalid();

        // An array of minimum to maximum items.
        public static List<object> List(object value, int minimum, int maximum, Func<Exception> invalid) =>
            value is List<object> list && list.Count >= minimum && list.Count <= maximum ? list : throw invalid();

        // A string of 1 to maximumLength characters.
        public static string Text(object value, int maximumLength, Func<Exception> invalid) =>
            value is string text && text.Length > 0 && text.Length <= maximumLength ? text : throw invalid();
    }
}
