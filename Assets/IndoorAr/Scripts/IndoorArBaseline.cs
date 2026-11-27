using System.Globalization;

namespace IndoorAr
{
    /// <summary>
    /// Durations and counts for indoor sign and MultiSet localization.
    /// No camera frames, poses, tokens, or user location.
    /// ArrowShownMs records the first HUD update with route guidance (ribbon or arrow), which needs a MultiSet
    /// pose or a validated sign pose.
    /// </summary>
    public sealed class IndoorArBaseline
    {
        long? _openedMs;
        long? _arReadyMs;
        long? _attemptOpenedMs;
        long? _firstPoseMs;
        long? _arrowShownMs;
        long? _signMs;
        int _unansweredClosed;

        public long? ArStartupMs { get; private set; }
        public long? RequestLatencyMs { get; private set; }
        public long? TimeToFirstLocalizationMs { get; private set; }
        public long? ArrowShownMs { get; private set; }
        /// <summary>Session open until OCR first confirms a room sign. Identity only, not a pose.</summary>
        public long? TimeToSignMs { get; private set; }
        /// <summary>Recognition time of the session's first OCR read, whatever it read.</summary>
        public long? OcrLatencyMs { get; private set; }
        /// <summary>Solve time of the session's first sign pose that came back Ok.</summary>
        public long? PoseEstimateUs { get; private set; }
        /// <summary>What gave the first accepted pose: "sign", "multiset", or "na" before one.</summary>
        public string PoseSource { get; private set; } = "na";
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

        public void NotePoseAccepted(long nowMs, bool fromSigns = false)
        {
            if (_openedMs == null || _firstPoseMs != null) return;
            _firstPoseMs = nowMs;
            PoseSource = fromSigns ? "sign" : "multiset";
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

        public void NoteSignConfirmed(long nowMs)
        {
            if (_openedMs == null || _signMs != null) return;
            _signMs = nowMs;
            TimeToSignMs = nowMs - _openedMs.Value;
        }

        public void NoteOcrRead(long latencyMs)
        {
            if (_openedMs == null || OcrLatencyMs != null) return;
            OcrLatencyMs = latencyMs;
        }

        public void NotePoseEstimate(long solveUs)
        {
            if (_openedMs == null || PoseEstimateUs != null) return;
            PoseEstimateUs = solveUs;
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
            + " unansweredAttempts=" + UnansweredAttempts.ToString(CultureInfo.InvariantCulture)
            + " " + Field("timeToSignMs", TimeToSignMs)
            + " poseSource=" + PoseSource
            + " " + Field("ocrLatencyMs", OcrLatencyMs)
            + " " + Field("poseEstimateUs", PoseEstimateUs);

        static string Field(string name, long? value) =>
            name + "=" + (value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "na");
    }
}
