using System;
using System.Globalization;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // The size pair on the upload bar. Both numbers share the unit the total picks, so the unit
    // stays the same for the whole upload. Units are 1024-based like EditorUtility.FormatBytes.
    internal static class TransferSizeText
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB" };

        internal static string Progress(long transferred, long total)
        {
            var largest = Math.Max(Math.Max(transferred, total), 0L);
            var unit = 0;
            var scale = 1d;
            // Rounded first, so 1,048,570 bytes reads 1.0 MB and not 1024.0 KB.
            while (unit < Units.Length - 1 && Math.Round(largest / scale, 1) >= 1024d)
            {
                unit++;
                scale *= 1024d;
            }
            var format = unit == 0 ? "0" : "0.0";
            return Number(transferred, scale, format) + " / " + Number(total, scale, format) + " " + Units[unit];
        }

        private static string Number(long bytes, double scale, string format) =>
            (Math.Max(bytes, 0L) / scale).ToString(format, CultureInfo.InvariantCulture);
    }
}
