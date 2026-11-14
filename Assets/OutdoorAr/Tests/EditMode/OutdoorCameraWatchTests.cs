using NUnit.Framework;

namespace OutdoorAr.Tests
{
    public class OutdoorCameraWatchTests
    {
        const float Late = OutdoorCameraWatch.CameraFirstFrameTimeoutS + .1f;

        [TestCase(0f, false, false, OutdoorCameraAction.None)]
        [TestCase(7.9f, false, false, OutdoorCameraAction.None)]
        [TestCase(Late, true, false, OutdoorCameraAction.None)]
        [TestCase(Late, true, true, OutdoorCameraAction.None)]
        [TestCase(Late, false, false, OutdoorCameraAction.Restart)]
        [TestCase(Late, false, true, OutdoorCameraAction.Fail)]
        [TestCase(1f, false, true, OutdoorCameraAction.None)]
        public void RestartsOnceThenFailsOnlyAfterAFramelessWait(float runningS, bool frame, bool restarted, OutdoorCameraAction expected)
        {
            Assert.AreEqual(expected, OutdoorCameraWatch.Decide(runningS, frame, restarted));
        }

        [TestCase(9.9f, false, false)]
        [TestCase(10f, false, true)]
        [TestCase(12f, false, true)]
        [TestCase(12f, true, false)]
        public void LoaderInitializationWaitIsBounded(float elapsedS, bool loaderReady, bool expected)
        {
            Assert.AreEqual(expected, OutdoorCameraWatch.LoaderInitializationTimedOut(elapsedS, loaderReady));
        }
    }
}
