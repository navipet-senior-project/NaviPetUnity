using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace IndoorAr
{
    /// <summary>
    /// Drives the AR camera straight from the XR input subsystem once this process has restarted the XR loader.
    /// On the 2026-11-14 VEC re-entry the session tracked and frames arrived, but the Input System pose driver left
    /// the camera at pathM=0.0 turnDeg=0 for the whole session, even after it was cycled. The XR subsystem is the
    /// same pose source the driver reads, so this takes over only after it returns a pose, and leaves the driver
    /// alone while it returns none.
    /// </summary>
    public sealed class XrCameraPose : MonoBehaviour
    {
        readonly List<InputDevice> _devices = new List<InputDevice>();
        TrackedPoseDriver _driver;

        /// <summary>Frames this component posed the camera. A count only, for the [ArPose] log line.</summary>
        public int Applied { get; private set; }

        void OnEnable()
        {
            _driver = GetComponent<TrackedPoseDriver>();
            Application.onBeforeRender += Apply;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= Apply;
            if (_driver != null) _driver.enabled = true;
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (!TryReadPose(_devices, out var position, out var rotation)) return;
            // Two writers would fight over the transform; the driver's pose is the stale one here.
            if (_driver != null && _driver.enabled) _driver.enabled = false;
            transform.SetLocalPositionAndRotation(position, rotation);
            Applied++;
        }

        /// <summary>The first valid XR device that reports both a position and a rotation, centre eye first.</summary>
        public static bool TryReadPose(List<InputDevice> devices, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            InputDevices.GetDevices(devices);
            foreach (var device in devices)
            {
                if (!device.isValid) continue;
                if (!device.TryGetFeatureValue(CommonUsages.centerEyePosition, out position)
                    && !device.TryGetFeatureValue(CommonUsages.devicePosition, out position)) continue;
                if ((device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation)
                     || device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
                    && Quaternion.Dot(rotation, rotation) > .5f) return true; // an untracked device reports all zeros
            }
            return false;
        }
    }
}
