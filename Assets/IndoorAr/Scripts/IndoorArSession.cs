using System;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorArState { Init, Auth, Localizing, Pinned, Done }

    /// <summary>
    /// Door verifier state machine. Pure C# so EditMode tests can drive it:
    /// IndoorArRunner feeds it AR, MultiSet and frame-time events and delivers
    /// ResultJson once State is Done.
    /// </summary>
    public sealed class IndoorArSession
    {
        public const float LocalizeTimeoutS = 30f;
        public const float WrongFloorM = 2f;

        float _elapsedS;
        float _localizingS;
        bool _retried;
        float? _confidence;

        public IndoorArSession(IndoorArRequest request, string buildMapCode)
        {
            Request = request;
            if (!IndoorArContract.CheckMap(request, buildMapCode))
                Finish(IndoorArContract.Error(IndoorArContract.MapMismatch, 0, 0));
        }

        /// <summary>Raised when the localizer must start a localization attempt.</summary>
        public event Action LocalizeRequested;

        public IndoorArRequest Request { get; }
        public IndoorArState State { get; private set; } = IndoorArState.Init;
        public int Queries { get; private set; }
        public bool WasPinned { get; private set; }
        public string ResultJson { get; private set; }

        long ElapsedMs => (long)(_elapsedS * 1000f);

        public void OnArSupport(bool supported)
        {
            if (State != IndoorArState.Init) return;
            if (supported) State = IndoorArState.Auth;
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

        // Re-localize only after tracking loss, never on a timer.
        public void OnTrackingLost()
        {
            if (State == IndoorArState.Pinned) StartLocalizing();
        }

        public void Tick(float dt)
        {
            if (State == IndoorArState.Done) return;
            _elapsedS += dt;
            if (State != IndoorArState.Localizing) return;
            _localizingS += dt;
            if (_localizingS < LocalizeTimeoutS) return;
            if (_retried)
            {
                Finish(IndoorArContract.LocalizationFailed(Queries, ElapsedMs));
                return;
            }
            _retried = true;
            _localizingS = 0f;
            LocalizeRequested?.Invoke();
        }

        /// <param name="cameraInMap">Camera position in the MapSet frame.</param>
        public void ImHere(Vector3 cameraInMap)
        {
            if (State != IndoorArState.Pinned) return;
            Finish(IndoorArContract.Arrived(HorizontalDistance(cameraInMap), Queries, ElapsedMs, _confidence));
        }

        public void Cancel()
        {
            if (State != IndoorArState.Done) Finish(IndoorArContract.Cancelled(Queries, ElapsedMs));
        }

        public float HorizontalDistance(Vector3 cameraInMap)
        {
            var door = Request.position;
            return new Vector2(cameraInMap.x - door.x, cameraInMap.z - door.z).magnitude;
        }

        public bool IsWrongFloor(Vector3 cameraInMap) =>
            Mathf.Abs(cameraInMap.y - Request.position.y) > WrongFloorM;

        void StartLocalizing()
        {
            State = IndoorArState.Localizing;
            _localizingS = 0f;
            _retried = false;
            LocalizeRequested?.Invoke();
        }

        void Finish(string resultJson)
        {
            ResultJson = resultJson;
            State = IndoorArState.Done;
        }
    }
}
