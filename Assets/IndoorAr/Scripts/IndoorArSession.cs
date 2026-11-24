using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorArState { Init, Auth, Localizing, Pinned, Arrived, Done }

    /// <summary>
    /// Door verifier state machine. Pure C# so EditMode tests can drive it:
    /// IndoorArRunner feeds it AR, MultiSet and frame-time events and delivers
    /// ResultJson once State is Done.
    /// </summary>
    public sealed class IndoorArSession
    {
        public const float StartupTimeoutS = 20f;
        public const float LocalizeTimeoutS = 30f;
        // Retry after this delay or at the existing timeout, whichever comes first.
        public const float NoPoseRetryS = 3f;
        public const float MinEyeHeightM = 1f;
        public const float MaxEyeHeightM = 2.2f;

        float _elapsedS;
        float _localizingS;
        float _retryAtS = LocalizeTimeoutS;
        float _startupS;
        bool _retried;
        bool _noPose;
        float? _confidence;
        float _arrivalDistance;
        // Supported floor of the accepted pose while tracking stays continuous; 0 until sampled.
        int _trackedFloor;
        bool _signSuperseded;
        readonly bool _allowSignRecovery;
        // The user's confirmed floor, trusted only with settled unchanged pressure. Weaker than a sign: it lapses when a
        // sign is read, tracked motion or pressure shows another level, or two attempts in a row land on another floor.
        bool _startSuperseded;
        int _lastConflictQueries = -1;
        int _startConflicts;
        const int MaxStartConflicts = 2;

        public IndoorArSession(IndoorArRequest request, string buildMapCode, bool allowSignRecovery = false)
        {
            Request = request;
            _allowSignRecovery = allowSignRecovery;
            if (!IndoorArContract.CheckMap(request, buildMapCode))
            {
                Finish(IndoorArContract.Error(IndoorArContract.MapMismatch, 0, 0));
                return;
            }
            var pressure = request.pressureSinceStart;
            if (request.floorEvidenceVersion == IndoorArContract.FloorEvidenceVersion && pressure != null
                && int.TryParse(request.startFloor, out var floor) && floor > 0)
            {
                PressureSamplingAllowed = true;
                if (pressure.settled && !pressure.moved
                    && request.startFloorAgeS is >= 0f and <= IndoorFloorBarometer.MaxAnchorAgeS
                    && pressure.sampleAgeS is >= 0f and <= IndoorFloorBarometer.MaxSampleGapS)
                    StartFloor = floor;
            }
        }

        /// <summary>Raised when the localizer must start a localization attempt.</summary>
        public event Action LocalizeRequested;
        public event Action FallbackStopped;

        public IndoorArRequest Request { get; }
        internal bool PressureSamplingAllowed { get; }
        public IndoorArState State { get; private set; } = IndoorArState.Init;
        public int Queries { get; private set; }
        public bool WasPinned { get; private set; }
        public bool FallbackExpired { get; private set; }
        public string ResultJson { get; private set; }
        /// <summary>Last room sign confirmed by OCR. Room identity only; never a camera pose.</summary>
        public IndoorRoomLandmark SignRoom { get; private set; }
        public bool ArrivedBySign { get; private set; }
        /// <summary>
        /// SignRoom while it is still evidence of the user's floor: null once continuously tracked motion from an
        /// accepted pose reached another supported floor, until a sign is confirmed again.
        /// </summary>
        public IndoorRoomLandmark FloorSign => _signSuperseded ? null : SignRoom;
        /// <summary>Relative pressure since the last sign. Only ever releases a stale sign, never names a floor.</summary>
        IndoorFloorBarometer Barometer { get; } = new IndoorFloorBarometer();

        /// <summary>Confirmed starting floor the host vouched for with settled, unchanged pressure; 0 when absent or distrusted.</summary>
        int StartFloor { get; }
        /// <summary>StartFloor while it is still evidence of the user's floor, else 0. A sign takes precedence.</summary>
        public int TrustedStartFloor => _startSuperseded || FloorSign != null ? 0 : StartFloor;

        long ElapsedMs => (long)(_elapsedS * 1000f);

        public void OnArSupport(bool supported)
        {
            if (State != IndoorArState.Init) return;
            if (supported)
            {
                State = IndoorArState.Auth;
                _startupS = 0f;
            }
            else Finish(IndoorArContract.Error(IndoorArContract.ArcoreUnsupported, Queries, ElapsedMs));
        }

        public void OnAuthenticated()
        {
            if (State == IndoorArState.Auth) StartLocalizing();
        }

        public void OnAuthFailed(bool offline)
        {
            if (State != IndoorArState.Auth) return;
            var code = offline ? IndoorArContract.Network : IndoorArContract.MultisetAuth;
            Finish(IndoorArContract.Error(code, Queries, ElapsedMs));
        }

        public void OnAttemptStarted()
        {
            // localizationQueries counts attempts: an upper bound when capture/blur/offline checks block a VPS call.
            if (State == IndoorArState.Localizing) Queries++;
        }

        public void OnLocalized(float? confidence = null)
        {
            if (State != IndoorArState.Localizing) return;
            State = IndoorArState.Pinned;
            _confidence = confidence;
            WasPinned = true;
        }

        public int CurrentFloor(float cameraY, IReadOnlyList<(int Floor, float Y)> floors)
        {
            if (FloorSign == null) return Mathf.Max(0, PoseFloor(cameraY, floors));
            var signFloor = FloorSign.Floor;
            foreach (var (floor, _) in floors)
                if (floor == signFloor) return floor;
            return 0;
        }

        public bool PoseConflictsWithSign(float cameraY, IReadOnlyList<(int Floor, float Y)> floors)
        {
            var poseFloor = PoseFloor(cameraY, floors);
            return poseFloor <= 0 || FloorSign != null && FloorSign.Floor > 0 && poseFloor != FloorSign.Floor
                || FloorSign == null && TrustedStartFloor > 0 && poseFloor != TrustedStartFloor;
        }

        /// <summary>The unique floor in the camera's eye-height band. Gaps and overlaps give 0, outside gives -1.</summary>
        public int PoseFloor(float cameraY, IReadOnlyList<(int Floor, float Y)> floors)
        {
            var match = 0;
            var low = float.PositiveInfinity;
            var high = float.NegativeInfinity;
            foreach (var (floor, floorY) in floors)
            {
                low = Mathf.Min(low, floorY);
                high = Mathf.Max(high, floorY);
                var eyeHeight = cameraY - floorY;
                if (eyeHeight >= MinEyeHeightM && eyeHeight <= MaxEyeHeightM)
                {
                    if (match != 0) return 0;
                    match = floor;
                }
            }
            if (match == 0 && floors.Count > 0 && (cameraY < low + MinEyeHeightM || cameraY > high + MaxEyeHeightM))
                return -1;
            return match;
        }

        /// <summary>
        /// Camera height under the accepted pose while tracking holds. Walking it to another supported floor supersedes
        /// the last sign's floor; between floors, outside them or across a tracking gap nothing changes.
        /// </summary>
        public void OnTrackedPose(float cameraY, IReadOnlyList<(int Floor, float Y)> floors)
        {
            if (State != IndoorArState.Pinned) return;
            var floor = PoseFloor(cameraY, floors);
            if (floor <= 0) return;
            if (_trackedFloor > 0 && floor != _trackedFloor)
            {
                if (FloorSign?.Floor != floor) _signSuperseded = true;
                if (StartFloor != floor) _startSuperseded = true;
            }
            _trackedFloor = floor;
        }

        /// <summary>
        /// One pressure reading from the host (hPa, any consistent offset). Settled pressure that moved since the sign
        /// was anchored supersedes the sign's floor like a tracked walk to another floor does. No sensor, a gap,
        /// unsettled pressure or an old anchor change nothing, so the existing floor policy stays in force.
        /// </summary>
        public void OnPressure(float hpa)
        {
            if (State == IndoorArState.Done) return;
            Barometer.Sample(hpa, _elapsedS);
            if (StartFloor > 0 && !_startSuperseded && SignRoom == null)
            {
                // Pressure at launch is the baseline for the host's "unchanged" claim; retry until it is settled.
                if (!Barometer.HasAnchor) Barometer.Anchor(_elapsedS);
                else if (Barometer.Status(_elapsedS) == IndoorBarometerStatus.Moved) _startSuperseded = true;
            }
            if (SignRoom != null && !_signSuperseded
                && Barometer.Status(_elapsedS) == IndoorBarometerStatus.Moved) _signSuperseded = true;
        }

        /// <summary>
        /// A MultiSet pose landed on a floor other than the trusted start floor. Once two separate attempts do, the start
        /// floor is dropped and today's behaviour returns, so a wrong confirmation costs two retries, not the session.
        /// </summary>
        public void NoteStartFloorConflict()
        {
            if (TrustedStartFloor == 0 || Queries == _lastConflictQueries) return;
            _lastConflictQueries = Queries;
            if (++_startConflicts >= MaxStartConflicts) _startSuperseded = true;
        }

        public void OnStepFreeUnverified() =>
            Finish(IndoorArContract.Unavailable(Queries, ElapsedMs, IndoorArContract.StepFreeUnverified));

        public void OnNotLocalized()
        {
            if (State != IndoorArState.Localizing) return;
            _noPose = true;
            if (!_retried) _retryAtS = Math.Min(_retryAtS, _localizingS + NoPoseRetryS);
        }

        public void OnSignConfirmed(IndoorRoomLandmark room)
        {
            if (State is IndoorArState.Done or IndoorArState.Arrived || room == null) return;
            SignRoom = room;
            _signSuperseded = false;
            _startSuperseded = true;
            Barometer.Anchor(_elapsedS);
            if (State is not (IndoorArState.Auth or IndoorArState.Localizing or IndoorArState.Pinned)
                || Request.LocalizeOnly
                || !int.TryParse(Request.floor, out var floor) || room.Floor != floor
                || !string.Equals(LiveDoorHud.RoomLabel(Request.doorKey), LiveDoorHud.RoomLabel(room.Id),
                    StringComparison.OrdinalIgnoreCase)) return;
            // The sign identifies the destination itself. A host with the signArrival capability gets no distance;
            // a legacy host still requires the field, so it keeps getting 0. signRoom records the evidence and
            // confidence stays unset because this arrival has no distance measurement.
            _arrivalDistance = 0f;
            _confidence = null;
            ArrivedBySign = true;
            State = IndoorArState.Arrived;
            FallbackStopped?.Invoke();
        }

        /// <summary>
        /// A confirmed sign is in view, so the camera faces a mapped wall. If the open attempt
        /// already answered without a pose, spend the one retry now instead of at the timeout.
        /// </summary>
        public void OnSignInView()
        {
            if (State != IndoorArState.Localizing || FallbackExpired || !_noPose || _retried) return;
            _retried = true;
            _noPose = false;
            _localizingS = 0f;
            LocalizeRequested?.Invoke();
        }

        // Re-localize only after tracking loss, never on a timer.
        public void OnTrackingLost()
        {
            if (State == IndoorArState.Pinned) StartLocalizing();
        }

        public void Tick(float dt)
        {
            if (State == IndoorArState.Done) return;
            _elapsedS += dt;
            if (State == IndoorArState.Init || State == IndoorArState.Auth)
            {
                _startupS += dt;
                if (_startupS >= StartupTimeoutS)
                {
                    // Auth already got past the offline check. Timing out here is an auth failure.
                    var code = State == IndoorArState.Auth
                        ? IndoorArContract.MultisetAuth
                        : IndoorArContract.Network;
                    Finish(IndoorArContract.Error(code, Queries, ElapsedMs));
                }
                return;
            }
            if (State != IndoorArState.Localizing || FallbackExpired) return;
            _localizingS += dt;
            if (_localizingS < (_retried ? LocalizeTimeoutS : _retryAtS)) return;
            if (_retried)
            {
                FallbackExpired = true;
                if (_allowSignRecovery)
                {
                    FallbackStopped?.Invoke();
                    return;
                }
                Finish(IndoorArContract.LocalizationFailed(Queries, ElapsedMs));
                return;
            }
            _retried = true;
            _noPose = false;
            _localizingS = 0f;
            LocalizeRequested?.Invoke();
        }

        /// <param name="cameraInMap">Camera position in the MapSet frame.</param>
        public void ImHere(Vector3 cameraInMap)
        {
            if (State != IndoorArState.Pinned || Request.LocalizeOnly || Request.position == null || Request.stepFree) return;
            _arrivalDistance = HorizontalDistance(cameraInMap);
            State = IndoorArState.Arrived;
        }

        public void ConfirmArrival()
        {
            if (State == IndoorArState.Arrived && ArrivedBySign && Request.SupportsSignArrival)
                Finish(IndoorArContract.ArrivedBySign(Queries, ElapsedMs));
            else if (State == IndoorArState.Arrived)
                Finish(IndoorArContract.Arrived(_arrivalDistance, Queries, ElapsedMs, _confidence));
        }

        public void Retry()
        {
            if (State != IndoorArState.Done || ResultJson == null || !ResultJson.Contains("\"status\":\"localizationFailed\"")) return;
            ResultJson = null;
            FallbackExpired = false;
            StartLocalizing();
        }

        public void Cancel()
        {
            if (State != IndoorArState.Done || LiveDoorHud.LocalizationFailed(this))
                Finish(IndoorArContract.Cancelled(Queries, ElapsedMs));
        }

        public float HorizontalDistance(Vector3 cameraInMap)
        {
            var door = Request.position;
            return new Vector2(cameraInMap.x - door.x, cameraInMap.z - door.z).magnitude;
        }

        public bool IsWrongFloor(int currentFloor) =>
            currentFloor > 0 && int.TryParse(Request.floor, out var floor) && currentFloor != floor;

        void StartLocalizing()
        {
            State = IndoorArState.Localizing;
            _trackedFloor = 0;
            _localizingS = 0f;
            _retried = false;
            _retryAtS = LocalizeTimeoutS;
            _noPose = false;
            LocalizeRequested?.Invoke();
        }

        void Finish(string resultJson)
        {
            ResultJson = IndoorArContract.WithSignRoom(resultJson, SignRoom?.Id);
            State = IndoorArState.Done;
            FallbackStopped?.Invoke();
        }
    }
}
