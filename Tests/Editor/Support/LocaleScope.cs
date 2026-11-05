using System;
using System.Reflection;

namespace VRVlog.LilToonExporter.Tests
{
    // Switches the editor language for one test and restores it afterwards.
    // ExporterLocalization keeps the locale and its loaded table in two private
    // fields; this is the only place a test names them.
    sealed class LocaleScope : IDisposable
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly FieldInfo LocaleField = typeof(ExporterLocalization).GetField("_locale", Flags);
        static readonly FieldInfo MessagesField = typeof(ExporterLocalization).GetField("_messages", Flags);
        readonly object locale, messages;

        public LocaleScope(string name)
        {
            locale = LocaleField.GetValue(null);
            messages = MessagesField.GetValue(null);
            LocaleField.SetValue(null, name);
            MessagesField.SetValue(null, null);
        }

        public void Dispose()
        {
            LocaleField.SetValue(null, locale);
            MessagesField.SetValue(null, messages);
        }
    }
}
