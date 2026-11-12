using System.Linq;
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
        // The indoor scene is first: it reads every request and loads the outdoor scene for mode "outdoor".
        // iOS has no ARCore authorization yet, so its export leaves the outdoor scene out
        // and the indoor scene answers outdoor requests with "unavailable".
        static readonly string[] IosScenes = { "Assets/IndoorAr/Scenes/IndoorArSpike.unity" };
        static readonly string[] AndroidScenes = { IosScenes[0], "Assets/OutdoorAr/Scenes/OutdoorGeospatialDemo.unity" };

        // ARCore Extensions checks and configures a build from the Build Settings scene list,
        // not from the exported one, and fails an iOS build that lists a Geospatial scene.
        static BuildReport Build(BuildPlayerOptions options)
        {
            var listed = EditorBuildSettings.scenes;
            EditorBuildSettings.scenes = options.scenes.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            try
            {
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                EditorBuildSettings.scenes = listed;
            }
        }

        [MenuItem("Indoor AR/Export Android Library")]
        public static void ExportAndroid()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            // Plain Activity entry: UnityHostActivity subclasses UnityPlayerActivity,
            // and GameActivity would pull androidx.games into the host app.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            var report = Build(new BuildPlayerOptions
            {
                scenes = AndroidScenes,
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
            // Apply immediately before the non-development build so this export cannot keep the splash.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            if (PlayerSettings.SplashScreen.show || PlayerSettings.SplashScreen.showUnityLogo)
            {
                Debug.LogError("[IndoorAr] Unity license still forces the splash screen and logo");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            var report = Build(new BuildPlayerOptions
            {
                scenes = IosScenes,
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
            project.AddFrameworkToProject(target, "Vision.framework", false);
            project.WriteToFile(projectPath);
            Debug.Log("[IndoorAr] linked ARKit, MultipeerConnectivity and Vision into UnityFramework");
        }
#endif
    }
}
