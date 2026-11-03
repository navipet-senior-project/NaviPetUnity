using System;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// Maps the door-verifier session onto IndoorHudView.
    /// Arrows are a straight line from the live pose to the launch door pose.
    /// shortcut: no walkable map, so this never turns or avoids walls.
    /// </summary>
    public static class LiveDoorHud
    {
        public const float LocationFoundHoldS = 1.5f;

        public static bool LocalizationFailed(IndoorArSession session) =>
            session.State == IndoorArState.Done
            && session.ResultJson != null
            && session.ResultJson.Contains("\"status\":\"localizationFailed\"");

        public static IndoorHudView Compose(IndoorArSession session, Vector3 cameraInMap, float pinnedForS)
        {
            var request = session.Request;
            var door = request.position;
            var target = new Vector3(door.x, door.y, door.z);
            return new IndoorHudView
            {
                State = StateFor(session, pinnedForS),
                Destination = Destination(request),
                Floor = int.TryParse(request.floor, out var floor) ? floor : 0,
                Distance = session.HorizontalDistance(cameraInMap),
                Waypoints = new[] { new Vector3(cameraInMap.x, door.y, cameraInMap.z), target },
            };
        }

        public static string Destination(IndoorArRequest request)
        {
            if (string.IsNullOrEmpty(request.room)) return request.doorKey;
            return request.room.StartsWith("VEC", StringComparison.Ordinal) ? request.room : "VEC " + request.room;
        }

        static IndoorHudState StateFor(IndoorArSession session, float pinnedForS)
        {
            if (session.State == IndoorArState.Pinned)
                return pinnedForS < LocationFoundHoldS ? IndoorHudState.Localized : IndoorHudState.Straight;
            if (LocalizationFailed(session)) return IndoorHudState.NotFound;
            return IndoorHudState.Scanning;
        }
    }
}
