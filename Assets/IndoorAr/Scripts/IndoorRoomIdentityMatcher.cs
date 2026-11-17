using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorRoomMatchStatus
    {
        Matched,
        MissingBuilding,
        BuildingMismatch,
        MissingFloor,
        InvalidRoomNumber,
        NoText,
        RecognizerUnavailable,
        NoMatch,
        Ambiguous
    }

    public sealed class IndoorRoomLandmark
    {
        public string Id { get; }
        public string BuildingId { get; }
        public string FloorId { get; }
        /// <summary>The door marker (a floor-level point, not the sign) in the MapSet frame; from the directory JSON.</summary>
        public Vector3 DoorPositionInMap { get; }
        /// <summary>Unity yaw of the door marker in the MapSet frame; it points from the hallway into the room.</summary>
        public float DoorHeadingInMapDeg { get; }
        /// <summary>The number after the last '_' of FloorId ("floor_2" is 2), or 0 when there is none.</summary>
        public int Floor
        {
            get
            {
                var at = FloorId?.LastIndexOf('_') ?? -1;
                return int.TryParse(at < 0 ? FloorId : FloorId.Substring(at + 1), out var floor) ? floor : 0;
            }
        }

        public IndoorRoomLandmark(string id, string buildingId, string floorId,
            Vector3 doorPositionInMap = default, float doorHeadingInMapDeg = 0f)
        {
            Id = id;
            BuildingId = buildingId;
            FloorId = floorId;
            DoorPositionInMap = doorPositionInMap;
            DoorHeadingInMapDeg = doorHeadingInMapDeg;
        }
    }

    public sealed class IndoorRoomMatch
    {
        public IndoorRoomMatchStatus Status { get; }
        public IndoorRoomLandmark Landmark { get; }

        internal IndoorRoomMatch(IndoorRoomMatchStatus status, IndoorRoomLandmark landmark = null)
        {
            Status = status;
            Landmark = landmark;
        }
    }

    /// <summary>Matches recognizer text to a room identity. It does not estimate or return a pose.</summary>
    public static class IndoorRoomIdentityMatcher
    {
        [Serializable]
        sealed class VecDirectory
        {
            public string buildingId;
            public List<VecObject> objects;
        }

        [Serializable]
        sealed class VecObject
        {
            public string id;
            public string type;
            public string floorId;
            public Vector3 position;
            public float rotationY;
        }

        public static IndoorRoomMatch Match(
            string recognizedText,
            string buildingId,
            string floorId,
            string vecDirectoryJson)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingBuilding);
            if (string.IsNullOrWhiteSpace(floorId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingFloor);

            var roomNumber = NormalizeRoomNumber(recognizedText);
            if (roomNumber == null)
                return new IndoorRoomMatch(IndoorRoomMatchStatus.InvalidRoomNumber);

            var directory = JsonUtility.FromJson<VecDirectory>(vecDirectoryJson);
            if (directory == null || string.IsNullOrWhiteSpace(directory.buildingId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingBuilding);
            if (!string.Equals(directory.buildingId, buildingId, StringComparison.OrdinalIgnoreCase))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.BuildingMismatch);

            IndoorRoomLandmark match = null;
            if (directory.objects == null)
                return new IndoorRoomMatch(IndoorRoomMatchStatus.NoMatch);
            foreach (var item in directory.objects)
            {
                if (item == null || !string.Equals(item.type, "door", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(item.floorId, floorId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(NormalizeRoomNumber(item.id), roomNumber, StringComparison.Ordinal))
                    continue;

                if (match != null)
                    return new IndoorRoomMatch(IndoorRoomMatchStatus.Ambiguous);
                match = Landmark(item, directory.buildingId);
            }

            return match == null
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoMatch)
                : new IndoorRoomMatch(IndoorRoomMatchStatus.Matched, match);
        }

        /// <summary>
        /// Reads a whole sign frame for a VEC door in the directory.
        /// Two different rooms in one frame are Ambiguous, never a guess.
        /// </summary>
        public static IndoorRoomMatch MatchSign(string recognizedText, string vecDirectoryJson)
        {
            if (string.IsNullOrWhiteSpace(recognizedText))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.InvalidRoomNumber);
            var numbers = new HashSet<string>();
            foreach (Match number in Regex.Matches(recognizedText,
                         @"(?<![A-Za-z0-9_])(?:(?:ROOM|VEC)[ \t-]*)?([0-9]{3})(?![A-Za-z0-9_])",
                         RegexOptions.IgnoreCase))
                numbers.Add(number.Groups[1].Value);
            if (numbers.Count == 0) return new IndoorRoomMatch(IndoorRoomMatchStatus.InvalidRoomNumber);
            if (numbers.Count > 1) return new IndoorRoomMatch(IndoorRoomMatchStatus.Ambiguous);

            var directory = JsonUtility.FromJson<VecDirectory>(vecDirectoryJson ?? "");
            if (directory == null || string.IsNullOrWhiteSpace(directory.buildingId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingBuilding);
            IndoorRoomLandmark match = null;
            foreach (var item in directory.objects ?? new List<VecObject>())
            {
                if (item == null || !string.Equals(item.type, "door", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.floorId)) continue;
                var number = NormalizeRoomNumber(item.id);
                if (number == null || !numbers.Contains(number)) continue;
                if (match != null) return new IndoorRoomMatch(IndoorRoomMatchStatus.Ambiguous);
                match = Landmark(item, directory.buildingId);
            }
            return match == null
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoMatch)
                : new IndoorRoomMatch(IndoorRoomMatchStatus.Matched, match);
        }

        /// <summary>
        /// Stairs and elevators on one floor, as the directory marks them, in the MapSet frame.
        /// </summary>
        public static List<(string Id, string Type, Vector3 PositionInMap)> FloorConnectors(string vecDirectoryJson, int floor) =>
            FloorMarkers(vecDirectoryJson, floor).FindAll(m => m.Type is "elevator" or "stairs");

        /// <summary>Every directory marker on one floor (doors, stairs, elevators, ...), in the MapSet frame.</summary>
        public static List<(string Id, string Type, Vector3 PositionInMap)> FloorMarkers(string vecDirectoryJson, int floor)
        {
            var found = new List<(string, string, Vector3)>();
            if (string.IsNullOrWhiteSpace(vecDirectoryJson)) return found;
            var directory = JsonUtility.FromJson<VecDirectory>(vecDirectoryJson);
            foreach (var item in directory?.objects ?? new List<VecObject>())
                if (item != null && new IndoorRoomLandmark(item.id, null, item.floorId).Floor == floor)
                    found.Add((item.id, item.type, IndoorMapFrame.ToMapSet(item.position)));
            return found;
        }

        static IndoorRoomLandmark Landmark(VecObject item, string buildingId) =>
            new IndoorRoomLandmark(item.id, buildingId, item.floorId,
                IndoorMapFrame.ToMapSet(item.position), IndoorMapFrame.HeadingToMapSet(item.rotationY));

        static string NormalizeRoomNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.StartsWith("ROOM", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(4).TrimStart();
            if (text.StartsWith("VEC", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(3).TrimStart();

            var digits = text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("-", "");
            if (digits.Length == 0) return null;
            for (var i = 0; i < digits.Length; i++)
                if (digits[i] < '0' || digits[i] > '9') return null;
            return digits;
        }
    }
}
