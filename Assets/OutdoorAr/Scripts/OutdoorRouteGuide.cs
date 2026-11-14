using System;
using IndoorAr;

namespace OutdoorAr
{
    /// <summary>Where a walker stands against a route, and where to look to keep following it.</summary>
    public struct OutdoorRouteGuidance
    {
        /// <summary>Route segment the walker is on. Pass it back so the guide never jumps back to an earlier leg.</summary>
        public int Segment;
        /// <summary>Metres from the walker to the nearest point of the route.</summary>
        public double OffRouteM;
        /// <summary>Metres to the end of the route, along the route from the nearest point, plus OffRouteM.</summary>
        public double RemainingM;
        /// <summary>Compass bearing (0 north, 90 east) to walk: the point LookAheadM further along the route, or back to the route when off it.</summary>
        public double TargetBearingDeg;
        public bool IsOffRoute;
    }

    /// <summary>
    /// Route-following for the outdoor HUD. Pure math on the request's walking waypoints;
    /// the route comes from the host's routing service, so avoiding obstacles is whatever that route avoids.
    /// shortcut: flat-earth metres around the walker, fine for a campus-sized route; swap for geodesics beyond a few km.
    /// </summary>
    public static class OutdoorRouteGuide
    {
        public const double LookAheadM = 12.0;
        public const double OffRouteLimitM = 30.0;
        const double MetersPerDegree = 6371000.0 * Math.PI / 180.0;

        public static OutdoorRouteGuidance Follow(IndoorArWaypoint[] route, double latitude, double longitude, int fromSegment = 0)
        {
            var cosLat = Math.Cos(latitude * Math.PI / 180.0);
            var east = new double[route.Length];
            var north = new double[route.Length];
            for (var i = 0; i < route.Length; i++)
            {
                east[i] = (route[i].longitude - longitude) * cosLat * MetersPerDegree;
                north[i] = (route[i].latitude - latitude) * MetersPerDegree;
            }

            // The walker is the origin. Find the nearest point on the route, never before fromSegment.
            var best = Math.Max(0, Math.Min(fromSegment, route.Length - 2));
            var bestT = 0.0;
            var bestDistance = double.MaxValue;
            for (var i = best; i < route.Length - 1; i++)
            {
                var t = ProjectOnSegment(east[i], north[i], east[i + 1], north[i + 1]);
                var distance = Math.Sqrt(Square(east[i] + t * (east[i + 1] - east[i]))
                    + Square(north[i] + t * (north[i + 1] - north[i])));
                if (distance >= bestDistance) continue;
                best = i;
                bestT = t;
                bestDistance = distance;
            }

            var nearestEast = east[best] + bestT * (east[best + 1] - east[best]);
            var nearestNorth = north[best] + bestT * (north[best + 1] - north[best]);
            var remaining = Length(nearestEast, nearestNorth, east[best + 1], north[best + 1]);
            for (var i = best + 1; i < route.Length - 1; i++)
                remaining += Length(east[i], north[i], east[i + 1], north[i + 1]);

            var offRoute = bestDistance > OffRouteLimitM;
            double targetEast = nearestEast, targetNorth = nearestNorth;
            if (!offRoute)
            {
                // Walk LookAheadM along the route from the nearest point, stopping at its end.
                var left = LookAheadM;
                var fromEast = nearestEast;
                var fromNorth = nearestNorth;
                for (var i = best + 1; i < route.Length; i++)
                {
                    var leg = Length(fromEast, fromNorth, east[i], north[i]);
                    if (leg >= left && leg > 0)
                    {
                        var share = left / leg;
                        targetEast = fromEast + share * (east[i] - fromEast);
                        targetNorth = fromNorth + share * (north[i] - fromNorth);
                        left = 0;
                        break;
                    }
                    left -= leg;
                    fromEast = targetEast = east[i];
                    fromNorth = targetNorth = north[i];
                }
            }

            return new OutdoorRouteGuidance
            {
                Segment = best,
                OffRouteM = bestDistance,
                RemainingM = remaining + bestDistance,
                IsOffRoute = offRoute,
                TargetBearingDeg = (Math.Atan2(targetEast, targetNorth) * 180.0 / Math.PI + 360.0) % 360.0
            };
        }

        // Fraction 0..1 of the way along a to b of the point closest to the origin.
        static double ProjectOnSegment(double ae, double an, double be, double bn)
        {
            var de = be - ae;
            var dn = bn - an;
            var squared = de * de + dn * dn;
            if (squared < 1e-9) return 0;
            return Math.Max(0, Math.Min(1, -(ae * de + an * dn) / squared));
        }

        static double Length(double ae, double an, double be, double bn) => Math.Sqrt(Square(be - ae) + Square(bn - an));
        static double Square(double value) => value * value;
    }
}
