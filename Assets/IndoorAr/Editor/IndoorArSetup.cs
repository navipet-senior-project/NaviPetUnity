using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace IndoorAr.Editor
{
    /// <summary>
    /// Scripted version of the manual Task 7 steps (XR plug-in, player settings,
    /// URP AR background feature, spike scene) so they can run in batch mode and
    /// be repeated. Idempotent.
    /// </summary>
    public static class IndoorArSetup
    {
        const string ScenePath = "Assets/IndoorAr/Scenes/IndoorArSpike.unity";
        const string ArCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";

        [MenuItem("Indoor AR/Configure Project")]
        public static void Configure()
        {
            ConfigureXr();
            ConfigurePlayer();
            AddArBackgroundFeature();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[IndoorAr] setup done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void ConfigureXr()
        {
            var getOrCreate = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate", BindingFlags.Static | BindingFlags.NonPublic);
            var perTarget = (XRGeneralSettingsPerBuildTarget)getOrCreate.Invoke(null, null);
            const BuildTargetGroup android = BuildTargetGroup.Android;
            if (!perTarget.HasSettingsForBuildTarget(android))
                perTarget.CreateDefaultSettingsForBuildTarget(android);
            if (!perTarget.HasManagerSettingsForBuildTarget(android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(android);
            var manager = perTarget.ManagerSettingsForBuildTarget(android);
            if (!XRPackageMetadataStore.AssignLoader(manager, ArCoreLoader, android)
                && !XRPackageMetadataStore.IsLoaderAssigned(ArCoreLoader, android))
                throw new System.Exception("could not assign the ARCore loader");
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(manager);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        }

        // Without this feature the camera feed renders black under URP.
        static void AddArBackgroundFeature()
        {
            var dataField = typeof(UniversalRenderPipelineAsset).GetField(
                "m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            var onValidate = typeof(ScriptableRendererData).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            var assets = Enumerable.Range(0, QualitySettings.names.Length)
                .Select(QualitySettings.GetRenderPipelineAssetAt)
                .OfType<UniversalRenderPipelineAsset>()
                .Distinct();
            foreach (var asset in assets)
            {
                foreach (var data in ((IEnumerable)dataField.GetValue(asset)).OfType<ScriptableRendererData>())
                {
                    if (data == null || data.rendererFeatures.OfType<ARBackgroundRendererFeature>().Any()) continue;
                    var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                    feature.name = "ARBackgroundRendererFeature";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    data.rendererFeatures.Add(feature);
                    onValidate.Invoke(data, null);
                    EditorUtility.SetDirty(data);
                    Debug.Log($"[IndoorAr] AR background feature added to {AssetDatabase.GetAssetPath(data)}");
                }
            }
        }

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            var spike = new GameObject("IndoorArSpike");
            spike.AddComponent<IndoorArSpike>();
            Debug.Log("[IndoorAr] scene roots: " + string.Join(", ", scene.GetRootGameObjects().Select(g => g.name)));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
