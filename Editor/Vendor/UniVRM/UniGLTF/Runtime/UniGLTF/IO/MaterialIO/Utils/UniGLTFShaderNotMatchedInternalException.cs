using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF
{
    internal class UniGLTFShaderNotMatchedInternalException : UniGLTFException
    {
        public UniGLTFShaderNotMatchedInternalException(Shader shader) : base(shader != null ? shader.name : "") { }
    }
}