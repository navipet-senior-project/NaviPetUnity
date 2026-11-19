using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace IndoorAr
{
    /// <summary>
    /// DX1. At loader start or an already-active indoor entry, logs XR device and pose-control counts and flags,
    /// now and again 1 s later. It separates a pose source the loader keeps from one missing after re-init.
    /// It never logs a pose value.
    /// </summary>
    public sealed class ArXrInputProbe : MonoBehaviour
    {
        public const float SecondLookS = 1f;
        static bool _watching;
        static int _added, _removed;

        /// <summary>Counts Input System XR devices that appear or go away after this call. Safe to call again.</summary>
        public static void WatchDeviceChanges()
        {
            if (_watching) return;
            _watching = true;
            InputSystem.onDeviceChange += (device, change) =>
            {
                if (device is not TrackedDevice) return;
                if (change == InputDeviceChange.Added) _added++;
                else if (change == InputDeviceChange.Removed) _removed++;
            };
        }

        /// <summary>Logs now, then once more after SecondLookS.</summary>
        public static void Probe()
        {
            Log("start");
            var host = new GameObject("XR input probe", typeof(ArXrInputProbe));
            DontDestroyOnLoad(host); // a scene reload inside that second must not drop the second look
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(SecondLookS);
            Log("1s");
            Destroy(gameObject);
        }

        static void Log(string when)
        {
            foreach (var line in IndoorArDiagnostics.XrInputLines(when, LegacyDevices(), InputSystemDeviceCount(),
                         _added, _removed, ControlCount(driver => driver.positionInput),
                         ControlCount(driver => driver.rotationInput)))
                Debug.Log(line);
        }

        static List<IndoorArDiagnostics.XrDeviceInfo> LegacyDevices()
        {
            var devices = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevices(devices);
            var info = new List<IndoorArDiagnostics.XrDeviceInfo>();
            foreach (var d in devices)
                info.Add(new IndoorArDiagnostics.XrDeviceInfo(d.isValid,
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 _),
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 _),
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion _),
                    d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion _)));
            return info;
        }

        static int InputSystemDeviceCount()
        {
            var count = 0;
            foreach (var device in InputSystem.devices)
                if (device is TrackedDevice) count++;
            return count;
        }

        // The first pose driver in the scene; -1 when there is none.
        static int ControlCount(System.Func<TrackedPoseDriver, InputActionProperty> action)
        {
            var driver = FindAnyObjectByType<TrackedPoseDriver>(FindObjectsInactive.Include);
            return driver == null || action(driver).action == null ? -1 : action(driver).action.controls.Count;
        }
    }
}
