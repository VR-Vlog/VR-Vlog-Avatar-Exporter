using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // The VR Vlog app's VRM reader (com.vrvlog.avatar-runtime) when it is installed.
    // Reflection only, so these tests compile without it. The app's own project has
    // its shader set ("VR Vlog/lilToon 2.3.4/<id>"); any other project reads with the
    // official lilToon shaders through the package's OfficialLilToonShaderSource.
    internal static class AppReader
    {
        const string AppPrefix="VR Vlog/lilToon 2.3.4/";
        static Type Find(string name)=>Type.GetType("FaceMaskVTuber.UniVrmRuntime."+name+", FaceMaskVTuber.UniVrmRuntime");
        internal static readonly Type Loader=Find("UniVrmRuntimeLoader");
        static readonly Type Official=Find("OfficialLilToonShaderSource");
        static readonly object OfficialSource=Official!=null?Activator.CreateInstance(Official):null;

        internal static bool AppShaders=>Shader.Find(AppPrefix+VRVlog.LilToon.LilToon234Catalogue.Shaders["lilToon"])!=null;

        // AvatarRuntimeHooks.ShaderSource is process-wide; the previous source is restored on Dispose.
        internal static IDisposable HostShaders()
        {
            if(Loader==null||AppShaders)return new Scope(null,null);
            Assert.That(OfficialSource,Is.Not.Null,"OfficialLilToonShaderSource");
            var field=Find("AvatarRuntimeHooks")?.GetField("ShaderSource",BindingFlags.Static|BindingFlags.Public);
            Assert.That(field,Is.Not.Null,"AvatarRuntimeHooks.ShaderSource");
            var previous=field.GetValue(null);field.SetValue(null,OfficialSource);
            return new Scope(field,previous);
        }

        sealed class Scope:IDisposable
        {
            readonly FieldInfo field;readonly object previous;
            internal Scope(FieldInfo field,object previous){this.field=field;this.previous=previous;}
            public void Dispose()=>field?.SetValue(null,previous);
        }

        internal static async Task<GameObject> LoadAsync(string path)
        {
            using(HostShaders())
            {
                // Older readers had an explicit lilToon switch; current ones detect lilToon themselves.
                Loader.GetMethod("ConfigureLilToon")?.Invoke(null,new object[]{true});
                return await (Task<GameObject>)Loader.GetMethod("LoadAsync").Invoke(null,new object[]{path,null,false,null});
            }
        }

        internal static string LastLilToonStatus=>(string)Loader.GetProperty("LastLilToonStatus").GetValue(null);

        // A shader of the host's runtime set: the app's own, otherwise the official one.
        internal static bool IsRuntimeShader(Shader shader)=>shader!=null&&(AppShaders
            ?shader.name.StartsWith(AppPrefix,StringComparison.Ordinal)
            :OfficialSource!=null&&Official.GetMethod("CatalogueId").Invoke(OfficialSource,new object[]{shader})!=null);

        // LilToonFullRuntime.ShaderName resolved through the host's shader set.
        internal static string RuntimeShaderName(Dictionary<string,object> spec)
        {
            using(HostShaders())return (string)Find("LilToonFullRuntime").GetMethod("ShaderName",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{spec});
        }
    }
}
