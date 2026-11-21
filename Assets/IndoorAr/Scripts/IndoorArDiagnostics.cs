using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>Why the route arrow is hidden while the session is pinned. A fixed enum, never a cell or a position.</summary>
    public enum IndoorRouteBlockedCause { None, NoRoute, CameraCellBlocked, NoClearLine }

    /// <summary>How the door pin is drawn: the 3D world pin, a screen pin docked to the viewport edge, or a projected screen pin outside the depth range.</summary>
    public enum IndoorPinMode { World, Docked, Clipped }

    /// <summary>Where the door projects relative to the camera viewport.</summary>
    public enum IndoorPinViewClass { InView, OffScreen, Behind }

    /// <summary>
    /// Fixed-label diagnostic lines for the on-site AR runs (DX1 to DX7). Builders use counts, categories,
    /// measurements and hashed map identity without including a position, image, key, token or recognized text.
    /// Every value matches [A-Za-z0-9._-]{1,40} and every key is a plain word, so the capture filter keeps it.
    /// </summary>
    public static class IndoorArDiagnostics
    {
        public const int MaxXrDevices = 8;
        const int MaxValue = 40;

        public static string Token(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "none";
            var text = new StringBuilder();
            foreach (var c in raw)
            {
                var plain = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-';
                if (plain) text.Append(c);
                else if (text.Length > 0 && text[text.Length - 1] != '.') text.Append('.');
                if (text.Length >= MaxValue) break;
            }
            var token = text.ToString().Trim('.');
            return token.Length == 0 ? "none" : token;
        }

        static string Bool(bool value) => value ? "true" : "false";

        static string Number(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        // ---- DX1: XR input at loader start and 1 s later -------------------------------------------------------

        /// <summary>What a legacy XR InputDevice reports without a pose value.</summary>
        public readonly struct XrDeviceInfo
        {
            public readonly bool IsValid;
            public readonly bool CenterEyePosition, DevicePosition, CenterEyeRotation, DeviceRotation;

            public XrDeviceInfo(bool isValid, bool centerEyePosition,
                bool devicePosition, bool centerEyeRotation, bool deviceRotation)
            {
                IsValid = isValid;
                CenterEyePosition = centerEyePosition;
                DevicePosition = devicePosition;
                CenterEyeRotation = centerEyeRotation;
                DeviceRotation = deviceRotation;
            }
        }

        /// <param name="when">"start" or "1s".</param>
        /// <param name="positionControls">Controls the pose driver's position action resolves to, or -1 without a driver.</param>
        public static List<string> XrInputLines(string when, IReadOnlyList<XrDeviceInfo> devices,
            int inputSystemDevices, int inputSystemAdded, int inputSystemRemoved,
            int positionControls, int rotationControls)
        {
            var lines = new List<string>
            {
                $"[ArDiag] xr input at={(when == "1s" ? "1s" : "start")} legacyDevices={devices.Count} inputSystemDevices={inputSystemDevices}"
                + $" inputSystemAdded={inputSystemAdded} inputSystemRemoved={inputSystemRemoved}"
                + $" positionControls={(positionControls < 0 ? "none" : positionControls.ToString(CultureInfo.InvariantCulture))}"
                + $" rotationControls={(rotationControls < 0 ? "none" : rotationControls.ToString(CultureInfo.InvariantCulture))}"
            };
            for (var i = 0; i < devices.Count && i < MaxXrDevices; i++)
            {
                var d = devices[i];
                lines.Add($"[ArDiag] xr device at={(when == "1s" ? "1s" : "start")} index={i}"
                    + $" valid={Bool(d.IsValid)} centerEyePos={Bool(d.CenterEyePosition)} devicePos={Bool(d.DevicePosition)}"
                    + $" centerEyeRot={Bool(d.CenterEyeRotation)} deviceRot={Bool(d.DeviceRotation)}");
            }
            return lines;
        }

        // ---- DX2: frozen camera --------------------------------------------------------------------------------

        public static string CameraFrozenLine(bool frozen, long stillMs, int frames) =>
            $"[IndoorAr] camera frozen={Bool(frozen)} stillMs={stillMs} frames={frames}";

        // ---- DX3: floor transitions ----------------------------------------------------------------------------

        static string FloorName(int floor) => floor > 0 ? floor.ToString(CultureInfo.InvariantCulture) : "unknown";

        /// <summary>
        /// The eye height above the reported floor, in 0.5 m buckets ("1.5-2.0").
        /// A height, not a position: it says nothing about where in the building the camera is.
        /// </summary>
        public static string EyeHeightBucket(float cameraY, int floor, IReadOnlyList<(int Floor, float Y)> floors)
        {
            var floorY = float.NegativeInfinity;
            foreach (var (number, y) in floors)
                if (floor > 0 && number == floor) { floorY = y; break; }
            if (float.IsNegativeInfinity(floorY)) return "unknown";
            var height = cameraY - floorY;
            if (height < 0f) return "below";
            if (height >= 4f) return "4.0-up";
            var low = Mathf.Floor(height / .5f) * .5f;
            return Number(low, "0.0") + "-" + Number(low + .5f, "0.0");
        }

        public static string FloorChangeLine(int from, int to, string eyeBucket) =>
            $"[IndoorAr] floor change from={FloorName(from)} to={FloorName(to)} eyeM={Token(eyeBucket)}";

        // ---- DX7: localization result --------------------------------------------------------------------------

        /// <summary>Eight hex digits of SHA-256 over the map codes, so two results can be tied to one map without the code.</summary>
        public static string MapHash(string mapCodes)
        {
            if (string.IsNullOrEmpty(mapCodes)) return "unavailable";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mapCodes));
            return System.BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
        }

        /// <param name="mapHash">MapHash output; sign poses and legacy SDK callbacks have no map identity and use "unavailable".</param>
        /// <param name="floor">The floor the pose height falls in; 0 for unknown, -1 for outside the supported floors.</param>
        public static string LocalizationResultLine(string mapHash, int floor, float cameraY,
            IReadOnlyList<(int Floor, float Y)> floors, float? confidence)
        {
            var floorY = float.NaN;
            foreach (var (number, y) in floors)
                if (floor > 0 && number == floor) { floorY = y; break; }
            var delta = float.IsNaN(floorY) ? "unknown" : Number(cameraY - floorY, "0.00");
            var floorName = floor < 0 ? "unsupported" : FloorName(floor);
            return $"[IndoorAr] localization result map={Token(mapHash)} floor={floorName} deltaM={delta}"
                + $" confidence={(confidence.HasValue ? Number(confidence.Value, "0.00") : "unknown")}";
        }

        // ---- DX4: Limited tracking episodes --------------------------------------------------------------------

        public static string TrackingLimitedBeginLine(string notTrackingReason) =>
            $"[IndoorAr] tracking limited phase=begin notTracking={Token(notTrackingReason)}";

        public static string TrackingLimitedEndLine(long durationMs, bool relocalize, string notTrackingReason, LimitedEpisodeEnd end) =>
            $"[IndoorAr] tracking limited phase=end durationMs={durationMs} relocalize={Bool(relocalize)} notTracking={Token(notTrackingReason)} end={end}";

        // ---- IT10a: per-request localization outcome and timing ------------------------------------------------

        /// <summary>Kind of one localization request. Signs are the OCR hold; MultiSet is the VPS request that owns the process lock.</summary>
        public enum LocalizationRequestKind { Sign, MultiSet }

        /// <summary>How one request ended. Timing alone does not identify the cause of a delay.</summary>
        public enum LocalizationOutcome { Ok, GenericFailure, Rejected, Timeout, Cancelled, Superseded, Late, Ignored }

        /// <param name="lockBusy">Another request held the process-wide lock or preceded this request in its queue; null for signs.</param>
        /// <param name="lockWaitMs">Time queued behind the lock; null for signs.</param>
        /// <param name="sdkMs">Time the SDK call was in flight; null for signs.</param>
        public static string LocalizationRequestLine(int attempt, LocalizationRequestKind kind, long elapsedMs,
            LocalizationOutcome outcome, bool? lockBusy, long? lockWaitMs, long? sdkMs) =>
            $"[IndoorAr] localization request attempt={attempt} kind={kind} elapsedMs={elapsedMs} outcome={outcome}"
            + $" lockBusy={(lockBusy.HasValue ? Bool(lockBusy.Value) : "na")}"
            + $" lockWaitMs={(lockWaitMs.HasValue ? lockWaitMs.Value.ToString(CultureInfo.InvariantCulture) : "na")}"
            + $" sdkMs={(sdkMs.HasValue ? sdkMs.Value.ToString(CultureInfo.InvariantCulture) : "na")}";

        // ---- DX5: waypoint churn and route blocked -------------------------------------------------------------

        public static string WaypointChurnLine(int changesPerS) => $"[IndoorAr] route churn changesPerS={changesPerS}";

        public static string RouteBlockedLine(bool blocked, IndoorRouteBlockedCause cause) =>
            $"[IndoorAr] route blocked={Bool(blocked)} cause={cause}";

        // ---- Pin mode transitions (#85) ------------------------------------------------------------------------

        /// <summary>Straight-line camera-to-pin distance as a coarse bucket, never the distance itself.</summary>
        public static string PinDistanceBucket(float meters)
        {
            if (float.IsNaN(meters)) return "unknown";
            if (meters < 1f) return "0-1";
            if (meters < 3f) return "1-3";
            if (meters < 6f) return "3-6";
            if (meters < 10f) return "6-10";
            return meters < 20f ? "10-20" : "20-up";
        }

        public static string PinModeLine(IndoorPinMode mode, float distanceM, IndoorPinViewClass view) =>
            $"[IndoorAr] pin mode={mode} distM={PinDistanceBucket(distanceM)} view={view}";

        // ---- DX6: re-pin jump ----------------------------------------------------------------------------------

        public static string RepinJumpLine(float shiftM, float turnDeg) =>
            $"[IndoorAr] re-pin jump shiftM={Number(shiftM, "0.00")} turnDeg={Number(turnDeg, "0.0")}";
    }

    /// <summary>
    /// DX2. The camera is frozen when frames keep arriving and the session tracks, yet the camera transform has not
    /// changed for FrozenAfterS. Frames that stop arriving are a different fault and never count as frozen.
    /// shortcut: the move thresholds are not measured on a phone resting on a table; raise them if that reads as frozen.
    /// </summary>
    public sealed class FrozenCameraDetector
    {
        public const float FrozenAfterS = 2f;
        public const float FrameGapS = .5f;
        public const float MoveEpsilonM = 1e-6f;
        public const float TurnEpsilonDeg = .01f;

        Vector3 _anchorPosition;
        Quaternion _anchorRotation;
        bool _anchored;
        int _lastFrames;
        float _noFrameS, _stillS;

        public bool Frozen { get; private set; }
        public long StillMs => (long)(_stillS * 1000f);

        /// <summary>True when Frozen changed on this call.</summary>
        public bool Observe(float dt, Vector3 position, Quaternion rotation, int frames, bool tracking)
        {
            var gap = _noFrameS + dt >= FrameGapS;
            _noFrameS = frames != _lastFrames ? 0f : _noFrameS + dt;
            _lastFrames = frames;
            if (!tracking || gap || _noFrameS >= FrameGapS || !_anchored || Vector3.Distance(position, _anchorPosition) > MoveEpsilonM
                || Quaternion.Angle(rotation, _anchorRotation) > TurnEpsilonDeg)
            {
                _anchorPosition = position;
                _anchorRotation = rotation;
                _anchored = true;
                _stillS = 0f;
            }
            else _stillS += dt;
            var frozen = tracking && _noFrameS < FrameGapS && _stillS >= FrozenAfterS;
            if (frozen == Frozen) return false;
            Frozen = frozen;
            return true;
        }

        public void Reset()
        {
            Frozen = false;
            _anchored = false;
            _lastFrames = 0;
            _noFrameS = _stillS = 0f;
        }
    }

    /// <summary>DX3. Reports a change of the floor the session reads, never the first reading.</summary>
    public sealed class FloorTransitionTracker
    {
        int? _floor;

        public bool Observe(int floor, out int from)
        {
            from = _floor ?? floor;
            var changed = _floor != null && _floor != floor;
            _floor = floor;
            return changed;
        }

        public void Reset() => _floor = null;
    }

    public enum LimitedEpisodePhase { None, Begin, End }
    public enum LimitedEpisodeEnd { TrackingRestored, SessionEnded, SessionReplaced, Destroyed }

    /// <summary>
    /// DX4. A Limited episode starts when tracking drops while pinned and ends on recovery or a terminal boundary.
    /// It records how long it lasted and whether the runner started a re-localization during it.
    /// </summary>
    public sealed class LimitedEpisodeTracker
    {
        float _startedAt, _durationS;

        public bool Active { get; private set; }
        public bool Relocalized { get; private set; }
        public long DurationMs => (long)(_durationS * 1000f);

        public LimitedEpisodePhase Observe(bool tracking, bool pinned, bool relocalizationStarted, float now)
        {
            if (Active)
            {
                _durationS = Mathf.Max(0f, now - _startedAt);
                Relocalized |= relocalizationStarted;
            }
            if (tracking)
            {
                if (!Active) return LimitedEpisodePhase.None;
                Active = false;
                return LimitedEpisodePhase.End;
            }
            if (!Active)
            {
                if (!pinned) return LimitedEpisodePhase.None;
                Active = true;
                Relocalized = relocalizationStarted;
                _startedAt = now;
                _durationS = 0f;
                return LimitedEpisodePhase.Begin;
            }
            return LimitedEpisodePhase.None;
        }

        public void End(float now)
        {
            if (Active) _durationS = Mathf.Max(0f, now - _startedAt);
            Active = false;
        }

        public void Reset()
        {
            Active = false;
            Relocalized = false;
            _durationS = 0f;
        }
    }

    /// <summary>DX5. Counts changes of the waypoint index the arrow aims at, per second.</summary>
    public sealed class WaypointChurnMeter
    {
        public const float WindowS = 1f;
        int _index = -1;
        int _changes;
        float _windowS;

        /// <summary>True once per window that saw at least one change; changesPerS is that count.</summary>
        public bool Observe(int index, float dt, out int changesPerS)
        {
            changesPerS = 0;
            if (index >= 0 && _index >= 0 && index != _index) _changes++;
            _index = index;
            _windowS += dt;
            if (_windowS < WindowS) return false;
            changesPerS = _changes;
            _changes = 0;
            _windowS = 0f;
            return changesPerS > 0;
        }

        public void Reset()
        {
            _index = -1;
            _changes = 0;
            _windowS = 0f;
        }
    }

    /// <summary>DX5. Reports a change of blocked state, or of cause while blocked.</summary>
    public sealed class RouteBlockTracker
    {
        bool _blocked;
        IndoorRouteBlockedCause _cause;

        public bool Observe(bool blocked, IndoorRouteBlockedCause cause)
        {
            if (!blocked) cause = IndoorRouteBlockedCause.None;
            var changed = blocked != _blocked || cause != _cause;
            _blocked = blocked;
            _cause = cause;
            return changed;
        }

        public void Reset()
        {
            _blocked = false;
            _cause = IndoorRouteBlockedCause.None;
        }
    }
}
