using System;
using System.Collections;
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

        [SerializeField] string mapCode = "MSET_XTBY7AM7ZK77";
        [SerializeField] IndoorArSdkManager sdk;
        [SerializeField] SingleFrameLocalizationManager manager;
        [SerializeField] Transform mapSpace;
        bool _authPending;

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action AttemptStarted;
        public event Action<float?> Localized;
        public Transform MapSpace => mapSpace;
        public string MapCode => mapCode;

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

        public void Localize()
        {
            manager.StopAllCoroutines();
            AttemptStarted?.Invoke();
            manager.LocalizeFrame();
        }

        void OnLocalized(LocalizationSuccessResponse result) => Localized?.Invoke(result.confidence);
        void OnLocalizedWithoutConfidence() => Localized?.Invoke(null);

        void OnDestroy()
        {
            EventManager<EventData>.StopListening("AuthCallBack", OnAuthCallback);
            if (manager != null)
            {
                if (ResponseEvent != null)
                    ResponseEvent.RemoveEventHandler(manager, (Action<LocalizationSuccessResponse>)OnLocalized);
                else manager.LocalizationSuccess.RemoveListener(OnLocalizedWithoutConfidence);
                manager.StopAllCoroutines();
            }
            if (sdk != null) sdk.StopAllCoroutines();
        }
    }
}
