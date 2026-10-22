using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal sealed class MaterialBakeIssue
    {
        public readonly Material Material;
        public readonly string MaterialName;
        public readonly string RendererPath, Layer, Setting, Reason, NextStep;

        internal MaterialBakeIssue(Material material, string rendererPath, string layer, string setting, string reason, string nextStep)
        {
            Material = material;
            MaterialName = material != null ? material.name : "マテリアル";
            RendererPath = rendererPath ?? "";
            Layer = layer;
            Setting = setting;
            Reason = reason;
            NextStep = nextStep;
        }

        public override string ToString() => MaterialName +
            " / メインカラー" + Layer + " / " + Setting + "\n" + Reason + "\n" + NextStep;
    }

    internal sealed class MaterialBakeException : InvalidOperationException
    {
        public IReadOnlyList<MaterialBakeIssue> Issues { get; }
        public bool SourceUnchanged { get; }

        internal MaterialBakeException(IEnumerable<MaterialBakeIssue> issues, bool sourceUnchanged = false)
            : this(issues.ToArray(), sourceUnchanged) { }

        private MaterialBakeException(MaterialBakeIssue[] issues, bool sourceUnchanged)
            : base("そのままの見た目では書き出せないマテリアル設定があります。\n\n" +
                string.Join("\n\n", issues.Select(issue => issue.ToString())))
        {
            Issues = Array.AsReadOnly(issues);
            SourceUnchanged = sourceUnchanged;
        }
    }

    // Consent is scoped to a single export attempt, never persisted on an asset
    // or in preferences. Identity is the source material, not its display name.
    internal sealed class MaterialBakeOptions
    {
        private readonly Dictionary<Material, HashSet<string>> omitted = new Dictionary<Material, HashSet<string>>();

        internal bool Omits(Material material, string layer) => material != null &&
            omitted.TryGetValue(material, out var layers) && layers.Contains(layer);
    }
}
