using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using MultiSet;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace IndoorAr
{
    /// <summary>
    /// SDK 1.15.2 API verified in MultiSetSDK.dll IL: deferred MultisetSdkManager.LoadConfiguration;
    /// EventManager&lt;EventData&gt; AuthCallBack + AuthSuccess; CheckInternetConnection for offline status.
    /// SingleFrameLocalizationManager uses ARCameraManager, Mapset + mapOrMapsetCode, LocalizeFrame,
    /// and OnLocalizationWithResponse.confidence. It writes the sample's serialized mapSpace transform.
    /// No request-sent callback exists: AttemptStarted counts LocalizeFrame starts, not billed VPS calls.
    /// Auto/background/relocalization/first-success retries and alerts are disabled; the session owns retries.
    /// </summary>
    public sealed class MultiSetLocalizer : MonoBehaviour, IIndoorArLocalizer
    {
        // Public-event lookup keeps the approved 1.9.3 rollback buildable; it exposes only a UnityEvent.
        static readonly EventInfo ResponseEvent = typeof(SingleFrameLocalizationManager).GetEvent("OnLocalizationWithResponse");
        static readonly FieldInfo MapSpaceField = typeof(SingleFrameLocalizationManager).GetField("mapSpace",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly FieldInfo BlurCheckField = typeof(SingleFrameLocalizationManager).GetField("enableBlurCheck",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static bool s_requestLive;
        static MultiSetLocalizer s_requestOwner;
        static readonly Queue<MultiSetLocalizer> WaitingLocalizers = new Queue<MultiSetLocalizer>();
        public const float RequestTimeoutS = 30f;

        sealed class SdkCompletionListener
        {
            readonly MultiSetLocalizer owner;

            public SdkCompletionListener(MultiSetLocalizer localizer) => owner = localizer;
            public void OnLocalized(LocalizationSuccessResponse result) => owner.OnLocalized(result);
            public void OnLocalizedWithoutConfidence() => owner.OnLocalizedWithoutConfidence();
            public void OnNotLocalized() => owner.OnNotLocalized();
        }

        [SerializeField] string mapCode = "MSET_XTBY7AM7ZK77";
        [SerializeField] IndoorArSdkManager sdk;
        [SerializeField] SingleFrameLocalizationManager manager;
        [SerializeField] Transform mapSpace;
        [SerializeField] Transform sdkMapSpace;
        bool _authPending;
        int _attemptId;
        int _generation;
        int _activeAttemptId;
        int _queuedAttemptId;
        int _requestGeneration;
        float _requestElapsed;
        float _queueElapsed;
        bool _requestInFlight;
        bool _ignoreInFlight;
        bool _fallbackUnavailable;
        bool _mapSpaceIsolated;
        bool _blurCheckDisabled;
        bool _ownerDestroyed;
        SdkCompletionListener _completionListener;

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action<int> AttemptStarted;
        public event Action<float?> Localized;
        public event Action<int> NotLocalized;
        public Func<Transform, bool> CandidatePoseValidator { get; set; }
        public Func<bool> TrackingReady { get; set; } = () => ARSession.state == ARSessionState.SessionTracking;
        public Transform MapSpace => mapSpace;
        public string MapCode => mapCode;
        public bool FallbackAvailable => _mapSpaceIsolated && _blurCheckDisabled && !_fallbackUnavailable;
        public bool HasLiveRequest => _requestInFlight;
        public Transform SdkMapSpace => sdkMapSpace;
        public string FallbackStatus => !FallbackAvailable ? "unavailable"
            : (s_requestLive && s_requestOwner != this) || _queuedAttemptId != 0 ? "waiting"
            : _requestInFlight ? "inFlight" : "available";

        public void Configure(IndoorArSdkManager sdkManager, SingleFrameLocalizationManager localizationManager, Transform space)
        {
            Configure(sdkManager, localizationManager, space, null);
        }

        public void Configure(IndoorArSdkManager sdkManager, SingleFrameLocalizationManager localizationManager,
            Transform space, Transform sdkSpace)
        {
            sdk = sdkManager;
            manager = localizationManager;
            mapSpace = space;
            sdkMapSpace = sdkSpace;
            ConfigureSdkMapSpace();
            DisableBlurCheck();
        }

        void Awake()
        {
            ConfigureSdkMapSpace();
            manager.enabled = false;
            manager.localizationType = LocalizationType.Mapset;
            manager.mapOrMapsetCode = mapCode;
            manager.autoLocalize = false;
            manager.backgroundLocalization = false;
            manager.relocalization = false;
            manager.firstLocalizationUntilSuccess = false;
            manager.confidenceCheck = false;
            manager.showAlert = false;
            DisableBlurCheck();
            _completionListener ??= new SdkCompletionListener(this);
            var mode = typeof(SingleFrameLocalizationManager).GetField("queryMode");
            if (mode != null) mode.SetValue(manager, Enum.Parse(mode.FieldType, "Vps1"));
            // Legacy 1.9.3 otherwise issues several single-image calls per LocalizeFrame start.
            typeof(SingleFrameLocalizationManager).GetField("_requestAttempts")?.SetValue(manager, 1);
            if (ResponseEvent != null)
                ResponseEvent.AddEventHandler(manager, (Action<LocalizationSuccessResponse>)_completionListener.OnLocalized);
            else manager.LocalizationSuccess.AddListener(_completionListener.OnLocalizedWithoutConfidence);
            manager.LocalizationFailure.AddListener(_completionListener.OnNotLocalized);
            EventManager<EventData>.StartListening("AuthCallBack", OnAuthCallback);
        }

        public void Authenticate() => StartCoroutine(CheckConnectionAndAuthenticate());

        IEnumerator CheckConnectionAndAuthenticate()
        {
            var connected = false;
            yield return MultiSetHttpClient.CheckInternetConnection(value => connected = value);
            if (!connected)
            {
                AuthFailed?.Invoke(true);
                yield break;
            }
            _authPending = true;
            sdk.BeginAuthentication();
        }

        void OnAuthCallback(EventData result)
        {
            if (!_authPending) return;
            _authPending = false;
            if (!result.AuthSuccess)
            {
                StartCoroutine(ReportAuthFailure());
                return;
            }
            manager.enabled = true;
            Authenticated?.Invoke();
        }

        IEnumerator ReportAuthFailure()
        {
            var connected = false;
            yield return MultiSetHttpClient.CheckInternetConnection(value => connected = value);
            AuthFailed?.Invoke(!connected);
        }

        public void AbandonPending()
        {
            _generation++;
            RemoveQueuedRequest();
            _fallbackUnavailable = false;
            _ignoreInFlight = _requestInFlight;
            if (!s_requestLive) manager.StopAllCoroutines();
        }

        public void Localize()
        {
            if (!FallbackAvailable) return;
            var attemptId = ++_attemptId;
            if (s_requestLive || _queuedAttemptId != 0 || WaitingLocalizers.Count > 0 || !TrackingReady())
            {
                QueueRequest(attemptId);
                return;
            }
            StartRequest(attemptId);
        }

        public void DropQueuedFallback()
        {
            RemoveQueuedRequest();
            if (_requestInFlight) _ignoreInFlight = true;
        }

        public void Tick(float deltaTime)
        {
            if (!s_requestLive) StartNextQueuedRequest();
            if (!FallbackAvailable) return;
            var dt = Mathf.Max(0f, deltaTime);
            var currentRequest = _requestInFlight && _requestGeneration == _generation;
            if (currentRequest) _requestElapsed += dt;
            else if (_queuedAttemptId != 0 && s_requestLive) _queueElapsed += dt;
            else return;
            if ((currentRequest ? _requestElapsed : _queueElapsed) < RequestTimeoutS) return;
            _fallbackUnavailable = true;
            RemoveQueuedRequest();
            Debug.Log("[IndoorAr] multiset fallback unavailable: prior request unresolved");
        }

        void Update() => Tick(Time.unscaledDeltaTime);

        void StartRequest(int attemptId)
        {
            if (s_requestLive || !FallbackAvailable) return;
            if (!TrackingReady()) { QueueRequest(attemptId); return; }
            manager.StopAllCoroutines();
            sdkMapSpace.SetPositionAndRotation(mapSpace.position, mapSpace.rotation);
            s_requestLive = true;
            s_requestOwner = this;
            _requestInFlight = true;
            _ignoreInFlight = false;
            _activeAttemptId = attemptId;
            _requestGeneration = _generation;
            _requestElapsed = 0f;
            AttemptStarted?.Invoke(attemptId);
            manager.LocalizeFrame();
        }

        void OnLocalized(LocalizationSuccessResponse result)
        {
            var success = result != null && result.poseFound;
            if (!CompleteRequest(out var attemptId))
                return;
            if (!success) NotLocalized?.Invoke(attemptId);
            else CompleteLocalizedPose(attemptId, result.confidence, result);
        }

        static void LogMapCodes(LocalizationSuccessResponse result)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var type = result.GetType();
            var field = type.GetField("mapCodes", flags);
            var property = type.GetProperty("mapCodes", flags);
            var value = field != null ? field.GetValue(result) : property?.GetValue(result);
            var codes = new StringBuilder();
            if (value is string text) codes.Append(text);
            else if (value is IEnumerable values)
                foreach (var code in values)
                {
                    if (codes.Length > 0) codes.Append(',');
                    codes.Append(code);
                }
            else if (value != null) codes.Append(value);
            Debug.Log($"[IndoorAr] localization mapCodes={(codes.Length == 0 ? "unknown" : codes.ToString())}");
        }

        void OnNotLocalized()
        {
            if (!CompleteRequest(out var attemptId)) return;
            NotLocalized?.Invoke(attemptId);
        }
        void OnLocalizedWithoutConfidence()
        {
            if (!CompleteRequest(out var attemptId)) return;
            CompleteLocalizedPose(attemptId, null, null);
        }

        void CompleteLocalizedPose(int attemptId, float? confidence, LocalizationSuccessResponse result)
        {
            if (CandidatePoseValidator == null || !CandidatePoseValidator(sdkMapSpace))
            {
                if (result != null) LogMapCodes(result);
                NotLocalized?.Invoke(attemptId);
                return;
            }
            CopyAcceptedPose();
            if (result != null) LogMapCodes(result);
            RemoveQueuedRequest();
            Localized?.Invoke(confidence);
        }

        bool CompleteRequest(out int attemptId)
        {
            attemptId = _activeAttemptId;
            if (!_requestInFlight || !s_requestLive || s_requestOwner != this)
            {
                LogStaleResponse();
                return false;
            }

            var accepted = !_ignoreInFlight && FallbackAvailable && _requestGeneration == _generation;
            // shortcut: SDK writes only to its proxy; if callbacks can vanish silently, keep the process lock until restart.
            s_requestLive = false;
            s_requestOwner = null;
            _requestInFlight = false;
            _activeAttemptId = 0;
            _requestElapsed = 0f;
            _ignoreInFlight = false;
            if (_ownerDestroyed) RemoveLocalizationListeners();
            if (!accepted)
            {
                LogStaleResponse();
                return false;
            }

            return true;
        }

        void QueueRequest(int attemptId)
        {
            if (_queuedAttemptId == 0)
            {
                _queueElapsed = 0f;
                WaitingLocalizers.Enqueue(this);
            }
            _queuedAttemptId = attemptId;
        }

        void RemoveQueuedRequest()
        {
            if (_queuedAttemptId == 0) return;
            _queuedAttemptId = 0;
            _queueElapsed = 0f;
            var waiting = WaitingLocalizers.ToArray();
            WaitingLocalizers.Clear();
            foreach (var localizer in waiting)
                if (localizer != null && localizer._queuedAttemptId != 0)
                    WaitingLocalizers.Enqueue(localizer);
        }

        static void StartNextQueuedRequest()
        {
            if (s_requestLive) return;
            while (WaitingLocalizers.Count > 0)
            {
                var next = WaitingLocalizers.Peek();
                if (next == null || next._queuedAttemptId == 0) { WaitingLocalizers.Dequeue(); continue; }
                if (!next.TrackingReady()) return;
                WaitingLocalizers.Dequeue();
                var attemptId = next._queuedAttemptId;
                next.RemoveQueuedRequest();
                if (!next.FallbackAvailable) continue;
                next.StartRequest(attemptId);
                return;
            }
        }

        void CopyAcceptedPose()
        {
            mapSpace.SetPositionAndRotation(sdkMapSpace.position, sdkMapSpace.rotation);
            mapSpace.gameObject.SetActive(sdkMapSpace.gameObject.activeSelf);
        }

        void ConfigureSdkMapSpace()
        {
            if (manager == null || mapSpace == null) return;
            if (sdkMapSpace == null || sdkMapSpace == mapSpace
                || sdkMapSpace.IsChildOf(mapSpace) || mapSpace.IsChildOf(sdkMapSpace))
            {
                var proxy = new GameObject("MultiSet VPS Map Space");
                proxy.transform.SetPositionAndRotation(mapSpace.position, mapSpace.rotation);
                sdkMapSpace = proxy.transform;
            }
            if (MapSpaceField == null) return;
            object value = MapSpaceField.FieldType == typeof(GameObject) ? sdkMapSpace.gameObject
                : MapSpaceField.FieldType == typeof(Transform) ? sdkMapSpace : null;
            if (value == null) return;
            MapSpaceField.SetValue(manager, value);
            _mapSpaceIsolated = MapSpaceField.GetValue(manager) == value
                && sdkMapSpace.gameObject != mapSpace.gameObject
                && !sdkMapSpace.IsChildOf(mapSpace) && !mapSpace.IsChildOf(sdkMapSpace);
        }

        void DisableBlurCheck()
        {
            _blurCheckDisabled = BlurCheckField != null && BlurCheckField.FieldType == typeof(bool);
            if (!_blurCheckDisabled) return;
            BlurCheckField.SetValue(manager, false);
            _blurCheckDisabled = !(bool)BlurCheckField.GetValue(manager);
        }

        static void LogStaleResponse() => Debug.Log("[IndoorAr] stale localization response ignored");

        void RemoveLocalizationListeners()
        {
            if (manager == null || _completionListener == null) return;
            if (ResponseEvent != null)
                ResponseEvent.RemoveEventHandler(manager, (Action<LocalizationSuccessResponse>)_completionListener.OnLocalized);
            else manager.LocalizationSuccess.RemoveListener(_completionListener.OnLocalizedWithoutConfidence);
            manager.LocalizationFailure.RemoveListener(_completionListener.OnNotLocalized);
        }

        void OnDestroy()
        {
            RemoveQueuedRequest();
            EventManager<EventData>.StopListening("AuthCallBack", OnAuthCallback);
            if (_ownerDestroyed) return;
            // The SDK manager may be destroyed first while its request still owes a callback.
            if (_requestInFlight && s_requestLive && s_requestOwner == this)
            {
                _ownerDestroyed = true;
                _generation++;
                _ignoreInFlight = true;
            }
            else RemoveLocalizationListeners();
            if (manager != null && !s_requestLive) manager.StopAllCoroutines();
            if (sdk != null && !s_requestLive) sdk.StopAllCoroutines();
        }
    }
}
