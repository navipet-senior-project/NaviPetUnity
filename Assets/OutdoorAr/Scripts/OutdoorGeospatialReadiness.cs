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
        string _gateKey;

        public OutdoorArState State { get; private set; } = OutdoorArState.Disabled;
        public bool CanPlaceGuidanceAnchor => OutdoorGeospatialGate.CanPlaceGuidanceAnchor(State);
        public bool CanGuide => OutdoorGeospatialGate.CanGuide(State);
        public GeospatialPose CurrentPose => earthManager.CameraGeospatialPose;

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

        public void RetryVpsCheck()
        {
            if (State != OutdoorArState.VpsCheckFailed) return;
            _vps = OutdoorArVps.Unknown;
            Refresh();
            CheckVpsAtCurrentPosition();
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
            LogGate(service, tracking, pose);
            if (state == State) return;
            State = state;
        }

        // Which gate input holds guidance back: one fixed-label line per change. Buckets only, no coordinates.
        void LogGate(EarthState service, TrackingState tracking, GeospatialPose pose)
        {
            if (Application.isEditor) return;
            var tracked = tracking == TrackingState.Tracking;
            var key = string.Join(" ", service, tracking, _vps,
                OutdoorGeospatialGate.AccuracyBucket(pose.HorizontalAccuracy, tracked, 5, 10, 20),
                OutdoorGeospatialGate.AccuracyBucket(pose.OrientationYawAccuracy, tracked, 10, 20, 45),
                Input.location.status);
            if (key == _gateKey) return;
            _gateKey = key;
            Debug.Log("[OutdoorAr] gate " + key);
        }

        IEnumerator CheckVps(VpsAvailabilityPromise promise, double latitude, double longitude)
        {
            _checkingVps = true;
            yield return promise;
            _vps = OutdoorGeospatialGate.MapVpsAvailability(promise.Result);
            if (_vps == OutdoorArVps.Available || _vps == OutdoorArVps.Unavailable)
            {
                _vpsLatitude = latitude;
                _vpsLongitude = longitude;
                _vpsPositionKnown = true;
            }
            _checkingVps = false;
            Refresh();
        }
    }
}
