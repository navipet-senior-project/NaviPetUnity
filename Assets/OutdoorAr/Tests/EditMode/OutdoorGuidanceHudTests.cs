using IndoorAr;
using NUnit.Framework;
using UnityEngine;

namespace OutdoorAr.Tests
{
    public class OutdoorGuidanceHudTests
    {
        static IndoorArRequest Request(string name = "Veterans Memorial", bool route = false)
        {
            var request = new IndoorArRequest
            {
                destination = new IndoorArDestination { name = name, latitude = 1, longitude = 1 }
            };
            if (route) request.route = new IndoorArRoute { waypoints = new[] { new IndoorArWaypoint(), new IndoorArWaypoint() } };
            return request;
        }

        static IndoorHudView Compose(IndoorArRequest request, OutdoorArState state = OutdoorArState.Ready,
            bool pose = true, bool arrived = false, float readyFor = 5f, double distance = 80, double turn = 30) =>
            OutdoorGuidanceHud.Compose(request, state, pose, arrived, distance, 120, turn, false, readyFor, null);

        [Test]
        public void NoPoseYetIsScanning()
        {
            var view = Compose(Request(), OutdoorArState.TrackingUncertain, pose: false);
            Assert.AreEqual(IndoorHudState.Scanning, view.State);
            Assert.IsTrue(view.Outdoor);
        }

        [Test]
        public void FailedCoverageCheckIsNotFoundSoRetryShows()
        {
            Assert.AreEqual(IndoorHudState.NotFound, Compose(Request(), OutdoorArState.VpsCheckFailed, pose: false).State);
        }

        [Test]
        public void NoCoverageAndMissingPermissionsGetTheirOwnWords()
        {
            Assert.AreEqual("No outdoor AR coverage here", Compose(Request(), OutdoorArState.VpsUnavailable, pose: false).TitleOverride);
            Assert.AreEqual("Allow camera and location", Compose(Request(), OutdoorArState.CameraPermissionRequired, pose: false).TitleOverride);
        }

        [Test]
        public void FirstFixShowsLocationFoundThenGuides()
        {
            Assert.AreEqual(IndoorHudState.Localized, Compose(Request(), readyFor: 0.2f).State);
            Assert.AreEqual(IndoorHudState.Straight, Compose(Request(), readyFor: LiveDoorHud.LocationFoundHoldS + .1f).State);
        }

        [Test]
        public void ApproximateAccuracyIsLabelledBesideTheDistance()
        {
            var settled = LiveDoorHud.LocationFoundHoldS + .1f;
            var view = Compose(Request(), OutdoorArState.Approximate, readyFor: settled, distance: 80);
            Assert.AreEqual(IndoorHudState.Straight, view.State);
            Assert.AreEqual(IndoorHud.DistanceLabel(80) + " · " + OutdoorGuidanceHud.ApproximateNote, view.DetailOverride);
            Assert.IsNull(Compose(Request(), OutdoorArState.Ready, readyFor: settled).DetailOverride);
            Assert.IsNull(Compose(Request(), OutdoorArState.Approximate, readyFor: 0.2f).DetailOverride);
        }

        [Test]
        public void OffRouteTextOutranksTheApproximateLabel()
        {
            var view = OutdoorGuidanceHud.Compose(Request(route: true), OutdoorArState.Approximate, true, false,
                80, 120, 30, true, LiveDoorHud.LocationFoundHoldS + .1f, null);
            Assert.IsNull(view.DetailOverride);
        }

        [Test]
        public void ArrivalWins()
        {
            Assert.AreEqual(IndoorHudState.Arrived, Compose(Request(), arrived: true, readyFor: 0f).State);
        }

        [Test]
        public void RejectedArrivalExplainsPreciseAccuracyRequirement()
        {
            var view = OutdoorGuidanceHud.Compose(Request(), OutdoorArState.Approximate, true, false,
                80, 120, 30, false, LiveDoorHud.LocationFoundHoldS + .1f, null,
                "Precise location needed to confirm arrival.");
            Assert.AreEqual("Precise location needed to confirm arrival.", view.DetailOverride);
        }

        [Test]
        public void ArrowFollowsTheTurnAndDistanceIsTheRouteWhenThereIsOne()
        {
            var direct = Compose(Request());
            Assert.AreEqual(30f, direct.ArrowYaw);
            Assert.IsFalse(direct.FollowingRoute);
            Assert.AreEqual(80, direct.DistanceM);
            var route = Compose(Request(route: true));
            Assert.IsTrue(route.FollowingRoute);
            Assert.AreEqual(120, route.DistanceM);
        }

        [Test]
        public void LongOrMissingNamesAreTamed()
        {
            Assert.AreEqual("Your destination", Compose(Request(name: "  ")).Destination);
            var name = Compose(Request(name: new string('x', 60))).Destination;
            Assert.AreEqual(OutdoorGuidanceHud.MaxNameLength, name.Length);
            Assert.IsTrue(name.EndsWith("…"));
        }
    }
}
