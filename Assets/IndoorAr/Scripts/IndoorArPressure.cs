using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IndoorAr
{
    /// <summary>
    /// The player's own barometer, in hPa, so floor-change detection needs nothing from the host or the bridge.
    /// iOS reads Core Motion's altimeter in IndoorArPressure.mm (the host app already declares NSMotionUsageDescription;
    /// Motion &amp; Fitness access can still be refused, which just means no readings). Android reads the Input System
    /// pressure sensor. A device without a barometer, a refusal and the editor give no readings, and every consumer
    /// treats that as "pressure unknown".
    /// </summary>
    public static class IndoorArPressure
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void IndoorArPressureStart();
        [DllImport("__Internal")] static extern void IndoorArPressureStop();
        [DllImport("__Internal")] static extern int IndoorArReadPressure(out float hpa);
        static int s_seq;
#elif UNITY_ANDROID && !UNITY_EDITOR
        static float s_lastReadS = float.NegativeInfinity;
#endif

        public static void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
            s_seq = 0;
            IndoorArPressureStart();
#elif UNITY_ANDROID && !UNITY_EDITOR
            var sensor = PressureSensor.current;
            if (sensor != null && !sensor.enabled) InputSystem.EnableDevice(sensor);
#endif
        }

        public static void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            IndoorArPressureStop();
#elif UNITY_ANDROID && !UNITY_EDITOR
            var sensor = PressureSensor.current;
            if (sensor != null && sensor.enabled) InputSystem.DisableDevice(sensor);
#endif
        }

        /// <summary>True when there is a reading newer than the last call (about once a second).</summary>
        public static bool TryRead(out float hpa)
        {
            hpa = 0f;
#if UNITY_IOS && !UNITY_EDITOR
            var seq = IndoorArReadPressure(out hpa);
            if (seq == 0 || seq == s_seq) return false;
            s_seq = seq;
            return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            var sensor = PressureSensor.current;
            if (sensor == null || !sensor.enabled || Time.unscaledTime - s_lastReadS < 1f) return false;
            s_lastReadS = Time.unscaledTime;
            hpa = sensor.atmosphericPressure.ReadValue();
            return hpa > 0f;
#else
            return false;
#endif
        }
    }
}
