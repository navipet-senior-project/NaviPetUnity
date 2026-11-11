using System;
using System.Collections.Generic;

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
        public static IndoorRoomMatch Match(
            string recognizedText,
            string buildingId,
            string floorId,
            IEnumerable<IndoorRoomLandmark> landmarks)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingBuilding);
            if (string.IsNullOrWhiteSpace(floorId))
                return new IndoorRoomMatch(IndoorRoomMatchStatus.MissingFloor);

            var roomNumber = NormalizeRoomNumber(recognizedText);
            if (roomNumber == null)
                return new IndoorRoomMatch(IndoorRoomMatchStatus.InvalidRoomNumber);

            IndoorRoomLandmark match = null;
            foreach (var landmark in landmarks)
            {
                if (landmark == null
                    || !string.Equals(landmark.BuildingId, buildingId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(landmark.FloorId, floorId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(NormalizeRoomNumber(landmark.Id), roomNumber, StringComparison.Ordinal))
                    continue;

                if (match != null)
                    return new IndoorRoomMatch(IndoorRoomMatchStatus.Ambiguous);
                match = landmark;
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
