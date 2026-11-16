using System;
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
        public const float MinEyeHeightM = 1f;
        public const float MaxEyeHeightM = 2.2f;

        float _elapsedS;
        float _localizingS;
        float _startupS;
        bool _retried;
        bool _noPose;
        float? _confidence;
        float _arrivalDistance;
        // Supported floor of the accepted pose while tracking stays continuous; 0 until sampled.
        int _trackedFloor;
        bool _signSuperseded;
        readonly bool _allowSignRecovery;

        public IndoorArSession(IndoorArRequest request, string buildMapCode, bool allowSignRecovery = false)
        {
            Request = request;
            _allowSignRecovery = allowSignRecovery;
            if (!IndoorArContract.CheckMap(request, buildMapCode))
                Finish(IndoorArContract.Error(IndoorArContract.MapMismatch, 0, 0));
        }

        /// <summary>Raised when the localizer must start a localization attempt.</summary>
        public event Action LocalizeRequested;
        public event Action FallbackStopped;

        public IndoorArRequest Request { get; }
        public IndoorArState State { get; private set; } = IndoorArState.Init;
        public int Queries { get; private set; }
        public bool WasPinned { get; private set; }
        public bool FallbackExpired { get; private set; }
        public string ResultJson { get; private set; }
        /// <summary>Last room sign confirmed by OCR. Room identity only; never a camera pose.</summary>
        public IndoorRoomLandmark SignRoom { get; private set; }
        /// <summary>
        /// SignRoom while it is still evidence of the user's floor: null once continuously tracked motion from an
        /// accepted pose reached another supported floor, until a sign is confirmed again.
        /// </summary>
        public IndoorRoomLandmark FloorSign => _signSuperseded ? null : SignRoom;

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

        public int CurrentFloor(float cameraY, float floor1Y, float floor2Y)
        {
            if (FloorSign != null) return FloorSign.Floor is 1 or 2 ? FloorSign.Floor : 0;
            if (!ValidFloorHeights(floor1Y, floor2Y)) return 0;
            var floor1 = cameraY - floor1Y;
            var floor2 = cameraY - floor2Y;
            var on1 = floor1 >= MinEyeHeightM && floor1 <= MaxEyeHeightM;
            var on2 = floor2 >= MinEyeHeightM && floor2 <= MaxEyeHeightM;
            return on1 == on2 ? 0 : on1 ? 1 : 2;
        }

        public bool PoseConflictsWithSign(float cameraY, float floor1Y, float floor2Y)
        {
            var poseFloor = CurrentPoseFloor(cameraY, floor1Y, floor2Y);
            if (poseFloor <= 0) return true;
            return FloorSign != null && FloorSign.Floor > 0
                && poseFloor != FloorSign.Floor;
        }

        public int PoseFloor(float cameraY, float floor1Y, float floor2Y) =>
            CurrentPoseFloor(cameraY, floor1Y, floor2Y);

        /// <summary>
        /// Camera height under the accepted pose while tracking holds. Walking it to another supported floor supersedes
        /// the last sign's floor; between floors, outside them or across a tracking gap nothing changes.
        /// </summary>
        public void OnTrackedPose(float cameraY, float floor1Y, float floor2Y)
        {
            if (State != IndoorArState.Pinned) return;
            var floor = CurrentPoseFloor(cameraY, floor1Y, floor2Y);
            if (floor <= 0) return;
            if (_trackedFloor > 0 && floor != _trackedFloor && FloorSign?.Floor != floor) _signSuperseded = true;
            _trackedFloor = floor;
        }

        int CurrentPoseFloor(float cameraY, float floor1Y, float floor2Y)
        {
            if (!ValidFloorHeights(floor1Y, floor2Y)) return 0;
            var floor1 = cameraY - floor1Y;
            var floor2 = cameraY - floor2Y;
            var on1 = floor1 >= MinEyeHeightM && floor1 <= MaxEyeHeightM;
            var on2 = floor2 >= MinEyeHeightM && floor2 <= MaxEyeHeightM;
            if (on1 != on2) return on1 ? 1 : 2;
            return cameraY < floor1Y + MinEyeHeightM || cameraY > floor2Y + MaxEyeHeightM ? -1 : 0;
        }

        static bool ValidFloorHeights(float floor1Y, float floor2Y) =>
            !float.IsNaN(floor1Y) && !float.IsInfinity(floor1Y)
            && !float.IsNaN(floor2Y) && !float.IsInfinity(floor2Y);

        public void OnStepFreeUnverified() =>
            Finish(IndoorArContract.Unavailable(Queries, ElapsedMs, IndoorArContract.StepFreeUnverified));

        public void OnNotLocalized()
        {
            if (State == IndoorArState.Localizing) _noPose = true;
        }

        public void OnSignConfirmed(IndoorRoomLandmark room)
        {
            if (State == IndoorArState.Done || room == null) return;
            SignRoom = room;
            _signSuperseded = false;
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
            if (_localizingS < LocalizeTimeoutS) return;
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
            if (State != IndoorArState.Pinned || Request.LocalizeOnly) return;
            _arrivalDistance = HorizontalDistance(cameraInMap);
            State = IndoorArState.Arrived;
        }

        public void ConfirmArrival()
        {
            if (State == IndoorArState.Arrived)
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
