using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace IndoorAr
{
    /// <summary>
    /// Device builds only: logs one fixed-label line whenever the AR session state or the camera feed changes,
    /// so a re-entry with no camera shows in the console. It records no location, image, key or token.
    /// </summary>
    public sealed class ArCameraDiagnostics : MonoBehaviour
    {
        static int _entries; // survives scene reloads, so it counts AR launches in this app process
        int _entry;
        int _textures = -1; // -1: no camera frame yet
        ARCameraManager _camera;
        ARSession _session;
        string _last;

        public static void Attach()
        {
            if (!Application.isEditor) new GameObject("AR diagnostics", typeof(ArCameraDiagnostics));
        }

        void Awake() => _entry = ++_entries;

        void Update()
        {
            if (_camera == null)
            {
                _camera = FindAnyObjectByType<ARCameraManager>();
                if (_camera != null) _camera.frameReceived += OnFrame;
            }
            var line = $"[ArDiag] entry={_entry} scene={SceneManager.GetActiveScene().name} arSession={ARSession.state} cameraManager={_camera != null && _camera.enabled} sessionRunning={SessionRunning()} cameraTextures={_textures}";
            if (line == _last) return;
            _last = line;
            Debug.Log(line);
        }

        // Separates "the session never started" from "it runs but delivers no texture" on a re-entry.
        bool SessionRunning()
        {
            if (_session == null) _session = FindAnyObjectByType<ARSession>();
            return _session != null && _session.subsystem != null && _session.subsystem.running;
        }

        void OnFrame(ARCameraFrameEventArgs args) => _textures = args.textures.Count;

        void OnDestroy()
        {
            if (_camera != null) _camera.frameReceived -= OnFrame;
        }
    }
}
