using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorRoomMatchStatus
    {
        Matched,
        MissingBuilding,
        MissingFloor,
        InvalidRoomNumber,
        NoMatch,
        Ambiguous
    }

    public sealed class IndoorRoomLandmark
    {
        public string Id { get; }
        public string BuildingId { get; }
        public string FloorId { get; }

        public IndoorRoomLandmark(string id, string buildingId, string floorId)
        {
            Id = id;
            BuildingId = buildingId;
            FloorId = floorId;
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
        }

        public static IndoorRoomMatch Match(
            string recognizedText,
            string floorId,
            string vecDirectoryJson)
        {
            if (string.IsNullOrWhiteSpace(floorId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingFloor);

            var roomNumber = NormalizeRoomNumber(recognizedText);
            if (roomNumber == null)
                return new IndoorRoomMatch(IndoorRoomMatchStatus.InvalidRoomNumber);

            var directory = JsonUtility.FromJson<VecDirectory>(vecDirectoryJson);
            if (directory == null || string.IsNullOrWhiteSpace(directory.buildingId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingBuilding);

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
                match = new IndoorRoomLandmark(item.id, directory.buildingId, item.floorId);
            }

            return match == null
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoMatch)
                : new IndoorRoomMatch(IndoorRoomMatchStatus.Matched, match);
        }

        static string NormalizeRoomNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (text.StartsWith("ROOM", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(4).TrimStart();
            if (text.StartsWith("VEC", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(3).TrimStart();

            var digits = text.Replace(" ", "").Replace("-", "");
            if (digits.Length == 0) return null;
            for (var i = 0; i < digits.Length; i++)
                if (digits[i] < '0' || digits[i] > '9') return null;
            return digits;
        }
    }
}
