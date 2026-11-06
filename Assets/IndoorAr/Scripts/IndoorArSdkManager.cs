using MultiSet;
using UnityEngine;

namespace IndoorAr
{
    // The SDK's OnEnable calls LoadConfiguration and authenticates automatically.
    // Defer that unchanged SDK flow until the session has checked AR support.
    public sealed class IndoorArSdkManager : MultisetSdkManager
    {
        // The SDK reads this PlayerPrefs JSON in GetAccessToken. Same shape as its AccessToken.
        const string AccessTokenPrefs = "MultiSet.AccessToken";

        protected override void LoadConfiguration() { }

        public void BeginAuthentication()
        {
            var supplied = IndoorArContract.ReadArToken(IndoorArBridge.ReadRequestJson());
            if (supplied != null && !string.IsNullOrEmpty(supplied.token) && !string.IsNullOrEmpty(supplied.expiresOn))
            {
                // Skip LoadConfiguration so no client id or secret is required.
                PlayerPrefs.SetString(AccessTokenPrefs, JsonUtility.ToJson(supplied));
                EventManager<EventData>.TriggerEvent("AuthCallBack", new EventData { AuthSuccess = true });
                return;
            }
            runtimeAuthentication = false;
            base.LoadConfiguration();
        }
    }
}
