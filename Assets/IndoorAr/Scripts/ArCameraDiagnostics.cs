using UnityEngine;
using UnityEngine.InputSystem.XR;
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
        const float PoseLogIntervalS = 3f;
        int _frames;
        float _pathM, _turnDeg, _nextPoseLog;
        Vector3 _lastPos;
        Quaternion _lastRot;
        bool _poseSeen;
        string _lastPose;

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
            LogPose();
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

        void OnFrame(ARCameraFrameEventArgs args)
        {
            _textures = args.textures.Count;
            _frames++;
        }

        /// <summary>
        /// Tells a camera that follows the phone from one that does not: frames keep arriving while the Unity
        /// camera's travelled distance and turned angle stay at zero. Totals only, never a position or heading.
        /// </summary>
        void LogPose()
        {
            if (_camera == null) return;
            var t = _camera.transform;
            if (_poseSeen)
            {
                _pathM += Vector3.Distance(t.position, _lastPos);
                _turnDeg += Quaternion.Angle(t.rotation, _lastRot);
            }
            _lastPos = t.position;
            _lastRot = t.rotation;
            _poseSeen = true;
            if (Time.unscaledTime < _nextPoseLog) return;
            _nextPoseLog = Time.unscaledTime + PoseLogIntervalS;
            var driver = FindAnyObjectByType<TrackedPoseDriver>();
            var line = $"[ArPose] entry={_entry} frames={_frames} pathM={_pathM:0.0} turnDeg={Mathf.Round(_turnDeg)} driver={(driver == null ? "none" : driver.isActiveAndEnabled ? "on" : "off")} notTracking={ARSession.notTrackingReason}";
            if (line == _lastPose) return;
            _lastPose = line;
            Debug.Log(line);
        }

        void OnDestroy()
        {
            if (_camera != null) _camera.frameReceived -= OnFrame;
        }
    }
}
