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
    }
}
