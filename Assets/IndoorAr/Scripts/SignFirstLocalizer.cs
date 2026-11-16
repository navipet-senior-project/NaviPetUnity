using System;
using UnityEngine;

namespace IndoorAr
{
    public sealed class SignFirstLocalizer : IIndoorArLocalizer
    {
        public const float HoldS = 15f;

        readonly IIndoorArLocalizer _inner;
        readonly IndoorSignCalibration _calibration;
        // Room signs lead (OCR on or a validated sign): the session starts without VPS sign-in and VPS waits HoldS.
        readonly bool _ownAuth;
        bool _open;
        bool _innerPosed;
        bool _innerReady;
        bool _innerWanted;
        bool _innerAuthFailed;
        float _holdLeft;

        /// <param name="readsSigns">Room-sign OCR is on. It needs no VPS sign-in, so neither does the session.</param>
        public SignFirstLocalizer(IIndoorArLocalizer inner, IndoorSignCalibration calibration = null,
            bool readsSigns = true)
        {
            _inner = inner;
            _calibration = calibration ?? new IndoorSignCalibration();
            SignsCanPose = _calibration.CanPose(SystemInfo.deviceModel);
            _ownAuth = readsSigns || SignsCanPose;
            _inner.Localized += OnInnerLocalized;
            _inner.Authenticated += OnInnerAuthenticated;
            _inner.AuthFailed += OnInnerAuthFailed;
        }

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action<int> AttemptStarted { add => _inner.AttemptStarted += value; remove => _inner.AttemptStarted -= value; }
        public event Action<float?> Localized;
        public event Action<int> NotLocalized { add => _inner.NotLocalized += value; remove => _inner.NotLocalized -= value; }

        public Func<Vector3, bool> CandidatePoseValidator;

        public Transform MapSpace => _inner.MapSpace;
        public string MapCode => _inner.MapCode;
        public bool SignsCanPose { get; }
        public bool FallbackAvailable => !_innerAuthFailed && (_inner is not MultiSetLocalizer multiSet || multiSet.FallbackAvailable);
        public string FallbackStatus => _innerAuthFailed ? "unavailable: authentication failed"
            : _inner is MultiSetLocalizer multiSet ? multiSet.FallbackStatus : "available";
        public bool FromSigns { get; private set; }
        public long LastSolveUs { get; private set; }

        public void Authenticate()
        {
            _innerReady = _innerWanted = _innerAuthFailed = false;
            _inner.Authenticate();
            if (_ownAuth) Authenticated?.Invoke();
        }

        public void Localize()
        {
            if (_open)
            {
                if (_holdLeft <= 0f) AskInner();
                return;
            }
            FromSigns = _innerPosed = false;
            // OCR off and no validated sign: VPS is the only pose source, so it is asked at once. Otherwise room
            // signs get the hold first, with or without calibration, and VPS stays the delayed fallback.
            if (!_ownAuth)
            {
                AskInner();
                return;
            }
            _open = true;
            _holdLeft = HoldS;
        }

        public void Reset()
        {
            _open = _innerPosed = _innerWanted = false;
            FromSigns = false;
            _holdLeft = 0f;
        }

        public void DropQueuedFallback()
        {
            _innerWanted = false;
            _holdLeft = 0f;
            if (_inner is MultiSetLocalizer multiSet) multiSet.DropQueuedFallback();
        }

        public void Tick(float dt)
        {
            if (!_open || _holdLeft <= 0f) return;
            _holdLeft -= dt;
            if (_holdLeft <= 0f) AskInner();
        }

        public IndoorSignPoseStatus ObservePose(IndoorSignObservation observation)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var pose = IndoorSignPose.Solve(observation, _calibration.Find(observation?.RoomId));
            LastSolveUs = watch.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
            if (pose.Status != IndoorSignPoseStatus.Ok || _innerPosed) return pose.Status;
            if (CandidatePoseValidator != null && !CandidatePoseValidator(pose.CameraPositionInMap))
                return IndoorSignPoseStatus.Implausible;
            DropQueuedFallback();
            IndoorSignPose.Place(MapSpace, observation.CameraWorldPosition, observation.CameraWorldRotation,
                pose.CameraPositionInMap, pose.CameraRotationInMap);
            FromSigns = true;
            if (!_open) return pose.Status;
            _open = false;
            Localized?.Invoke(null);
            return pose.Status;
        }

        void AskInner()
        {
            if (_ownAuth && !_innerReady)
            {
                _innerWanted = !_innerAuthFailed;
                return;
            }
            _innerWanted = false;
            _inner.Localize();
        }

        void OnInnerAuthenticated()
        {
            _innerReady = true;
            if (!_ownAuth) Authenticated?.Invoke();
            else if (_innerWanted) AskInner();
        }

        void OnInnerAuthFailed(bool network)
        {
            if (!_ownAuth) AuthFailed?.Invoke(network);
            else _innerAuthFailed = true;
        }

        void OnInnerLocalized(float? confidence)
        {
            _innerPosed = true;
            FromSigns = false;
            _open = false;
            Localized?.Invoke(confidence);
        }
    }
}
