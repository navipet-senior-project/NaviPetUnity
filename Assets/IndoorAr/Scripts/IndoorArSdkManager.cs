using MultiSet;

namespace IndoorAr
{
    // The SDK's OnEnable calls LoadConfiguration and authenticates automatically.
    // Defer that unchanged SDK flow until the session has checked AR support.
    public sealed class IndoorArSdkManager : MultisetSdkManager
    {
        protected override void LoadConfiguration() { }

        public void BeginAuthentication()
        {
            runtimeAuthentication = false;
            base.LoadConfiguration();
        }
    }
}
