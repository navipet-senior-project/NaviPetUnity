using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace IndoorAr
{
    /// <summary>
    /// Outdoor exit stops the XR loader and does not start another one in that entry. Indoor exit only disables the
    /// AR components and keeps the loader (see <see cref="StopSessionsAndTearDown"/>). The camera-failure restart is a
    /// different path. The next entry initializes a loader only when none is running.
    /// </summary>
    public static class ArSessionLifecycle
    {
        public const float LoaderInitializationTimeoutS = 10f;

        public static bool ShouldInitialize(bool loaderActive) => !loaderActive;

        /// <summary>First Start of a reloaded scene after an exit. Outdoor requests arm their own scene.</summary>
        public static bool StartNeedsLoader(bool isEditor, bool loaderActive, bool outdoorRequest) =>
            !isEditor && !outdoorRequest && ShouldInitialize(loaderActive);

        public static bool InitializationTimedOut(float elapsedS, bool loaderReady) =>
            !loaderReady && elapsedS >= LoaderInitializationTimeoutS;

        public static bool LoaderActive => Manager != null && Manager.activeLoader != null;

        // Exit order is stop, then tear down. There is no initialize step.
        public static void ApplyExit(bool loaderActive, Action stopSessions, Action tearDownLoader)
        {
            stopSessions();
            if (loaderActive) tearDownLoader();
        }

        /// <summary>Only a loader that is up and not being kept is torn down.</summary>
        public static bool ShouldTearDownOnExit(bool loaderActive, bool keepLoader) => loaderActive && !keepLoader;

        /// <summary>
        /// keepLoader is the indoor exit. A second loader in the same process never got its XR input device back on
        /// device (every in-process re-entry froze the camera pose in the 2026-11-19 and 2026-11-20 runs, no first entry did), so the first one
        /// stays up and only the AR components stop. Not device-verified: the black camera feed that the exit
        /// teardown was added for (before #23) may return, and then the loader must be torn down again.
        /// </summary>
        public static void StopSessionsAndTearDown(bool keepLoader = false)
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            ApplyExit(ShouldTearDownOnExit(LoaderActive, keepLoader), DisableArComponents, TearDownActiveLoader);
        }

        public static IEnumerator InitializeOnEntry(bool probeInput = false)
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer || !ShouldInitialize(LoaderActive)) yield break;
            var manager = Manager;
            if (manager == null)
            {
                Debug.Log("[ArSession] entry init no manager");
                yield break;
            }
            var initialize = manager.InitializeLoader();
            var started = Time.realtimeSinceStartup;
            while (initialize.MoveNext())
            {
                if (InitializationTimedOut(Time.realtimeSinceStartup - started, manager.activeLoader != null))
                {
                    Debug.Log("[ArSession] entry init timeout");
                    yield break;
                }
                yield return initialize.Current;
            }
            if (manager.activeLoader == null)
            {
                Debug.Log("[ArSession] entry init no loader");
                yield break;
            }
            manager.StartSubsystems();
            if (probeInput) ArXrInputProbe.Probe();
            LoaderRestarted = true;
            DriveCameraFromXrInput();
            Debug.Log("[ArSession] entry loader started");
        }

        /// <summary>This process started a loader after an exit tore one down. Survives scene reloads.</summary>
        public static bool LoaderRestarted { get; private set; }

        /// <summary>
        /// After a loader restart the Input System pose driver left the camera frozen on device (2026-11-14 VEC
        /// re-entry, pathM=0.0 for 166 s), so every AR camera then also reads the XR subsystem directly.
        /// </summary>
        public static void DriveCameraFromXrInput()
        {
            if (!LoaderRestarted) return;
            foreach (var driver in UnityEngine.Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (driver.GetComponent<XrCameraPose>() == null) driver.gameObject.AddComponent<XrCameraPose>();
        }

        static XRManagerSettings Manager =>
            XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;

        static void DisableArComponents() => SetArComponents(false);

        /// <summary>
        /// Session first, camera background last on enable and the reverse on disable. The background keeps a static
        /// reference to the camera subsystem until it is disabled, so it must cycle with every loader.
        /// </summary>
        public static void SetArComponents(bool enabled)
        {
            var sessions = UnityEngine.Object.FindObjectsByType<ARSession>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cameras = UnityEngine.Object.FindObjectsByType<ARCameraManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var backgrounds = UnityEngine.Object.FindObjectsByType<ARCameraBackground>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            // Unproven candidate for a camera that renders the feed but no longer follows the phone after a loader
            // restart: the pose driver binds its input actions to the XR devices a loader creates, so it cycles too.
            var drivers = UnityEngine.Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (enabled)
            {
                foreach (var session in sessions) session.enabled = true;
                foreach (var camera in cameras) camera.enabled = true;
                foreach (var background in backgrounds) background.enabled = true;
                foreach (var driver in drivers) driver.enabled = true;
                return;
            }
            foreach (var driver in drivers) driver.enabled = false;
            foreach (var background in backgrounds) background.enabled = false;
            foreach (var camera in cameras) camera.enabled = false;
            foreach (var session in sessions) session.enabled = false;
        }

        /// <summary>A scene that loaded while no loader ran enabled its AR components against nothing. Cycle them.</summary>
        public static void RestartArComponents()
        {
            SetArComponents(false);
            SetArComponents(true);
        }

        static void TearDownActiveLoader()
        {
            var manager = Manager;
            if (manager == null || manager.activeLoader == null) return;
            try
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            catch (Exception e)
            {
                Debug.Log("[ArSession] exit teardown failed " + e.GetType().Name);
                return;
            }
            Debug.Log(manager.activeLoader == null
                ? "[ArSession] exit teardown"
                : "[ArSession] exit teardown loader remained");
        }
    }
}
