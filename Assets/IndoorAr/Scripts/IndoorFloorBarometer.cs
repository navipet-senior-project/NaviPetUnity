using System.Collections.Generic;

namespace IndoorAr
{
    public enum IndoorBarometerStatus { Unanchored, Unknown, Unchanged, Moved }

    /// <summary>
    /// Compares settled pressure to a launch or sign anchor. Pressure is relative, so
    /// the only claim is "settled pressure moved at least MoveHpa from the settled pressure at the anchor".
    /// When evidence is missing or unsettled, callers keep their existing floor policy.
    /// Pure C# so EditMode tests can drive it. Any consistent hPa series works; only differences are used.
    /// Constants come from one iPhone in one building on one afternoon (stairs and elevator, seated and standing,
    /// 5 floors): a storey step was 0.415-0.53 hPa, the worst single same-floor error 0.225 hPa, and revisit drift
    /// about 0.3 hPa/h over at most 20 min. They are not calibrated for other devices, buildings or days.
    /// </summary>
    public sealed class IndoorFloorBarometer
    {
        /// <summary>Seconds of steady pressure before a level is trusted. Arrival settling took up to about 45 s.</summary>
        public const float SettleWindowS = 20f;
        /// <summary>Largest peak-to-peak spread in that window. Settled holds measured 0.02-0.06 hPa.</summary>
        public const float SettleRangeHpa = 0.06f;
        /// <summary>
        /// Settled change that counts as a level change: above the worst same-floor error seen (0.225), below the
        /// smallest storey step (0.415). A real step hidden by opposing posture and drift error is missed on purpose.
        /// </summary>
        public const float MoveHpa = (float)WireMoveHpa;
        internal const double WireMoveHpa = 0.30;
        /// <summary>Drift was only observed over 20 min, so an older anchor is not compared.</summary>
        public const float MaxAnchorAgeS = 1200f;
        /// <summary>Samples arrived about every 1.07 s; a longer silence is sensor loss.</summary>
        public const float MaxSampleGapS = 10f;
        /// <summary>Readings closer than this are dropped, so a 60 Hz feed costs the same as 1 Hz.</summary>
        public const float MinSpacingS = 0.5f;
        public const int MinSamples = 8;
        /// <summary>
        /// Settled pressure change per storey. Measured segment-to-segment steps ran 0.40-0.59 hPa (stairs and elevator,
        /// up and down); the slack below covers posture shifts and drift on top. One device, one building.
        /// </summary>
        const int StepMinHundredths = 40;
        const int StepMaxHundredths = 55;
        const int StepSlackHundredths = 10;
        /// <summary>Largest storey count tried; the calibration building has 5 floors.</summary>
        public const int MaxStoreys = 4;
        /// <summary>Larger settled changes than this are rejected as a sensor or unit error, not a floor change.</summary>
        public const float MaxDeltaHpa = 3f;

        readonly List<(double T, float Hpa)> _window = new List<(double, float)>();
        float? _baseline;
        double _anchoredAtS;

        public bool HasAnchor => _baseline != null;

        public void Sample(float hpa, double capturedAtS)
        {
            if (!float.IsFinite(hpa) || !double.IsFinite(capturedAtS)) return;
            if (_window.Count > 0)
            {
                var last = _window[_window.Count - 1].T;
                if (capturedAtS < last || capturedAtS - last > MaxSampleGapS) _window.Clear();
                else if (capturedAtS - last < MinSpacingS) return;
            }
            _window.Add((capturedAtS, hpa));
            _window.RemoveAll(s => s.T < capturedAtS - SettleWindowS);
        }

        /// <summary>
        /// Starts a new comparison from the current settled level, dropping any older one. If pressure is not settled
        /// there is no anchor: a level taken mid-travel would be wrong. With keepIfUnsettled, an unsettled reading leaves
        /// a good baseline alone, for a re-read of the sign the baseline already belongs to.
        /// </summary>
        public void Anchor(double nowS, bool keepIfUnsettled = false)
        {
            var level = SettledLevel(nowS);
            if (level == null && keepIfUnsettled && _baseline != null) return;
            _baseline = level;
            _anchoredAtS = nowS;
        }

        public IndoorBarometerStatus Status(double nowS)
        {
            if (_baseline == null) return IndoorBarometerStatus.Unanchored;
            if (nowS - _anchoredAtS > MaxAnchorAgeS) return IndoorBarometerStatus.Unknown;
            var level = SettledLevel(nowS);
            if (level == null) return IndoorBarometerStatus.Unknown;
            return System.Math.Abs(level.Value - _baseline.Value) >= MoveHpa
                ? IndoorBarometerStatus.Moved
                : IndoorBarometerStatus.Unchanged;
        }

        /// <summary>
        /// Returns a signed offset only when the settled pressure change fits exactly one storey count.
        /// Positive is up (pressure falls going up).
        /// </summary>
        public static bool TryStoreyOffset(double deltaHpa, out int offset)
        {
            offset = 0;
            var change = System.Math.Abs(deltaHpa);
            if (!double.IsFinite(deltaHpa) || change < WireMoveHpa || change > MaxDeltaHpa) return false;
            var count = 0;
            for (var k = 1; k <= MaxStoreys; k++)
            {
                if (change < (k * StepMinHundredths - StepSlackHundredths) / 100.0
                    || change > (k * StepMaxHundredths + StepSlackHundredths) / 100.0) continue;
                if (count != 0) return false;
                count = k;
            }
            offset = deltaHpa < 0 ? count : -count;
            return count != 0;
        }

        float? SettledLevel(double nowS)
        {
            var cutoff = nowS - SettleWindowS;
            var first = _window.FindIndex(sample => sample.T >= cutoff);
            if (first < 0 || _window.Count - first < MinSamples) return null;
            var last = _window[_window.Count - 1].T;
            if (last > nowS || last - _window[first].T < SettleWindowS - 2f) return null;
            var values = new List<float>(_window.Count - first);
            for (var i = first; i < _window.Count; i++) values.Add(_window[i].Hpa);
            values.Sort();
            if (values[values.Count - 1] - values[0] > SettleRangeHpa) return null;
            var mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2f;
        }
    }
}
