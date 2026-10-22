using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToon;
using VRVlog.LilToonExporter;
using Check = VRVlog.LilToonExporter.LilToonStructureCheck;

// Host tests for the pre-export comparison. Unity reading of installed shaders
// is covered by Tests/Editor/LilToonStructureCheckTests.cs.
public static class LilToonStructureCheckTests
{
    static int count;
    static void Expect(bool value, string message) { count++; if (!value) throw new Exception(message); }

    static Check.ShaderStructure FromCatalogue(string shader)
    {
        var result = new Check.ShaderStructure(shader);
        var names = shader == Check.BakerShader ? Check.BakerProperties : LilToon234Catalogue.RequiredProperties[shader];
        foreach (var name in names) result.Properties.Add((name, Type(name), Dimension(name)));
        if (shader == Check.BakerShader) { result.Passes.Add(("", "")); return result; }
        foreach (var pass in LilToon234Catalogue.PassLightModes[shader]) result.Passes.Add((pass.Key, pass.Value));
        return result;
    }
    static string Type(string name) => LilToon234Catalogue.Properties[name].Split(':')[0];
    static string Dimension(string name) => Type(name) != "texture" ? "" :
        LilToon234Catalogue.TextureFaces.TryGetValue(name, out var faces) && faces == 6 ? "Cube" : "2D";
    static void Replace(Check.ShaderStructure shader, string name, string type, string dimension)
    {
        var index = shader.Properties.FindIndex(p => p.Name == name);
        shader.Properties[index] = (name, type, dimension);
    }
    static List<Check.Difference> Only(Check.ShaderStructure shader, string asShader, Check.Kind kind, string name)
    {
        var differences = Check.Compare(shader, asShader);
        Expect(differences.Count == 1 && differences[0].Kind == kind && differences[0].Name == name && differences[0].Shader == asShader,
            "Expected only " + kind + " " + name + ": " + string.Join("; ", differences));
        return differences;
    }

    public static void Run()
    {
        foreach (var shader in LilToon234Catalogue.Shaders.Keys)
            Expect(Check.Compare(FromCatalogue(shader), shader).Count == 0, "Catalogue structure matches itself: " + shader);
        Expect(LilToon234Catalogue.Shaders.Count == 52, "Every official material shader is checked");

        // Editor-only and placeholder declarations are known but not required.
        var lilToon = FromCatalogue("lilToon");
        lilToon.Properties.Add(("_DummyProperty", "float", ""));
        lilToon.Properties.Add(("_PackingTexture1", "texture", "2D"));
        Expect(Check.Compare(lilToon, "lilToon").Count == 0, "Editor and placeholder properties pass");

        var type = FromCatalogue("lilToon");
        Replace(type, "_Cutoff", "color", "");
        var difference = Only(type, "lilToon", Check.Kind.PropertyType, "_Cutoff")[0];
        Expect(difference.Actual == "color" && difference.Expected == "float", "Type difference keeps both types");

        var cube = FromCatalogue("lilToon");
        Replace(cube, "_ReflectionCubeTex", "texture", "2D");
        Only(cube, "lilToon", Check.Kind.TextureDimension, "_ReflectionCubeTex");
        var flat = FromCatalogue("lilToon");
        Replace(flat, "_MainTex", "texture", "Cube");
        Only(flat, "lilToon", Check.Kind.TextureDimension, "_MainTex");

        var missing = FromCatalogue("lilToon");
        missing.Properties.RemoveAll(p => p.Name == "_ShadowColor");
        Only(missing, "lilToon", Check.Kind.MissingProperty, "_ShadowColor");

        var unknown = FromCatalogue("lilToon");
        unknown.Properties.Add(("_VrvNotInCatalogue", "float", ""));
        Only(unknown, "lilToon", Check.Kind.UnknownProperty, "_VrvNotInCatalogue");

        // A runtime property that 2.3.4 declares only in another shader.
        var furOnly = LilToon234Catalogue.RequiredProperties["Hidden/lilToonFur"].Except(LilToon234Catalogue.RequiredProperties["lilToon"]).First();
        var moved = FromCatalogue("lilToon");
        moved.Properties.Add((furOnly, Type(furOnly), Dimension(furOnly)));
        Only(moved, "lilToon", Check.Kind.UnknownProperty, furOnly);

        // GrabPass, lilToon's fake passes and a repeated name are not counted.
        var passes = FromCatalogue("Hidden/lilToonGem");
        passes.Passes.Insert(0, ("", ""));
        passes.Passes.Add(("", "Never"));
        passes.Passes.Add(("FAKE", "Never"));
        passes.Passes.Add((passes.Passes[1].Name, "ShadowCaster"));
        Expect(Check.Compare(passes, "Hidden/lilToonGem").Count == 0, "Unnamed, Never and repeated passes are not counted");

        var upper = FromCatalogue("lilToon");
        upper.Passes[upper.Passes.FindIndex(p => p.Name == "FORWARD")] = ("FORWARD", "FORWARDBASE");
        Expect(Check.Compare(upper, "lilToon").Count == 0, "LightMode is compared case-insensitively");
        var mode = FromCatalogue("lilToon");
        mode.Passes[mode.Passes.FindIndex(p => p.Name == "FORWARD")] = ("FORWARD", "ForwardAdd");
        difference = Only(mode, "lilToon", Check.Kind.PassLightMode, "FORWARD")[0];
        Expect(difference.Actual == "ForwardAdd" && difference.Expected == "ForwardBase", "LightMode difference keeps both modes");

        // lilToon's Shader Setting: "Use ForwardAdd" off.
        var forwardAdd = FromCatalogue("Hidden/lilToonCutoutOutline");
        forwardAdd.Passes.RemoveAll(p => p.Name.StartsWith("FORWARD_ADD", StringComparison.Ordinal));
        var noForwardAdd = Check.Compare(forwardAdd, "Hidden/lilToonCutoutOutline");
        Expect(noForwardAdd.Count == 2 && noForwardAdd.All(d => d.Kind == Check.Kind.MissingPass), "ForwardAdd passes are missing");
        var remedies = Check.Remedies(noForwardAdd).ToList();
        Expect(remedies.Count == 1 && remedies[0].Contains("ForwardAdd"), "Missing ForwardAdd points to the Shader Setting");

        // lilToon's Shader Setting: "Fix for Deffered" on.
        var deferred = FromCatalogue("Hidden/lilToonCutoutOutline");
        deferred.Passes[deferred.Passes.FindIndex(p => p.Name == "SHADOW_CASTER_OUTLINE")] = ("SHADOW_CASTER", "ShadowCaster");
        var renamed = Check.Compare(deferred, "Hidden/lilToonCutoutOutline");
        Expect(renamed.Count == 2 && renamed.Any(d => d.Kind == Check.Kind.MissingPass && d.Name == "SHADOW_CASTER_OUTLINE") &&
            renamed.Any(d => d.Kind == Check.Kind.UnknownPass && d.Name == "SHADOW_CASTER"), "Deferred fix renames the outline shadow pass");
        remedies = Check.Remedies(renamed).ToList();
        Expect(remedies.Count == 1 && remedies[0].Contains("Fix for Deffered"), "Renamed shadow pass points to the Shader Setting");
        remedies = Check.Remedies(Check.Compare(missing, "lilToon")).ToList();
        Expect(remedies.Count == 1 && remedies[0].Contains("{0}に切り替える"), "Property differences point to the catalogue version");

        // The distributed baker lacks four decal parameters of the tag source.
        var baker = FromCatalogue(Check.BakerShader);
        baker.Properties.Add(("_PackingTexture1", "texture", "2D"));
        baker.Properties.Add(("_MainTex3D", "texture", "3D"));
        baker.Properties.Add(("_ResX", "float", ""));
        Expect(Check.Compare(baker, Check.BakerShader).Count == 0, "Distributed baker passes");
        baker.Properties.Add(("_Main2ndTexDecalAnimation", "vector", ""));
        Expect(Check.Compare(baker, Check.BakerShader).Count == 0, "Baker with the source's decal parameters passes");
        var bakerMissing = FromCatalogue(Check.BakerShader);
        bakerMissing.Properties.RemoveAll(p => p.Name == "_Main2ndTex");
        Only(bakerMissing, Check.BakerShader, Check.Kind.MissingProperty, "_Main2ndTex");
        var bakerType = FromCatalogue(Check.BakerShader);
        Replace(bakerType, "_MainTexHSVG", "color", "");
        Only(bakerType, Check.BakerShader, Check.Kind.PropertyType, "_MainTexHSVG");
        var bakerPass = FromCatalogue(Check.BakerShader);
        bakerPass.Passes.Clear();
        Only(bakerPass, Check.BakerShader, Check.Kind.MissingShader, "");

        // Only the baker stops the export; the shaders the avatar uses warn.
        foreach (var stop in new[] { bakerMissing, bakerType, bakerPass })
            Expect(Check.Compare(stop, Check.BakerShader).All(Check.Stops), "Baker differences stop: " + string.Join("; ", Check.Compare(stop, Check.BakerShader)));
        foreach (var (shader, asShader) in new[] { (type, "lilToon"), (cube, "lilToon"), (missing, "lilToon"), (unknown, "lilToon"), (mode, "lilToon") })
            Expect(!Check.Compare(shader, asShader).Any(Check.Stops), "Material shader differences only warn: " + asShader);

        bool rejected = false;
        try { Check.Compare(lilToon, "Hidden/VRVlogTests/lilToon"); } catch (ArgumentException) { rejected = true; }
        Expect(rejected, "Only catalogue shaders are compared");

        // The stop message names both versions and the first differences, then the rest as a count.
        var many = FromCatalogue("lilToon");
        foreach (var name in new[] { "_Cutoff", "_Invisible", "_AsUnlit", "_FlipNormal", "_ShiftBackfaceUV", "_BackfaceForceShadow", "_VertexLightStrength" })
            Replace(many, name, "vector", "");
        var all = Check.Compare(many, "lilToon");
        Expect(all.Count == 7, "Seven type differences");
        var message = Check.Message(all, "2.1.0", text => text);
        var lines = message.Split('\n');
        Expect(lines[0] == "lilToon 2.1.0のベイク用シェーダーが2.3.4と違うため書き出せません", "Header names both versions: " + lines[0]);
        Expect(lines[1] == "lilToon: プロパティ_Invisibleの型がvectorです（2.3.4はfloat）", "Difference line: " + lines[1]);
        Expect(lines.Length == 1 + Check.ShownDifferences + 2 && lines[1 + Check.ShownDifferences] == "ほか2件", "Only the first differences are listed: " + message);
        Expect(lines[lines.Length - 1] == "VCC／ALCOMでlilToonを入れ直すか2.3.4に切り替える", "Remedy line: " + lines[lines.Length - 1]);
        var translated = Check.Message(all, "2.1.0", text => "[" + text + "]");
        Expect(translated.Split('\n').All(line => line.StartsWith("[", StringComparison.Ordinal)), "Every fixed text is translated: " + translated);

        // Warnings: one line per shader and kind saying what the export does, then the remedy.
        var warnings = Check.Warnings(all, text => text).ToList();
        Expect(warnings.Count == 2, "One line for the kind and one remedy: " + string.Join(" | ", warnings));
        Expect(warnings[0].StartsWith("lilToon: 型がlilToon 2.3.4と違うプロパティがあるマテリアルはMToonで近似します（", StringComparison.Ordinal) &&
            warnings[0].Contains("_Invisible vector→float") && warnings[0].Contains("_VertexLightStrength vector→float"), "Type line lists every property: " + warnings[0]);
        Expect(warnings[1] == "lilToon 2.3.4に切り替えるとUnityでもアプリと同じ見た目になります", "Remedy names the catalogue version: " + warnings[1]);
        warnings = Check.Warnings(Check.Compare(unknown, "lilToon"), text => text).ToList();
        Expect(warnings[0] == "lilToon: lilToon 2.3.4にないプロパティは書き出しません（_VrvNotInCatalogue）", "Unknown property line: " + warnings[0]);
        warnings = Check.Warnings(noForwardAdd, text => text).ToList();
        Expect(warnings.Count == 2 && warnings[0].StartsWith("Hidden/lilToonCutoutOutline: 足りないパスはlilToon 2.3.4のパスとして書き出します（FORWARD_ADD", StringComparison.Ordinal) &&
            warnings[1].Contains("ForwardAddパスを使用"), "Missing ForwardAdd line and its Shader Setting: " + string.Join(" | ", warnings));
        Expect(Check.Warnings(all, text => "[" + text + "]").All(line => line.StartsWith("[", StringComparison.Ordinal)), "Every warning is translated");
        Expect(!Check.Warnings(new List<Check.Difference>(), text => text).Any(), "No differences, no warnings");

        Console.WriteLine("lilToon structure check tests passed: " + count);
    }
}
