namespace OutdoorAr
{
    public enum OutdoorCameraAction { None, Restart, Fail }

    /// <summary>When a session that never gets a camera frame stops waiting.</summary>
    public static class OutdoorCameraWatch
    {
        // A healthy first entry on the iPhone 14 Plus had textured frames one second after the session started.
        public const float CameraFirstFrameTimeoutS = 8f;
        public const float LoaderInitializationTimeoutS = 10f;

        public static bool LoaderInitializationTimedOut(float elapsedS, bool loaderReady) =>
            !loaderReady && elapsedS >= LoaderInitializationTimeoutS;

        public static OutdoorCameraAction Decide(float sessionRunningS, bool frameSeen, bool alreadyRestarted) =>
            frameSeen || sessionRunningS < CameraFirstFrameTimeoutS ? OutdoorCameraAction.None
            : alreadyRestarted ? OutdoorCameraAction.Fail
            : OutdoorCameraAction.Restart;
    }
}
