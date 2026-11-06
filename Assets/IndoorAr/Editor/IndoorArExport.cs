using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IndoorAr.Editor
{
    /// <summary>
    /// Exports the AR scene as a Unity-as-a-Library Gradle project.
    /// tools/build-indoor-ar-aar.sh builds it into AARs for the Flutter app.
    /// </summary>
    public static class IndoorArExport
    {
        public const string ExportPath = "Builds/IndoorArAndroid";
        static readonly string[] Scenes = { "Assets/IndoorAr/Scenes/IndoorArSpike.unity" };

        [MenuItem("Indoor AR/Export Android Library")]
        public static void ExportAndroid()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            // Plain Activity entry: UnityHostActivity subclasses UnityPlayerActivity,
            // and GameActivity would pull androidx.games into the host app.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = ExportPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            var succeeded = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"[IndoorAr] export {report.summary.result} to {ExportPath}");
            if (Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
        }

        public const string IosExportPath = "Builds/IndoorArIos";

        [MenuItem("Indoor AR/Export iOS Xcode Project")]
        public static void ExportIos()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                Debug.LogError("[IndoorAr] iOS Build Support is not installed for this editor");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = IosExportPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            });
            var succeeded = report.summary.result == BuildResult.Succeeded;
            Debug.Log($"[IndoorAr] iOS export {report.summary.result} to {IosExportPath}");
#if UNITY_IOS
            if (succeeded) LinkFrameworksIntoUnityFramework();
#endif
            if (Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
        }

#if UNITY_IOS
        // The ARKit plug-in and the MultiSet plug-in record these on the host
        // target. Flutter only links UnityFramework.
        static void LinkFrameworksIntoUnityFramework()
        {
            var projectPath = UnityEditor.iOS.Xcode.PBXProject.GetPBXProjectPath(IosExportPath);
            var project = new UnityEditor.iOS.Xcode.PBXProject();
            project.ReadFromFile(projectPath);
            var target = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(target, "ARKit.framework", false);
            project.AddFrameworkToProject(target, "MultipeerConnectivity.framework", false);
            project.WriteToFile(projectPath);
            Debug.Log("[IndoorAr] linked ARKit and MultipeerConnectivity into UnityFramework");
        }
#endif
    }
}
