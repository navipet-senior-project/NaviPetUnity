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

        static Func<double> Clock = () => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;

        sealed class RequestTrace
        {
            public int Attempt;
            public bool LockBusy;
            public double Started, SdkStarted, QueueUpdated, LockWaitS;
            public bool SdkStartedSet;
            public bool Ended;
        }

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
        RequestTrace _queuedTrace;
        RequestTrace _activeTrace;
        SdkCompletionListener _completionListener;

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action<int> AttemptStarted;
        public event Action<float?> Localized;
        public event Action<int> NotLocalized;
        public Func<Transform, bool> CandidatePoseValidator { get; set; }
        public event Action<Transform, string, float?> CandidateLocalized;
        public Func<bool> TrackingReady { get; set; } = () => ARSession.state == ARSessionState.SessionTracking;
        public Transform MapSpace => mapSpace;
        public string MapCode => mapCode;
        public string LastMapHash { get; private set; } = "unavailable";
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
            EndTrace(_activeTrace, IndoorArDiagnostics.LocalizationOutcome.Cancelled);
            if (!s_requestLive) manager.StopAllCoroutines();
        }

        public void Localize()
        {
            if (!FallbackAvailable) return;
            var attemptId = ++_attemptId;
            var trace = new RequestTrace { Attempt = attemptId, Started = Clock() };
            if (s_requestLive || _queuedAttemptId != 0 || WaitingLocalizers.Count > 0 || !TrackingReady())
            {
                QueueRequest(attemptId, trace);
                return;
            }
            StartRequest(attemptId, trace);
        }

        public void DropQueuedFallback()
        {
            RemoveQueuedRequest();
            if (!_requestInFlight) return;
            _ignoreInFlight = true;
            EndTrace(_activeTrace, IndoorArDiagnostics.LocalizationOutcome.Cancelled);
        }

        public void Tick(float deltaTime)
        {
            var dt = Mathf.Max(0f, deltaTime);
            if (!s_requestLive) StartNextQueuedRequest();
            if (!FallbackAvailable) return;
            var currentRequest = _requestInFlight && _requestGeneration == _generation;
            if (currentRequest) _requestElapsed += dt;
            else if (_queuedAttemptId != 0 && s_requestLive) _queueElapsed += dt;
            else return;
            if ((currentRequest ? _requestElapsed : _queueElapsed) < RequestTimeoutS) return;
            _fallbackUnavailable = true;
            if (currentRequest && _activeTrace != null)
            {
                EndTrace(_activeTrace, IndoorArDiagnostics.LocalizationOutcome.Timeout);
            }
            RemoveQueuedRequest(IndoorArDiagnostics.LocalizationOutcome.Timeout);
            Debug.Log("[IndoorAr] multiset fallback unavailable: prior request unresolved");
        }

        void Update() => Tick(Time.unscaledDeltaTime);

        void StartRequest(int attemptId, RequestTrace trace)
        {
            if (s_requestLive || !FallbackAvailable) return;
            if (!TrackingReady()) { QueueRequest(attemptId, trace); return; }
            manager.StopAllCoroutines();
            sdkMapSpace.SetPositionAndRotation(mapSpace.position, mapSpace.rotation);
            UpdateQueueWaits();
            s_requestLive = true;
            s_requestOwner = this;
            _requestInFlight = true;
            _ignoreInFlight = false;
            _activeAttemptId = attemptId;
            _requestGeneration = _generation;
            _requestElapsed = 0f;
            trace.SdkStarted = Clock();
            trace.SdkStartedSet = true;
            _activeTrace = trace;
            AttemptStarted?.Invoke(attemptId);
            manager.LocalizeFrame();
        }

        void OnLocalized(LocalizationSuccessResponse result)
        {
            var success = result != null && result.poseFound;
            if (!CompleteRequest(out var attemptId))
                return;
            if (!success)
            {
                EndActiveTrace(IndoorArDiagnostics.LocalizationOutcome.GenericFailure);
                NotLocalized?.Invoke(attemptId);
            }
            else CompleteLocalizedPose(attemptId, result.confidence, result);
        }

        static string MapCodesOf(LocalizationSuccessResponse result)
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
            return codes.ToString();
        }

        void OnNotLocalized()
        {
            if (!CompleteRequest(out var attemptId)) return;
            EndActiveTrace(IndoorArDiagnostics.LocalizationOutcome.GenericFailure);
            NotLocalized?.Invoke(attemptId);
        }
        void OnLocalizedWithoutConfidence()
        {
            if (!CompleteRequest(out var attemptId)) return;
            CompleteLocalizedPose(attemptId, null, null);
        }

        void CompleteLocalizedPose(int attemptId, float? confidence, LocalizationSuccessResponse result)
        {
            LastMapHash = IndoorArDiagnostics.MapHash(result == null ? null : MapCodesOf(result));
            CandidateLocalized?.Invoke(sdkMapSpace, LastMapHash, confidence);
            if (CandidatePoseValidator == null || !CandidatePoseValidator(sdkMapSpace))
            {
                EndActiveTrace(IndoorArDiagnostics.LocalizationOutcome.Rejected);
                NotLocalized?.Invoke(attemptId);
                return;
            }
            CopyAcceptedPose();
            EndActiveTrace(IndoorArDiagnostics.LocalizationOutcome.Ok);
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
            UpdateQueueWaits();
            s_requestLive = false;
            s_requestOwner = null;
            _requestInFlight = false;
            _activeAttemptId = 0;
            _requestElapsed = 0f;
            _ignoreInFlight = false;
            if (_ownerDestroyed) RemoveLocalizationListeners();
            if (!accepted)
            {
                var ended = _activeTrace != null && _activeTrace.Ended;
                EndActiveTrace(ended ? IndoorArDiagnostics.LocalizationOutcome.Late : IndoorArDiagnostics.LocalizationOutcome.Ignored);
                LogStaleResponse();
                return false;
            }

            return true;
        }

        void QueueRequest(int attemptId, RequestTrace trace)
        {
            UpdateQueueWaits();
            if (_queuedAttemptId == 0)
            {
                _queueElapsed = 0f;
                WaitingLocalizers.Enqueue(this);
            }
            else if (_queuedTrace != trace) EndTrace(_queuedTrace, IndoorArDiagnostics.LocalizationOutcome.Superseded);
            _queuedAttemptId = attemptId;
            _queuedTrace = trace;
            trace.QueueUpdated = Clock();
            if (s_requestLive || WaitingLocalizers.Peek() != this) trace.LockBusy = true;
        }

        static void UpdateQueueWaits()
        {
            var now = Clock();
            var blocked = s_requestLive;
            foreach (var localizer in WaitingLocalizers)
            {
                if (localizer == null || localizer._queuedTrace == null) continue;
                var trace = localizer._queuedTrace;
                if (blocked)
                {
                    trace.LockBusy = true;
                    trace.LockWaitS += now - trace.QueueUpdated;
                }
                trace.QueueUpdated = now;
                blocked = true;
            }
        }

        // A Late line may follow a Timeout or Cancelled one: the lock stays held until the SDK answers.
        static void EndTrace(RequestTrace trace, IndoorArDiagnostics.LocalizationOutcome outcome)
        {
            if (trace == null || trace.Ended && outcome != IndoorArDiagnostics.LocalizationOutcome.Late) return;
            trace.Ended = true;
            var now = Clock();
            Debug.Log(IndoorArDiagnostics.LocalizationRequestLine(trace.Attempt,
                IndoorArDiagnostics.LocalizationRequestKind.MultiSet, (long)((now - trace.Started) * 1000), outcome,
                trace.LockBusy, (long)(trace.LockWaitS * 1000),
                trace.SdkStartedSet ? (long)((now - trace.SdkStarted) * 1000) : 0));
        }

        void EndActiveTrace(IndoorArDiagnostics.LocalizationOutcome outcome)
        {
            EndTrace(_activeTrace, outcome);
            _activeTrace = null;
        }

        void RemoveQueuedRequest(IndoorArDiagnostics.LocalizationOutcome outcome = IndoorArDiagnostics.LocalizationOutcome.Cancelled)
        {
            if (_queuedAttemptId == 0) return;
            UpdateQueueWaits();
            EndTrace(_queuedTrace, outcome);
            _queuedTrace = null;
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
                UpdateQueueWaits();
                WaitingLocalizers.Dequeue();
                var attemptId = next._queuedAttemptId;
                var trace = next._queuedTrace;
                next._queuedTrace = null;
                next.RemoveQueuedRequest();
                if (!next.FallbackAvailable) { EndTrace(trace, IndoorArDiagnostics.LocalizationOutcome.Cancelled); continue; }
                next.StartRequest(attemptId, trace);
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
            EndTrace(_activeTrace, IndoorArDiagnostics.LocalizationOutcome.Cancelled);
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
