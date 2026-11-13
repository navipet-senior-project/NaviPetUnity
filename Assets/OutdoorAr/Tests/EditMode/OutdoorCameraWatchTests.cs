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
    }
}
