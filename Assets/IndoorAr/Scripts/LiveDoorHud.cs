using System;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Maps the door-verifier session onto IndoorHudView.
    /// The guide arrow is the horizontal yaw from the camera to the door.
    /// shortcut: no walkable map, so this never turns or avoids walls.
    /// </summary>
    public static class LiveDoorHud
    {
        public const float LocationFoundHoldS = 1.5f;

        public static bool LocalizationFailed(IndoorArSession session) =>
            session.State == IndoorArState.Done
            && session.ResultJson != null
            && session.ResultJson.Contains("\"status\":\"localizationFailed\"");

        public static bool AuthFailed(IndoorArSession session) =>
            session.State == IndoorArState.Done
            && session.ResultJson != null
            && session.ResultJson.Contains("\"errorCode\":\"MULTISET_AUTH\"");

        public static IndoorHudView Compose(IndoorArSession session, Vector3 cameraInMap, Vector3 cameraForwardInMap, float pinnedForS)
        {
            var request = session.Request;
            var door = request.position;
            var target = door == null ? Vector3.zero : new Vector3(door.x, door.y, door.z);
            var authFailed = AuthFailed(session);
            return new IndoorHudView
            {
                State = StateFor(session, pinnedForS),
                Destination = Destination(request),
                Floor = int.TryParse(request.floor, out var floor) ? floor : 0,
                ArrowYaw = door == null ? 0f : GuidanceCue.ArrowYawDegrees(cameraForwardInMap, cameraInMap, target),
                LocalizeOnly = request.LocalizeOnly,
                WrongFloor = !request.LocalizeOnly && door != null && session.IsWrongFloor(cameraInMap),
                TitleOverride = authFailed ? "AR sign-in failed" : null,
                DetailOverride = authFailed ? "The session could not use the AR token." : null,
            };
        }

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
