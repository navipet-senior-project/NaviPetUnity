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

    [TestCase(false)]
    [TestCase(true)]
    public void StartupTimeoutReportsNetworkAndIgnoresLateAuthentication(bool auth)
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        if (auth) session.OnArSupport(true);
        session.Tick(19f);
        Assert.AreEqual(auth ? IndoorArState.Auth : IndoorArState.Init, session.State);
        session.Tick(1f);
        Assert.AreEqual(IndoorArState.Done, session.State);
        var code = auth ? "MULTISET_AUTH" : "NETWORK";
        Assert.AreEqual($"{{\"status\":\"error\",\"errorCode\":\"{code}\",\"localizationQueries\":0,\"durationMs\":20000}}", session.ResultJson);
        session.OnArSupport(true);
        session.OnAuthenticated();
        Assert.AreEqual(IndoorArState.Done, session.State);
    }

    [Test]
    public void AuthenticationGetsItsOwnTimeoutBudget()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        session.Tick(19f);
        session.OnArSupport(true);
        session.Tick(19f);
        Assert.AreEqual(IndoorArState.Auth, session.State);
        session.Tick(1f);
        StringAssert.Contains("\"errorCode\":\"MULTISET_AUTH\"", session.ResultJson);
    }

    [Test]
    public void AuthStartsLocalizingAndCountsAttempts()
    {
        var session = Localizing(out var requests);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(1, requests);
        session.OnAttemptStarted();
        session.OnAttemptStarted();
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
        session.OnAttemptStarted();

        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(2, requests);

        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(
            "{\"status\":\"localizationFailed\",\"localizationQueries\":1,\"durationMs\":60000}",
            session.ResultJson);
    }

    [Test]
    public void NoPoseAnswerSpendsTheOneRetrySoonInsteadOfAtTheTimeout()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var requests = 0;
        session.LocalizeRequested += () => requests++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnAttemptStarted();
        session.Tick(1f);
        session.OnNotLocalized();

        session.Tick(IndoorArSession.NoPoseRetryS - 0.1f);
        Assert.AreEqual(1, requests, "the retry waits for a new view");
        session.Tick(0.2f);
        Assert.AreEqual(2, requests);

        session.OnNotLocalized();
        session.Tick(IndoorArSession.NoPoseRetryS);
        Assert.AreEqual(2, requests, "the attempt budget is unchanged");
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        StringAssert.StartsWith("{\"status\":\"localizationFailed\"", session.ResultJson);
    }

    [Test]
    public void LateNoPoseUsesExistingTimeoutForTheOneRetry()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var requests = 0;
        session.LocalizeRequested += () => requests++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.Tick(IndoorArSession.LocalizeTimeoutS - 1f);
        session.OnNotLocalized();
        session.Tick(0.9f);
        Assert.AreEqual(1, requests);
        session.Tick(0.1f);
        Assert.AreEqual(2, requests);
        session.Tick(IndoorArSession.NoPoseRetryS);
        Assert.AreEqual(2, requests);
    }

    [Test]
    public void SignRecoveryOutlivesVpsBudgetWithoutRetryingFallback()
    {
        var session = new IndoorArSession(Vec119(), MapCode, allowSignRecovery: true);
        var requests = 0;
        var stopped = 0;
        session.LocalizeRequested += () => requests++;
        session.FallbackStopped += () => stopped++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.IsTrue(session.FallbackExpired);
        Assert.AreEqual(1, stopped);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.IsNull(session.ResultJson);
        session.OnNotLocalized();
        session.OnSignConfirmed(Sign120);
        session.OnSignInView();
        session.Tick(300f);
        Assert.AreEqual(2, requests);
        Assert.AreEqual(1, stopped);
        Assert.AreSame(Sign120, session.SignRoom);
        session.OnLocalized();
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        session.Cancel();
        Assert.AreEqual(2, stopped);
        StringAssert.Contains("\"status\":\"cancelled\"", session.ResultJson);
        session.OnLocalized();
        Assert.AreEqual(IndoorArState.Done, session.State);
    }

    [TestCase("timeout")]
    [TestCase("auth")]
    [TestCase("cancel")]
    [TestCase("arrival")]
    [TestCase("unavailable")]
    public void EveryTerminalTransitionStopsFallback(string terminal)
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var stopped = 0;
        session.FallbackStopped += () => stopped++;
        session.OnArSupport(true);
        if (terminal == "auth") session.OnAuthFailed(false);
        else
        {
            session.OnAuthenticated();
            switch (terminal)
            {
                case "timeout":
                    session.Tick(IndoorArSession.LocalizeTimeoutS);
                    session.Tick(IndoorArSession.LocalizeTimeoutS);
                    break;
                case "cancel": session.Cancel(); break;
                case "arrival":
                    session.OnLocalized();
                    session.ImHere(Vector3.zero);
                    session.ConfirmArrival();
                    break;
                case "unavailable": session.OnStepFreeUnverified(); break;
            }
        }
        Assert.AreEqual(IndoorArState.Done, session.State);
        Assert.AreEqual(1, stopped);
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
        Assert.AreEqual(IndoorArState.Arrived, session.State);
        Assert.IsNull(session.ResultJson);
        session.ConfirmArrival();
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"checkDistanceM\":5,\"localizationQueries\":0,\"durationMs\":1500}",
            session.ResultJson);
    }

    [Test]
    public void ArrivalUsesLatestLocalizationConfidence()
    {
        var session = Localizing(out _);
        session.OnLocalized(0.2f);
        session.OnTrackingLost();
        session.OnLocalized(0.73f);
        session.ImHere(new Vector3(-5.7651f, 0f, 22.6689f));
        session.ConfirmArrival();
        StringAssert.Contains("\"confidence\":0.73,", session.ResultJson);
    }

    [Test]
    public void FailedScanCanRetryAndLocalizeOnlyCannotArrive()
    {
        var request = new IndoorArRequest { schemaVersion = 1, mode = "localize", mapCode = MapCode, frame = IndoorArContract.Frame };
        var session = new IndoorArSession(request, MapCode);
        int attempts = 0;
        session.LocalizeRequested += () => attempts++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(IndoorArState.Done, session.State);
        session.Retry();
        Assert.AreEqual(3, attempts);
        session.OnLocalized();
        session.ImHere(Vector3.zero);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        var view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 10f);
        Assert.AreEqual(IndoorHudState.Localized, view.State);
        Assert.IsTrue(view.LocalizeOnly);
        session.Cancel();
        StringAssert.Contains("cancelled", session.ResultJson);
    }

    [Test]
    public void AttemptCanCountWithoutALocalizationResponse()
    {
        var session = Localizing(out _);
        session.OnAttemptStarted(); // capture/blur/offline checks may prevent a real VPS call
        Assert.AreEqual(1, session.Queries);
        session.Cancel();
        StringAssert.Contains("\"localizationQueries\":1,", session.ResultJson);
    }

    [Test]
    public void HorizontalDistanceIgnoresHeight()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        Assert.AreEqual(0f, session.HorizontalDistance(new Vector3(-5.7651f, 50f, 22.6689f)), 1e-4f);
    }

    [Test]
    public void FloorEvidenceNeverUsesDestinationOrUnknownHeight()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        Assert.AreEqual(0, session.CurrentFloor(2.2f, new[] { (1, -2f), (2, 2.2f) }), "destination floor 1 is not inferred from height");
        Assert.AreEqual(0, session.CurrentFloor(20f, new[] { (1, -2f), (2, 2.2f) }), "out-of-range height is unknown");
        Assert.IsFalse(session.IsWrongFloor(0));
        Assert.IsTrue(session.IsWrongFloor(2));
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

    static readonly IndoorRoomLandmark Sign120 = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");

    [Test]
    public void AConfirmedSignFloorOverridesTheCameraHeight()
    {
        var session = Localizing(out _);
        var upstairsHeight = 4f;
        Assert.AreEqual(2, session.CurrentFloor(upstairsHeight, new[] { (1, -2f), (2, 2.2f) }));
        session.OnSignConfirmed(Sign120);
        Assert.AreEqual(1, session.CurrentFloor(upstairsHeight, new[] { (1, -2f), (2, 2.2f) }), "confirmed sign wins over height");
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
        Assert.IsTrue(session.IsWrongFloor(session.CurrentFloor(0f, new[] { (1, -2f), (2, 2.2f) })), "sign floor is current-floor evidence");
        Assert.AreEqual(2, new IndoorRoomLandmark("VEC215", "VEC", "floor_2").Floor);
        Assert.AreEqual(0, new IndoorRoomLandmark("VEC1", "VEC", "lobby").Floor);
    }

    [Test]
    public void OverlappingEyeHeightBandsCannotAcceptAPoseOrChangeTheTrackedFloor()
    {
        var floors = new[] { (1, -2f), (3, 3f), (2, 2.2f) };
        var session = Localizing(out _);
        const float overlap = 4.3f;
        Assert.AreEqual(0, session.CurrentFloor(overlap, floors));
        Assert.IsTrue(session.PoseConflictsWithSign(overlap, floors));
        session.OnSignConfirmed(Sign120);
        session.OnLocalized();
        session.OnTrackedPose(0f, floors);
        session.OnTrackedPose(overlap, floors);
        Assert.AreSame(Sign120, session.FloorSign);
        session.OnTrackedPose(5f, floors);
        Assert.IsNull(session.FloorSign);
        Assert.AreEqual(3, session.CurrentFloor(5f, floors));
    }

    [Test]
    public void TrackedWalkToAnotherFloorSupersedesTheLastSignUntilASignIsReadAgain()
    {
        const float floor1 = 0f, floor2 = 4f, between = 2f;
        var session = Localizing(out _);
        session.OnSignConfirmed(Sign120);
        session.OnTrackedPose(floor2, new[] { (1, -2f), (2, 2.2f) });
        Assert.AreSame(Sign120, session.FloorSign, "no accepted pose yet: height is not tracked motion");

        session.OnLocalized();
        session.OnTrackedPose(floor1, new[] { (1, -2f), (2, 2.2f) });
        session.OnTrackedPose(between, new[] { (1, -2f), (2, 2.2f) });
        session.OnTrackedPose(20f, new[] { (1, -2f), (2, 2.2f) });
        Assert.AreSame(Sign120, session.FloorSign, "between or outside floors changes nothing");
        session.OnTrackedPose(floor2, new[] { (1, -2f), (2, 2.2f) });
        Assert.IsNull(session.FloorSign, "tracked ascent supersedes the floor-1 sign");
        Assert.AreSame(Sign120, session.SignRoom, "the sign that was read is still shown");
        Assert.AreEqual(2, session.CurrentFloor(floor2, new[] { (1, -2f), (2, 2.2f) }));
        Assert.IsFalse(session.PoseConflictsWithSign(floor2, new[] { (1, -2f), (2, 2.2f) }));

        session.OnSignConfirmed(Sign120);
        Assert.AreEqual(1, session.CurrentFloor(floor2, new[] { (1, -2f), (2, 2.2f) }), "a fresh sign wins again");
        Assert.IsTrue(session.PoseConflictsWithSign(floor2, new[] { (1, -2f), (2, 2.2f) }));

        // Tracked floor is 2. Tracking loss breaks continuity: the first height after the next pose is a new baseline.
        session.OnTrackingLost();
        session.OnTrackedPose(floor1, new[] { (1, -2f), (2, 2.2f) });
        session.OnLocalized();
        session.OnTrackedPose(floor1, new[] { (1, -2f), (2, 2.2f) });
        Assert.AreSame(Sign120, session.FloorSign, "a jump across a tracking gap is not a tracked walk");
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void FreshMatchingSignSurvivesTheNextTrackedFloorSample(int from, int to)
    {
        var session = Localizing(out _);
        var original = new IndoorRoomLandmark("original", "VEC", "floor_" + from);
        var fresh = new IndoorRoomLandmark("fresh", "VEC", "floor_" + to);
        session.OnSignConfirmed(original);
        session.OnLocalized();
        session.OnTrackedPose(from == 1 ? 0f : 4f, new[] { (1, -2f), (2, 2.2f) });
        session.OnSignConfirmed(fresh);
        session.OnTrackedPose(to == 1 ? 0f : 4f, new[] { (1, -2f), (2, 2.2f) });
        Assert.AreSame(fresh, session.FloorSign);
        session.OnTrackingLost();
        Assert.IsTrue(session.PoseConflictsWithSign(from == 1 ? 0f : 4f, new[] { (1, -2f), (2, 2.2f) }));
        Assert.IsFalse(session.PoseConflictsWithSign(to == 1 ? 0f : 4f, new[] { (1, -2f), (2, 2.2f) }));
    }

    [Test]
    public void MultiSetPoseConflictRetriesAndAgreeingPoseIsAccepted()
    {
        var session = Localizing(out _);
        var retries = 0;
        session.LocalizeRequested += () => retries++;
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
        Assert.IsTrue(session.PoseConflictsWithSign(6.5f, new[] { (1, -2f), (2, 2.2f) }), "floor-3-height pose conflicts with floor-2 sign");
        session.OnNotLocalized();
        session.OnSignInView();
        Assert.AreEqual(1, retries);
        Assert.IsFalse(session.WasPinned);

        var agreeing = Localizing(out _);
        agreeing.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
        Assert.IsFalse(agreeing.PoseConflictsWithSign(4f, new[] { (1, -2f), (2, 2.2f) }));
        agreeing.OnLocalized();
        Assert.IsTrue(agreeing.WasPinned);
    }

    [Test]
    public void UnsupportedSignAndUnmappedPoseCannotEstablishRouteFloor()
    {
        var session = Localizing(out _);
        Assert.AreEqual(0, session.CurrentFloor(6.5f, new[] { (1, -2f), (2, 2.2f) }));
        Assert.IsTrue(session.PoseConflictsWithSign(6.5f, new[] { (1, -2f), (2, 2.2f) }), "a pose resolving above supported floors is rejected even without a sign");
        Assert.AreEqual(0, session.CurrentFloor(-5f, new[] { (1, -2f), (2, 2.2f) }));
        Assert.IsTrue(session.PoseConflictsWithSign(-5f, new[] { (1, -2f), (2, 2.2f) }), "a pose below Floor 1 is rejected");
        var inBand = Localizing(out _);
        Assert.IsFalse(inBand.PoseConflictsWithSign(-0.4f, new[] { (1, -2f), (2, 2.2f) }), "a pose in Floor 1's eye-height band is accepted");
        inBand.OnLocalized();
        Assert.IsTrue(inBand.WasPinned);
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC999", "VEC", "floor_3"));
        Assert.AreEqual(0, session.CurrentFloor(0f, new[] { (1, -2f), (2, 2.2f) }));
        Assert.IsTrue(session.PoseConflictsWithSign(0f, new[] { (1, -2f), (2, 2.2f) }));
    }

    [Test]
    public void StepFreeWithoutEvidenceReturnsUnavailableAndNoHudRoute()
    {
        var request = Vec119();
        request.stepFree = true;
        request.floor = "2";
        var session = new IndoorArSession(request, MapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        session.OnSignConfirmed(Sign120);
        session.OnStepFreeUnverified();
        StringAssert.Contains("\"status\":\"unavailable\"", session.ResultJson);
        StringAssert.Contains("\"errorCode\":\"STEP_FREE_UNVERIFIED\"", session.ResultJson);
        var view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 2f);
        Assert.IsFalse(view.FollowingRoute);
        Assert.AreEqual(IndoorHudState.NotFound, view.State);
        Assert.AreEqual("A step-free route to this destination cannot be confirmed.", view.DetailOverride);
    }


    [TestCase("VEC119", "floor_1", true)]
    [TestCase("VEC119", "floor_2", false)]
    [TestCase("VEC120", "floor_1", false)]
    public void OnlyMatchingDestinationSignStopsNavigation(string room, string floor, bool arrived)
    {
        var session = Localizing(out _);
        session.OnSignConfirmed(new IndoorRoomLandmark(room, "VEC", floor));
        var view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f);
        Assert.AreEqual(arrived ? IndoorHudState.Arrived : IndoorHudState.Scanning, view.State);
        Assert.AreEqual(session.SignRoom.Floor, view.CurrentFloor);
        Assert.IsFalse(view.FollowingRoute);
        Assert.IsFalse(session.WasPinned);
        if (arrived)
        {
            Assert.AreEqual("Found the VEC 119 sign · Floor 1", view.DetailOverride);
            session.ConfirmArrival();
            StringAssert.Contains("\"status\":\"arrived\"", session.ResultJson);
            StringAssert.Contains("\"signRoom\":\"VEC119\"", session.ResultJson);
        }
    }

    [Test]
    public void SignArrivalOmitsDistanceOnlyForHostsThatSentTheCapability()
    {
        var capable = Vec119();
        capable.capabilities = new[] { IndoorArContract.SignArrivalCapability };
        var session = new IndoorArSession(capable, MapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC119", "VEC", "floor_1"));
        session.ConfirmArrival();
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"localizationQueries\":0,\"durationMs\":0,\"signRoom\":\"VEC119\"}",
            session.ResultJson);

        var legacy = Localizing(out _);
        legacy.OnSignConfirmed(new IndoorRoomLandmark("VEC119", "VEC", "floor_1"));
        legacy.ConfirmArrival();
        StringAssert.Contains("\"checkDistanceM\":0,", legacy.ResultJson, "legacy hosts still require the field");
        StringAssert.Contains("\"signRoom\":\"VEC119\"", legacy.ResultJson);

        var other = Vec119();
        other.capabilities = new[] { "somethingElse" };
        Assert.IsFalse(other.SupportsSignArrival);
    }

    [Test]
    public void CapableHostStillGetsTheMeasuredDistanceForManualArrival()
    {
        var request = Vec119();
        request.capabilities = new[] { IndoorArContract.SignArrivalCapability };
        var session = new IndoorArSession(request, MapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        session.Tick(1.5f);
        session.ImHere(new Vector3(-5.7651f + 3f, 10f, 22.6689f + 4f));
        session.ConfirmArrival();
        StringAssert.Contains("\"checkDistanceM\":5", session.ResultJson);
    }

    [Test]
    public void ConfirmedSignIsReportedButNeverPins()
    {
        var session = Localizing(out _);
        session.OnSignConfirmed(Sign120);
        session.OnSignInView();
        Assert.AreEqual(IndoorArState.Localizing, session.State, "room identity is not a pose");
        Assert.IsFalse(session.WasPinned);
        session.Cancel();
        Assert.AreEqual(
            "{\"status\":\"cancelled\",\"localizationQueries\":0,\"durationMs\":0,\"signRoom\":\"VEC120\"}",
            session.ResultJson);
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC119", "VEC", "floor_1"));
        StringAssert.Contains("VEC120", session.ResultJson, "a finished result does not change");
    }

    [Test]
    public void SignInViewSpendsTheRetryEarlyOnlyAfterANoPoseAnswer()
    {
        var session = new IndoorArSession(Vec119(), MapCode);
        var requests = 0;
        session.LocalizeRequested += () => requests++;
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnAttemptStarted();
        session.OnSignConfirmed(Sign120);
        session.OnSignInView();
        Assert.AreEqual(1, requests, "no early retry while the attempt is still open");

        session.OnNotLocalized();
        session.OnSignInView();
        Assert.AreEqual(2, requests);
        session.OnNotLocalized();
        session.OnSignInView();
        Assert.AreEqual(2, requests, "one retry per localizing pass");

        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(IndoorArState.Done, session.State, "the attempt budget is unchanged");
        StringAssert.StartsWith("{\"status\":\"localizationFailed\"", session.ResultJson);
        StringAssert.EndsWith(",\"signRoom\":\"VEC120\"}", session.ResultJson);
    }

    [Test]
    public void ResultHasNoSignRoomWithoutASign()
    {
        var session = Localizing(out _);
        session.OnNotLocalized();
        session.OnSignInView();
        session.Cancel();
        StringAssert.DoesNotContain("signRoom", session.ResultJson);
    }

    [Test]
    public void UnknownGapPoseIsRejectedWithOrWithoutSignEvidence()
    {
        var session = Localizing(out _);
        Assert.AreEqual(0, session.PoseFloor(1f, new[] { (1, -2f), (2, 2.3f) }));
        Assert.IsTrue(session.PoseConflictsWithSign(1f, new[] { (1, -2f), (2, 2.3f) }), "gap remains unsupported without a sign");
        session.OnSignConfirmed(Sign120);
        Assert.IsTrue(session.PoseConflictsWithSign(1f, new[] { (1, -2f), (2, 2.3f) }), "a sign cannot make an unknown pose acceptable");
        Assert.IsFalse(session.PoseConflictsWithSign(-0.5f, new[] { (1, -2f), (2, 2.3f) }), "a supported floor-1 pose remains acceptable");
    }
}
