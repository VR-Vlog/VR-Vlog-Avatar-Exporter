using System.Collections.Generic;
using UnityEngine;
using Mode = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.Mode;
using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;

namespace VRVlog.LilToonExporter
{
    // How the materials of one shader (or of one family of shaders) are drawn
    // with official lilToon when the generic mapping cannot read the shader's
    // own names. Each shader has one class in this folder, listed once in
    // ShaderApproximations.All, and one test file in
    // Tests/Editor/ShaderApprox/<Class>Tests.cs.
    //
    // The generic mapping runs first, using the render mode and the outline
    // given here. It covers the main image and color (from the [MainTexture]
    // and [MainColor] marks and the common names), normal map, emission, shade
    // color, cull and cutoff. Apply then writes what the shader keeps under its
    // own names.
    internal interface IShaderApproximation
    {
        // Whether the source is a material of this shader. shaderName is null
        // for a missing shader; its saved values are still in values.
        bool Matches(string shaderName, SourceValues values);

        // The lilToon render mode. Null leaves it to the generic reading
        // (keywords, queue, blend factors).
        Mode? RenderMode(SourceValues values);

        // Whether the substitute takes lilToon's outline shader.
        bool Outline(SourceValues values);

        // material is the lilToon substitute the generic mapping filled.
        // Images baked here go to textures (null: do not bake). Effects of the
        // source that lilToon does not draw go to dropped, by display name.
        void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped);
    }
}
