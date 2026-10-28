using System;
using System.Collections;
using MultiSet;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>
    /// SDK 1.9.0 API inspected in MultiSetSDK.dll IL:
    /// MultisetSdkManager.LoadConfiguration loads Resources/MultiSetConfig and authenticates;
    /// EventManager&lt;EventData&gt; "AuthCallBack" carries AuthSuccess only. The SDK's
    /// MultiSetHttpClient.CheckInternetConnection supplies the missing offline classification.
    /// MapLocalizationManager uses LocalizationType.Mapset + mapOrMapsetCode; LocalizeFrame
    /// captures frames, then LocalizationRequested fires immediately before LocalizeRequestMultiQuery.
    /// LocalizationSuccess/LocalizationFailure finish a query. All callbacks run in Unity coroutines.
    /// The sample binds private serialized mapSpace to "Map Space"; the editor wires the same object.
    /// autoLocalize, backgroundLocalization, relocalization and firstLocalizationUntilSuccess
    /// are disabled: IndoorArSession owns attempts and retries. showAlert disables duplicate SDK UI.
    /// </summary>
    public sealed class MultiSetLocalizer : MonoBehaviour, IIndoorArLocalizer
    {
        [SerializeField] string mapCode = "MSET_XTBY7AM7ZK77";
        [SerializeField] IndoorArSdkManager sdk;
        [SerializeField] MapLocalizationManager manager;
        [SerializeField] Transform mapSpace;
        bool _authPending;

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action QuerySent;
        public event Action Localized;
        public Transform MapSpace => mapSpace;
        public string MapCode => mapCode;

        public void Configure(IndoorArSdkManager sdkManager, MapLocalizationManager localizationManager, Transform space)
        {
            sdk = sdkManager;
            manager = localizationManager;
            mapSpace = space;
        }

        void Awake()
        {
            manager.enabled = false; // Do not let its AuthCallBack start localization or SDK UI.
            manager.localizationType = LocalizationType.Mapset;
            manager.mapOrMapsetCode = mapCode;
            manager.autoLocalize = false;
            manager.backgroundLocalization = false;
            manager.relocalization = false;
            manager.firstLocalizationUntilSuccess = false;
            manager.showAlert = false;
            manager.LocalizationRequested.AddListener(OnQuerySent);
            manager.LocalizationSuccess.AddListener(OnLocalized);
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
            manager.LocalizeFrame();
        }

        void OnQuerySent() => QuerySent?.Invoke();
        void OnLocalized() => Localized?.Invoke();

        void OnDestroy()
        {
            EventManager<EventData>.StopListening("AuthCallBack", OnAuthCallback);
            if (manager != null)
            {
                manager.LocalizationRequested.RemoveListener(OnQuerySent);
                manager.LocalizationSuccess.RemoveListener(OnLocalized);
                manager.StopAllCoroutines();
            }
            if (sdk != null) sdk.StopAllCoroutines();
        }
    }
}
