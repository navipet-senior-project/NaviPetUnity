using Google.XR.ARCoreExtensions;
using NUnit.Framework;
using UnityEngine;
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

        [TestCase(LocationServiceStatus.Running, OutdoorArPermission.Granted)]
        [TestCase(LocationServiceStatus.Failed, OutdoorArPermission.Denied)]
        [TestCase(LocationServiceStatus.Initializing, OutdoorArPermission.Unknown)]
        [TestCase(LocationServiceStatus.Stopped, OutdoorArPermission.Unknown)]
        public void IosLocationFollowsTheLocationService(LocationServiceStatus status, OutdoorArPermission expected)
        {
            Assert.AreEqual(expected, OutdoorGeospatialGate.IosLocationPermission(status));
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
        [TestCase(TrackingState.Tracking, 10.01, 1)]
        [TestCase(TrackingState.Tracking, 1, 20.01)]
        [TestCase(TrackingState.Tracking, -1, 1)]
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

        [TestCase(5.01, 1)]
        [TestCase(1, 10.01)]
        [TestCase(10, 20)]
        public void GpsClassAccuracyGuidesAsApproximateWithoutAnAnchor(double horizontal, double heading)
        {
            var actual = EvaluateReady(horizontal: horizontal, heading: heading);
            Assert.AreEqual(OutdoorArState.Approximate, actual);
            Assert.IsTrue(OutdoorGeospatialGate.CanGuide(actual));
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(actual));
            Assert.IsFalse(OutdoorGeospatialGate.IsUnavailable(actual));
        }

        [TestCase(OutdoorArState.Ready, true)]
        [TestCase(OutdoorArState.Approximate, true)]
        [TestCase(OutdoorArState.TrackingUncertain, false)]
        [TestCase(OutdoorArState.ServiceInitializing, false)]
        [TestCase(OutdoorArState.VpsCheckRequired, false)]
        public void OnlyReadyAndApproximateMayGuide(OutdoorArState state, bool expected)
        {
            Assert.AreEqual(expected, OutdoorGeospatialGate.CanGuide(state));
        }

        [TestCase(false, 3.0, "none")]
        [TestCase(true, double.NaN, "bad")]
        [TestCase(true, -1.0, "bad")]
        [TestCase(true, 4.0, "le5")]
        [TestCase(true, 5.0, "le5")]
        [TestCase(true, 5.1, "le10")]
        [TestCase(true, 20.0, "le20")]
        [TestCase(true, 20.1, "gt20")]
        public void LogsAccuracyOnlyAsABucket(bool tracking, double value, string expected)
        {
            Assert.AreEqual(expected, OutdoorGeospatialGate.AccuracyBucket(value, tracking, 5, 10, 20));
        }

        [Test]
        public void VPSCheckOnlyAppliesNearItsQueriedPosition()
        {
            Assert.IsTrue(OutdoorGeospatialGate.VpsCheckIsCurrent(33.78, -118.11, 33.78001, -118.11));
            Assert.IsFalse(OutdoorGeospatialGate.VpsCheckIsCurrent(33.78, -118.11, 33.7801, -118.11));
        }

        [Test]
        public void MeasuresDistanceAndBearingToADestination()
        {
            // 0.001 degrees of latitude is 111.2 m on the 6371 km sphere.
            Assert.AreEqual(111.2, OutdoorGeospatialGate.DistanceM(33.78, -118.11, 33.781, -118.11), 0.1);
            Assert.AreEqual(0, OutdoorGeospatialGate.DistanceM(33.78, -118.11, 33.78, -118.11), 1e-6);
            Assert.AreEqual(0, OutdoorGeospatialGate.BearingDeg(33.78, -118.11, 33.781, -118.11), 0.01);
            Assert.AreEqual(90, OutdoorGeospatialGate.BearingDeg(33.78, -118.11, 33.78, -118.109), 0.01);
            Assert.AreEqual(180, OutdoorGeospatialGate.BearingDeg(33.78, -118.11, 33.779, -118.11), 0.01);
            Assert.AreEqual(270, OutdoorGeospatialGate.BearingDeg(33.78, -118.11, 33.78, -118.111), 0.01);
        }

        [TestCase(90, 0, 90, "to your right")]
        [TestCase(0, 90, -90, "to your left")]
        [TestCase(350, 10, -20, "ahead")]
        [TestCase(10, 350, 20, "ahead")]
        [TestCase(10, -170, 180, "behind you")]
        [TestCase(180, 20, 160, "behind you")]
        [TestCase(45, 0, 45, "to your right")]
        public void TurnsTheShortWayFromTheHeadingToTheBearing(
            double bearing, double heading, double expectedTurn, string expectedHint)
        {
            var turn = OutdoorGeospatialGate.TurnDeg(bearing, heading);
            Assert.AreEqual(expectedTurn, System.Math.Abs(expectedTurn) == 180 ? System.Math.Abs(turn) : turn, 1e-9);
            Assert.AreEqual(expectedHint, OutdoorGeospatialGate.TurnHint(turn));
        }

        [TestCase(OutdoorArState.UnsupportedDevice, true)]
        [TestCase(OutdoorArState.CameraPermissionDenied, true)]
        [TestCase(OutdoorArState.LocationPermissionDenied, true)]
        [TestCase(OutdoorArState.AuthorizationRequired, true)]
        [TestCase(OutdoorArState.ServiceUnavailable, true)]
        [TestCase(OutdoorArState.DeviceSupportUnknown, false)]
        [TestCase(OutdoorArState.CameraPermissionRequired, false)]
        [TestCase(OutdoorArState.ServiceInitializing, false)]
        [TestCase(OutdoorArState.VpsCheckFailed, false)]
        [TestCase(OutdoorArState.VpsUnavailable, false)]
        [TestCase(OutdoorArState.TrackingUncertain, false)]
        [TestCase(OutdoorArState.Approximate, false)]
        [TestCase(OutdoorArState.Ready, false)]
        public void OnlyStatesWaitingCannotFixEndAHostedSession(OutdoorArState state, bool expected)
        {
            Assert.AreEqual(expected, OutdoorGeospatialGate.IsUnavailable(state));
        }

        [TestCase(VpsAvailability.Unknown)]
        [TestCase(VpsAvailability.ErrorInternal)]
        [TestCase(VpsAvailability.ErrorNetworkConnection)]
        [TestCase(VpsAvailability.ErrorNotAuthorized)]
        [TestCase(VpsAvailability.ErrorResourceExhausted)]
        public void VPSRequestErrorsRemainRetryableAndBlockGuidance(VpsAvailability availability)
        {
            var vps = OutdoorGeospatialGate.MapVpsAvailability(availability);
            var state = EvaluateReady(vps: vps);

            Assert.AreEqual(OutdoorArVps.RequestFailed, vps);
            Assert.AreEqual(OutdoorArState.VpsCheckFailed, state);
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(state));
        }

        [Test]
        public void ConfirmedVpsUnavailabilityIsNotTreatedAsRequestFailure()
        {
            var vps = OutdoorGeospatialGate.MapVpsAvailability(VpsAvailability.Unavailable);
            var state = EvaluateReady(vps: vps);

            Assert.AreEqual(OutdoorArVps.Unavailable, vps);
            Assert.AreEqual(OutdoorArState.VpsUnavailable, state);
            Assert.IsFalse(OutdoorGeospatialGate.CanPlaceGuidanceAnchor(state));
        }
    }
}
