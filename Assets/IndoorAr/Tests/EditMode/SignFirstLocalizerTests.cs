using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class SignFirstLocalizerTests
{
    [Test]
    public void WithoutDeviceValidatedCalibrationMultiSetStartsImmediately()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAnswer = true;
            var first = new SignFirstLocalizer(inner,
                new IndoorSignCalibration { signs = new[] { IndoorSignPoseTests.Sign(device: "OtherPhone2,1") } });
            Assert.IsFalse(first.SignsCanPose);
            var localized = 0;
            first.Localized += _ => localized++;
            first.Authenticate();
            first.Localize();
            Assert.AreEqual(1, inner.AttemptCount);
            Assert.AreEqual(0, localized);
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(Vector3.zero, host.transform.position);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void ValidatedPoseIsCheckedBeforePlacementAndMultiSetRemainsFallback()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            var calibration = IndoorSignPoseTests.ValidatedHere(out var sighting, out var camera);
            var first = new SignFirstLocalizer(inner, calibration);
            var localized = 0;
            first.Localized += _ => localized++;
            first.Authenticate();
            first.Localize();
            first.Tick(SignFirstLocalizer.HoldS - 1f);
            Assert.AreEqual(0, inner.AttemptCount);

            first.CandidatePoseValidator = _ => false;
            Assert.AreEqual(IndoorSignPoseStatus.Implausible, first.ObservePose(sighting));
            Assert.AreEqual(0, localized);
            Assert.AreEqual(Vector3.zero, host.transform.position);

            first.CandidatePoseValidator = null;
            Assert.AreEqual(IndoorSignPoseStatus.Ok, first.ObservePose(sighting));
            Assert.AreEqual(1, localized);
            Assert.IsTrue(first.FromSigns);
            Assert.Less(Vector3.Distance(sighting.CameraWorldPosition, host.transform.TransformPoint(camera)), .01f);
            first.Tick(5f);
            Assert.AreEqual(0, inner.AttemptCount);

            first.Localize();
            Assert.IsFalse(first.FromSigns);
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, inner.AttemptCount);
            Assert.AreEqual(2, localized);
            Assert.IsFalse(first.FromSigns);

            host.transform.SetPositionAndRotation(new Vector3(9f, 9f, 9f), Quaternion.identity);
            Assert.AreEqual(IndoorSignPoseStatus.Ok, first.ObservePose(sighting));
            Assert.AreEqual(new Vector3(9f, 9f, 9f), host.transform.position);
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(2, localized);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void ValidatedSignDoesNotWaitForMultiSetAuthentication()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAuth = true;
            var first = new SignFirstLocalizer(inner, IndoorSignPoseTests.ValidatedHere(out var sighting, out _));
            var authenticated = 0;
            var failed = 0;
            first.Authenticated += () => authenticated++;
            first.AuthFailed += _ => failed++;
            first.Authenticate();
            Assert.AreEqual(1, authenticated);
            Assert.AreEqual(1, inner.AuthenticationCalls);

            first.Localize();
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(0, inner.AttemptCount);
            inner.CompleteAuth();
            Assert.AreEqual(1, authenticated);
            Assert.AreEqual(1, inner.AttemptCount);

            first.Reset();
            first.Authenticate();
            inner.FailAuth(true);
            Assert.AreEqual(0, failed);
            Assert.IsFalse(first.FallbackAvailable);
            Assert.AreEqual("unavailable: authentication failed", first.FallbackStatus);
            first.Localize();
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, inner.AttemptCount);
            first.Localize();
            Assert.AreEqual(IndoorSignPoseStatus.Ok, first.ObservePose(sighting));
            Assert.IsTrue(first.FromSigns);
        }
        finally { Object.DestroyImmediate(host); }
    }

    // The shipped calibration is empty: room-sign OCR must still run when VPS sign-in fails or is slow.
    [Test]
    public void WithoutCalibrationRoomSignsDoNotWaitForMultiSetAuthentication()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAuth = true;
            var first = new SignFirstLocalizer(inner);
            Assert.IsFalse(first.SignsCanPose);
            var authenticated = 0;
            var failed = 0;
            var localized = 0;
            first.Authenticated += () => authenticated++;
            first.AuthFailed += _ => failed++;
            first.Localized += _ => localized++;

            first.Authenticate();
            Assert.AreEqual(1, authenticated, "the session starts, so OCR reads signs");
            first.Localize();
            Assert.AreEqual(0, inner.AttemptCount, "no VPS query before VPS sign-in");
            inner.FailAuth(true);
            Assert.AreEqual(0, failed, "offline VPS does not end the session");
            Assert.IsFalse(first.FallbackAvailable);
            Assert.AreEqual("unavailable: authentication failed", first.FallbackStatus);
            first.Localize();
            Assert.AreEqual(0, inner.AttemptCount);
            Assert.AreEqual(0, localized, "no pose without a calibrated sign or VPS");
            Assert.AreEqual(Vector3.zero, host.transform.position);

            // Delayed sign-in: the attempt the session asked for runs once VPS is ready, and its pose is the fallback.
            first.Reset();
            first.Authenticate();
            Assert.AreEqual(2, authenticated);
            first.Localize();
            Assert.AreEqual(0, inner.AttemptCount);
            inner.CompleteAuth();
            Assert.AreEqual(1, inner.AttemptCount);
            Assert.AreEqual(1, localized);
            Assert.IsFalse(first.FromSigns);
            Assert.IsTrue(first.FallbackAvailable);
            Assert.AreEqual(0, failed);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void WithOcrOffAndNoCalibrationMultiSetAuthenticationStillGatesTheSession()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAuth = true;
            var first = new SignFirstLocalizer(inner, readsSigns: false);
            var authenticated = 0;
            var failed = 0;
            first.Authenticated += () => authenticated++;
            first.AuthFailed += _ => failed++;
            first.Authenticate();
            Assert.AreEqual(0, authenticated);
            inner.FailAuth(false);
            Assert.AreEqual(1, failed);

            first.Authenticate();
            inner.CompleteAuth();
            Assert.AreEqual(1, authenticated);
            first.Localize();
            Assert.AreEqual(1, inner.AttemptCount, "VPS-only: asked at once");
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DroppedFallbackCannotDispatchAfterDelayedAuthentication(bool calibrated, bool duringHold)
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAuth = true;
            inner.DeferAnswer = true;
            var first = new SignFirstLocalizer(inner,
                calibrated ? IndoorSignPoseTests.ValidatedHere(out _, out _) : null);
            first.Authenticate();
            first.Localize();
            if (!duringHold) first.Tick(SignFirstLocalizer.HoldS);
            first.DropQueuedFallback();
            inner.CompleteAuth();
            first.Tick(SignFirstLocalizer.HoldS * 2f);
            Assert.AreEqual(0, inner.AttemptCount);

            first.Reset();
            first.Authenticate();
            first.Localize();
            first.Tick(SignFirstLocalizer.HoldS);
            inner.CompleteAuth();
            Assert.AreEqual(1, inner.AttemptCount);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void SessionRetryDuringHoldDoesNotQueryMultiSetEarly()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.Succeed = false;
            var first = new SignFirstLocalizer(inner, IndoorSignPoseTests.ValidatedHere(out _, out _));
            first.Authenticate();
            first.Localize();
            first.Localize();
            Assert.AreEqual(0, inner.AttemptCount);
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, inner.AttemptCount);
            first.Localize();
            Assert.AreEqual(2, inner.AttemptCount);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
