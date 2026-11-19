using System;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Maps the door-verifier session onto IndoorHudView.
    /// The guide arrow follows a route only with grid clearance from the camera; an obstructed route hides it.
    /// </summary>
    public static class LiveDoorHud
    {
        public const float LocationFoundHoldS = 1.5f;
        // The arrow aims at the first route point at least this far away, so it does not flip at a corner point.
        public const float RouteLookAheadM = 1f;
        public const string CameraFrozenTitle = "Camera stopped updating";
        public const string CameraFrozenDetail = "Close AR and open it again to restart.";

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
        /// <param name="cameraFrozen">The camera pose has stopped following the phone.</param>
        public static IndoorHudView Compose(IndoorArSession session, Vector3 cameraInMap, Vector3 cameraForwardInMap,
            float pinnedForS, Vector3[] route = null, string floorChange = null, bool floorUnknown = false,
            bool wrongFloorEvidence = false, IndoorWalkGrid routeGrid = null, int currentFloor = 0, bool cameraFrozen = false)
        {
            var request = session.Request;
            var door = request.position;
            cameraFrozen &= session.State is IndoorArState.Pinned or IndoorArState.Arrived;
            if (cameraFrozen) route = null;
            var target = Vector3.zero;
            var wrongFloor = wrongFloorEvidence || session.FloorSign != null && session.IsWrongFloor(session.FloorSign.Floor);
            var waypointIndex = -1;
            var cause = IndoorRouteBlockedCause.NoRoute;
            // On the wrong floor only a route to stairs or an elevator helps; one to the door would cross floors.
            if (session.State != IndoorArState.Pinned || door == null || route == null || route.Length < 2 || request.LocalizeOnly || floorUnknown
                || wrongFloor != (floorChange != null)) route = null;
            else if (routeGrid == null) route = null;
            else if (!routeGrid.TryNextWaypoint(route, cameraInMap, RouteLookAheadM, out target, out waypointIndex))
            {
                route = null;
                cause = routeGrid.IsWalkableAt(cameraInMap) ? IndoorRouteBlockedCause.NoClearLine : IndoorRouteBlockedCause.CameraCellBlocked;
            }
            var routeBlocked = session.State == IndoorArState.Pinned && !request.LocalizeOnly && route == null && !cameraFrozen;
            var authFailed = AuthFailed(session);
            var stepFreeUnavailable = StepFreeUnavailable(session);
            var view = new IndoorHudView
            {
                State = stepFreeUnavailable ? IndoorHudState.NotFound
                    : cameraFrozen || floorUnknown && session.State == IndoorArState.Pinned ? IndoorHudState.Localized : StateFor(session, pinnedForS),
                Destination = Destination(request),
                CurrentFloor = session.FloorSign?.Floor ?? currentFloor,
                Floor = int.TryParse(request.floor, out var floor) ? floor : 0,
                ArrowYaw = route == null ? 0f : GuidanceCue.ArrowYawDegrees(cameraForwardInMap, cameraInMap, target),
                LocalizeOnly = request.LocalizeOnly,
                WrongFloor = !request.LocalizeOnly && door != null && wrongFloor,
                FollowingRoute = route != null,
                RouteBlocked = routeBlocked,
                RouteWaypointIndex = route == null ? -1 : waypointIndex,
                RouteBlockedCause = routeBlocked ? cause : IndoorRouteBlockedCause.None,
                FloorChange = route == null ? null : floorChange,
                DistanceM = route == null ? double.NaN : IndoorWalkGrid.LengthM(route),
                TitleOverride = stepFreeUnavailable ? "Step-free route unavailable" : authFailed ? "AR sign-in failed"
                    : cameraFrozen ? CameraFrozenTitle : routeBlocked ? "Route unavailable" : null,
                DetailOverride = stepFreeUnavailable ? "A step-free route to this destination cannot be confirmed."
                    : cameraFrozen ? CameraFrozenDetail
                    : floorUnknown && session.State == IndoorArState.Pinned ? "Your floor could not be confirmed."
                    : routeBlocked ? "No clear hallway route from your position."
                    : session.ArrivedBySign ? "Found the " + RoomLabel(session.SignRoom.Id) + " sign · Floor " + session.SignRoom.Floor
                    : authFailed ? "The session could not use the AR token." : null,
            };
            if (view.State == IndoorHudState.Scanning && session.FloorSign != null)
            {
                view.TitleOverride = SignDetected(session.FloorSign);
                view.DetailOverride = view.WrongFloor ? view.Destination + " is on Floor " + view.Floor
                    : "From the room sign. AR is still aligning";
            }
            return view;
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
