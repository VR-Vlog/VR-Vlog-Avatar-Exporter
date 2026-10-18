using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // One serialized value a gimmick sets on its own objects (for example a
    // contact parameter). Configuration data is evidence; object names are not.
    internal sealed class ExportGimmickValue
    {
        internal string Path;
        internal string Type;
        internal string Property;
        internal string Value;
    }

    // A gimmick identity as data. Every criterion that is set must hold. Names
    // only locate children; a rule always also needs a component type, a
    // configured value, a shader name or a render texture loop.
    internal sealed class ExportGimmickRule
    {
        // Reported with every finding (ExportResultEntry.RuleId).
        internal string Id;
        internal string Reason;
        // A component of one of these types marks its object as the gimmick.
        // A name with a dot is a full type name; without one, the type name in
        // any namespace. Anchored gimmicks are found on inactive objects too.
        internal string[] AnchorTypes = Array.Empty<string>();
        // Other components an anchor object may carry and still be removed as
        // a whole. Otherwise only the anchor components are removed.
        internal string[] DedicatedTypes = Array.Empty<string>();
        // Anchor properties that point at objects belonging to the gimmick.
        // Objects inside the avatar are removed as their own hierarchies.
        internal string[] LinkedObjectProperties = Array.Empty<string>();
        // Avatar root children a build tool generates for an anchored gimmick.
        internal string[] GeneratedRootChildren = Array.Empty<string>();
        // The candidate object itself must carry one of these types.
        internal string[] EntryTypes = Array.Empty<string>();
        internal string[] RequiredChildren = Array.Empty<string>();
        internal ExportGimmickValue[] RequiredValues = Array.Empty<ExportGimmickValue>();
        // Some renderer in the hierarchy uses one of these shaders.
        internal string[] ShaderNames = Array.Empty<string>();
        // A camera in the hierarchy renders to a texture that a renderer in the
        // same hierarchy shows. VRM has no cameras, so the pair never works there.
        internal bool RenderTextureLoop;
        // Optional prefab identity. It records how a match was found and never
        // matches on its own, so unpacked copies are found by structure.
        internal string[] PrefabGuids = Array.Empty<string>();
        // Checks of this gimmick only, run before the shared safety checks.
        internal Func<GameObject, Transform, Func<Transform, bool>, string> UnsafeReason;

        internal bool Anchored => AnchorTypes.Length > 0;
        internal bool HasEvidence => Anchored || RequiredValues.Length > 0 || ShaderNames.Length > 0 || RenderTextureLoop;
    }

    internal static class ExportGimmickRules
    {
        internal const string PettingSystem = "petting-system";
        internal const string LightLimitSettings = "light-limit-settings";

        const string ContactReceiver = "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver";
        const string MenuInstaller = "nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller";

        static ExportGimmickValue Contact(string path, string parameter) =>
            new ExportGimmickValue { Path = path, Type = ContactReceiver, Property = "parameter", Value = parameter };

        // Anchored rules come first: an object they claim is not matched again
        // by a structural rule.
        internal static readonly IReadOnlyList<ExportGimmickRule> All = new[]
        {
            new ExportGimmickRule
            {
                Id = LightLimitSettings,
                Reason = "ライトの明るさを制限する設定です。書き出す材質に効果が残らないよう、書き出し用のコピーから外します。",
                AnchorTypes = new[] { "LightLimitChangerSettings" },
                DedicatedTypes = new[] { MenuInstaller },
            },
            new ExportGimmickRule
            {
                Id = PettingSystem,
                Reason = "確認済みの撫でシステムです。影・音・接触判定をまとめて省略します。",
                RequiredChildren = new[] { "HeadSystem", "Proxies" },
                RequiredValues = new[]
                {
                    Contact("RxHeadMain", "NadeRxHeadMain"),
                    Contact("RightHandSystem/RxRight", "NadeRxRightHand"),
                    Contact("LeftHandSystem/RxLeft", "NadeRxLeftHand"),
                },
                PrefabGuids = new[] { "491c3f399da5d064d9966982ddf0d191" },
                UnsafeReason = ExportGimmickDetection.PettingSystemUnsafeReason,
            },
        };
    }
}
