using IndoorAr;
using NUnit.Framework;

public class IndoorArBaselineTests
{
    [Test]
    public void StartupIsAvailabilityAndIgnoresASecondReady()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteArReady(500);
        Assert.IsNull(baseline.ArStartupMs);
        baseline.NoteSessionOpened(1000);
        baseline.NoteSessionOpened(1100);
        baseline.NoteArReady(1250);
        baseline.NoteArReady(1800);
        Assert.AreEqual(250, baseline.ArStartupMs);
    }

    [Test]
    public void RequestLatencyIsTheAttemptThatReturnedTheFirstPose()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteSessionOpened(1000);
        baseline.NoteAttemptStarted(1100);
        baseline.NoteAttemptStarted(4100);
        baseline.NotePoseAccepted(4500);
        baseline.NotePoseAccepted(9000);

        Assert.AreEqual(400, baseline.RequestLatencyMs);
        Assert.AreEqual(3500, baseline.TimeToFirstLocalizationMs);
        Assert.AreEqual(2, baseline.Attempts);
        Assert.AreEqual(1, baseline.Retries);
        Assert.AreEqual(1, baseline.UnansweredAttempts);
    }

    [Test]
    public void TimeoutsLeaveRequestLatencyUnset()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteSessionOpened(0);
        baseline.NoteAttemptStarted(100);
        baseline.NoteAttemptStarted(30100);
        Assert.IsNull(baseline.RequestLatencyMs);
        Assert.IsNull(baseline.TimeToFirstLocalizationMs);
        Assert.AreEqual(2, baseline.UnansweredAttempts);
        Assert.AreEqual(1, baseline.Retries);
    }

    [Test]
    public void SignTimeIsTheFirstConfirmedSignOnly()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteSignConfirmed(500);
        Assert.IsNull(baseline.TimeToSignMs);
        baseline.NoteSessionOpened(1000);
        baseline.NoteSignConfirmed(2200);
        baseline.NoteSignConfirmed(5000);
        Assert.AreEqual(1200, baseline.TimeToSignMs);
        Assert.IsNull(baseline.TimeToFirstLocalizationMs, "a sign is not a pose");
        StringAssert.EndsWith(" timeToSignMs=1200 timeToSignFixMs=na poseSource=na ocrLatencyMs=na poseEstimateUs=na", baseline.LogLine());
    }

    [Test]
    public void SignFixTimeAndPoseSourceAreTheFirstOnly()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteSessionOpened(1000);
        baseline.NoteSignFix(4000);
        baseline.NoteSignFix(9000);
        Assert.AreEqual(3000, baseline.TimeToSignFixMs);
        Assert.IsNull(baseline.TimeToFirstLocalizationMs, "a sign fix that was not applied is not the pose");
        baseline.NotePoseAccepted(4100, fromSigns: true);
        baseline.NotePoseAccepted(8000);
        StringAssert.EndsWith(" timeToSignFixMs=3000 poseSource=sign ocrLatencyMs=na poseEstimateUs=na", baseline.LogLine());
    }

    [Test]
    public void OcrLatencyAndPoseEstimateAreTheFirstOnly()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteOcrRead(90);
        baseline.NotePoseEstimate(40);
        baseline.NoteSessionOpened(1000);
        baseline.NoteOcrRead(120);
        baseline.NoteOcrRead(300);
        baseline.NotePoseEstimate(850);
        baseline.NotePoseEstimate(20);
        StringAssert.EndsWith(" ocrLatencyMs=120 poseEstimateUs=850", baseline.LogLine());
    }

    [Test]
    public void ArrowDisplayIsNotWorldAlignment()
    {
        var baseline = new IndoorArBaseline();
        baseline.NoteSessionOpened(1000);
        baseline.NotePoseAccepted(1600);
        baseline.NoteArrowShown(3100);
        baseline.NoteArrowShown(4000);
        baseline.NoteTrackingLost();

        Assert.AreEqual(2100, baseline.ArrowShownMs);
        Assert.IsFalse(baseline.ArrowAlignmentMeasured);
        Assert.IsNull(baseline.RequestLatencyMs);
        Assert.AreEqual(1, baseline.TrackingLosses);
        Assert.AreEqual(
            "[IndoorArBaseline] arStartupMs=na requestLatencyMs=na timeToFirstLocalizationMs=600 arrowShownMs=2100 arrowAlignment=unmeasured attempts=0 retries=0 trackingLosses=1 unansweredAttempts=0 timeToSignMs=na timeToSignFixMs=na poseSource=multiset ocrLatencyMs=na poseEstimateUs=na",
            baseline.LogLine());
    }
}
