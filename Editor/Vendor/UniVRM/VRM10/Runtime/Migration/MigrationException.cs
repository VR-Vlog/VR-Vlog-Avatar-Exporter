using System;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    internal class MigrationException : Exception
    {
        public MigrationException(string key, string value) : base($"{key}: {value}")
        {
        }
    }
}
