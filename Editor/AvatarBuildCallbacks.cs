using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    // The avatar preprocess callbacks the VRChat SDK runs on an upload, read from
    // the SDK's own list (as NDMF and VRCFury read it) and run on the export copy
    // in the same order. NdmfExportPreparation runs NDMF's two hooks and VRCFury's
    // build hook itself at their places, and calls RunBefore with their orders, so
    // every other callback runs between them as on an upload.
    //
    // A callback that also takes the build request or the post-process is not run:
    // such a callback brackets the whole upload, changing project settings before
    // the build and restoring them after it (lilToon's shader settings), and the
    // export copy has no build to restore them after.
    internal sealed class AvatarBuildCallbacks
    {
        internal const int NdmfTransformingOrder = -11000, VrcFuryOrder = -10000, NdmfOptimizingOrder = -1025;

        // Set only by tests: the registered callbacks, in place of the SDK's list.
        internal static Func<IEnumerable<object>> RegisteredOverride;

        private static readonly HashSet<string> RunByExporter = new HashSet<string>(StringComparer.Ordinal)
        {
            "nadena.dev.ndmf.VRChat.BuildFrameworkPreprocessHook",
            "nadena.dev.ndmf.VRChat.BuildFrameworkOptimizeHook",
            "VF.Hooks.VrcPreuploadHook",
        };

        internal const string FailureMessage = "VRChat SDKのビルド前処理が失敗しました\n{0}\nコンソールのエラーで原因を確認";
        internal const string FailedWarning = "次のVRChat SDKのビルド前処理が失敗したため、その処理だけを除いて書き出しました\n{0}";

        private sealed class Callback
        {
            internal int Order;
            internal object Instance;
            internal MethodInfo Run;
        }

        private readonly List<Callback> callbacks;
        private int next;

        private AvatarBuildCallbacks(List<Callback> callbacks) { this.callbacks = callbacks; }

        // The callbacks an upload of this avatar runs besides the ones the
        // exporter runs itself. The SDK runs them only on a VRChat avatar.
        internal static AvatarBuildCallbacks For(GameObject avatar)
        {
            var registered = RegisteredOverride != null ? RegisteredOverride() : IsVrcAvatar(avatar) ? Registered() : null;
            var list = new List<Callback>();
            foreach (var instance in registered ?? Enumerable.Empty<object>())
            {
                var type = instance?.GetType();
                if (type == null || RunByExporter.Contains(type.FullName)) continue;
                var interfaces = type.GetInterfaces();
                if (interfaces.Any(contract => contract.Name == "IVRCSDKPostprocessAvatarCallback" || contract.Name == "IVRCSDKBuildRequestedCallback"))
                    continue;
                var contract = interfaces.FirstOrDefault(value => value.Name == "IVRCSDKPreprocessAvatarCallback");
                var run = contract?.GetMethod("OnPreprocessAvatar", new[] { typeof(GameObject) });
                // The SDK declares the order on a base interface (IOrderedCallback).
                var order = interfaces.Select(value => value.GetProperty("callbackOrder"))
                    .FirstOrDefault(property => property != null && property.PropertyType == typeof(int))?.GetValue(instance);
                if (run == null || run.ReturnType != typeof(bool) || !(order is int value)) continue;
                list.Add(new Callback { Order = value, Instance = instance, Run = run });
            }
            // The SDK sorts its list by order when it runs it, keeping the list's
            // order among equal ones; OrderBy is stable.
            return new AvatarBuildCallbacks(list.OrderBy(callback => callback.Order).ToList());
        }

        internal static bool IsVrcAvatar(GameObject avatar) => avatar.GetComponents<Component>().Any(component => component != null &&
            component.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");

        private static IEnumerable<object> Registered()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks", false))
                .FirstOrDefault(value => value != null);
            var field = type?.GetField("_preprocessAvatarCallbacks", BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) is System.Collections.IEnumerable list ? list.Cast<object>().ToList() : null;
        }

        // Below every order, int.MaxValue included: the SDK's last callbacks use it.
        internal const long Rest = (long)int.MaxValue + 1;

        internal bool AnyBefore(long below) => next < callbacks.Count && callbacks[next].Order < below;

        // Runs, in order, every callback not yet run whose order is below the given one.
        // A callback that fails is left out with a warning when the caller tolerates
        // failed passes; otherwise the preparation fails.
        internal void RunBefore(long below, GameObject avatar, ICollection<string> warnings, bool tolerate)
        {
            for (; next < callbacks.Count && callbacks[next].Order < below; next++)
            {
                var callback = callbacks[next];
                Exception error = null;
                var succeeded = false;
                try { succeeded = callback.Run.Invoke(callback.Instance, new object[] { avatar }) is bool result && result; }
                catch (TargetInvocationException failure) when (failure.InnerException != null) { error = failure.InnerException; }
                if (error is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                if (succeeded) continue;
                var name = NameOf(callback.Instance.GetType());
                if (!tolerate) throw new InvalidOperationException(string.Format(ExporterLocalization.T(FailureMessage), name), error);
                if (error != null) Debug.LogWarning(name + "\n" + error);
                warnings?.Add(string.Format(ExporterLocalization.T(FailedWarning), name));
            }
        }

        // The package that installed the callback and its class, which is what
        // the user can find in their project.
        private static string NameOf(Type type)
        {
            string package = null;
            try { package = PackageInfo.FindForAssembly(type.Assembly)?.displayName; }
            catch { }
            return (string.IsNullOrEmpty(package) ? type.Assembly.GetName().Name : package) + " / " + type.Name;
        }
    }
}
