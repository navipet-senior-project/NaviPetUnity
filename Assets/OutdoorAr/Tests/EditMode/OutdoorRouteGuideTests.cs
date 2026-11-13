using IndoorAr;
using NUnit.Framework;

namespace OutdoorAr.Tests
{
    public class OutdoorRouteGuideTests
    {
        const double MetersPerDegree = 6371000.0 * System.Math.PI / 180.0;

        // Route 100 m due north of (0,0), then 100 m due east. Near the equator one degree of longitude is the same as latitude.
        static IndoorArWaypoint P(double northM, double eastM) =>
            new IndoorArWaypoint { latitude = northM / MetersPerDegree, longitude = eastM / MetersPerDegree };

        static readonly IndoorArWaypoint[] LShape = { P(0, 0), P(100, 0), P(100, 100) };

        [Test]
        public void OnTheRouteTheBearingLooksAhead()
        {
            var g = OutdoorRouteGuide.Follow(LShape, 10 / MetersPerDegree, 0);
            Assert.AreEqual(0, g.Segment);
            Assert.AreEqual(0, g.OffRouteM, 0.5);
            Assert.AreEqual(0, g.TargetBearingDeg, 0.5);
            Assert.AreEqual(190, g.RemainingM, 1);
            Assert.IsFalse(g.IsOffRoute);
        }

        [Test]
        public void LookAheadBendsAroundACorner()
        {
            // 95 m up: 5 m to the corner, then 7 m east, so the target lies north-east.
            var g = OutdoorRouteGuide.Follow(LShape, 95 / MetersPerDegree, 0);
            Assert.Greater(g.TargetBearingDeg, 45);
            Assert.Less(g.TargetBearingDeg, 90);
        }

        [Test]
        public void OnTheSecondLegTheBearingIsEast()
        {
            var g = OutdoorRouteGuide.Follow(LShape, 100 / MetersPerDegree, 40 / MetersPerDegree);
            Assert.AreEqual(1, g.Segment);
            Assert.AreEqual(90, g.TargetBearingDeg, 0.5);
            Assert.AreEqual(60, g.RemainingM, 1);
        }

        [Test]
        public void NearTheEndTheTargetIsTheEnd()
        {
            var g = OutdoorRouteGuide.Follow(LShape, 100 / MetersPerDegree, 95 / MetersPerDegree);
            Assert.AreEqual(90, g.TargetBearingDeg, 0.5);
            Assert.AreEqual(5, g.RemainingM, 1);
        }

        [Test]
        public void OffTheRoutePointsBackToTheNearestPoint()
        {
            // 50 m west of the first leg, level with 40 m north: nearest point is due east.
            var g = OutdoorRouteGuide.Follow(LShape, 40 / MetersPerDegree, -50 / MetersPerDegree);
            Assert.IsTrue(g.IsOffRoute);
            Assert.AreEqual(50, g.OffRouteM, 0.5);
            Assert.AreEqual(90, g.TargetBearingDeg, 0.5);
            Assert.AreEqual(60 + 100 + 50, g.RemainingM, 1.5);
        }

        [Test]
        public void NeverFallsBackToAnEarlierSegment()
        {
            // Back at the start of the route but already past leg 0.
            var g = OutdoorRouteGuide.Follow(LShape, 1 / MetersPerDegree, 0, 1);
            Assert.AreEqual(1, g.Segment);
        }

        [Test]
        public void DuplicatePointsDoNotBreakIt()
        {
            var route = new[] { P(0, 0), P(0, 0), P(100, 0) };
            var g = OutdoorRouteGuide.Follow(route, 10 / MetersPerDegree, 0);
            Assert.AreEqual(0, g.TargetBearingDeg, 0.5);
            Assert.IsFalse(double.IsNaN(g.RemainingM));
        }
    }
}
