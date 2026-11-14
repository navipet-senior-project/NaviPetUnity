using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace IndoorAr
{
    /// <summary>
    /// User exit stops the XR loader and does not start another one in that entry.
    /// The camera-failure restart is a different path. The next entry initializes a loader only when none is running.
    /// </summary>
    public static class ArSessionLifecycle
    {
        public const float LoaderInitializationTimeoutS = 10f;

        public static bool ShouldInitialize(bool loaderActive) => !loaderActive;

        public static bool InitializationTimedOut(float elapsedS, bool loaderReady) =>
            !loaderReady && elapsedS >= LoaderInitializationTimeoutS;

        public static bool LoaderActive => Manager != null && Manager.activeLoader != null;

        // Exit order is stop, then tear down. There is no initialize step.
        public static void ApplyExit(bool loaderActive, Action stopSessions, Action tearDownLoader)
        {
            stopSessions();
            if (loaderActive) tearDownLoader();
        }

        public static void StopSessionsAndTearDown()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            ApplyExit(LoaderActive, DisableArComponents, TearDownActiveLoader);
        }

        public static IEnumerator InitializeOnEntry()
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
            Debug.Log("[ArSession] entry loader started");
        }

        static XRManagerSettings Manager =>
            XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;

        static void DisableArComponents()
        {
            foreach (var session in UnityEngine.Object.FindObjectsByType<ARSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                session.enabled = false;
            foreach (var camera in UnityEngine.Object.FindObjectsByType<ARCameraManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                camera.enabled = false;
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
