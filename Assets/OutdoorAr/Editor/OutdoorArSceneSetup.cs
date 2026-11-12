using Google.XR.ARCoreExtensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

namespace OutdoorAr.Editor
{
    public static class OutdoorArSceneSetup
    {
        const string ConfigPath = "Assets/OutdoorAr/OutdoorGeospatialConfig.asset";
        const string ScenePath = "Assets/OutdoorAr/Scenes/OutdoorGeospatialDemo.unity";

        [MenuItem("NaviPet/Outdoor AR/Create Geospatial Demo Scene")]
        public static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/OutdoorAr/Scenes"))
                AssetDatabase.CreateFolder("Assets/OutdoorAr", "Scenes");
            var config = AssetDatabase.LoadAssetAtPath<ARCoreExtensionsConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ARCoreExtensionsConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.GeospatialMode = GeospatialMode.Enabled;
            EditorUtility.SetDirty(config);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sessionObject = new GameObject("AR Session");
            var session = sessionObject.AddComponent<ARSession>();
            session.enabled = false;
            sessionObject.AddComponent<ARInputManager>();
            var extensions = sessionObject.AddComponent<ARCoreExtensions>();
            extensions.Session = session;
            extensions.ARCoreExtensionsConfig = config;
            var earth = sessionObject.AddComponent<AREarthManager>();

            var originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<XROrigin>();
            var cameraObject = new GameObject("AR Camera", typeof(Camera), typeof(ARCameraManager),
                typeof(ARCameraBackground), typeof(AudioListener), typeof(TrackedPoseDriver));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(originObject.transform, false);
            origin.Camera = cameraObject.GetComponent<Camera>();
            origin.CameraFloorOffsetObject = originObject;
            var cameraManager = cameraObject.GetComponent<ARCameraManager>();
            extensions.CameraManager = cameraManager;
            extensions.Origin = origin;
            originObject.AddComponent<ARAnchorManager>();

            var readinessObject = new GameObject("Outdoor Geospatial Demo");
            var readiness = readinessObject.AddComponent<OutdoorGeospatialReadiness>();
            var demo = readinessObject.AddComponent<OutdoorGeospatialDemo>();
            Set(readiness, "earthManager", earth);
            Set(demo, "readiness", readiness);
            Set(demo, "session", session);
            Set(demo, "anchors", originObject.GetComponent<ARAnchorManager>());

            if (session.enabled || extensions.ARCoreExtensionsConfig.GeospatialMode != GeospatialMode.Enabled
                || extensions.Session != session || extensions.CameraManager != cameraManager
                || readinessObject.GetComponent<OutdoorGeospatialReadiness>() != readiness
                || readinessObject.GetComponent<OutdoorGeospatialDemo>() != demo
                || !OutdoorGeospatialGate.LocationPermissionsGranted(true, true, true)
                || OutdoorGeospatialGate.LocationPermissionsGranted(true, false, true)
                || !OutdoorGeospatialGate.LocationPermissionsGranted(true, false, false))
                throw new System.InvalidOperationException("Outdoor Geospatial scene setup is incomplete.");

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes;
            var found = false;
            foreach (var entry in scenes)
                if (entry.path == ScenePath) found = true;
            if (!found)
            {
                var entries = new EditorBuildSettingsScene[scenes.Length + 1];
                scenes.CopyTo(entries, 0);
                entries[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = entries;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void Set(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
