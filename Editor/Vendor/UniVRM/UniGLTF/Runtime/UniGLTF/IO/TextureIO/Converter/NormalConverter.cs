using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF
{
    public static class NormalConverter
    {
        private static Material _exporter;
        private static Material Exporter
        {
            get
            {
                if (_exporter == null)
                {
                    _exporter = new Material(Shader.Find("Hidden/VRVlog/LilToonExporter/Vendor/NormalMapExporter"));
                }
                return _exporter;
            }
        }

        // Unity texture to GLTF data
        public static Texture2D Export(Texture texture)
        {
            return TextureConverter.CopyTexture(texture, ColorSpace.Linear, false, Exporter);
        }
    }
}
