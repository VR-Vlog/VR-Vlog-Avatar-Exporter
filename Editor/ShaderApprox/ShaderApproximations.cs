using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;

namespace VRVlog.LilToonExporter
{
    // The shaders with their own approximation (see IShaderApproximation).
    // To add a shader, add its class to this folder and one line here. The
    // first one that matches is used.
    internal static class ShaderApproximations
    {
        internal static readonly IShaderApproximation[] All =
        {
            new NonToonApproximation(),
            new UtsApproximation(),
            new ArktoonApproximation(),
            new SunaoApproximation(),
            new UnlitWFApproximation(),
        };

        internal static IShaderApproximation Find(string shaderName, SourceValues values)
        {
            foreach (var approximation in All)
                if (approximation.Matches(shaderName, values)) return approximation;
            return null;
        }
    }
}
