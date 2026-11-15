using System;
using System.Collections;
using System.Text;
using System.Reflection;
using MultiSet;
using UnityEngine;

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
        public const float RequestTimeoutS = 30f;

        [SerializeField] string mapCode = "MSET_XTBY7AM7ZK77";
        [SerializeField] IndoorArSdkManager sdk;
        [SerializeField] SingleFrameLocalizationManager manager;
        [SerializeField] Transform mapSpace;
        bool _authPending;
        int _attemptId;
        int _generation;
        int _activeAttemptId;
        int _queuedAttemptId;
        int _requestGeneration;
        float _requestElapsed;
        bool _requestInFlight;
        bool _ignoreInFlight;
        bool _fallbackUnavailable;

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action<int> AttemptStarted;
        public event Action<float?> Localized;
        public event Action<int> NotLocalized;
        public Transform MapSpace => mapSpace;
        public string MapCode => mapCode;
        public bool FallbackAvailable => !_fallbackUnavailable;
        public bool HasLiveRequest => _requestInFlight;
        public string FallbackStatus => _fallbackUnavailable ? "unavailable"
            : _requestInFlight && _queuedAttemptId != 0 ? "waiting"
            : _requestInFlight ? "inFlight" : "available";

        public void Configure(IndoorArSdkManager sdkManager, SingleFrameLocalizationManager localizationManager, Transform space)
        {
            sdk = sdkManager;
            manager = localizationManager;
            mapSpace = space;
        }

        void Awake()
        {
            manager.enabled = false;
            manager.localizationType = LocalizationType.Mapset;
            manager.mapOrMapsetCode = mapCode;
            manager.autoLocalize = false;
            manager.backgroundLocalization = false;
            manager.relocalization = false;
            manager.firstLocalizationUntilSuccess = false;
            manager.confidenceCheck = false;
            manager.showAlert = false;
            var mode = typeof(SingleFrameLocalizationManager).GetField("queryMode");
            if (mode != null) mode.SetValue(manager, Enum.Parse(mode.FieldType, "Vps1"));
            // Legacy 1.9.3 otherwise issues several single-image calls per LocalizeFrame start.
            typeof(SingleFrameLocalizationManager).GetField("_requestAttempts")?.SetValue(manager, 1);
            if (ResponseEvent != null)
                ResponseEvent.AddEventHandler(manager, (Action<LocalizationSuccessResponse>)OnLocalized);
            else manager.LocalizationSuccess.AddListener(OnLocalizedWithoutConfidence);
            manager.LocalizationFailure.AddListener(OnNotLocalized);
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
            _queuedAttemptId = 0;
            _fallbackUnavailable = false;
            _ignoreInFlight = _requestInFlight;
            manager.StopAllCoroutines();
        }

        public void Localize()
        {
            if (_fallbackUnavailable) return;
            var attemptId = ++_attemptId;
            if (_requestInFlight)
            {
                _queuedAttemptId = attemptId;
                return;
            }
            StartRequest(attemptId);
        }

        public void DropQueuedFallback()
        {
            _queuedAttemptId = 0;
            if (_requestInFlight) _ignoreInFlight = true;
        }

        public void Tick(float deltaTime)
        {
            if (!_requestInFlight || _fallbackUnavailable) return;
            _requestElapsed += Mathf.Max(0f, deltaTime);
            if (_requestElapsed < RequestTimeoutS) return;
            _fallbackUnavailable = true;
            _queuedAttemptId = 0;
            Debug.Log("[IndoorAr] multiset fallback unavailable: prior request unresolved");
        }

        void Update() => Tick(Time.unscaledDeltaTime);

        void StartRequest(int attemptId)
        {
            manager.StopAllCoroutines();
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
            if (!CompleteRequest(out var attemptId)) return;
            if (success)
            {
                LogMapCodes(result);
                _queuedAttemptId = 0;
                Localized?.Invoke(result.confidence);
            }
            else
            {
                NotLocalized?.Invoke(attemptId);
                StartQueuedRequest();
            }
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
            StartQueuedRequest();
        }
        void OnLocalizedWithoutConfidence()
        {
            if (!CompleteRequest(out _)) return;
            _queuedAttemptId = 0;
            Localized?.Invoke(null);
        }

        bool CompleteRequest(out int attemptId)
        {
            attemptId = _activeAttemptId;
            if (!_requestInFlight)
            {
                LogStaleResponse();
                return false;
            }

            // shortcut: SDK may write mapSpace before this guard; revisit if it adds transactional pose delivery.
            var accepted = !_ignoreInFlight && !_fallbackUnavailable && _requestGeneration == _generation;
            _requestInFlight = false;
            _activeAttemptId = 0;
            _requestElapsed = 0f;
            _ignoreInFlight = false;
            if (!accepted)
            {
                LogStaleResponse();
                StartQueuedRequest();
                return false;
            }

            return true;
        }

        void StartQueuedRequest()
        {
            if (_queuedAttemptId == 0 || _requestInFlight || _fallbackUnavailable) return;
            var attemptId = _queuedAttemptId;
            _queuedAttemptId = 0;
            StartRequest(attemptId);
        }

        static void LogStaleResponse() => Debug.Log("[IndoorAr] stale localization response ignored");

        void OnDestroy()
        {
            EventManager<EventData>.StopListening("AuthCallBack", OnAuthCallback);
            if (manager != null)
            {
                if (ResponseEvent != null)
                    ResponseEvent.RemoveEventHandler(manager, (Action<LocalizationSuccessResponse>)OnLocalized);
                else manager.LocalizationSuccess.RemoveListener(OnLocalizedWithoutConfidence);
                manager.LocalizationFailure.RemoveListener(OnNotLocalized);
                manager.StopAllCoroutines();
            }
            if (sdk != null) sdk.StopAllCoroutines();
        }
    }
}
