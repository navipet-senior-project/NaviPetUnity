using System;
using Google.XR.ARCoreExtensions;
using UnityEngine.XR.ARSubsystems;

namespace OutdoorAr
{
    public enum OutdoorArState
    {
        Disabled,
        ConsentRequired,
        DeviceSupportUnknown,
        UnsupportedDevice,
        CameraPermissionRequired,
        CameraPermissionDenied,
        LocationPermissionRequired,
        LocationPermissionDenied,
        ServiceInitializing,
        AuthorizationRequired,
        ServiceUnavailable,
        VpsCheckRequired,
        VpsCheckFailed,
        VpsUnavailable,
        TrackingUncertain,
        Ready
    }

    public enum OutdoorArPermission { Unknown, Granted, Denied }
    public enum OutdoorArVps { Unknown, Available, Unavailable, RequestFailed }

    public static class OutdoorGeospatialGate
    {
        public const double MaxHorizontalAccuracyM = 5.0;
        public const double MaxHeadingAccuracyDeg = 10.0;
        // Both need real-device validation on campus.
        public const double ArrivalRadiusM = 10.0;
        public const float ReadyTimeoutS = 60f;

        public static bool LocationPermissionsGranted(bool fineGranted, bool coarseGranted,
            bool requiresCoarse) => fineGranted && (!requiresCoarse || coarseGranted);

        public static OutdoorArVps MapVpsAvailability(VpsAvailability availability)
        {
            switch (availability)
            {
                case VpsAvailability.Available: return OutdoorArVps.Available;
                case VpsAvailability.Unavailable: return OutdoorArVps.Unavailable;
                default: return OutdoorArVps.RequestFailed;
            }
        }

        public static bool VpsCheckIsCurrent(double checkedLatitude, double checkedLongitude,
            double currentLatitude, double currentLongitude)
        {
            var distance = DistanceM(checkedLatitude, checkedLongitude, currentLatitude, currentLongitude);
            return !double.IsNaN(distance) && distance <= MaxHorizontalAccuracyM;
        }

        public static double DistanceM(double fromLatitude, double fromLongitude,
            double toLatitude, double toLongitude)
        {
            const double earthRadiusM = 6371000;
            var lat1 = fromLatitude * Math.PI / 180;
            var lat2 = toLatitude * Math.PI / 180;
            var deltaLat = lat2 - lat1;
            var deltaLon = (toLongitude - fromLongitude) * Math.PI / 180;
            var sinLat = Math.Sin(deltaLat / 2);
            var sinLon = Math.Sin(deltaLon / 2);
            var haversine = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            return earthRadiusM * 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
        }

        /// <summary>Compass bearing to walk from the first point to the second: 0 north, 90 east.</summary>
        public static double BearingDeg(double fromLatitude, double fromLongitude,
            double toLatitude, double toLongitude)
        {
            var lat1 = fromLatitude * Math.PI / 180;
            var lat2 = toLatitude * Math.PI / 180;
            var deltaLon = (toLongitude - fromLongitude) * Math.PI / 180;
            var east = Math.Sin(deltaLon) * Math.Cos(lat2);
            var north = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
            return (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
        }

        /// <summary>Turn from the camera heading to a bearing, in -180..180: negative is left.</summary>
        public static double TurnDeg(double bearingDeg, double headingDeg) =>
            ((bearingDeg - headingDeg) % 360 + 540) % 360 - 180;

        public static string TurnHint(double turnDeg) =>
            Math.Abs(turnDeg) <= 20 ? "ahead"
            : Math.Abs(turnDeg) >= 150 ? "behind you"
            : turnDeg < 0 ? "to your left" : "to your right";

        /// <summary>States a hosted session cannot leave by waiting, so it ends as "unavailable".</summary>
        public static bool IsUnavailable(OutdoorArState state) =>
            state == OutdoorArState.UnsupportedDevice
            || state == OutdoorArState.CameraPermissionDenied
            || state == OutdoorArState.LocationPermissionDenied
            || state == OutdoorArState.AuthorizationRequired
            || state == OutdoorArState.ServiceUnavailable;

        public static OutdoorArState Evaluate(
            bool enabled,
            bool consented,
            FeatureSupported support,
            OutdoorArPermission cameraPermission,
            OutdoorArPermission locationPermission,
            EarthState service,
            OutdoorArVps vps,
            TrackingState tracking,
            double horizontalAccuracyM,
            double headingAccuracyDeg)
        {
            if (!enabled) return OutdoorArState.Disabled;
            if (!consented) return OutdoorArState.ConsentRequired;
            if (support == FeatureSupported.Unknown) return OutdoorArState.DeviceSupportUnknown;
            if (support == FeatureSupported.Unsupported) return OutdoorArState.UnsupportedDevice;
            if (cameraPermission == OutdoorArPermission.Unknown) return OutdoorArState.CameraPermissionRequired;
            if (cameraPermission == OutdoorArPermission.Denied) return OutdoorArState.CameraPermissionDenied;
            if (locationPermission == OutdoorArPermission.Unknown) return OutdoorArState.LocationPermissionRequired;
            if (locationPermission == OutdoorArPermission.Denied) return OutdoorArState.LocationPermissionDenied;
            if (service == EarthState.ErrorEarthNotReady || service == EarthState.ErrorSessionNotReady)
                return OutdoorArState.ServiceInitializing;
            if (service == EarthState.ErrorNotAuthorized) return OutdoorArState.AuthorizationRequired;
            if (service != EarthState.Enabled) return OutdoorArState.ServiceUnavailable;
            if (vps == OutdoorArVps.Unknown) return OutdoorArState.VpsCheckRequired;
            if (vps == OutdoorArVps.RequestFailed) return OutdoorArState.VpsCheckFailed;
            if (vps == OutdoorArVps.Unavailable) return OutdoorArState.VpsUnavailable;
            if (tracking != TrackingState.Tracking
                || double.IsNaN(horizontalAccuracyM) || double.IsInfinity(horizontalAccuracyM)
                || double.IsNaN(headingAccuracyDeg) || double.IsInfinity(headingAccuracyDeg)
                || horizontalAccuracyM < 0 || headingAccuracyDeg < 0
                || horizontalAccuracyM > MaxHorizontalAccuracyM
                || headingAccuracyDeg > MaxHeadingAccuracyDeg)
                return OutdoorArState.TrackingUncertain;

            return OutdoorArState.Ready;
        }

        public static bool CanPlaceGuidanceAnchor(OutdoorArState state) => state == OutdoorArState.Ready;
    }
}
