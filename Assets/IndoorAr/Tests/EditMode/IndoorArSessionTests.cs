using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorArSessionTests
{
    const string MapCode = "MSET_XTBY7AM7ZK77";

    static IndoorArRequest Vec119(string frame = "multiset-mapset", string mapCode = MapCode) =>
        new IndoorArRequest
        {
            schemaVersion = 1, room = "119", doorKey = "VEC119", floor = "1",
            position = new IndoorArVector3 { x = -5.7651f, y = -1.8186f, z = 22.6689f },
            headingDeg = 185.36f, mapCode = mapCode, frame = frame,
        };

    static IndoorArSession Localizing(out int localizeRequests)
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var count = 0;
        session.LocalizeRequested += () => count++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        localizeRequests = count;
        return session;
    }

    [TestCase("unity-world", MapCode)]
    [TestCase("multiset-mapset", "MSET_OTHER")]
    public void WrongFrameOrMapIsMapMismatch(string frame, string mapCode)
    {
        var session = new IndoorArSession(Vec119(frame, mapCode), MapCode);
        Assert.AreEqual(IndoorArState.Done, session.State);
        Assert.AreEqual(
            "{\"status\":\"error\",\"errorCode\":\"MAP_MISMATCH\",\"localizationQueries\":0,\"durationMs\":0}",
            session.ResultJson);
    }

    [Test]
    public void UnsupportedDeviceIsArcoreUnsupported()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        session.OnArSupport(false);
        StringAssert.Contains("\"errorCode\":\"ARCORE_UNSUPPORTED\"", session.ResultJson);
    }

    [TestCase(false, "MULTISET_AUTH")]
    [TestCase(true, "NETWORK")]
    public void AuthFailureIsReported(bool offline, string code)
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        session.OnArSupport(true);
        Assert.AreEqual(IndoorArState.Auth, session.State);
        session.OnAuthFailed(offline);
        StringAssert.Contains($"\"errorCode\":\"{code}\"", session.ResultJson);
    }

    [Test]
    public void AuthStartsLocalizingAndCountsQueries()
    {
        var session = Localizing(out var requests);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(1, requests);
        session.OnQuerySent();
        session.OnQuerySent();
        Assert.AreEqual(2, session.Queries);
    }

    [Test]
    public void TimeoutRetriesOnceThenFails()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var requests = 0;
        session.LocalizeRequested += () => requests++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnQuerySent();

        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(2, requests);

        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(
            "{\"status\":\"localizationFailed\",\"localizationQueries\":1,\"durationMs\":60000}",
            session.ResultJson);
    }

    [Test]
    public void TrackingLossGoesBackToLocalizingWithAFreshRetry()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var requests = 0;
        session.LocalizeRequested += () => requests++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.OnLocalized();
        Assert.AreEqual(IndoorArState.Pinned, session.State);

        session.OnTrackingLost();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.IsTrue(session.WasPinned);
        Assert.AreEqual(3, requests);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(IndoorArState.Localizing, session.State, "retry budget resets after tracking loss");
    }

    [Test]
    public void ImHereReportsHorizontalDistanceOnlyWhenPinned()
    {
        var session = Localizing(out _);
        var camera = new Vector3(-5.7651f + 3f, 10f, 22.6689f + 4f);
        session.ImHere(camera);
        Assert.AreEqual(IndoorArState.Localizing, session.State, "ignored before the pin shows");

        session.OnLocalized();
        session.Tick(1.5f);
        session.ImHere(camera);
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"checkDistanceM\":5,\"localizationQueries\":0,\"durationMs\":1500}",
            session.ResultJson);
    }

    [Test]
    public void HorizontalDistanceIgnoresHeight()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        Assert.AreEqual(0f, session.HorizontalDistance(new Vector3(-5.7651f, 50f, 22.6689f)), 1e-4f);
    }

    [TestCase(-0.4f, false)]
    [TestCase(3.7f, true)]
    public void WrongFloorUsesTheDoorHeight(float cameraY, bool wrong)
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        Assert.AreEqual(wrong, session.IsWrongFloor(new Vector3(0f, cameraY, 0f)));
    }

    [Test]
    public void CancelWorksInAnyStateAndOnlyOnce()
    {
        var session = Localizing(out _);
        session.Tick(0.25f);
        session.Cancel();
        session.Cancel();
        session.OnLocalized();
        Assert.AreEqual(IndoorArState.Done, session.State);
        Assert.AreEqual(
            "{\"status\":\"cancelled\",\"localizationQueries\":0,\"durationMs\":250}",
            session.ResultJson);
    }
}
