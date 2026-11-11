using System;
using System.Collections;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace OutdoorAr
{
    public sealed class OutdoorGeospatialReadiness : MonoBehaviour
    {
        [SerializeField] AREarthManager earthManager;
        [SerializeField] bool optInEnabled;

        OutdoorArPermission _cameraPermission;
        OutdoorArPermission _locationPermission;
        OutdoorArVps _vps;
        bool _consented;
        bool _checkingVps;
        bool _vpsPositionKnown;
        double _vpsLatitude;
        double _vpsLongitude;

        public event Action<OutdoorArState> StateChanged;

        public OutdoorArState State { get; private set; } = OutdoorArState.Disabled;
        public bool CanPlaceGuidanceAnchor => OutdoorGeospatialGate.CanPlaceGuidanceAnchor(State);

        public void SetOptIn(bool enabled, bool consented)
        {
            optInEnabled = enabled;
            _consented = consented;
            Refresh();
        }

        public void SetPermissions(OutdoorArPermission camera, OutdoorArPermission location)
        {
            _cameraPermission = camera;
            _locationPermission = location;
            Refresh();
        }

        void Update() => Refresh();

        public void CheckVpsAtCurrentPosition()
        {
            Refresh();
            if (State != OutdoorArState.VpsCheckRequired || _checkingVps || earthManager == null
                || earthManager.EarthTrackingState != TrackingState.Tracking) return;

            var pose = earthManager.CameraGeospatialPose;
            StartCoroutine(CheckVps(
                AREarthManager.CheckVpsAvailabilityAsync(pose.Latitude, pose.Longitude),
                pose.Latitude, pose.Longitude));
        }

        void Refresh()
        {
            var support = earthManager == null
                ? FeatureSupported.Unknown
                : earthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            var service = earthManager == null ? EarthState.ErrorSessionNotReady : earthManager.EarthState;
            var tracking = earthManager == null ? TrackingState.None : earthManager.EarthTrackingState;
            var pose = tracking == TrackingState.Tracking && earthManager != null
                ? earthManager.CameraGeospatialPose
                : default;
            if (_vpsPositionKnown && tracking == TrackingState.Tracking
                && !OutdoorGeospatialGate.VpsCheckIsCurrent(
                    _vpsLatitude, _vpsLongitude, pose.Latitude, pose.Longitude))
            {
                _vps = OutdoorArVps.Unknown;
                _vpsPositionKnown = false;
            }
            var state = OutdoorGeospatialGate.Evaluate(
                optInEnabled, _consented, support, _cameraPermission, _locationPermission,
                service, _vps, tracking, pose.HorizontalAccuracy, pose.OrientationYawAccuracy);
            if (state == State) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        IEnumerator CheckVps(VpsAvailabilityPromise promise, double latitude, double longitude)
        {
            _checkingVps = true;
            yield return promise;
            _vpsLatitude = latitude;
            _vpsLongitude = longitude;
            _vpsPositionKnown = true;
            _vps = promise.Result == VpsAvailability.Available
                ? OutdoorArVps.Available
                : OutdoorArVps.Unavailable;
            _checkingVps = false;
            Refresh();
        }
    }
}
