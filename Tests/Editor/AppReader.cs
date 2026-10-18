using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // The VR Vlog app's VRM reader (com.vrvlog.avatar-runtime) when it is installed.
    // Reflection only, so these tests compile without it. The app's own project installs
    // its shader set ("VR Vlog/lilToon 2.3.4/<id>"); any other project reads with the
    // official lilToon shaders through the package's OfficialLilToonShaderSource.
    internal static class AppReader
    {
        internal const string AppPrefix="VR Vlog/lilToon 2.3.4/";
        static Type Find(string name)=>Type.GetType("FaceMaskVTuber.UniVrmRuntime."+name+", FaceMaskVTuber.UniVrmRuntime");
        internal static readonly Type Loader=Find("UniVrmRuntimeLoader");
        static readonly Type Official=Find("OfficialLilToonShaderSource");
        static readonly object OfficialSource=Official!=null?Activator.CreateInstance(Official):null;

        // The app's host installer lives in the app's own assembly, not in the package.
        internal static readonly bool AppProject=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetType("FaceMaskVTuber.AvatarRuntimeHostInstaller")!=null);

        // AvatarRuntimeHooks.ShaderSource is process-wide; Dispose restores the previous
        // source unless something else replaced ours in the meantime. The app project keeps its own.
        internal static IDisposable HostShaders()
        {
            if(Loader==null||AppProject)return new Scope(null,null,null);
            Assert.That(OfficialSource,Is.Not.Null,"OfficialLilToonShaderSource");
            var field=Find("AvatarRuntimeHooks")?.GetField("ShaderSource",BindingFlags.Static|BindingFlags.Public);
            Assert.That(field,Is.Not.Null,"AvatarRuntimeHooks.ShaderSource");
            var previous=field.GetValue(null);field.SetValue(null,OfficialSource);
            return new Scope(field,previous,OfficialSource);
        }

        sealed class Scope:IDisposable
        {
            readonly FieldInfo field;readonly object previous,set;
            internal Scope(FieldInfo field,object previous,object set){this.field=field;this.previous=previous;this.set=set;}
            public void Dispose(){if(field!=null&&ReferenceEquals(field.GetValue(null),set))field.SetValue(null,previous);}
        }

        internal static async Task<GameObject> LoadAsync(string path)
        {
            // The preview window swaps the same hooks under this gate; older packages have none.
            var gate=(SemaphoreSlim)Find("AvatarRuntimePreview")?.GetField("LoadGate",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null);
            if(gate!=null)await gate.WaitAsync();
            try
            {
                using(HostShaders())
                {
                    // Older readers had an explicit lilToon switch; current ones detect lilToon themselves.
                    Loader.GetMethod("ConfigureLilToon")?.Invoke(null,new object[]{true});
                    return await (Task<GameObject>)Loader.GetMethod("LoadAsync").Invoke(null,new object[]{path,null,false,null});
                }
            }
            finally{gate?.Release();}
        }

        internal static string LastLilToonStatus=>(string)Loader.GetProperty("LastLilToonStatus").GetValue(null);

        // A shader of the host's runtime set: the app's own, otherwise the official one.
        internal static bool IsRuntimeShader(Shader shader)=>shader!=null&&(AppProject
            ?shader.name.StartsWith(AppPrefix,StringComparison.Ordinal)
            :OfficialSource!=null&&Official.GetMethod("CatalogueId").Invoke(OfficialSource,new object[]{shader})!=null);

        // LilToonFullRuntime.ShaderName resolved through the host's shader set. No gate:
        // nothing awaits inside the swap, and waiting synchronously here would block the main thread.
        internal static string RuntimeShaderName(Dictionary<string,object> spec)
        {
            using(HostShaders())return (string)Find("LilToonFullRuntime").GetMethod("ShaderName",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{spec});
        }
    }
}
