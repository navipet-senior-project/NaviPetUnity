namespace IndoorAr
{
    /// <summary>
    /// Confidence gate for room-sign OCR. A room is confirmed only after ConfirmReads
    /// frames name it within ReadWindowS of each other; a frame naming two rooms resets the
    /// count. Confirmation is room identity only: it never implies a pose.
    /// </summary>
    public sealed class IndoorSignTracker
    {
        public const int ConfirmReads = 2;
        public const float ReadWindowS = 4f;

        IndoorRoomLandmark _candidate;
        int _hits;
        float _lastHitAt;

        public IndoorRoomLandmark Confirmed { get; private set; }

        /// <returns>true when this read confirmed a room different from the previous one.</returns>
        public bool Observe(IndoorRoomMatch match, float nowS)
        {
            if (match == null) return false;
            if (match.Status == IndoorRoomMatchStatus.Ambiguous)
            {
                _candidate = null;
                _hits = 0;
                return false;
            }
            if (match.Status != IndoorRoomMatchStatus.Matched) return false;
            var room = match.Landmark;
            if (_candidate != null && _candidate.Id == room.Id && nowS - _lastHitAt <= ReadWindowS) _hits++;
            else
            {
                _candidate = room;
                _hits = 1;
            }
            _lastHitAt = nowS;
            if (_hits < ConfirmReads || (Confirmed != null && Confirmed.Id == room.Id)) return false;
            Confirmed = room;
            return true;
        }

        /// <summary>The confirmed room's sign was read again within the window.</summary>
        public bool InView(float nowS) =>
            Confirmed != null && _candidate != null && _candidate.Id == Confirmed.Id
            && _hits >= ConfirmReads && nowS - _lastHitAt <= ReadWindowS;
    }
}
