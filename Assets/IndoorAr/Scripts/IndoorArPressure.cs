using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IndoorAr
{
    /// <summary>
    /// The player's own barometer, in hPa.
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
        static PressureSensor s_sensor;
        static double s_lastEventTime;
        static float s_lastReadS;
#endif

        public static void Start()
        {
#if UNITY_IOS && !UNITY_EDITOR
            s_seq = 0;
            IndoorArPressureStart();
#elif UNITY_ANDROID && !UNITY_EDITOR
            s_sensor = PressureSensor.current;
            if (s_sensor == null) return;
            s_lastEventTime = s_sensor.lastUpdateTime;
            s_lastReadS = float.NegativeInfinity;
            if (!s_sensor.enabled) InputSystem.EnableDevice(s_sensor);
#endif
        }

        public static void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            IndoorArPressureStop();
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (s_sensor != null && s_sensor.added && s_sensor.enabled) InputSystem.DisableDevice(s_sensor);
            s_sensor = null;
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
            if (s_sensor == null || !s_sensor.added || !s_sensor.enabled || Time.unscaledTime - s_lastReadS < 1f) return false;
            var eventTime = s_sensor.lastUpdateTime;
            if (eventTime <= s_lastEventTime) return false;
            s_lastEventTime = eventTime;
            s_lastReadS = Time.unscaledTime;
            hpa = s_sensor.atmosphericPressure.ReadValue();
            return hpa > 0f;
#else
            return false;
#endif
        }
    }
}
