using Google.XR.ARCoreExtensions;
using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;

namespace OutdoorAr.Tests
{
    public class OutdoorGeospatialGateTests
    {
        static OutdoorArState EvaluateReady(
            OutdoorArPermission camera = OutdoorArPermission.Granted,
            OutdoorArPermission location = OutdoorArPermission.Granted,
            OutdoorArVps vps = OutdoorArVps.Available,
            TrackingState tracking = TrackingState.Tracking,
            double horizontal = 2,
            double heading = 5) => OutdoorGeospatialGate.Evaluate(
                true, true, FeatureSupported.Supported, camera, location,
                EarthState.Enabled, vps, tracking, horizontal, heading);

        [TestCase(false, false, OutdoorArState.Disabled)]
        [TestCase(true, false, OutdoorArState.ConsentRequired)]
        public void RequiresOptInAndConsent(bool enabled, bool consent, OutdoorArState expected)
        {
            var actual = OutdoorGeospatialGate.Evaluate(
                enabled, consent, FeatureSupported.Supported, OutdoorArPermission.Granted,
                OutdoorArPermission.Granted, EarthState.Enabled, OutdoorArVps.Available,
                TrackingState.Tracking, 1, 1);
            Assert.AreEqual(expected, actual);
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(actual));
        }

        [TestCase(FeatureSupported.Unknown, OutdoorArState.DeviceSupportUnknown)]
        [TestCase(FeatureSupported.Unsupported, OutdoorArState.UnsupportedDevice)]
        public void BlocksUnknownAndUnsupportedDevices(FeatureSupported support, OutdoorArState expected)
        {
            var actual = OutdoorGeospatialGate.Evaluate(
                true, true, support, OutdoorArPermission.Granted, OutdoorArPermission.Granted,
                EarthState.Enabled, OutdoorArVps.Available, TrackingState.Tracking, 1, 1);
            Assert.AreEqual(expected, actual);
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(actual));
        }

        [TestCase(OutdoorArPermission.Unknown, OutdoorArState.CameraPermissionRequired)]
        [TestCase(OutdoorArPermission.Denied, OutdoorArState.CameraPermissionDenied)]
        public void RequiresCameraPermission(OutdoorArPermission permission, OutdoorArState expected)
        {
            Assert.AreEqual(expected, EvaluateReady(camera: permission));
        }

        [TestCase(OutdoorArPermission.Unknown, OutdoorArState.LocationPermissionRequired)]
        [TestCase(OutdoorArPermission.Denied, OutdoorArState.LocationPermissionDenied)]
        public void RequiresLocationPermission(OutdoorArPermission permission, OutdoorArState expected)
        {
            Assert.AreEqual(expected, EvaluateReady(location: permission));
        }

        [TestCase(true, false, false, true)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, true)]
        [TestCase(false, true, false, false)]
        public void AndroidLocationPermissionRequiresBothWhenCoarseIsRequired(
            bool fine, bool coarse, bool requireCoarse, bool expected)
        {
            Assert.AreEqual(expected,
                OutdoorGeospatialGate.LocationPermissionsGranted(fine, coarse, requireCoarse));
        }

        [TestCase(EarthState.ErrorNotAuthorized, OutdoorArState.AuthorizationRequired)]
        [TestCase(EarthState.ErrorEarthNotReady, OutdoorArState.ServiceInitializing)]
        [TestCase(EarthState.ErrorResourcesExhausted, OutdoorArState.ServiceUnavailable)]
        public void RequiresAuthorizedGeospatialService(EarthState service, OutdoorArState expected)
        {
            var serviceState = OutdoorGeospatialGate.Evaluate(
                true, true, FeatureSupported.Supported, OutdoorArPermission.Granted,
                OutdoorArPermission.Granted, service, OutdoorArVps.Available,
                TrackingState.Tracking, 1, 1);
            Assert.AreEqual(expected, serviceState);
        }

        [Test]
        public void RequiresVpsAvailabilityBeforeGuidance()
        {
            var vpsUnknown = EvaluateReady(vps: OutdoorArVps.Unknown);
            var vpsMissing = EvaluateReady(vps: OutdoorArVps.Unavailable);
            Assert.AreEqual(OutdoorArState.VpsCheckRequired, vpsUnknown);
            Assert.AreEqual(OutdoorArState.VpsUnavailable, vpsMissing);
        }

        [TestCase(TrackingState.None, 1, 1)]
        [TestCase(TrackingState.Tracking, 5.01, 1)]
        [TestCase(TrackingState.Tracking, 1, 10.01)]
        [TestCase(TrackingState.Tracking, double.NaN, 1)]
        [TestCase(TrackingState.Tracking, 1, double.PositiveInfinity)]
        public void RejectsUncertainOrInvalidPose(TrackingState tracking, double horizontal, double heading)
        {
            var actual = EvaluateReady(tracking: tracking, horizontal: horizontal, heading: heading);
            Assert.AreEqual(OutdoorArState.TrackingUncertain, actual);
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(actual));
        }

        [Test]
        public void AllowsGuidanceOnlyAtTheConfiguredConfidenceThresholds()
        {
            var actual = EvaluateReady(horizontal: 5, heading: 10);
            Assert.AreEqual(OutdoorArState.Ready, actual);
            Assert.IsTrue(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(actual));
        }

        [Test]
        public void VPSCheckOnlyAppliesNearItsQueriedPosition()
        {
            Assert.IsTrue(OutdoorGeospatialGate.VpsCheckIsCurrent(33.78, -118.11, 33.78001, -118.11));
            Assert.IsFalse(OutdoorGeospatialGate.VpsCheckIsCurrent(33.78, -118.11, 33.7801, -118.11));
        }
    }
}
