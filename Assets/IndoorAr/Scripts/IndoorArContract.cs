using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace IndoorAr
{
    [Serializable]
    public class IndoorArVector3
    {
        public float x;
        public float y;
        public float z;
    }

    /// <summary>WGS84 point an outdoor request guides to.</summary>
    [Serializable]
    public class IndoorArDestination
    {
        public string name;
        public double latitude;
        public double longitude;
    }

    /// <summary>One walking-route point. NaN until the JSON supplies it, so a missing coordinate is not read as 0.</summary>
    [Serializable]
    public class IndoorArWaypoint
    {
        public double latitude = double.NaN;
        public double longitude = double.NaN;
    }

    /// <summary>The walking route an outdoor request follows, start to destination.</summary>
    [Serializable]
    public class IndoorArRoute
    {
        public IndoorArWaypoint[] waypoints;
    }

    /// <summary>
    /// Bridge contract v1/v2 request from the Flutter app. The pose is in the
    /// MultiSet MapSet frame and is used as is. v2 sends arToken as an object;
    /// ReadArToken reads it because JsonUtility cannot store that object in the string field.
    /// </summary>
    [Serializable]
    public class IndoorArRequest
    {
        public int schemaVersion;
        public string mode;
        public bool LocalizeOnly => mode == "localize";
        public bool OutdoorOnly => mode == "outdoor";
        public IndoorArDestination destination;
        public IndoorArRoute route;
        /// <summary>An outdoor request with a route to follow. Without one the guidance is a straight direction only.</summary>
        public bool HasRoute => route != null && route.waypoints != null && route.waypoints.Length > 0;
        public bool dataUseAccepted;
        public string room;
        public string doorKey;
        public string floor;
        public IndoorArVector3 position;
        public float headingDeg;
        public string mapCode;
        public string frame;
        public string arToken;
    }

    /// <summary>Flutter sends arToken as an object. JsonUtility cannot put that object in a string.</summary>
    [Serializable]
    public class IndoorArSuppliedToken
    {
        public string token;
        public string expiresOn;
    }

    public static class IndoorArContract
    {
        public const int SchemaVersion = 1;
        public const int SchemaVersionV2 = 2;
        public const string RequestExtra = "navipet.indoorAr.request";
        public const string BadRequest = "BAD_REQUEST";
        public const string Frame = "multiset-mapset";
        public const string MapMismatch = "MAP_MISMATCH";
        public const string ArcoreUnsupported = "ARCORE_UNSUPPORTED";
        public const string MultisetAuth = "MULTISET_AUTH";
        public const string Network = "NETWORK";
        public const string OutdoorScene = "OutdoorGeospatialDemo";

        /// <summary>
        /// JsonUtility fills a missing object field with defaults, so a missing
        /// position reads as (0, 0, 0). The Dart side validates the pose before
        /// sending; this only rejects what JsonUtility can tell apart.
        /// </summary>
        public static bool TryParseRequest(string json, out IndoorArRequest request)
        {
            request = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            IndoorArRequest parsed;
            try
            {
                parsed = JsonUtility.FromJson<IndoorArRequest>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (parsed == null
                || (parsed.schemaVersion != SchemaVersion && parsed.schemaVersion != SchemaVersionV2))
            {
                return false;
            }
            if (parsed.OutdoorOnly)
            {
                var to = parsed.destination;
                var supplied = new IndoorArRequest
                {
                    destination = new IndoorArDestination { latitude = double.NaN, longitude = double.NaN }
                };
                JsonUtility.FromJsonOverwrite(json, supplied);
                var coordinates = supplied.destination;
                if (to == null || coordinates == null || double.IsNaN(coordinates.latitude)
                    || double.IsNaN(coordinates.longitude)
                    || !(Math.Abs(to.latitude) <= 90) || !(Math.Abs(to.longitude) <= 180))
                {
                    return false;
                }
                // A route that is present but unusable is a sender bug; guiding by bearing instead would pass it off as a route.
                if (parsed.HasRoute && !OutdoorRouteIsValid(parsed.route.waypoints)) return false;
                request = parsed;
                return true;
            }
            if ((!string.IsNullOrEmpty(parsed.mode) && !parsed.LocalizeOnly)
                || (!parsed.LocalizeOnly && (string.IsNullOrEmpty(parsed.doorKey) || string.IsNullOrEmpty(parsed.floor)))
                || string.IsNullOrEmpty(parsed.mapCode))
            {
                return false;
            }
            request = parsed;
            return true;
        }

        public const int MaxRouteWaypoints = 1000;

        static bool OutdoorRouteIsValid(IndoorArWaypoint[] waypoints)
        {
            if (waypoints.Length < 2 || waypoints.Length > MaxRouteWaypoints) return false;
            foreach (var point in waypoints)
                if (point == null || double.IsNaN(point.latitude) || double.IsNaN(point.longitude)
                    || !(Math.Abs(point.latitude) <= 90) || !(Math.Abs(point.longitude) <= 180)) return false;
            return true;
        }

        public static IndoorArSuppliedToken ReadArToken(string json)
        {
            const string key = "\"arToken\":";
            if (string.IsNullOrEmpty(json)) return null;
            var at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;
            var rest = json.Substring(at + key.Length).TrimStart();
            if (!rest.StartsWith("{", StringComparison.Ordinal)) return null;
            var end = rest.IndexOf('}');
            if (end < 0) return null;
            // shortcut: first closing brace ends the object; Flutter's token has no nested braces.
            return JsonUtility.FromJson<IndoorArSuppliedToken>(rest.Substring(0, end + 1));
        }

        /// <summary>
        /// A pose in another frame or map would put the pin in the wrong place, so
        /// the session refuses it as MAP_MISMATCH instead of guessing.
        /// </summary>
        public static bool CheckMap(IndoorArRequest request, string buildMapCode) =>
            request.frame == Frame && request.mapCode == buildMapCode;

        /// <summary>
        /// A NaN, infinite or negative distance is a bug on this side, and the app
        /// rejects it as BAD_RESULT. It is reported as an error result instead of
        /// clamped: contract v1 has no better code than BAD_REQUEST.
        /// </summary>
        public static string Arrived(float checkDistanceM, int localizationQueries, long durationMs, float? confidence = null) =>
            float.IsNaN(checkDistanceM) || float.IsInfinity(checkDistanceM) || checkDistanceM < 0f
                || (confidence.HasValue && (float.IsNaN(confidence.Value) || float.IsInfinity(confidence.Value)))
                ? Error(BadRequest, localizationQueries, durationMs)
                : Build("arrived", checkDistanceM, null, localizationQueries, durationMs, confidence);

        public static string Cancelled(int localizationQueries, long durationMs) =>
            Build("cancelled", null, null, localizationQueries, durationMs);

        public static string LocalizationFailed(int localizationQueries, long durationMs) =>
            Build("localizationFailed", null, null, localizationQueries, durationMs);

        /// <summary>The session cannot run here: device, permission, authorization or a build without the scene.</summary>
        public static string Unavailable(int localizationQueries, long durationMs) =>
            Build("unavailable", null, null, localizationQueries, durationMs);

        public static string Error(string errorCode, int localizationQueries, long durationMs) =>
            Build("error", null, errorCode, localizationQueries, durationMs);

        // Written by hand: JsonUtility cannot omit fields. Status and error codes
        // are constants from this file, so they need no escaping.
        static string Build(string status, float? checkDistanceM, string errorCode,
            int localizationQueries, long durationMs, float? confidence = null)
        {
            var json = new StringBuilder("{\"status\":\"").Append(status).Append('"');
            if (checkDistanceM.HasValue)
                json.Append(",\"checkDistanceM\":")
                    .Append(checkDistanceM.Value.ToString("0.###", CultureInfo.InvariantCulture));
            if (confidence.HasValue)
                json.Append(",\"confidence\":")
                    .Append(confidence.Value.ToString("R", CultureInfo.InvariantCulture));
            if (errorCode != null)
                json.Append(",\"errorCode\":\"").Append(errorCode).Append('"');
            json.Append(",\"localizationQueries\":")
                .Append(localizationQueries.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"durationMs\":").Append(durationMs.ToString(CultureInfo.InvariantCulture));
            return json.Append('}').ToString();
        }
    }
}
