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
            "[IndoorArBaseline] arStartupMs=na requestLatencyMs=na timeToFirstLocalizationMs=600 arrowShownMs=2100 arrowAlignment=unmeasured attempts=0 retries=0 trackingLosses=1 unansweredAttempts=0",
            baseline.LogLine());
    }
}
