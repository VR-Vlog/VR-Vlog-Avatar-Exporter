using System;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF
{
    public static class UriByteBuffer
    {
        public static Byte[] ReadEmbedded(string uri)
        {
            var pos = uri.IndexOf(";base64,");
            if (pos < 0)
            {
                throw new NotImplementedException();
            }
            else
            {
                return Convert.FromBase64String(uri.Substring(pos + 8));
            }
        }
    }
}
