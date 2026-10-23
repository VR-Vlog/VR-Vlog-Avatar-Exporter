#!/usr/bin/env python3
import json
import re
from pathlib import Path
from unity_meta import validate_meta_guids

root = Path(__file__).resolve().parents[1]
validate_meta_guids(root)
package = json.loads((root / "package.json").read_text(encoding="utf-8"))
schema = json.loads(
    (root / "Schema/VRVLOG_materials_liltoon.schema.json").read_text(encoding="utf-8")
)
profile = (root / "Editor/LilToonMobileProfile.cs").read_text(encoding="utf-8")
validator = (root / "Editor/LilToonExtensionValidator.cs").read_text(encoding="utf-8")
glb = (root / "Editor/GlbDocument.cs").read_text(encoding="utf-8")
injector = (root / "Editor/LilToonGlbExtension.cs").read_text(encoding="utf-8")
reader = (root / "Editor/LilToonMaterialReader.cs").read_text(encoding="utf-8")
window = (root / "Editor/LilToonExporterWindow.cs").read_text(encoding="utf-8")
one_click = (root / "Editor/UniVrmOneClickExporter.cs").read_text(encoding="utf-8")
recovery = (root / "Editor/ExportRecovery.cs").read_text(encoding="utf-8")
fingerprint = (root / "Editor/ExportSourceFingerprint.cs").read_text(encoding="utf-8")
recovery_session = (root / "Editor/ExportRecoverySession.cs").read_text(encoding="utf-8")
recovery_comparison = (root / "Editor/ExportRecoveryComparisonWindow.cs").read_text(encoding="utf-8")
failure_window = (root / "Editor/ExportFailureWindow.cs").read_text(encoding="utf-8")
listing = json.loads((root / "source.json").read_text(encoding="utf-8"))

assert package["name"] == "com.vrvlog.liltoon-vrm-exporter"
assert package["unity"] == "2022.3"
number = r"(?:0|[1-9][0-9]*)"
assert re.fullmatch(rf"{number}\.{number}\.{number}(?:-beta\.{number})?", package["version"])
assert one_click.index("AvatarBaseShape.Preserve(clone, clone,") < one_click.index("Vrm10AppearanceExporter.Export(")
assert "foreach (var mesh in temporaryMeshes) UnityEngine.Object.DestroyImmediate(mesh);" in one_click
# UniVRM is bundled under Editor/Vendor/UniVRM; the Unity modules it needs come from the Unity registry.
assert package["vpmDependencies"] == {}
assert package["dependencies"] == {"com.unity.mathematics": "1.2.6", "com.unity.timeline": "1.7.6"}
assert listing["url"].startswith("https://") and listing["url"].endswith("/index.json")
assert listing["url"] == "https://vr-vlog.github.io/VR-Vlog-Avatar-Exporter/index.json"
assert listing["author"]["url"] == "https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter"
assert listing["infoLink"]["url"] == "https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter"
assert listing["githubRepos"] == ["VR-Vlog/VR-Vlog-Avatar-Exporter"]
assert schema["properties"]["schemaMajor"]["const"] == 1
assert schema["$id"].endswith("/1.3/schema.json")
assert 'backlight' in schema["$defs"]["material"]["properties"]["features"]["items"]["enum"]
assert schema["properties"]["materials"]["maxItems"] == 64
assert "VRVLOG_materials_liltoon" in profile
assert "new HashSet<int>()" in validator
assert "!materialIndices.Add(material.materialIndex)" in validator
assert "Duplicate materialIndex" in validator
assert "IsSupportedShaderFamily(material.shaderFamily)" in validator
assert "SupportedShaderFamilies" in profile
assert "extension.schemaMinor < 0" in validator
assert "IsValidVersion(extension.exporterVersion)" in validator
assert "IsSupportedRenderMode(material.renderMode)" in validator
assert "IsSupportedCullMode(material.cullMode)" in validator
assert "material.renderQueue > 5000" in validator
assert "MaximumFeaturesPerMaterial" in validator
assert "new HashSet<string>(StringComparer.Ordinal)" in validator
assert "!features.Add(feature)" in validator
assert "ValidateProperties(material, extension.schemaMinor, out error)" in validator
assert "MaximumFloatProperties" in validator
assert "MaximumColorProperties" in validator
assert "MaximumTextureProperties" in validator
assert "item.textureIndex >= LilToonMobileProfile.MaximumTextures" in validator
assert "float.IsNaN(value)" in validator
for property_names in ("floatNames", "colorNames", "textureNames"):
    assert f"!{property_names}.Add(item.name)" in validator
for supported in ("lilToon", "lilToonLite", "lilToonMulti"):
    assert f'"{supported}"' in profile
for rejected in ("fur", "refraction", "gem", "tessellation", "audioLink", "custom"):
    assert f'"{rejected}"' in profile

# Delivery contracts: GLB 2.0 parsing/writing, fallback preservation, optional
# extension injection, real material extraction, atomic output, and Unity tests.
assert "Only GLB 2.0 is supported" in glb
assert "GLB contains multiple JSON chunks" in glb
assert "VRMC_materials_mtoon" in injector
assert "extensionsRequired" in injector
assert "must be an array" in injector and "must be an object" in injector
assert "ExportRendererSelection.Enumerate(clone)" in one_click
assert "ExportRendererSelection.RequireActiveRoot(source)" not in one_click
assert one_click.index("ExportRendererSelection.HiddenInScene(source)") < one_click.index("NdmfExportPreparation.InstantiateOwnedCopy(source)")
assert "if (!copy.activeSelf) copy.SetActive(true);" in (root / "Editor/NdmfExportPreparation.cs").read_text(encoding="utf-8")
assert "_MainTex" in reader and "_UseShadow" in reader and "_UseOutline" in reader
assert "特殊シェーダーは標準lilToonとして近似しました" in reader
assert 'const string optionalPrefix = "[Optional]";' in reader
assert 'leaf.StartsWith(optionalPrefix, StringComparison.OrdinalIgnoreCase)' in reader
assert 'return leaf.StartsWith("lilToon", StringComparison.OrdinalIgnoreCase) ? leaf : "";' in reader
assert 'ShaderVariant(material.shader.name).Length > 0' in reader
assert 'item.Semantic == "mainColor"' in reader
assert "メイン画像 '{texture.name}' を元のVRMへ対応付けできません" in reader
assert 'EndsWith("Outline"' in reader
assert 'm.shader.name.EndsWith("Outline"' in reader
assert "未対応の透明モード" in reader
for transparent_variant in ("Refraction", "Gem", "Fur"):
    assert f'n.IndexOf("{transparent_variant}"' in reader
    assert f'shaderName.IndexOf("{transparent_variant}"' in one_click
assert "EnabledOrTexture" in reader
assert "UnsupportedFeatureToggles" in reader
assert "未対応機能 {toggle} は省略" in reader
assert 'AddFeature(record, "backlight", Enabled(material, "_UseBacklight"))' in reader
assert '"_UseBacklight"' not in reader.split("UnsupportedFeatureToggles", 1)[1].split("};", 1)[0]
for backlight_property in ("_BacklightColor", "_BacklightMainStrength", "_BacklightNormalStrength", "_BacklightBorder", "_BacklightBlur", "_BacklightDirectivity", "_BacklightViewStrength", "_BacklightReceiveShadow", "_BacklightBackfaceMask"):
    assert f'"{backlight_property}"' in reader
assert '("_BacklightColorTex", "backlight")' in reader
assert 'case "backlight": return Enabled(material, "_UseBacklight")' in reader
assert 'item.Name == "_BacklightColorTex"' in reader and 'texture == Texture2D.whiteTexture' in reader
assert 'LilToonMaterialReader.Read(source, 0, (_, __) => 0, warnings, suppressSharedTextureEmission);' in one_click
assert 'var warningText' not in window
texture_encoder = (root / "Editor/MobileTextureEncoder.cs").read_text(encoding="utf-8")
assert "PngWriter.Encode(destination)" in texture_encoder
assert "UnityEngine.Object.DestroyImmediate(destination)" in texture_encoder
assert 'EmbeddedBuffer()["byteLength"] = (long)bytes.Length' in glb
for unsupported_toggle in ("_UseEmission2nd", "_UseBump2ndMap", "_UseMatCap2nd"):
    assert f'"{unsupported_toggle}"' in reader
assert "TextureFeatureEnabled" in reader
assert "ValidateEncodedTexture" in injector
assert "Encoded fallback texture is" in injector
assert "PNG dimensions must be positive" in injector
assert "JPEG SOF segment is invalid" in injector
assert "JPEG dimensions must be positive" in injector
assert "complete baseline/progressive JPEG" in injector
assert "JPEG SOS segment is invalid" in injector
assert "MIME type does not match" in injector
assert "PNG chunk CRC is invalid" in injector
assert "PNG IDAT/IEND structure is invalid" in injector
assert "FromDom(root)" in injector
assert "LilToonExtensionValidator.TryValidate(extension" in injector
assert "Extension is missing from extensionsUsed" in injector
assert "outside the GLB materials array" in injector
assert "outside the GLB textures array" in injector
assert "RequireMToonFallback(glb.Json, material.materialIndex)" in injector
assert "VRMC_materials_mtoon 1.0 fallback" in injector
assert "RequireVrm10Root(glb.Json);" in injector
vrm_root_guard = injector.split("private static void RequireVrm10Root", 1)[1].split("private static bool AllNonEmptyStrings", 1)[0]
assert 'Contains(used,"VRMC_vrm")' in vrm_root_guard
assert 'extensionsRequired' not in vrm_root_guard
assert 'text!="1.0"' in vrm_root_guard
assert 'humanoid.TryGetValue("humanBones",out var rawBones)' in injector
assert "RequiredHumanBones" in injector
assert "has no valid node" in injector
assert "new HashSet<long>()" in injector
assert "!assignedNodes.Add(node)" in injector
assert "foreach(var pair in bones)" in injector
assert "bones.ContainsKey(boneName)" in injector
assert "HumanBoneParents.ContainsKey(pair.Key)" in injector
assert "ValidateHumanBoneHierarchy(root,boneNodes)" in injector
assert "IsNodeDescendant" in injector
assert "BonesRequiringDirectParent" in injector
assert 'meta.TryGetValue("name",out var rawName)' in injector
assert 'meta.TryGetValue("authors",out var rawAuthors)' in injector
assert 'meta.TryGetValue("licenseUrl"' not in injector
for required_meta in ("avatarPermission", "allowExcessivelyViolentUsage", "allowExcessivelySexualUsage", "commercialUsage", "allowPoliticalOrReligiousUsage", "allowAntisocialOrHateUsage", "creditNotation", "allowRedistribution", "modification"):
    assert f'"{required_meta}"' in injector
assert "RequireBoolean" in injector
assert "RequireEnum" in injector
assert "does not declare VRMC_materials_mtoon in extensionsUsed" in injector
assert "ValidateEncodedTexture(glb, texture.textureIndex, textureSources)" in injector
assert "Unexpected extension property" in injector
assert 'Guid.NewGuid().ToString("N")' in recovery_session
assert "File.Replace(temporary, destination, null)" in recovery_session
assert "finally { if (File.Exists(temporary)) File.Delete(temporary); }" in recovery_session
assert recovery_session.index("LilToonGlbExtension.Validate(bytes)") < recovery_session.index("Directory.CreateDirectory(directory)")
assert "UniVrmOneClickExporter.Export" not in window
assert 'Compatibility.DependencyPolicy.LilToonError(version, ExporterLocalization.T)' in window
assert 'var version = InstalledLilToonVersion();' in window and 'return version;' in window
# lilToon is the registered package or, imported from its unitypackage, found under Assets.
installation = (root / "Editor/Compatibility/LilToonInstallation.cs").read_text(encoding="utf-8")
assert 'internal static string InstalledLilToonVersion() => Compatibility.LilToonInstallation.Find()?.Version;' in window
assert '"2.3.4"' not in (root / "Editor/ExperimentalExpressionCaptureSession.cs").read_text(encoding="utf-8")
assert 'public const string PackageName = "jp.lilxyzw.liltoon";' in installation and 'package.name, PackageName' in installation
assert installation.index('PackageInfo.GetAllRegisteredPackages()') < installation.index('return FindUnderAssets();')
assert 'PackageJsonGuid = "397d2fa9e93fb5d44a9540d5f01437fc"' in installation
assert "RequireLilToon(GameObject avatar, ICollection<string> warnings)" in window
# Both export entries compare the shaders the avatar uses with the catalogue;
# only a missing lilToon (or its baker) stops, other differences are warnings.
assert window.index("if (error != null) throw new InvalidOperationException(error);") < window.index("LilToonStructureCheck.Inspect(avatar, Compatibility.DependencyPolicy.DisplayLilToon(version, ExporterLocalization.T), warnings);")
assert "LilToonExporterWindow.RequireLilToon(Source, warnings);" in (root / "Editor/ExperimentalExpressionCaptureSession.cs").read_text(encoding="utf-8")
# Both "アプリに転送" exports go through the expression board's save; the quick
# one prepares a board out of sight, so it writes what an untouched "通常" writes.
assert "ExperimentalExpressionCaptureWindow.PrepareHidden(avatar)" in (root / "Editor/AppTransferExport.cs").read_text(encoding="utf-8")
assert "board.TransferExport(" in (root / "Editor/AppTransferExport.cs").read_text(encoding="utf-8")
assert "UniVrmOneClickExporter.Export" not in (root / "Editor/AppTransferExport.cs").read_text(encoding="utf-8")
assert 'return info != null && !string.IsNullOrWhiteSpace(info.version) ? info.version : package["version"]' not in window
assert f'? info.version : "{package["version"]}"' in window
assert "using UnityEditor.PackageManager;" not in window
assert "using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;" in window
assert "FindLilToonPackage" not in window and "GetAllRegisteredPackages" not in window
assert "Vrm10AppearanceExporter.Export" in one_click
assert one_click.index("NdmfExportPreparation.ValidateSource(source, exclusions.Contains, deferUnresolvedTargets: true)") < one_click.index("VrChatExpressionMenu.Read(source)")
assert one_click.index("NdmfExportPreparation.InstantiateOwnedCopy(source)") < one_click.index("NdmfExportPreparation.ValidateCopy(source, clone,") < one_click.index("MaAppearanceSnapshot.Apply(source, clone,")
assert "target => exclusions.ContainsCopyTransform(clone, target)" in one_click
assert one_click.index("VrChatExpressionSampler.Analyze(clone,") < one_click.index("FaceEmoExpressions.ApplyPreparedDefaultFace(clone,") < one_click.index("NeutralShapeSnapshot.Capture(clone)") < one_click.index("preparedMenuBindings.Capture(menu)") < one_click.index("VrChatExpressionBaker.Bake(null, clone,") < one_click.index("ExportOptimizationBindings.Capture(clone,")
assert "VrChatExpressionSampler.ApplyMergedDefaults(source, clone," not in one_click
assert one_click.index("optimizerBindings.ValidateAndApply(") < one_click.index("LilToonFullSnapshot.Capture(clone,") < one_click.index("Vrm10AppearanceExporter.Export(")
assert one_click.index("NdmfExportPreparation.Prepare(source, clone,") < one_click.index("VrChatExpressionBaker.Bake(null, clone,")
assert one_click.index("SkinnedMeshFallbackWeights.Preserve(clone,") < one_click.index("NdmfExportPreparation.Prepare(source, clone,") < one_click.index("AvatarBaseShape.Preserve(clone, clone,")
assert one_click.index("NdmfExportPreparation.Prepare(source, clone,") < one_click.rindex("SkinnedMeshFallbackWeights.Preserve(clone,") < one_click.index("Vrm10AppearanceExporter.Export(")
assert one_click.count("SkinnedMeshFallbackWeights.Preserve(clone, temporaryMeshes, warnings, fixedRootJoints)") == 2
assert "new ExportAttachmentSession(source, clone, fixedRootJoints: fixedRootJoints)" in one_click
assert one_click.index("Vrm10AppearanceExporter.Export(") < one_click.index("ExportSkinRoots.Repair(exported,")
assert "new MobileTextureSerializer(warnings)" in one_click
assert "NdmfExportPreparation.InstantiateOwnedCopy(source)" in one_click
assert "ReplaceLilToonMaterials(clone" in one_click
assert "DestroyImmediate(clone)" in one_click
assert "MToon10Meta.UnityShaderName" in one_click
assert "CreateMToonFallback(source, created, warnings, suppressSharedTextureEmission, textures, outlineMasks, outlineScale, matcapBakes)" in one_click
assert "created.Add(material)" in one_click
assert 'Float(source, "_Cull", 2f) == 2f' in one_click
assert "context.Validate()" in one_click
assert "using UnityEditor.PackageManager;" not in one_click
assert "PackageManagerPackageInfo" not in one_click and "EnsureUniVrmVersion" not in one_click
assert (root / ".github/workflows/build-listing.yml").is_file()
assert (root / ".github/workflows/release-vpm.yml").is_file()
assert (root / "Website/index.html").is_file()
assert (root / "Website/app.js").is_file()
listing_workflow = (root / ".github/workflows/build-listing.yml").read_text(encoding="utf-8")
release_workflow = (root / ".github/workflows/release-vpm.yml").read_text(encoding="utf-8")
assert "cb31c3b5d17d1070d7741c61de2ca1b219224039" in listing_workflow
assert "dotnet-version: 8.0.x" in listing_workflow
assert "--disable-build-servers" in listing_workflow
assert "--maxcpucount:1" in listing_workflow
assert "${{ env.pathToCi }}/.nuke/temp" not in listing_workflow
assert "pull_request:" in listing_workflow
# Trigger contracts are parsed and regression-tested by check-pipeline.py and test-pipeline.py.
for listing_path in (".github/workflows/build-listing.yml", "package.json", "source.json",
                     "Tools/check-listing.py", "Tools/test-listing.py"):
    assert f'"{listing_path}"' in listing_workflow
assert 'path.startswith("Website/")' in listing_workflow
assert "\n  push:" not in listing_workflow
assert "check-listing-builder:" in listing_workflow
assert "if: github.event_name == 'pull_request' && needs.changes.outputs.relevant == 'true'" in listing_workflow
assert "\n    needs: changes\n" in listing_workflow
assert "if: ${{ always() && github.event_name == 'pull_request' }}" in listing_workflow
assert "needs: [changes, check-listing-builder]" in listing_workflow
assert "github.event.release.draft == false" in listing_workflow
assert "types: [published, released]" in listing_workflow
assert listing_workflow.index("github.event.release.draft == false") < listing_workflow.index("environment:")
assert "Build listing tool" in listing_workflow
assert "Generate VPM listing" in listing_workflow
assert "Generate listing from a local release fixture" in listing_workflow
assert "--package-listing-source-folder" in listing_workflow
assert 'test -s "$fixture/output/index.json"' in listing_workflow
assert 'Compatibility/dependencies.json' not in release_workflow
# The existing workflow may retain a historical stable UI default. Publication
# uses the explicit input and release_policy rejects any manifest mismatch.
assert re.search(r"version:\n\s+description: Exporter version without the v prefix\n\s+required: true\n\s+default: \d+\.\d+\.\d+\n", release_workflow)
# Event-specific version, commit, hash and channel inputs are checked by check-pipeline.py.
assert '--target "${EXPECTED_COMMIT}"' in release_workflow
release_policy = (root / "Tools/release_policy.py").read_text(encoding="utf-8")
assert '"refs/heads/main"' in release_policy
assert 'sha != expected_sha' in release_policy
assert 'version != package["version"]' in release_policy
assert '["--prerelease", "--latest=false"]' in release_policy
assert 'Built ZIP differs from the validated release candidate' in release_workflow
assert 'python3 Tools/release_policy.py > release-flags.txt' in release_workflow
assert '"${release_flags[@]}"' in release_workflow
import importlib.util
runner_spec = importlib.util.spec_from_file_location("validation_runner", root / "Tools/run-validation.py")
validation_runner = importlib.util.module_from_spec(runner_spec)
runner_spec.loader.exec_module(validation_runner)
for script in ("Tools/test-release-policy.py", "Tools/test-listing.py"):
    assert [str(__import__('sys').executable), script] in validation_runner.plan()
for workflow_name in ("validate.yml", "release-vpm.yml", "release-candidate.yml"):
    assert 'python3 Tools/run-validation.py --report work/validation.json' in (root / ".github/workflows" / workflow_name).read_text(encoding="utf-8")
assert 'tag v${VERSION} already exists' in release_workflow
assert 'git ls-remote --exit-code --tags origin' in release_workflow
# Listing dispatch pins beta to its published tag; check-pipeline.py checks the complete script.
# UniVRM is bundled, so the release neither builds nor attaches UniVRM packages.
assert "com.vrmc" not in release_workflow and "univrm" not in release_workflow.lower()
assert (root / "ThirdPartyNotices/UniVRM.md").is_file()
assert (root / "LICENSE").is_file()
assert "instance.Vrm.Meta.CopyTo" not in one_click
assert "Never copy contact information" in one_click
assert "NormalTextureScale = normalEnabled ?" in one_click
assert "EmissiveFactorLinear = FallbackColor(source, MaterialColorType.emissionColor," in one_click
assert "MatcapColorFactorSrgb = FallbackColor(source, MaterialColorType.matcapColor," in one_click
assert "var parametricRim = FallbackColor(source, MaterialColorType.rimColor," in one_click
assert "ParametricRimColorFactorSrgb = parametricRim," in one_click
assert 'source.GetFloat("_UseBacklight") > .5f ? Color(source, "_BacklightColor"' in one_click
assert 'backlightEnabled ? Float(source, "_BacklightDirectivity"' in one_click
assert "ShadeColorFactorSrgb = FallbackColor(source, MaterialColorType.shadeColor," in one_click
assert "ShadeColorTexture = shadowEnabled" in one_click
for gated_texture in ("NormalTexture", "EmissiveTexture", "MatcapTexture", "RimMultiplyTexture"):
    assert f"{gated_texture} =" in one_click and "? Texture(source," in one_click
assert 'Float(source, "_OutlineWidth", 0f)) * 0.01f' in one_click
assert 'OutlineMaskTexture.Create(Texture(source, "_OutlineWidthMask"), textures, outlineMasks)' in one_click
assert "HasPortableOutline(source)" in one_click
assert 'OutlineWidthMultiplyTexture = outlineEnabled ? Texture(source, "_OutlineTex")' not in one_click
assert 'private string avatarName = "";' not in window
normal_export = window.split('internal static ExportRecoverySession ExportAndSaveNormally(', 1)[1].split(
    'internal static string PackageVersion()', 1
)[0]
assert 'bytes = create(new ExportRecoveryOptions(), report, warnings);' in normal_export
assert 'ExportOutputWriter.Write(destination, bytes);' in normal_export
assert 'catch (OperationCanceledException)' in normal_export
assert 'ExportRecoverySession.FromFailedExport(source, destination, create, exception, report,' in normal_export
assert normal_export.index('bytes = create(') < normal_export.index('ExportOutputWriter.Write(')
# A healthy export has no previous diagnosis/draft to invalidate. The new
# behavior tests execute this route; it must not construct or retry recovery.
assert 'new ExportRecoverySession(' not in normal_export
assert '.Attempt(' not in normal_export and '.SavePending(' not in normal_export
assert 'ExportRecoverySourceStamp' not in normal_export
assert (root / 'Tests/Editor/ExportFlowTests.cs').is_file()
assert 'ExportAndSaveNormally(' in (root / 'Tests/Editor/ExportFlowTests.cs').read_text(encoding='utf-8')
assert 'ExcludeHiddenRenderer' in recovery and 'ExportGimmickDetection.Inspect(action.Renderer, source.transform)?.Unit' in recovery
assert 'code == "audio-link"' in recovery and 'Official(audio)' in recovery
# Recovery compares partial solver output with the original capture separately
# from the exact authored-input hash; it must not replace that baseline.
assert 'ExportSourceFingerprint.Capture(source, out var partialRotations)' in recovery
assert 'readonly ExportSourceFingerprint.PartialRotation[] partialRotations;' in recovery
assert 'partialRotations[index].Matches(current[index])' in recovery
assert 'EditorJsonUtility.ToJson(' not in fingerprint
assert 'new StringBuilder' not in fingerprint
assert 'LastSuccess = new SuccessfulAttempt' in recovery_session
assert 'item.Action.Id == diagnostic.Action.Id' in recovery_session
assert 'internal static ExportRecoverySession FromFailedExport(' in recovery_session
recovery_save = recovery_session.split('internal void SavePending()', 1)[1].split(
    'internal static ExportRecoveryOptions CopyOptions(', 1
)[0]
assert 'CheckForChanges() || !CanSave' in recovery_save
assert 'ExportOutputWriter.Write(Destination, LastSuccess.Bytes);' in recovery_save
assert 'HasPendingSave = false;' in recovery_save
output_writer = recovery_session.split('internal static class ExportOutputWriter', 1)[1]
assert output_writer.index('LilToonGlbExtension.Validate(bytes);') < output_writer.index('Directory.CreateDirectory(directory);')
assert 'File.WriteAllBytes(temporary, bytes);' in output_writer
assert 'LilToonGlbExtension.Validate(File.ReadAllBytes(temporary));' in output_writer
assert 'File.Replace(temporary, destination, null)' in output_writer
assert 'File.Move(temporary, destination)' in output_writer
assert 'finally { if (File.Exists(temporary)) File.Delete(temporary); }' in output_writer
assert 'ExportVrmLilToonPreview.Apply(attempt.Bytes, imported.gameObject)' in recovery_comparison
assert 'session.CreatePreview(' not in recovery_comparison
assert 'GUILayout.Toolbar(' not in recovery_comparison
assert 'controlRigGenerationOption: ControlRigGenerationOption.None' in recovery_comparison
assert '!HasVrmPreview || !session.CanSave' in recovery_comparison
assert 'BuildSupportText(LilToonExporterWindow.PackageVersion())' in failure_window
assert (root / "Tests/Editor/ExportRecoveryTests.cs").is_file()
assert (root / "Tests/Editor/ExportRecoverySessionTests.cs").is_file()
assert one_click.index('MaAppearanceSnapshot.Apply(source, clone,') < one_click.index('LilToonMainTextureBaker.ValidateAvatar(clone,') < one_click.index('NdmfExportPreparation.Prepare(source, clone,')
assert 'LilToonMainTextureBaker.ApplyOmissions(clone, temporaryMaterials, warnings, bakeOptions);' in one_click
assert 'fullSnapshot.Inject(exported, exporterVersion, lilToonVersion)' in one_click
assert one_click.index('LilToonFullSnapshot.Capture(clone,suppressSharedTextureEmission,suppressHdrTextureEmission, warnings, animationSource: source)') < one_click.index('LilToonMainTextureBaker.Prepare(clone,')
assert 'guard.Step(ExportStage.LilToonSnapshot, () => fullSnapshot.Bind(converter, model, storage));' in one_click
assert 'guard.Watch(ExportStage.BlinkCapture, () => blink.Bind(converter, model, storage));' in one_click
assert 'excludedExpressions' not in window and 'DrawExpressions' not in window
assert 'var read = VrChatExpressionMenu.Read(source);' in one_click
assert 'var menuPolicy = guard.IsOmitted(ExportStage.ExpressionMenu) ? new VrChatMenuImportPolicy { SkipAll = true } : null;' in one_click
assert 'bool ExcludedBinding(string path) => exclusions.ContainsPath(path) || gimmicks.ContainsPath(path);' in one_click
assert 'new PreparedExpressionBindings(clone, menu, excludedPath: ExcludedBinding)' in one_click
assert 'VrChatExpressionSampler.Analyze(clone, expressionBindings.ExcludesPreparedPath,' in one_click
assert one_click.index('FaceEmoExpressions.Capture(source, clone, ExcludedBinding,') < one_click.index('NdmfExportPreparation.Prepare(source, clone,')
assert 'faceEmoBindings?.RebindPrepared(transformed.PreparedRendererFor,\n                        transformed.ObjectRegistry, transformed.IsolatedCopyOf);' in one_click
assert 'new PoseExportSession(source, poseOptions, exclusions.Contains, menuPolicy)' in one_click
assert 'SkipVrChatMenus' not in recovery and 'ExcludeMenuBranch' not in recovery
assert one_click.index('AvatarBaseShape.Preserve(clone, clone,') < one_click.index('VrChatExpressionBaker.Bake(') < one_click.index('Vrm10AppearanceExporter.Export(')
assert 'VrmMenuExpressions.Add(exported, expressions, optimizerBindings, warnings)' in one_click
assert 'emotionCandidates: VrmMenuExpressions.EmotionCandidates(expressions)' in one_click
assert "ValidateAllEncodedTextures(glb, textureSources);" in injector
assert "materialCount > LilToonMobileProfile.MaximumMaterials" in injector
assert (root / "Tests/Editor/GlbDocumentTests.cs").is_file()
assert (root / "Tests/Editor/VRVlog.LilToonExporter.Editor.Tests.asmdef").is_file()
test_assembly = json.loads((root / "Tests/Editor/VRVlog.LilToonExporter.Editor.Tests.asmdef").read_text(encoding="utf-8"))
vendor_assemblies = {"VRVlog.LilToonExporter.Vendor." + name for name in ("VRM10", "UniGLTF", "VrmLib")}
assert vendor_assemblies.issubset(test_assembly["references"])
for assembly_path in ("Editor/VRVlog.LilToonExporter.Editor.asmdef", "Tests/Editor/VRVlog.LilToonExporter.Editor.Tests.asmdef"):
    assembly = json.loads((root / assembly_path).read_text(encoding="utf-8"))
    assert not assembly.get("defineConstraints") and not assembly.get("versionDefines"), assembly_path
    assert not any(reference in {"VRM10", "UniGLTF", "VrmLib"} for reference in assembly["references"]), assembly_path

# User-facing UE failures must remain actionable in every shipped locale.
unified_failure_sources = (
    "Modular Avatar / NDMF の処理で Unified Expressions の追跡用変形が失われました。メッシュや BlendShape を変更する追加ツールの設定を確認してください。",
    "Unified Expressions の書き出しに必要な VRM 情報がありません。",
    "Unified Expressions の出力 mesh に primitive がありません: ",
    "Unified Expressions の morph target 参照が不正です: ",
) + tuple("Invalid Unified Expressions " + index + " index." for index in ("node", "mesh", "morph", "material"))
recovery_sources = set()
for source_text in (recovery, recovery_session, recovery_comparison, failure_window,
                    (root / "Editor/ExportDetailsText.cs").read_text(encoding="utf-8")):
    for literal in re.findall(r'"((?:\\.|[^"\\])*)"', source_text):
        source = json.loads('"' + literal + '"')
        if re.search('[ぁ-んァ-ヶ一-龠]', source):
            recovery_sources.add(source)
for locale in ("en", "ko", "zh-Hans", "zh-Hant"):
    entries = json.loads((root / ("Editor/Locales/ExporterLocale_" + locale + ".json")).read_text(encoding="utf-8"))["entries"]
    translations = {entry["source"]: entry["translated"] for entry in entries}
    assert len(translations) == len(entries), locale + ": duplicate locale source"
    for source in unified_failure_sources:
        assert translations.get(source) and translations[source] != source, locale + ": missing UE failure translation"
    for source in recovery_sources:
        # Some short Chinese labels (for example 正面) are also valid Japanese.
        assert translations.get(source), locale + ": missing recovery translation"

print("Exporter implementation, package, schema, UE and recovery locale checks passed.")

# A name inventory alone must not hide an unclassified rendering property.
catalogue = json.loads((root / "Schema/LilToon234Catalogue.json").read_text(encoding="utf-8"))
properties = {p["name"]: p for p in catalogue["properties"]}
assert len(properties) == len(catalogue["properties"]) == 614
assert len(catalogue["shaders"]) == 65
for shader in catalogue["shaders"]:
    assert set(shader["propertyNames"]) <= properties.keys()
for prop in properties.values():
    assert prop["category"] in ("runtime", "editor", "external")
    assert prop["rendering"]["kind"] and prop["storage"]
    if prop["category"] == "runtime":
        assert set(prop["validation"]) == {"snapshot", "active", "boundary"}
        assert prop["rendering"]["sources"] or prop["rendering"]["kind"] == "declared-unused-by-pinned-passes"
for name in ("_egci", "_egc1", "_e2ga7", "_IDMaskCompile"):
    assert properties[name]["category"] == "editor"
for name in ("_EmissionGradTex", "_Emission2ndGradTex", "_IDMask1"):
    assert properties[name]["category"] == "runtime"

assert "PreserveVertexColors(model, exporter.Storage)" in (root / "Editor/Vrm10AppearanceExporter.cs").read_text(encoding="utf-8")
