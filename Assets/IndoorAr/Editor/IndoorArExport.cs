using System;
using System.IO;
using System.Linq;
using Google.XR.ARCoreExtensions.Internal;
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
        // An export without the outdoor scene answers outdoor requests with "unavailable".
        static readonly string[] IndoorScenes = { "Assets/IndoorAr/Scenes/IndoorArSpike.unity" };
        static readonly string[] AllScenes = { IndoorScenes[0], "Assets/OutdoorAr/Scenes/OutdoorGeospatialDemo.unity" };

        // The iOS ARCore API key stays outside the repository. The export reads it into memory,
        // never logs it and never saves it to project settings.
        public static readonly string IosKeyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/navipet/arcore-ios-key");

        public static string ReadIosKey(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : "";

        // ARCore Extensions checks and configures a build from the Build Settings scene list,
        // not from the exported one, and fails an iOS build that lists a Geospatial scene
        // while iOS authorization is off.
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
                scenes = AllScenes,
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
            var key = ReadIosKey(IosKeyPath);
            var settings = ARCoreExtensionsProjectSettings.Instance;
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
            if (key == "")
            {
                Debug.LogWarning($"[IndoorAr] no ARCore API key at {IosKeyPath}: this iOS export leaves outdoor AR out");
            }
            else
            {
                settings.IsIOSSupportEnabled = true;
                settings.IOSAuthenticationStrategySetting = IOSAuthenticationStrategy.ApiKey;
                settings.IOSCloudServicesApiKey = key;
            }
            bool succeeded;
            try
            {
                succeeded = Build(new BuildPlayerOptions
                {
                    scenes = key == "" ? IndoorScenes : AllScenes,
                    locationPathName = IosExportPath,
                    target = BuildTarget.iOS,
                    options = BuildOptions.None,
                }).summary.result == BuildResult.Succeeded;
            }
            finally
            {
                // Back to the committed state: iOS support off, no key, no ARCore defines.
                settings.Load();
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, defines);
                // ARCore Extensions copies the key into this asset for the build.
                AssetDatabase.DeleteAsset("Assets/ExtensionsAssets/Runtime/RuntimeConfig.asset");
            }
            Debug.Log($"[IndoorAr] iOS export {(succeeded ? "succeeded" : "failed")} to {IosExportPath}");
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
            // shortcut: ARCore Extensions pins arcore-ios-sdk 1.57.0, which Xcode cannot resolve because
            // its CloudAnchors checksum is wrong (google-ar/arcore-ios-sdk#126). 1.57.1 is the same SDK with
            // that checksum fixed. Remove when ARCore Extensions pins a version that resolves.
            File.WriteAllText(projectPath, project.WriteToString().Replace("version = 1.57.0;", "version = 1.57.1;"));
            Debug.Log("[IndoorAr] linked ARKit, MultipeerConnectivity and Vision into UnityFramework");
        }
#endif
    }
}
