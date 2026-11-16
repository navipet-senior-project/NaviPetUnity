using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Maps the door-verifier session onto IndoorHudView.
    /// The guide arrow follows a route only with grid clearance from the camera; an obstructed route hides it.
    /// Without a route it uses the horizontal yaw straight to the door, which never turns or avoids walls.
    /// </summary>
    public static class LiveDoorHud
    {
        public const float LocationFoundHoldS = 1.5f;
        // The arrow aims at the first route point at least this far away, so it does not flip at a corner point.
        public const float RouteLookAheadM = 1f;

        public static bool LocalizationFailed(IndoorArSession session) =>
            session.State == IndoorArState.Done
            && session.ResultJson != null
            && session.ResultJson.Contains("\"status\":\"localizationFailed\"");

        public static bool AuthFailed(IndoorArSession session) =>
            session.State == IndoorArState.Done
            && session.ResultJson != null
            && session.ResultJson.Contains("\"errorCode\":\"MULTISET_AUTH\"");

        public static bool StepFreeUnavailable(IndoorArSession session) =>
            session.State == IndoorArState.Done && session.ResultJson != null
            && session.ResultJson.Contains("\"errorCode\":\"STEP_FREE_UNVERIFIED\"");

        /// <param name="route">Hallway waypoints from the camera to the door in the MapSet frame, or null for none.</param>
        /// <param name="floorChange">"stairs" or "elevator" when the route ends there instead of at the door.</param>
        public static IndoorHudView Compose(IndoorArSession session, Vector3 cameraInMap, Vector3 cameraForwardInMap,
            float pinnedForS, Vector3[] route = null, string floorChange = null, bool floorUnknown = false,
            bool wrongFloorEvidence = false, IndoorWalkGrid routeGrid = null, string roomDirections = null)
        {
            var request = session.Request;
            var door = request.position;
            var target = door == null ? Vector3.zero : new Vector3(door.x, door.y, door.z);
            var routeBlocked = false;
            var wrongFloor = wrongFloorEvidence || session.FloorSign != null && session.FloorSign.Floor is (1 or 2)
                && int.TryParse(request.floor, out var requestedFloor) && session.FloorSign.Floor != requestedFloor;
            // On the wrong floor only a route to stairs or an elevator helps; one to the door would cross floors.
            if (door == null || route == null || route.Length < 2 || request.LocalizeOnly || floorUnknown
                || wrongFloor != (floorChange != null)) route = null;
            else if (routeGrid == null || !routeGrid.TryNextWaypoint(route, cameraInMap, RouteLookAheadM, out target))
            {
                route = null;
                routeBlocked = true;
            }
            var authFailed = AuthFailed(session);
            var stepFreeUnavailable = StepFreeUnavailable(session);
            var view = new IndoorHudView
            {
                State = stepFreeUnavailable ? IndoorHudState.NotFound
                    : floorUnknown && session.State == IndoorArState.Pinned ? IndoorHudState.Localized : StateFor(session, pinnedForS),
                Destination = Destination(request),
                Floor = int.TryParse(request.floor, out var floor) ? floor : 0,
                ArrowYaw = door == null || routeBlocked ? 0f : GuidanceCue.ArrowYawDegrees(cameraForwardInMap, cameraInMap, target),
                LocalizeOnly = request.LocalizeOnly,
                WrongFloor = !request.LocalizeOnly && door != null && wrongFloor,
                FollowingRoute = route != null,
                RouteBlocked = routeBlocked,
                FloorChange = route == null ? null : floorChange,
                DistanceM = route == null ? double.NaN : IndoorWalkGrid.LengthM(route),
                TitleOverride = stepFreeUnavailable ? "Step-free route unavailable" : authFailed ? "AR sign-in failed"
                    : routeBlocked ? "Route unavailable" : null,
                DetailOverride = stepFreeUnavailable ? "A step-free route to this destination cannot be confirmed."
                    : floorUnknown && session.State == IndoorArState.Pinned ? "Your floor could not be confirmed."
                    : routeBlocked ? "No clear hallway route from your position."
                    : authFailed ? "The session could not use the AR token." : null,
            };
            if (view.State == IndoorHudState.Scanning && session.SignRoom != null) ShowSign(view, session.SignRoom, roomDirections);
            return view;
        }

        /// <summary>
        /// Room-sign copy while the AR pose is still pending. It names a room and, when given, the heading-free
        /// directions from it; never a position, an arrow or a left/right turn.
        /// </summary>
        static void ShowSign(IndoorHudView view, IndoorRoomLandmark sign, string directions)
        {
            var label = RoomLabel(sign.Id);
            var signFloor = sign.Floor;
            if (!view.LocalizeOnly && directions != null)
            {
                view.TitleOverride = SignDetected(sign);
                view.DetailOverride = directions;
            }
            else if (!view.LocalizeOnly && view.Floor > 0 && signFloor != view.Floor)
            {
                view.TitleOverride = SignDetected(sign);
                view.DetailOverride = view.Destination + " is on Floor " + view.Floor;
            }
            else if (!view.LocalizeOnly && string.Equals(label, view.Destination, StringComparison.OrdinalIgnoreCase))
            {
                view.TitleOverride = "Room " + label + " detected";
                view.DetailOverride = "Keep scanning so AR can confirm the door";
            }
            else
            {
                view.TitleOverride = SignDetected(sign);
                view.DetailOverride = view.LocalizeOnly
                    ? "From the room sign. AR is still aligning"
                    : "From the room sign. Keep scanning for the arrow";
            }
        }

        // A door this close to the route's hallway line, and this far along it, is one the user walks past.
        public const float PassingDoorM = 2.5f;
        public const string HeadingUnknown = "No arrow until AR knows which way you face.";

        /// <summary>
        /// Directions from a room sign the camera read while no pose is validated. The route is the scan-grid hallway
        /// from that room's door marker to the destination door, or on another floor to the nearest stairs or elevator.
        /// The user's heading is unknown, so the copy names the first mapped door the route passes (its sign can be
        /// read) and a distance, never left, right or an arrow. Null when the grid finds no route; the caller then
        /// keeps the plain sign copy instead of guessing.
        /// </summary>
        public static string RoomDirections(IndoorRoomLandmark sign, IndoorArRequest request, IndoorWalkGrid grid,
            List<(string Id, string Type, Vector3 PositionInMap)> markers)
        {
            if (sign == null || request == null || request.LocalizeOnly || request.position == null) return null;
            var destination = Destination(request);
            if (string.Equals(RoomLabel(sign.Id), destination, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(request.floor, out var floor)) return null;
            // Strict accessibility: the scan has no step evidence, so no unlabeled step-free directions.
            if (request.stepFree) return "A step-free route can't be confirmed from this room.";
            if (grid == null || markers == null) return null;
            var door = new Vector3(request.position.x, request.position.y, request.position.z);
            Vector3[] route = null;
            string via = null;
            if (sign.Floor == floor)
            {
                if (!grid.TryRoute(sign.DoorPositionInMap, door, out route)) return null;
            }
            else
                foreach (var (_, type, at) in markers)
                    if (type is "stairs" or "elevator" && grid.TryRoute(sign.DoorPositionInMap, at, out var leg)
                        && (route == null || IndoorWalkGrid.LengthM(leg) < IndoorWalkGrid.LengthM(route)))
                    {
                        route = leg;
                        via = type;
                    }
            if (route == null) return null;
            var length = IndoorHud.DistanceLabel(IndoorWalkGrid.LengthM(route));
            var toward = FirstDoorPassed(route, markers, RoomLabel(sign.Id), destination);
            var walk = toward == null ? "Walk about " + length : "Walk toward " + RoomLabel(toward) + ", about " + length;
            return walk + (via == null ? " to " + destination : " to the " + via + ", then go to Floor " + floor)
                + ". " + HeadingUnknown;
        }

        static string FirstDoorPassed(Vector3[] route, List<(string Id, string Type, Vector3 PositionInMap)> markers,
            string from, string to)
        {
            var total = IndoorWalkGrid.LengthM(route);
            string first = null;
            var firstAt = float.MaxValue;
            foreach (var (id, type, at) in markers)
            {
                var label = RoomLabel(id);
                if (type != "door" || string.Equals(label, from, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, to, StringComparison.OrdinalIgnoreCase)) continue;
                var p = new Vector2(at.x, at.z);
                var along = 0f;
                for (var i = 1; i < route.Length; i++)
                {
                    var a = new Vector2(route[i - 1].x, route[i - 1].z);
                    var segment = new Vector2(route[i].x, route[i].z) - a;
                    var length = segment.magnitude;
                    var t = length < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, segment) / (length * length));
                    var passedAt = along + t * length;
                    if ((a + segment * t - p).magnitude <= PassingDoorM && passedAt >= PassingDoorM
                        && passedAt < total - 1f && passedAt < firstAt)
                    {
                        first = id;
                        firstAt = passedAt;
                    }
                    along += length;
                }
            }
            return first;
        }

        /// <summary>"Room VEC 127 detected · Floor 1": the sign that was read, not where the camera is.</summary>
        public static string SignDetected(IndoorRoomLandmark sign) =>
            "Room " + RoomLabel(sign.Id) + " detected · Floor " + sign.Floor;

        public static string Destination(IndoorArRequest request)
        {
            var label = string.IsNullOrEmpty(request.room) ? request.doorKey : request.room;
            return RoomLabel(label);
        }

        /// <summary>"119", "VEC119", and "VEC 119" all show the searched label "VEC 119".</summary>
        public static string RoomLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("VEC", StringComparison.OrdinalIgnoreCase))
            {
                var body = trimmed.Substring(3).TrimStart();
                if (Digits(body)) return "VEC " + body;
                return trimmed;
            }
            return "VEC " + trimmed;
        }

        static bool Digits(string value)
        {
            if (value.Length == 0) return false;
            foreach (var c in value)
                if (c < '0' || c > '9') return false;
            return true;
        }

        static IndoorHudState StateFor(IndoorArSession session, float pinnedForS)
        {
            if (session.State == IndoorArState.Arrived) return IndoorHudState.Arrived;
            if (session.State == IndoorArState.Pinned)
                return session.Request.LocalizeOnly || pinnedForS < LocationFoundHoldS ? IndoorHudState.Localized : IndoorHudState.Straight;
            if (LocalizationFailed(session) || AuthFailed(session)) return IndoorHudState.NotFound;
            return IndoorHudState.Scanning;
        }
    }

    public static class GuidanceCue
    {
        public const float LookAheadDegrees = 20f;
        // shortcut: not measured in the VEC hallway; raise only if a slow scan still times out.
        public const float SlowScanMetersPerSecond = 0.75f;
        public const float SlowScanDegreesPerSecond = 45f;
        public const float SlowScanHoldS = 1f;

        /// <summary>Seconds the slow-scan reminder stays up: restarts on every too-fast frame, then counts down.</summary>
        public static float HoldSlowScan(float remaining, bool tooFast, float deltaSeconds) =>
            tooFast ? SlowScanHoldS : Mathf.Max(0f, remaining - deltaSeconds);

        /// <summary>Signed degrees from horizontal camera forward to the waypoint. 0 is ahead, positive is right.</summary>
        public static float ArrowYawDegrees(Vector3 cameraForward, Vector3 cameraPosition, Vector3 waypoint)
        {
            var forward = cameraForward;
            forward.y = 0f;
            var to = waypoint - cameraPosition;
            to.y = 0f;
            if (forward.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(forward, to, Vector3.up);
        }

        /// <summary>UI z rotation for an arrow that points up at yaw 0. Positive yaw (right) is clockwise.</summary>
        public static float ArrowScreenRotation(float yawDegrees) => -yawDegrees;

        public static bool FacingWaypoint(float yawDegrees) =>
            Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) <= LookAheadDegrees;

        public static bool ScanTooFast(float movedMeters, float turnedDegrees, float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return false;
            return movedMeters / deltaSeconds > SlowScanMetersPerSecond
                || Mathf.Abs(turnedDegrees) / deltaSeconds > SlowScanDegreesPerSecond;
        }
    }
}
