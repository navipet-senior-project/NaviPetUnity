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
            const double earthRadiusM = 6371000;
            var lat1 = checkedLatitude * Math.PI / 180;
            var lat2 = currentLatitude * Math.PI / 180;
            var deltaLat = lat2 - lat1;
            var deltaLon = (currentLongitude - checkedLongitude) * Math.PI / 180;
            var sinLat = Math.Sin(deltaLat / 2);
            var sinLon = Math.Sin(deltaLon / 2);
            var haversine = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
            var distance = earthRadiusM * 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
            return !double.IsNaN(distance) && distance <= MaxHorizontalAccuracyM;
        }

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
