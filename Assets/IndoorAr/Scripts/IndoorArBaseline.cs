using System.Globalization;

namespace IndoorAr
{
    /// <summary>
    /// Durations and counts for the MultiSet localization baseline.
    /// No camera frames, poses, tokens, or user location.
    /// ArrowShownMs is when the HUD first enters the guide-arrow state, not a rendered-frame or real-world alignment check.
    /// </summary>
    public sealed class IndoorArBaseline
    {
        long? _openedMs;
        long? _arReadyMs;
        long? _attemptOpenedMs;
        long? _firstPoseMs;
        long? _arrowShownMs;
        int _unansweredClosed;

        public long? ArStartupMs { get; private set; }
        public long? RequestLatencyMs { get; private set; }
        public long? TimeToFirstLocalizationMs { get; private set; }
        public long? ArrowShownMs { get; private set; }
        public bool ArrowAlignmentMeasured => false;
        public int Attempts { get; private set; }
        public int Retries { get; private set; }
        public int TrackingLosses { get; private set; }
        public int UnansweredAttempts => _unansweredClosed + (_attemptOpenedMs != null ? 1 : 0);

        public void NoteSessionOpened(long nowMs)
        {
            if (_openedMs != null) return;
            _openedMs = nowMs;
        }

        public void NoteArReady(long nowMs)
        {
            if (_openedMs == null || _arReadyMs != null) return;
            _arReadyMs = nowMs;
            ArStartupMs = nowMs - _openedMs.Value;
        }

        public void NoteAttemptStarted(long nowMs)
        {
            if (_openedMs == null) return;
            if (_attemptOpenedMs != null) _unansweredClosed++;
            _attemptOpenedMs = nowMs;
            Attempts++;
            if (Attempts > 1) Retries++;
        }

        public void NotePoseAccepted(long nowMs)
        {
            if (_openedMs == null || _firstPoseMs != null) return;
            _firstPoseMs = nowMs;
            TimeToFirstLocalizationMs = nowMs - _openedMs.Value;
            if (_attemptOpenedMs == null) return;
            RequestLatencyMs = nowMs - _attemptOpenedMs.Value;
            _attemptOpenedMs = null;
        }

        public void NoteArrowShown(long nowMs)
        {
            if (_openedMs == null || _arrowShownMs != null) return;
            _arrowShownMs = nowMs;
            ArrowShownMs = nowMs - _openedMs.Value;
        }

        public void NoteTrackingLost()
        {
            if (_openedMs == null) return;
            TrackingLosses++;
        }

        public string LogLine() =>
            "[IndoorArBaseline] "
            + Field("arStartupMs", ArStartupMs) + " "
            + Field("requestLatencyMs", RequestLatencyMs) + " "
            + Field("timeToFirstLocalizationMs", TimeToFirstLocalizationMs) + " "
            + Field("arrowShownMs", ArrowShownMs)
            + " arrowAlignment=unmeasured"
            + " attempts=" + Attempts.ToString(CultureInfo.InvariantCulture)
            + " retries=" + Retries.ToString(CultureInfo.InvariantCulture)
            + " trackingLosses=" + TrackingLosses.ToString(CultureInfo.InvariantCulture)
            + " unansweredAttempts=" + UnansweredAttempts.ToString(CultureInfo.InvariantCulture);

        static string Field(string name, long? value) =>
            name + "=" + (value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "na");
    }
}
