using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IndoorAr.Editor
{
    public static class IndoorHudCapture
    {
        [MenuItem("Indoor AR/Open Mock HUD Preview")]
        public static void OpenPreview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.6f, -1); camera.transform.rotation = Quaternion.Euler(10, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.36f, .42f, .47f);
            var light = new GameObject("Light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            Box("Floor", new Vector3(0, -.12f, 6), new Vector3(5, .2f, 16), new Color(.49f, .53f, .55f));
            Box("Left wall", new Vector3(-2.5f, 1.5f, 6), new Vector3(.2f, 3, 16), new Color(.70f, .74f, .76f));
            Box("Right wall", new Vector3(2.5f, 1.5f, 6), new Vector3(.2f, 3, 16), new Color(.65f, .70f, .73f));
            var preview = new GameObject("Mock HUD preview").AddComponent<IndoorHudMockPreview>(); preview.previewCamera = camera;
            Selection.activeGameObject = preview.gameObject;
        }

        // Batch-render a deliberately illustrative corridor and each real HUD state.
        public static void Capture()
        {
            var folder = Environment.GetEnvironmentVariable("NAVIPET_HUD_SHOTS");
            if (string.IsNullOrEmpty(folder)) throw new Exception("NAVIPET_HUD_SHOTS is required");
            Directory.CreateDirectory(folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.6f, -1); camera.transform.rotation = Quaternion.Euler(10, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.36f, .42f, .47f);
            camera.fieldOfView = 60; camera.targetTexture = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32);
            var light = new GameObject("Light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0); light.intensity = 1.2f;
            Box("Floor", new Vector3(0, -.12f, 6), new Vector3(5, .2f, 16), new Color(.49f, .53f, .55f));
            Box("Left wall", new Vector3(-2.5f, 1.5f, 6), new Vector3(.2f, 3, 16), new Color(.70f, .74f, .76f));
            Box("Right wall", new Vector3(2.5f, 1.5f, 6), new Vector3(.2f, 3, 16), new Color(.65f, .70f, .73f));
            Box("Ceiling", new Vector3(0, 3, 6), new Vector3(5, .15f, 16), new Color(.75f, .79f, .80f));
            for (int i = 0; i < 4; i++)
            {
                Box("Door", new Vector3(2.38f, 1, 1 + i * 3), new Vector3(.06f, 2, 1.2f), new Color(.29f, .36f, .41f));
                Box("Ceiling light", new Vector3(0, 2.9f, 1 + i * 3), new Vector3(1.2f, .04f, .35f), Color.white);
            }
            var states = (IndoorHudState[])Enum.GetValues(typeof(IndoorHudState));
            int index = 0, frames = 0;
            IndoorHud hud = null;
            EditorApplication.CallbackFunction update = null;
            update = () =>
            {
                try
                {
                    if (hud == null)
                    {
                        hud = new GameObject("HUD").AddComponent<IndoorHud>();
                        hud.Initialize(camera);
                        hud.Show(MockIndoorRoute.Snapshot(states[index], 1));
                    }
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    if (++frames < 4) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                    RenderTexture.active = camera.targetTexture;
                    var image = new Texture2D(720, 1280, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 720, 1280), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.Combine(folder, states[index].ToString().ToLowerInvariant() + ".png"), image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    UnityEngine.Object.DestroyImmediate(hud.gameObject);
                    hud = null; frames = 0;
                    if (++index < states.Length) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                    EditorApplication.update -= update;
                    RenderTexture.active = null; camera.targetTexture.Release();
                    Debug.Log("[IndoorHud] screenshots saved to " + folder);
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    EditorApplication.update -= update;
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.update += update;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Box(string name, Vector3 position, Vector3 scale, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.position = position; box.transform.localScale = scale;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
