using System;
using System.Globalization;
using System.Text;

namespace VRVlog.LilToonExporter.LanTransfer
{
    internal static class LanTransferProtocol
    {
        internal const string QrPrefix = "vrvlog-transfer:";

        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        internal static string DisplayName(string name)
        {
            var result = new StringBuilder();
            var text = name ?? "avatar.vrm";
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                // Names are display-only. Remove control and bidi formatting characters.
                if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                if (result.Length >= 256) break;
                if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) continue;
                    if (result.Length + 2 > 256) break;
                    if (CharUnicodeInfo.GetUnicodeCategory(text, i) == UnicodeCategory.Format) { i++; continue; }
                    result.Append(c).Append(text[++i]);
                    continue;
                }
                result.Append(c);
            }
            return result.Length == 0 ? "avatar.vrm" : result.ToString();
        }

        internal static string JsonString(string value)
        {
            var result = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    default:
                        if (c < 0x20 || char.IsSurrogate(c)) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else result.Append(c);
                        break;
                }
            }
            return result.Append('"').ToString();
        }
    }
}
