using System;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorLocalizationMode
    {
        /// <summary>Camera pose from a read room sign (IndoorSignPose). Never calls MultiSet.</summary>
        SignPose,
        /// <summary>The original MultiSet single-frame VPS flow.</summary>
        MultiSet,
        /// <summary>Room signs first (SignFirstLocalizer); MultiSet only when no usable sign fix arrives in time.</summary>
        SignFirst
    }

    /// <summary>
    /// Localizes from a room sign instead of MultiSet. It makes no network calls and raises no AttemptStarted,
    /// so the result's localizationQueries stays 0. Localized fires only when IndoorSignPose returns a pose from a
    /// validated calibration entry plus observed corners; anything else leaves the session localizing until its
    /// own timeout reports localizationFailed. MapSpace and MapCode come from the scene's MultiSet localizer,
    /// which is never authenticated or asked to localize in this mode.
    /// </summary>
    public sealed class SignPoseLocalizer : IIndoorArLocalizer
    {
        readonly IndoorSignCalibration _calibration;
        bool _open;

        public SignPoseLocalizer(Transform mapSpace, string mapCode, IndoorSignCalibration calibration)
        {
            MapSpace = mapSpace;
            MapCode = mapCode;
            _calibration = calibration ?? new IndoorSignCalibration();
        }

        public event Action Authenticated;
        public event Action<bool> AuthFailed { add { } remove { } } // nothing to authenticate against
        public event Action<int> AttemptStarted { add { } remove { } }
        public event Action<float?> Localized;
        public event Action<int> NotLocalized { add { } remove { } }

        public Transform MapSpace { get; }
        public string MapCode { get; }
        /// <summary>Checks a solved camera position in the map, such as its floor, before it may pose the map.</summary>
        public Func<Vector3, bool> CandidatePoseValidator;

        public void Authenticate() => Authenticated?.Invoke();

        public void Localize() => _open = true;

        /// <summary>Feeds one sign sighting. Returns why no pose was produced, or Ok when MapSpace was moved.</summary>
        public IndoorSignPoseStatus Observe(IndoorSignObservation observation)
        {
            var result = IndoorSignPose.Solve(observation, _calibration.Find(observation?.RoomId));
            if (result.Status != IndoorSignPoseStatus.Ok || !_open) return result.Status;
            if (CandidatePoseValidator != null && !CandidatePoseValidator(result.CameraPositionInMap))
                return IndoorSignPoseStatus.Implausible;
            _open = false;
            Place(MapSpace, observation.CameraWorldPosition, observation.CameraWorldRotation,
                result.CameraPositionInMap, result.CameraRotationInMap);
            Localized?.Invoke(null);
            return IndoorSignPoseStatus.Ok;
        }

        /// <summary>
        /// Moves the MapSet origin so the AR camera, at its world pose, sits at the solved map pose. Yaw only:
        /// the AR world is gravity aligned, so the sign's pitch and roll estimate must not tilt the map.
        /// </summary>
        public static void Place(Transform mapSpace, Vector3 cameraWorldPosition, Quaternion cameraWorldRotation,
            Vector3 cameraPositionInMap, Quaternion cameraRotationInMap)
        {
            var yaw = Yaw(cameraWorldRotation) - Yaw(cameraRotationInMap);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            mapSpace.SetPositionAndRotation(cameraWorldPosition - rotation * cameraPositionInMap, rotation);
        }

        static float Yaw(Quaternion q)
        {
            var forward = q * Vector3.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }
    }
}
