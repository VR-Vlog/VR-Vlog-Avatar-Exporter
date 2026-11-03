using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(VRVlog.LilToonExporter.Tests.SharedObjectTestPlugin))]

namespace VRVlog.LilToonExporter.Tests
{
    // A test-only build pass that assigns objects it keeps across builds to the
    // avatar, as a tool with its own cache would. It does nothing until a test
    // sets Armed, so a project that only installs the package is safe.
    public sealed class SharedObjectTestPlugin : Plugin<SharedObjectTestPlugin>
    {
        public static bool Armed;
        public static Material[] Materials;
        public static Mesh Mesh;

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).Run("VR-Vlog shared object test", context =>
            {
                if (!Armed) return;
                foreach (var renderer in context.AvatarRootObject.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = Materials;
                var holder = new GameObject("Shared mesh holder");
                holder.transform.SetParent(context.AvatarRootObject.transform, false);
                holder.AddComponent<MeshFilter>().sharedMesh = Mesh;
            });
        }
    }
}
