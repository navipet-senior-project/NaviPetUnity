using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IndoorAr;
using MultiSet;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;

public sealed class RunnerFakeLocalizer : MonoBehaviour, IIndoorArLocalizer
{
    public event Action Authenticated;
    public event Action<bool> AuthFailed;
    public event Action<int> AttemptStarted;
    public event Action<float?> Localized;
    public event Action<int> NotLocalized;
    public Transform MapSpace => transform;
    public string MapCode => "MSET_XTBY7AM7ZK77";
    public int AuthenticationCalls { get; private set; }
    public int AttemptCount { get; private set; }
    public int LastAttemptId { get; private set; }
    public bool DeferAuth;
    public void Authenticate() { AuthenticationCalls++; if (!DeferAuth) Authenticated?.Invoke(); }
    public void CompleteAuth() => Authenticated?.Invoke();
    public void FailAuth(bool network) => AuthFailed?.Invoke(network);
    public bool Succeed = true;
    public bool DeferAnswer;
    public void Localize()
    {
        LastAttemptId = ++AttemptCount;
        AttemptStarted?.Invoke(LastAttemptId);
        if (DeferAnswer) return;
        if (Succeed) Localized?.Invoke(0.73f);
        else NotLocalized?.Invoke(LastAttemptId);
    }
    public void CompleteWithoutPose(int attemptId) => NotLocalized?.Invoke(attemptId);
    public void CompleteLocalized(float? confidence) => Localized?.Invoke(confidence);
}

public class IndoorArRunnerTests
{
    const string ConfirmedFloorEvidence = ",\"floorEvidenceVersion\":1,\"startFloor\":\"1\",\"startFloorAgeS\":0," +
        "\"pressureSinceStart\":{\"settled\":true,\"moved\":false,\"sampleAgeS\":0}";

    GameObject _host;
    GameObject _pinPrefab;
    string _request;

    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    // Only a visible floor path replaces the screen arrow.
    static void AssertPath(IndoorArRunner runner, bool shown)
    {
        var path = Field<IndoorRoutePath>(runner, "_path");
        Assert.AreEqual(shown, path != null && path.Root.activeInHierarchy && path.Root.GetComponent<MeshRenderer>().enabled, "floor path");
        if (shown) Assert.IsFalse(ArrowVisible(runner), "no arrow beside the path");
    }

    // A route without a visible ribbon falls back to the screen arrow.
    static void AssertGuiding(IndoorArRunner runner)
    {
        var path = Field<IndoorRoutePath>(runner, "_path");
        Assert.AreNotEqual(path != null && path.Root.activeInHierarchy && path.Root.GetComponent<MeshRenderer>().enabled,
            ArrowVisible(runner), "exactly one of floor path or arrow");
    }

    static bool ArrowVisible(IndoorArRunner runner)
    {
        var arrow = Field<IndoorHud>(runner, "_hud").transform.Find("HUD Canvas/Safe area/Guide arrow");
        return arrow != null && arrow.gameObject.activeInHierarchy;
    }

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static readonly MethodInfo SetArState =
        typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
    UnityEngine.XR.ARFoundation.ARSessionState _arState;

    [SetUp]
    public void SetUp()
    {
        // Queries wait for AR tracking, so tests start from a tracking session unless they set another state.
        _arState = UnityEngine.XR.ARFoundation.ARSession.state;
        SetArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        _request = "{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"," +
            "\"position\":{\"x\":-5.7651,\"y\":-1.8186,\"z\":22.6689},\"headingDeg\":185.36," +
            "\"mapCode\":\"MSET_XTBY7AM7ZK77\",\"frame\":\"multiset-mapset\"}";
        IndoorArBridge.EditorRequestJson = _request;
        _host = new GameObject("Runner test");
        _pinPrefab = new GameObject("Pin");
    }

    [TearDown]
    public void TearDown()
    {
        SetArState.Invoke(null, new object[] { _arState });
        IndoorArBridge.EditorRequestJson = _request;
        UnityEngine.Object.DestroyImmediate(_host);
        UnityEngine.Object.DestroyImmediate(_pinPrefab);
    }

    IndoorArRunner Runner(IndoorLocalizationMode mode = IndoorLocalizationMode.MultiSet)
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, _host.AddComponent<Camera>());
        runner.SetLocalizationMode(mode);
        return runner;
    }

    [Test]
    public void OutdoorRequestInABuildWithoutTheOutdoorSceneDeliversUnavailableOnce()
    {
        IndoorArBridge.EditorRequestJson = "{\"schemaVersion\":2,\"mode\":\"outdoor\"," +
            "\"destination\":{\"name\":\"VEC\",\"latitude\":33.7829,\"longitude\":-118.1108},\"dataUseAccepted\":true}";
        var listed = EditorBuildSettings.scenes;
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
        try
        {
            var runner = Runner();
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*unavailable"));
            Assert.IsFalse(((IEnumerator)Call(runner, "Start")).MoveNext());
            Call(runner, "Update");
            Call(runner, "Update");
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            EditorBuildSettings.scenes = listed;
        }
    }

    [TestCase("BAD_REQUEST")]
    [TestCase("MAP_MISMATCH")]
    public void InvalidLaunchDeliversExactlyOnce(string code)
    {
        IndoorArBridge.EditorRequestJson = code == "BAD_REQUEST" ? "bad json"
            : _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")
                .Replace("multiset-mapset", "unity-world").TrimEnd('}') + ConfirmedFloorEvidence + "}";
        var runner = Runner();
        var sensorStarts = 0;
        runner.StartPressureSampling = () => sensorStarts++;
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*" + code));
        Assert.IsFalse(((IEnumerator)Call(runner, "Start")).MoveNext());
        Call(runner, "Update");
        Call(runner, "Update");
        Assert.AreEqual(0, sensorStarts);
        LogAssert.NoUnexpectedReceived();
    }

    [TestCase(2, "", "", false, 0, TestName = "SkippedFloorConfirmationDoesNotStartPressure")]
    [TestCase(2, "\"floorEvidenceVersion\":1", "\"floorEvidenceVersion\":2", false, 0)]
    [TestCase(2, "\"startFloor\":\"1\"", "\"startFloor\":\"unknown\"", false, 0)]
    [TestCase(2, ",\"moved\":false", "", false, 0)]
    [TestCase(2, "\"startFloorAgeS\":0", "\"startFloorAgeS\":1201", false, 0)]
    [TestCase(2, "\"sampleAgeS\":0", "\"sampleAgeS\":11", false, 0)]
    [TestCase(2, "\"startFloorAgeS\":0", "\"startFloorAgeS\":1200.000001", false, 0, TestName = "AStartAgeRoundingDownToTheLimitIsStillStale")]
    [TestCase(2, "\"sampleAgeS\":0", "\"sampleAgeS\":10.0000001", false, 0, TestName = "ASampleAgeRoundingDownToTheLimitIsStillStale")]
    [TestCase(2, "\"startFloorAgeS\":0", "\"startFloorAgeS\":-1", false, 0)]
    [TestCase(2, "\"startFloorAgeS\":0", "\"startFloorAgeS\":1200", true, 1, TestName = "AStartAgeAtTheLimitIsFresh")]
    [TestCase(2, "\"sampleAgeS\":0", "\"sampleAgeS\":10", true, 1, TestName = "ASampleAgeAtTheLimitIsFresh")]
    [TestCase(1, null, null, false, 0, TestName = "SchemaOneIgnoresCompleteFloorEvidence")]
    [TestCase(2, null, null, true, 1, TestName = "CompleteFloorConfirmationStartsPressure")]
    [TestCase(2, "\"moved\":false", "\"moved\":true", true, 0, TestName = "MovedPressureAllowsSamplingWithoutTrustingTheStartFloor")]
    public void LaunchPressureRequiresCompleteFloorConfirmation(int schemaVersion, string field, string replacement,
        bool starts, int trustedFloor)
    {
        var evidence = field == null ? ConfirmedFloorEvidence
            : field.Length == 0 ? "" : ConfirmedFloorEvidence.Replace(field, replacement);
        IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", $"\"schemaVersion\":{schemaVersion}").TrimEnd('}') +
            evidence + "}";
        var runner = Runner();
        var sensorStarts = 0;
        runner.StartPressureSampling = () => sensorStarts++;

        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        Assert.AreEqual(starts ? 1 : 0, sensorStarts);
        var session = Field<IndoorArSession>(runner, "_session");
        var heights = Field<(int Floor, float Y)[]>(runner, "_floorHeights");
        var floor2CameraY = heights.Single(floor => floor.Floor == 2).Y + 1.5f;
        Assert.AreEqual(trustedFloor, session.TrustedStartFloor);
        Assert.AreEqual(trustedFloor != 0, session.PoseConflictsWithSign(floor2CameraY, heights));
        var floor1Sign = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");
        session.OnSignConfirmed(floor1Sign);
        Assert.AreSame(floor1Sign, session.FloorSign);
        Assert.IsTrue(session.PoseConflictsWithSign(floor2CameraY, heights));

        Call(runner, "BeginSession", _request, runner.RequestClock());
        Assert.AreEqual(starts ? 1 : 0, sensorStarts, "A warm launch without confirmation must skip pressure.");
    }

    [Test]
    public void ExitDuringAvailabilityDeliversCancelledAndPreventsLaterAuth()
    {
        IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2").TrimEnd('}') +
            ConfirmedFloorEvidence + "}";
        var runner = Runner();
        var starts = 0;
        var stops = 0;
        runner.StartPressureSampling = () => starts++;
        runner.StopPressureSampling = () => stops++;
        var start = (IEnumerator)Call(runner, "Start");
        Assert.IsTrue(start.MoveNext()); // availability is still pending
        Assert.AreEqual(1, starts);
        var session = Field<IndoorArSession>(runner, "_session");
        session.Cancel();
        Assert.AreEqual(1, stops);
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*cancelled"));
        Call(runner, "Update");
        Assert.IsFalse(start.MoveNext());
        Assert.AreEqual(IndoorArState.Done, session.State);
        Call(runner, "Update");
        Assert.AreEqual(1, stops);
        LogAssert.NoUnexpectedReceived();
    }

    // Fails the first attempt pair so the retryable failure card is up; returns the pieces the retry tests share.
    (IndoorArRunner Runner, IndoorArSession Session, RunnerFakeLocalizer Localizer, IndoorRoomLandmark Sign, Func<float> Now)
        RunnerAtFailureCard(bool confirmedFloor, Action onStart = null, Action onStop = null, string deltaField = "")
    {
        if (confirmedFloor)
            IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2").TrimEnd('}') +
                ConfirmedFloorEvidence.Insert(ConfirmedFloorEvidence.Length - 1, deltaField) + "}";
        var runner = Runner();
        runner.StartPressureSampling = onStart ?? (() => { });
        runner.StopPressureSampling = onStop ?? (() => { });
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        var now = 0f;
        session.PressureClock = () => now;
        session.OnArSupport(true);
        localizer.Authenticate();
        for (var i = 0; i < 21; i++)
        {
            now += 1f;
            session.OnPressure(1000f, now);
        }
        var sign = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");
        session.OnSignConfirmed(sign);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.IsTrue(LiveDoorHud.LocalizationFailed(session));
        return (runner, session, localizer, sign, () => now);
    }

    [Test]
    public void TryAgainWithoutFloorEvidenceRetriesInPlaceWithoutPressure()
    {
        var starts = 0;
        var stops = 0;
        var (runner, session, _, sign, _) = RunnerAtFailureCard(false, () => starts++, () => stops++);
        Assert.AreEqual(0, starts);
        Assert.AreEqual(2, session.Queries);

        Call(runner, "OnHudRetry");
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(3, session.Queries);
        Assert.AreEqual(0, starts);
        Assert.AreEqual(0, stops);
        Assert.AreSame(sign, session.FloorSign);
    }

    [TestCase("")]
    [TestCase(",\"deltaHpa\":null")]
    public void TryAgainWithFloorEvidenceReturnsLocalizationFailedToTheHostWithoutPressureOrRetry(string deltaField)
    {
        var starts = 0;
        var stops = 0;
        var (runner, session, _, sign, now) = RunnerAtFailureCard(true, () => starts++, () => stops++, deltaField);
        Assert.AreEqual(1, starts);
        Assert.AreEqual(1, stops, "the failure card already stopped sampling");
        var reads = 0;
        runner.ReadPressure = (out float hpa, out double capturedAtS) =>
        {
            reads++;
            hpa = 999.53f;
            capturedAtS = now();
            return true;
        };
        Call(runner, "Update");
        Assert.IsTrue(LiveDoorHud.LocalizationFailed(session));
        Assert.AreEqual(0, reads);
        for (var i = 0; i < 25; i++) session.OnPressure(999.53f, now() + i); // late readings reach nothing

        Call(runner, "OnHudRetry");
        Assert.AreEqual(IndoorArState.Done, session.State, "no in-place retry with launch-time evidence");
        Assert.AreEqual(2, session.Queries);
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*localizationFailed"));
        Call(runner, "Update");
        Call(runner, "Update");
        Call(runner, "OnHudRetry");
        Assert.AreEqual(0, reads);
        Assert.AreEqual(1, starts);
        Assert.AreEqual(1, stops);
        Assert.AreSame(sign, session.FloorSign, "pressure never released it");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void PressureCaptureTimestampsSurviveDelayedDeliveryAndASilenceOverTheGapLimitClearsTheWindow()
    {
        IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2").TrimEnd('}') +
            ConfirmedFloorEvidence + "}";
        var runner = Runner();
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        var sign = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");
        double clock = 0;
        session.PressureClock = () => clock;
        session.OnArSupport(true);
        localizer.Authenticate();
        for (var i = 1; i <= 21; i++)
        {
            clock = i;
            session.OnPressure(1000f, i);
        }
        session.OnSignConfirmed(sign);

        var next = (Hpa: 0f, CapturedAtS: 0d);
        runner.ReadPressure = (out float hpa, out double capturedAtS) =>
        {
            (hpa, capturedAtS) = next;
            return true;
        };
        void Deliver(double capturedAtS, double deliveredAtS)
        {
            next = (999.53f, capturedAtS);
            clock = deliveredAtS;
            Call(runner, "Update");
        }

        for (var t = 22; t <= 32; t++) Deliver(t, t);
        Deliver(32 + IndoorFloorBarometer.MinSpacingS, 33.4);
        Deliver(42.9, 42.9);
        Assert.AreSame(sign, session.FloorSign, "the 10.4 s capture gap cleared the window despite a 9.5 s delivery gap");

        for (var t = 44; t <= 61; t++) Deliver(t, t);
        Assert.IsNull(session.FloorSign, "a new settled window releases the sign");
    }

    [Test]
    public void EditorAvailabilityNeverAuthenticates()
    {
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        try
        {
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.Ready });
            var runner = Runner();
            var start = (IEnumerator)Call(runner, "Start");
            Assert.IsTrue(start.MoveNext());
            Assert.IsFalse(start.MoveNext());
            Assert.AreEqual(0, _host.GetComponent<RunnerFakeLocalizer>().AuthenticationCalls);
            Assert.AreEqual(IndoorArState.Done, Field<IndoorArSession>(runner, "_session").State);
        }
        finally { state.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void BlockedCaptureStillCountsOneAttempt()
    {
        var runner = Runner();
        _host.GetComponent<RunnerFakeLocalizer>().Succeed = false;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(1, session.Queries);
    }

    [Test]
    public void PriorSessionNoPoseCannotTriggerCurrentSessionsSignRetry()
    {
        var runner = Runner();
        var start = (IEnumerator)Call(runner, "Start");
        start.MoveNext();
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        var oldSession = Field<IndoorArSession>(runner, "_session");
        oldSession.OnArSupport(true);
        oldSession.OnAuthenticated();
        var oldAttempt = localizer.LastAttemptId;

        runner.GetType().GetMethod("BeginSession", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runner, new object[] { _request, runner.RequestClock() });
        var currentSession = Field<IndoorArSession>(runner, "_session");
        currentSession.OnArSupport(true);
        currentSession.OnAuthenticated();
        var currentAttempt = localizer.LastAttemptId;
        localizer.CompleteWithoutPose(oldAttempt);

        var match = IndoorRoomIdentityMatcher.MatchSign("VEC 120", "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"VEC120\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        var started = Field<float>(runner, "_sessionStarted");
        var onSignRead = runner.GetType().GetMethod("OnSignRead", BindingFlags.Instance | BindingFlags.NonPublic);
        onSignRead.Invoke(runner, new object[] { "VEC 120", match, started, null });
        onSignRead.Invoke(runner, new object[] { "VEC 120", match, started, null });
        Assert.AreEqual(2, localizer.AttemptCount, "old failure cannot spend current session retry");

        localizer.CompleteWithoutPose(currentAttempt);
        onSignRead.Invoke(runner, new object[] { "VEC 120", match, started, null });
        Assert.AreEqual(3, localizer.AttemptCount, "current session failure retains early sign retry");
    }

    [Test]
    public void StaleSignReadCannotSetNewSessionRoom()
    {
        var runner = Runner();
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        var match = IndoorRoomIdentityMatcher.MatchSign("VEC 119", "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"119\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        runner.GetType().GetMethod("OnSignRead", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runner, new object[] { "VEC 119", match, Field<float>(runner, "_sessionStarted") - 1f, null });
        Assert.IsNull(session.SignRoom);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PinEasesAfterTrackingOrFloorReturns(bool trackingLoss)
    {
        var runner = Runner();
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        var camera = new GameObject("AR Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.SetParent(_host.transform, false);
        runner.Configure(localizer, _pinPrefab, camera);
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Call(runner, "UpdateHud");
        var pin = Field<Transform>(runner, "_pin");
        var original = new Vector3(-5.7651f, -1.8186f, 22.6689f);
        Assert.IsNull(pin.parent);
        Assert.AreEqual(original, pin.position);
        Assert.IsNotNull(pin.Find("Amber pin"));
        Assert.IsNotNull(pin.Find("White core"));
        Assert.IsTrue(pin.gameObject.activeSelf);
        if (trackingLoss)
        {
            localizer.DeferAnswer = true;
            session.OnTrackingLost();
        }
        else camera.transform.localPosition = Vector3.up;
        Call(runner, "UpdateHud");
        Assert.IsFalse(pin.gameObject.activeSelf);

        localizer.transform.position += Vector3.right;
        camera.transform.localPosition = Vector3.zero;
        if (trackingLoss) localizer.CompleteLocalized(0.73f);
        Call(runner, "UpdateHud");
        Assert.IsTrue(pin.gameObject.activeSelf);
        Assert.That(pin.position.x, Is.GreaterThanOrEqualTo(original.x).And.LessThan(original.x + 1f));
        Assert.AreEqual(original + Vector3.right, Field<Vector3>(runner, "_pinTarget"));
    }

    [Test]
    public void PinAndItsRoomLabelStayBehindTheArrivalCard()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        // One meter short of the door, facing it.
        camObject.transform.localPosition = new Vector3(-5.7651f, -0.4f, 21.6689f);
        camObject.transform.LookAt(new Vector3(-5.7651f, -1.8186f, 22.6689f));
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camObject.GetComponent<Camera>());
        var arState = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        arState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            ((IEnumerator)Call(runner, "Start")).MoveNext();
            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            session.OnAuthenticated();
            session.OnLocalized(0.5f);
            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            var hud = _host.GetComponentInChildren<IndoorHud>();
            var label = hud.transform.Find("HUD Canvas/Safe area/Pin label");
            Assert.IsTrue(label.gameObject.activeSelf);
            StringAssert.Contains("VEC 119", label.GetComponentInChildren<UnityEngine.UI.Text>().text);
            Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Arrival card"));

            hud.transform.Find("HUD Canvas/Safe area/Here").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Call(runner, "Update");
            Assert.AreEqual(IndoorArState.Arrived, session.State);
            Assert.IsNotNull(hud.transform.Find("HUD Canvas/Safe area/Arrival card"));
            Assert.IsTrue(Field<Transform>(runner, "_pin").gameObject.activeSelf);
            Assert.IsTrue(hud.transform.Find("HUD Canvas/Safe area/Pin label").gameObject.activeSelf);
        }
        finally { arState.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void FrozenCameraHidesPinLabelAndArrowThenReleasesWhenThePoseMoves()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        camObject.transform.localPosition = new Vector3(-5.625f, -0.4f, 23.625f);
        camObject.transform.LookAt(new Vector3(-5.7651f, -1.8186f, 22.6689f)); // facing the door keeps the pin in view
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camObject.GetComponent<Camera>());
        SetArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized(0.5f);
        runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
        Call(runner, "Update");
        var hud = _host.GetComponentInChildren<IndoorHud>();
        var pin = Field<Transform>(runner, "_pin");
        Assert.IsTrue(pin.gameObject.activeSelf);
        AssertGuiding(runner);
        Assert.IsTrue(hud.transform.Find("HUD Canvas/Safe area/Pin label").gameObject.activeSelf);

        var detector = Field<FrozenCameraDetector>(runner, "_frozenCamera");
        var frames = 0;
        for (var i = 0; i < 25; i++) detector.Observe(.1f, Vector3.one, Quaternion.identity, ++frames, true);
        Assert.IsTrue(detector.Frozen);
        Call(runner, "UpdateHud");
        Assert.IsFalse(pin.gameObject.activeSelf, "no world pin");
        Assert.IsFalse(hud.transform.Find("HUD Canvas/Safe area/Pin label").gameObject.activeSelf, "no room label");
        Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Guide arrow"), "no arrow");
        AssertPath(runner, false);
        Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Here"), "no I'm here button");
        Assert.AreEqual(LiveDoorHud.CameraFrozenTitle, hud.transform.Find("HUD Canvas/Safe area/Title").GetComponent<UnityEngine.UI.Text>().text);
        Assert.IsNotNull(hud.transform.Find("HUD Canvas/Safe area/Exit"), "the user can still leave");
        Assert.AreEqual(IndoorArState.Pinned, session.State, "the guard does not touch localization or the session");

        detector.Observe(.1f, Vector3.one + Vector3.right, Quaternion.identity, ++frames, true);
        Assert.IsFalse(detector.Frozen);
        Call(runner, "UpdateHud");
        Assert.IsTrue(pin.gameObject.activeSelf);
        AssertGuiding(runner);
    }

    [Test]
    public void BuiltSceneWiresTheSameMapSpaceAndArCamera()
    {
        var scene = EditorSceneManager.OpenScene("Assets/IndoorAr/Scenes/IndoorArSpike.unity", OpenSceneMode.Additive);
        try
        {
            var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var runner = objects.Select(t=>t.GetComponent<IndoorArRunner>()).FirstOrDefault(c=>c!=null);
            var iosOcr = objects.Select(t=>t.GetComponent<IndoorRoomMlKitOcr>()).FirstOrDefault(c=>c!=null);
            var androidOcr = objects.Select(t=>t.GetComponent<IndoorRoomOcrDemo>()).FirstOrDefault(c=>c!=null);
            var localizer = objects.Select(t=>t.GetComponent<MultiSetLocalizer>()).FirstOrDefault(c=>c!=null);
            var manager = objects.Select(t=>t.GetComponent<SingleFrameLocalizationManager>()).FirstOrDefault(c=>c!=null);
            Assert.IsNotNull(runner);
            Assert.AreEqual(IndoorLocalizationMode.MultiSet, Field<IndoorLocalizationMode>(runner, "localizationMode"));
            Assert.IsNotNull(iosOcr);
            Assert.IsNotNull(androidOcr);
            Assert.IsNotNull(localizer);
            Assert.IsNotNull(manager);
            var serialized = new SerializedObject(manager);
            Assert.AreNotSame(localizer.MapSpace, localizer.SdkMapSpace);
            Assert.AreSame(localizer.SdkMapSpace.gameObject, serialized.FindProperty("mapSpace").objectReferenceValue);
            Assert.IsFalse(serialized.FindProperty("enableBlurCheck").boolValue);
            var camera = (Camera)serialized.FindProperty("arCamera").objectReferenceValue;
            Assert.IsNotNull(camera);
            Assert.IsNotNull(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>());
            var iosSerialized = new SerializedObject(iosOcr);
            Assert.AreSame(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(),
                iosSerialized.FindProperty("cameraManager").objectReferenceValue);
            var directory = (TextAsset)iosSerialized.FindProperty("directory").objectReferenceValue;
            Assert.IsNotNull(directory);
            Assert.AreEqual(3, IndoorRoomIdentityMatcher.MatchSign("VEC301", directory.text).Landmark.Floor);
            var androidSerialized = new SerializedObject(androidOcr);
            Assert.AreSame(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(),
                androidSerialized.FindProperty("cameraManager").objectReferenceValue);
            Assert.AreEqual(iosSerialized.FindProperty("directory").objectReferenceValue,
                androidSerialized.FindProperty("directory").objectReferenceValue);
            Assert.IsNotNull(Field<GameObject>(runner,"doorPinPrefab"));
            Assert.IsFalse(manager.enabled);
            Assert.IsNotNull(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>());
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            Assert.IsFalse(objects.Any(t => t.name == "Left wall" || t.name == "Floor" || t.name == "Right wall"));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void PinnedHudDrawsOneFloorPathAlongTheClearRoute()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        camObject.transform.localPosition = new Vector3(-5.625f, -0.4f, 23.625f);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camObject.GetComponent<Camera>());
        var arState = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        arState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            ((IEnumerator)Call(runner, "Start")).MoveNext();
            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            session.OnAuthenticated();
            session.OnLocalized(0.5f);
            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            var hud = _host.GetComponentInChildren<IndoorHud>();
            Assert.AreSame(localizer.MapSpace, hud.transform.parent);
            var route = Field<Vector3[]>(runner, "_route");
            Assert.IsTrue(Field<IndoorWalkGrid>(runner, "_walkGrid").TryNextWaypoint(route,
                camObject.transform.position, LiveDoorHud.RouteLookAheadM, out var target));
            camObject.transform.LookAt(target);
            Call(runner, "Update");
            AssertPath(runner, true);
            Assert.AreEqual(0, hud.GetComponentsInChildren<MeshRenderer>(true).Length);
            var path = Field<IndoorRoutePath>(runner, "_path");
            Assert.AreSame(localizer.MapSpace, path.Root.transform.parent);
            var mesh = path.Root.GetComponent<MeshFilter>().sharedMesh;
            Assert.GreaterOrEqual(mesh.vertexCount, 4);
            var floorY = Field<IndoorWalkGrid>(runner, "_walkGrid").FloorY;
            var vertices = mesh.vertices.Select(path.Root.transform.TransformPoint).ToArray();
            Assert.IsTrue(vertices.All(v => Mathf.Approximately(v.y, floorY + IndoorRoutePath.LiftM)), "flat on the floor");
            Assert.Less(Vector3.Distance((vertices[1] + vertices[2]) * .5f, target + Vector3.up * IndoorRoutePath.LiftM), 1e-4f,
                "the ribbon stops at the waypoint checked for clearance");
            // Camera height is in the supported eye-height band above Floor 1.
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("Hallway route to VEC 119")));
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("VEC 119")));

            camObject.transform.localRotation = Quaternion.LookRotation(Vector3.right);
            Call(runner, "Update");
            AssertPath(runner, false);
            AssertGuiding(runner);
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Any(t => t.text.Contains("Look this way")));
        }
        finally { arState.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void SecondSessionShowsTheNewRoomAfterTheFirstWasDelivered()
    {
        var runner = Runner();
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var first = Field<IndoorArSession>(runner, "_session");
        first.OnArSupport(true);
        first.OnAuthenticated();
        runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
        Call(runner, "Update");
        CollectionAssert.Contains(Labels(runner), "VEC 119 · Floor 1");

        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*cancelled"));
        Call(runner, "OnHudExit");
        Call(runner, "Update");
        Assert.IsTrue(Field<bool>(runner, "_delivered"));

        IndoorArBridge.EditorRequestJson = _request
            .Replace("\"room\":\"119\"", "\"room\":\"127\"")
            .Replace("\"doorKey\":\"VEC119\"", "\"doorKey\":\"VEC127\"");
        Call(runner, "Update");
        var second = Field<IndoorArSession>(runner, "_session");
        Assert.AreNotSame(first, second);
        Assert.AreEqual("127", second.Request.room);
        Assert.AreEqual("VEC127", second.Request.doorKey);
        Assert.IsFalse(Field<bool>(runner, "_delivered"));
        CollectionAssert.Contains(Labels(runner), "VEC 127 · Floor 1");
        CollectionAssert.DoesNotContain(Labels(runner), "VEC 119 · Floor 1");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void DestroyDuringSignHoldLogsCancellation()
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        var signFirst = Field<SignFirstLocalizer>(runner, "_signFirst");
        session.OnArSupport(true);
        signFirst.Authenticate();
        signFirst.Tick(2f);
        LogAssert.Expect(LogType.Log, "[IndoorAr] localization request attempt=1 kind=Sign elapsedMs=2000 outcome=Cancelled lockBusy=na lockWaitMs=na sdkMs=na");
        Call(runner, "OnDestroy");
    }

    // X and the back key both call OnHudExit; arrival is "I'm here" then Done.
    [TestCase("cancel", "cancelled")]
    [TestCase("arrive", "arrived")]
    public void EveryExitDeliversOnceThenTheNextLaunchStartsClean(string exit, string status)
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        runner.SetSignCalibration(IndoorSignPoseTests.ValidatedHere(out _, out _));
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var first = Field<IndoorArSession>(runner, "_session");
        var signFirst = Field<SignFirstLocalizer>(runner, "_signFirst");
        first.OnArSupport(true);
        signFirst.Authenticate(); // opens the sign hold
        var signOutcome = exit == "cancel" ? "Cancelled" : "Timeout";
        var signLine = new Regex($"\\[IndoorAr\\] localization request attempt=\\d+ kind=Sign .*outcome={signOutcome}");
        // Arriving logs the expired hold first; cancellation closes the hold before the result.
        LogAssert.Expect(LogType.Log, signLine);
        LogAssert.Expect(LogType.Log, new Regex($"\\[IndoorAr\\] result .*\"status\":\"{status}\""));
        if (exit == "cancel")
        {
            signFirst.Tick(SignFirstLocalizer.HoldS - 1f); // leave mid-hold: 1 s of it must not carry over
            Call(runner, "OnHudExit");
        }
        else
        {
            signFirst.Tick(SignFirstLocalizer.HoldS); // hold runs out: the fake MultiSet pins
            Assert.AreEqual(IndoorArState.Pinned, first.State);
            Call(runner, "OnHudHere");
            Call(runner, "OnHudDone");
        }
        Call(runner, "Update");
        Assert.IsTrue(Field<bool>(runner, "_delivered"));

        IndoorArBridge.EditorRequestJson = _request.Replace("\"room\":\"119\"", "\"room\":\"127\"")
            .Replace("\"doorKey\":\"VEC119\"", "\"doorKey\":\"VEC127\"");
        Call(runner, "Update");
        var second = Field<IndoorArSession>(runner, "_session");
        Assert.AreNotSame(first, second);
        Assert.IsFalse(Field<bool>(runner, "_delivered"));
        Assert.AreEqual(1, _host.GetComponentsInChildren<IndoorHud>(true).Length, "one shared HUD, not one per launch");
        Assert.IsFalse(signFirst.FromSigns);

        // A fresh hold: MultiSet is not asked the moment the new session authenticates.
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        var queried = localizer.AttemptCount;
        second.OnArSupport(true);
        second.OnAuthenticated();
        signFirst.Tick(SignFirstLocalizer.HoldS - 1f);
        Assert.AreEqual(queried, localizer.AttemptCount);
        Assert.AreEqual(IndoorArState.Localizing, second.State);
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void SameRoomSearchedTwiceStartsAFreshSession()
    {
        var runner = Runner();
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var first = Field<IndoorArSession>(runner, "_session");
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*cancelled"));
        Call(runner, "OnHudExit");
        Call(runner, "Update");
        Assert.IsTrue(Field<bool>(runner, "_delivered"));

        IndoorArBridge.EditorRequestJson = _request;
        Call(runner, "Update");
        Assert.AreNotSame(first, Field<IndoorArSession>(runner, "_session"));
        Assert.IsFalse(Field<bool>(runner, "_delivered"));
        CollectionAssert.Contains(Labels(runner), "VEC 119 · Floor 1");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void LocalizationQueryWaitsForArTracking()
    {
        SetArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionInitializing });
        var runner = Runner();
        var localizer = _host.GetComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Call(runner, "Update");
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(0, localizer.AttemptCount, "MultiSet never answers a frame sent before tracking");

        SetArState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        Call(runner, "Update");
        Call(runner, "Update");
        Assert.AreEqual(1, localizer.AttemptCount, "one query, not one per frame");
    }

    [Test]
    public void ExitDeliversCancelledWhenTrackingIsLost()
    {
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            var runner = Runner();
            Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            session.OnAuthenticated();
            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.None });
            var button = _host.GetComponentInChildren<UnityEngine.UI.Button>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, button.GetComponentInParent<Canvas>().renderMode);
            button.onClick.Invoke();
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*cancelled"));
            Call(runner, "Update");
            Assert.AreEqual(IndoorArState.Done, session.State);
            StringAssert.Contains("\"status\":\"cancelled\"", session.ResultJson);
            Assert.AreEqual(1, session.Queries);
            LogAssert.NoUnexpectedReceived();
        }
        finally { state.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void BaselineHooksRecordStartupPoseArrowRetryAndTrackingLoss()
    {
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            var runner = Runner();
            var camera = new GameObject("AR Camera").AddComponent<Camera>();
            camera.transform.SetParent(_host.transform, false);
            camera.transform.localPosition = new Vector3(-5.625f, -0.4f, 23.625f);
            runner.Configure(_host.GetComponent<RunnerFakeLocalizer>(), _pinPrefab, camera);
            Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
            var baseline = Field<IndoorArBaseline>(runner, "_baseline");
            Assert.IsNull(baseline.ArStartupMs);
            Assert.AreEqual(0, baseline.Attempts);

            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            Call(runner, "Update");
            Assert.IsNotNull(baseline.ArStartupMs);

            session.OnAuthenticated();
            Assert.AreEqual(1, baseline.Attempts);
            Assert.AreEqual(0, baseline.Retries);
            Assert.IsNotNull(baseline.RequestLatencyMs);
            Assert.IsNotNull(baseline.TimeToFirstLocalizationMs);
            Assert.IsNull(baseline.ArrowShownMs);
            Assert.IsFalse(baseline.ArrowAlignmentMeasured);
            StringAssert.Contains("arrowAlignment=unmeasured", baseline.LogLine());

            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            Assert.IsNotNull(baseline.ArrowShownMs);
            Assert.AreEqual(0, baseline.TrackingLosses);
            Assert.IsFalse(baseline.ArrowAlignmentMeasured);

            _host.GetComponent<RunnerFakeLocalizer>().Succeed = false;
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.None });
            Call(runner, "Update");
            Assert.AreEqual(1, baseline.TrackingLosses);
            Assert.AreEqual(1, baseline.Attempts, "the retry waits for tracking to return");
            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
            Call(runner, "Update");
            Assert.AreEqual(2, baseline.Attempts);
            Assert.AreEqual(1, baseline.Retries);
            Assert.AreEqual(1, baseline.UnansweredAttempts);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
        }
        finally { state.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void SignFirstRejectsUnknownGapThenAcceptsSupportedFloor()
    {
        var gridJson = Resources.Load<TextAsset>("IndoorWalkGrid").text;
        var floor1Y = IndoorWalkGrid.ForFloor(gridJson, 1).FloorY;
        var floor2Y = IndoorWalkGrid.ForFloor(gridJson, 2).FloorY;
        var sign = IndoorSignPoseTests.Sign(device: SystemInfo.deviceModel);
        var fake = _host.AddComponent<RunnerFakeLocalizer>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(fake, _pinPrefab, _host.AddComponent<Camera>());
        runner.SetLocalizationMode(IndoorLocalizationMode.SignFirst);
        runner.SetSignCalibration(new IndoorSignCalibration { signs = new[] { sign } });
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        Field<IIndoorArLocalizer>(runner, "_localizer").Authenticate();
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC120", "VEC", "floor_1"));
        session.OnLocalized();
        Call(runner, "UpdateHud");
        var localizer = Field<SignFirstLocalizer>(runner, "_signFirst");
        localizer.Localize();
        var root = localizer.MapSpace;
        var pin = Field<Transform>(runner, "_pin");
        var hud = Field<IndoorHud>(runner, "_hud").transform;
        var beforePosition = root.position;
        var beforeRotation = root.rotation;
        var beforePinPosition = pin.position;
        var beforePinRotation = pin.rotation;
        var beforeHudPosition = hud.position;
        var beforeHudRotation = hud.rotation;

        var gapY = (floor1Y + IndoorArSession.MaxEyeHeightM + floor2Y + IndoorArSession.MinEyeHeightM) / 2f;
        sign.centerInMap = new Vector3(2f, gapY, 8f);
        var rejected = IndoorSignPoseTests.Observe(sign, new Vector3(0f, gapY, 5f),
            Quaternion.Euler(0f, 100f, 0f), SystemInfo.deviceModel);
        Assert.AreEqual(IndoorSignPoseStatus.Implausible, localizer.ObservePose(rejected));
        Assert.AreEqual(beforePosition, root.position);
        Assert.AreEqual(beforeRotation, root.rotation);
        Assert.AreEqual(beforePinPosition, pin.position);
        Assert.AreEqual(beforePinRotation, pin.rotation);
        Assert.AreEqual(beforeHudPosition, hud.position);
        Assert.AreEqual(beforeHudRotation, hud.rotation);
        Assert.AreEqual(IndoorArState.Pinned, session.State);

        var supportedY = floor1Y + 1.5f;
        sign.centerInMap = new Vector3(2f, supportedY, 8f);
        var accepted = IndoorSignPoseTests.Observe(sign, new Vector3(0f, supportedY, 5f),
            Quaternion.Euler(0f, 100f, 0f), SystemInfo.deviceModel);
        Assert.AreEqual(IndoorSignPoseStatus.Ok, localizer.ObservePose(accepted));
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.AreNotEqual(beforePosition, root.position);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelledSessionCannotDispatchFallbackWhenAuthenticationArrivesLate(bool beforeDelivery)
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        var inner = _host.GetComponent<RunnerFakeLocalizer>();
        inner.DeferAuth = true;
        inner.DeferAnswer = true;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        var localizer = Field<SignFirstLocalizer>(runner, "_signFirst");
        session.OnArSupport(true);
        localizer.Authenticate();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Call(runner, "OnHudExit");
        if (beforeDelivery) inner.CompleteAuth();
        Assert.AreEqual(0, inner.AttemptCount);
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*\"status\":\"cancelled\""));
        Call(runner, "Update");
        Assert.IsTrue(Field<bool>(runner, "_delivered"));
        if (!beforeDelivery) inner.CompleteAuth();
        Assert.AreEqual(0, inner.AttemptCount);
    }

    [Test]
    public void SameSignRecoversAfterRejectedPoseAndTrackingLoss()
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        var sign = IndoorSignPoseTests.Sign(device: SystemInfo.deviceModel);
        var floorY = IndoorWalkGrid.ForFloor(Resources.Load<TextAsset>("IndoorWalkGrid").text, 1).FloorY;
        sign.centerInMap.y = floorY + 1.5f;
        runner.SetSignCalibration(new IndoorSignCalibration { signs = new[] { sign } });
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        Field<SignFirstLocalizer>(runner, "_signFirst").Authenticate();
        var match = IndoorRoomIdentityMatcher.MatchSign("VEC120",
            "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"VEC120\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        var camera = sign.centerInMap + new Vector3(-1.2f, 0f, .2f);
        var observation = IndoorSignPoseTests.Observe(sign, camera, Quaternion.Euler(0f, 100f, 0f), SystemInfo.deviceModel);
        var started = Field<float>(runner, "_sessionStarted");
        Call(runner, "OnSignRead", "VEC120", match, started, null);
        Call(runner, "OnSignRead", "VEC120", match, started, new IndoorSignObservation { RoomId = "VEC120" });
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Call(runner, "OnSignRead", "VEC120", match, started, observation);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.Less(Vector3.Distance(observation.CameraWorldPosition, _host.transform.TransformPoint(camera)), .01f);

        session.OnTrackedPose(floorY + 1.5f, new[] { (1, floorY), (2, floorY + 4f) });
        session.OnTrackedPose(floorY + 5.5f, new[] { (1, floorY), (2, floorY + 4f) });
        Assert.IsNull(session.FloorSign);
        session.OnTrackingLost();
        _host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Call(runner, "OnSignRead", "VEC120", match, started, observation);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.AreEqual("VEC120", session.FloorSign.Id);
        Assert.Less(Vector3.Distance(observation.CameraWorldPosition, _host.transform.TransformPoint(camera)), .01f);
        session.OnTrackingLost();
        Assert.IsTrue(session.PoseConflictsWithSign(floorY + 5.5f, new[] { (1, floorY), (2, floorY + 4f) }));
        Assert.AreEqual(0, _host.GetComponent<RunnerFakeLocalizer>().AttemptCount);
    }

    [Test]
    public void RoomSignShowsFloorThenMultiSetPoseShowsArrowInTheSameSession()
    {
        var directory = System.IO.File.ReadAllText(
            System.IO.Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json"));
        var ocr = _host.AddComponent<IndoorRoomMlKitOcr>();
        ocr.Configure(null, new TextAsset(directory));
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        camObject.transform.localPosition = new Vector3(-5.625f, -0.4f, 23.625f);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camObject.GetComponent<Camera>());
        runner.SetLocalizationMode(IndoorLocalizationMode.SignFirst);
        runner.SetSignCalibration(new IndoorSignCalibration()); // the shipped file has no measured signs
        var arState = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        arState.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            ((IEnumerator)Call(runner, "Start")).MoveNext();
            var session = Field<IndoorArSession>(runner, "_session");
            var signFirst = Field<SignFirstLocalizer>(runner, "_signFirst");
            Assert.IsFalse(signFirst.SignsCanPose);
            var restarted = ArSessionLifecycle.LoaderRestarted;
            session.OnArSupport(true);
            signFirst.Authenticate();
            Assert.AreEqual(IndoorArState.Localizing, session.State);

            var match = IndoorRoomIdentityMatcher.MatchSign("VEC 127", directory);
            var started = Field<float>(runner, "_sessionStarted");
            Call(runner, "OnSignRead", "VEC 127", match, started, null);
            Call(runner, "OnSignRead", "VEC 127", match, started, null);
            Assert.AreEqual("VEC127", session.SignRoom.Id);
            Call(runner, "Update");
            var hud = _host.GetComponentInChildren<IndoorHud>();
            var labels = hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text).ToArray();
            Assert.IsTrue(labels.Any(t => t.Contains("Room VEC 127 detected")), string.Join(" | ", labels));
            CollectionAssert.Contains(labels, "You're on Floor 1");
            Assert.IsFalse(labels.Any(t => t.StartsWith("Walk ")));
            Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Guide arrow"), "no arrow before a validated pose");
            Assert.AreEqual(0, localizer.AttemptCount, "MultiSet waits out the sign hold");

            signFirst.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, localizer.AttemptCount, "then MultiSet is the delayed fallback");
            Call(runner, "Update");
            Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Guide arrow"), "an open attempt is not a pose");

            localizer.CompleteLocalized(.5f);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            Assert.AreSame(session, Field<IndoorArSession>(runner, "_session"), "same session, no relaunch");
            Assert.AreSame(hud, _host.GetComponentInChildren<IndoorHud>());
            Assert.AreSame(camObject.GetComponent<Camera>(), Field<Camera>(runner, "arCamera"));
            Assert.AreEqual(restarted, ArSessionLifecycle.LoaderRestarted, "the AR camera was not restarted");
            AssertGuiding(runner);
            CollectionAssert.Contains(Labels(runner), "You're on Floor 1");
        }
        finally { arState.Invoke(null, new object[] { previous }); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SameSignRecoversAfterVpsDeadlineWithoutRestartingCamera(bool delayedAuth)
    {
        var directory = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,
            "IndoorNavigation/Data/vec_floors12345.json"));
        var ocr = _host.AddComponent<IndoorRoomMlKitOcr>();
        ocr.Configure(null, new TextAsset(directory));
        var androidOcr = _host.AddComponent<IndoorRoomOcrDemo>();
        androidOcr.Configure(null, directory);
        var inner = _host.AddComponent<RunnerFakeLocalizer>();
        inner.DeferAuth = delayedAuth;
        inner.DeferAnswer = true;
        var camera = new GameObject("AR Camera").AddComponent<Camera>();
        var cameraInMap = new Vector3(-5.625f, -0.4f, 23.625f);
        camera.transform.position = cameraInMap;
        camera.transform.rotation = Quaternion.Euler(0f, 100f, 0f);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(inner, _pinPrefab, camera);
        runner.SetLocalizationMode(IndoorLocalizationMode.SignFirst);
        var sign = IndoorSignPoseTests.Sign(device: SystemInfo.deviceModel);
        sign.doorKey = "VEC127";
        sign.centerInMap = cameraInMap + new Vector3(1.2f, 0f, -.2f);
        runner.SetSignCalibration(new IndoorSignCalibration { signs = new[] { sign } });
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            ((IEnumerator)Call(runner, "Start")).MoveNext();
            var session = Field<IndoorArSession>(runner, "_session");
            var first = Field<SignFirstLocalizer>(runner, "_signFirst");
            var restarted = ArSessionLifecycle.LoaderRestarted;
            session.OnArSupport(true);
            first.Authenticate();
            var match = IndoorRoomIdentityMatcher.MatchSign("VEC 127", directory);
            var started = Field<float>(runner, "_sessionStarted");
            Call(runner, "OnSignRead", "VEC 127", match, started, null);
            Call(runner, "OnSignRead", "VEC 127", match, started, null);
            first.Tick(SignFirstLocalizer.HoldS);
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            session.Tick(IndoorArSession.LocalizeTimeoutS);
            Assert.IsTrue(session.FallbackExpired);
            Assert.IsFalse(first.FallbackAvailable);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
            Assert.IsNull(session.ResultJson);
            var attempts = inner.AttemptCount;
            if (delayedAuth) inner.CompleteAuth();
            first.Tick(300f);
            session.Tick(300f);
            Assert.AreEqual(attempts, inner.AttemptCount);
            Call(runner, "Update");
            Assert.IsTrue(ocr.enabled);
            Assert.IsTrue(androidOcr.enabled);
            var hud = Field<IndoorHud>(runner, "_hud");
            Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Guide arrow"));
            CollectionAssert.Contains(Labels(runner), "You're on Floor 1");
            StringAssert.Contains("\"state\":\"localizing\"", IndoorArBridge.LastStatusJson);
            StringAssert.Contains("\"fallbackAvailable\":false", IndoorArBridge.LastStatusJson);
            StringAssert.DoesNotContain("\"pose\"", IndoorArBridge.LastStatusJson);

            var observation = IndoorSignPoseTests.Observe(sign, cameraInMap, camera.transform.rotation, SystemInfo.deviceModel);
            observation.RoomId = sign.doorKey;
            observation.CameraWorldPosition = camera.transform.position;
            observation.CameraWorldRotation = camera.transform.rotation;
            Call(runner, "OnSignRead", "VEC 127", match, started, observation);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(runner, LiveDoorHud.LocationFoundHoldS);
            Call(runner, "Update");
            Assert.AreSame(session, Field<IndoorArSession>(runner, "_session"));
            Assert.AreSame(hud, Field<IndoorHud>(runner, "_hud"));
            Assert.AreSame(camera, Field<Camera>(runner, "arCamera"));
            Assert.AreEqual(restarted, ArSessionLifecycle.LoaderRestarted);
            AssertGuiding(runner);
            StringAssert.Contains("\"source\":\"sign\"", IndoorArBridge.LastStatusJson);

            session.OnTrackingLost();
            first.Tick(300f);
            session.Tick(300f);
            Assert.AreEqual(attempts, inner.AttemptCount);
            Call(runner, "OnSignRead", "VEC 127", match, started, observation);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Call(runner, "OnHudExit");
            Call(runner, "UpdateSignOcr");
            Assert.IsFalse(ocr.enabled);
            Assert.IsFalse(androidOcr.enabled);
            Call(runner, "OnSignRead", "VEC 127", match, started, observation);
            Assert.AreEqual(IndoorArState.Done, session.State);
        }
        finally
        {
            state.Invoke(null, new object[] { previous });
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    [Test]
    public void CalibratedOcrOffKeepsAdaptersDisabledAndWaitsForMultiSetAuth()
    {
        var ocr = _host.AddComponent<IndoorRoomMlKitOcr>();
        var androidOcr = _host.AddComponent<IndoorRoomOcrDemo>();
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        runner.GetType().GetField("signLocalization", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runner, false);
        runner.SetSignCalibration(IndoorSignPoseTests.ValidatedHere(out var observation, out _));
        var inner = _host.GetComponent<RunnerFakeLocalizer>();
        inner.DeferAuth = true;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        Field<SignFirstLocalizer>(runner, "_signFirst").Authenticate();
        Call(runner, "UpdateSignOcr");
        Assert.IsFalse(ocr.enabled);
        Assert.IsFalse(androidOcr.enabled);
        Assert.AreEqual(IndoorArState.Auth, session.State);
        var rootPosition = _host.transform.position;
        var match = IndoorRoomIdentityMatcher.MatchSign("VEC 120",
            "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"VEC120\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        Call(runner, "OnSignRead", "VEC 120", match, Field<float>(runner, "_sessionStarted"), observation);
        Assert.IsNull(session.SignRoom);
        Assert.AreEqual(rootPosition, _host.transform.position);
        Assert.AreEqual(0, inner.AttemptCount);
        inner.CompleteAuth();
        Assert.AreEqual(1, inner.AttemptCount);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
    }

    [Test]
    public void MultiSetModeKeepsTheOriginalLocalizer()
    {
        var runner = Runner(IndoorLocalizationMode.MultiSet);
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        Assert.IsInstanceOf<RunnerFakeLocalizer>(Field<IIndoorArLocalizer>(runner, "_localizer"));
    }

    [TestCase(false, "2")]
    [TestCase(true, "none")]
    public void UnsupportedPoseHeightIsRejectedAndLeavesLocalizationRetryable(bool belowFloorOne, string signFloor)
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        var cameraObject = new GameObject("AR Camera", typeof(Camera));
        cameraObject.transform.SetParent(_host.transform, false);
        var gridJson = Resources.Load<TextAsset>("IndoorWalkGrid").text;
        var floor1 = IndoorWalkGrid.ForFloor(gridJson, 1);
        var floor5 = IndoorWalkGrid.ForFloor(gridJson, 5);
        cameraObject.transform.localPosition = Vector3.up * (belowFloorOne ? floor1.FloorY - 3f : floor5.FloorY + 4f);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, cameraObject.GetComponent<Camera>());
        runner.SetLocalizationMode(IndoorLocalizationMode.MultiSet);
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        if (!belowFloorOne) session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));

        LogAssert.Expect(LogType.Log, new Regex($"\\[IndoorAr\\] floor conflict sign={signFloor} pose=unsupported"));
        localizer.CompleteLocalized(.5f);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.IsFalse(session.WasPinned);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(2, localizer.AttemptCount, "the rejected pose follows the normal retry path");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void SecondMultiSetCandidateOnAnotherFloorPinsWithoutAnotherRetry()
    {
        IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":2").TrimEnd('}') +
            ConfirmedFloorEvidence + "}";
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        var camera = new GameObject("AR Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.SetParent(_host.transform, false);
        var grid = Resources.Load<TextAsset>("IndoorWalkGrid").text;
        camera.transform.localPosition = Vector3.up * (IndoorWalkGrid.ForFloor(grid, 2).FloorY + 1.5f);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camera);
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        localizer.Authenticate();

        LogAssert.Expect(LogType.Log, "[IndoorAr] floor conflict sign=none pose=2");
        Assert.IsFalse((bool)Call(runner, "IsCandidateMultiSetPoseAllowed", localizer.MapSpace));
        Assert.IsFalse(session.WasPinned);
        Assert.AreEqual(1, session.TrustedStartFloor);
        session.Tick(IndoorArSession.LocalizeTimeoutS);
        Assert.AreEqual(2, localizer.AttemptCount);

        Assert.IsTrue((bool)Call(runner, "IsCandidateMultiSetPoseAllowed", localizer.MapSpace));
        localizer.CompleteLocalized(.73f);
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        Assert.AreEqual(0, session.TrustedStartFloor);
        Assert.AreEqual(2, session.Queries);
    }

    [TestCase("signConflict")]
    [TestCase("below")]
    [TestCase("above")]
    [TestCase("gap")]
    public void MultiSetCandidateMustPassFloorEvidenceBeforeChangingNavigation(string candidateKind)
    {
        var sdkObject = new GameObject("MultiSet SDK");
        sdkObject.SetActive(false);
        var sdk = sdkObject.AddComponent<IndoorArSdkManager>();
        var managerObject = new GameObject("MultiSet Manager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<SingleFrameLocalizationManager>();
        var localizerObject = new GameObject("MultiSet Localizer");
        localizerObject.SetActive(false);
        var localizer = localizerObject.AddComponent<MultiSetLocalizer>();
        localizer.TrackingReady = () => true;
        var mapRoot = new GameObject("Navigation Map Space").transform;
        mapRoot.SetPositionAndRotation(new Vector3(7f, 2f, 9f), Quaternion.Euler(0f, 20f, 0f));
        localizer.Configure(sdk, manager, mapRoot);
        localizerObject.SetActive(true);
        Call(localizer, "Awake");
        var camera = new GameObject("AR Camera", typeof(Camera)).GetComponent<Camera>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camera);
        runner.SetLocalizationMode(IndoorLocalizationMode.SignFirst);
        var diagnostics = new System.Collections.Generic.List<string>();
        runner.DiagnosticSink = diagnostics.Add;
        var gridJson = Resources.Load<TextAsset>("IndoorWalkGrid").text;
        var floor1Y = IndoorWalkGrid.ForFloor(gridJson, 1).FloorY;
        var floor2Y = IndoorWalkGrid.ForFloor(gridJson, 2).FloorY;
        // A sign validated on this device, on floor 2 at eye height.
        var sign = IndoorSignPoseTests.Sign(device: SystemInfo.deviceModel);
        sign.doorKey = "VEC215";
        sign.centerInMap = new Vector3(2f, floor2Y + 1.5f, 8f);
        var signCamera = sign.centerInMap + new Vector3(-1.2f, 0f, 0.2f);
        var sighting = IndoorSignPoseTests.Observe(sign, signCamera, Quaternion.Euler(0f, 100f, 0f), SystemInfo.deviceModel);
        sighting.RoomId = sign.doorKey;
        runner.SetSignCalibration(new IndoorSignCalibration { signs = new[] { sign } });
        // Play-mode tests leave the scene's destroyed ToastManager in its static; SDK alerts would warn through it.
        typeof(ToastManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
        var start = (IEnumerator)Call(runner, "Start");

        try
        {
            Assert.IsTrue(start.MoveNext());
            var session = Field<IndoorArSession>(runner, "_session");
            var signFirst = Field<SignFirstLocalizer>(runner, "_signFirst");
            session.OnArSupport(true);
            session.OnAuthenticated();
            // MultiSet's own authentication lands behind the sign hold.
            ((Action)typeof(MultiSetLocalizer).GetField("Authenticated", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(localizer)).Invoke();

            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=Sign .*outcome=Timeout"));
            LogAssert.Expect(LogType.Log, "Localization not supported in Unity Editor. Using simulation data to test localization.");
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            TickSignFallbackWithoutEditorFailure(signFirst, localizer, manager);
            localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.identity);
            camera.transform.position = localizer.SdkMapSpace.TransformPoint(0f, floor1Y + 1.5f, 0f);
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=MultiSet .*outcome=Ok"));
            ReportLocalized(manager, true);
            Assert.AreEqual(1, diagnostics.FindAll(line => line.StartsWith("[IndoorAr] localization result ")).Count);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Call(runner, "UpdateHud");

            if (candidateKind == "signConflict")
                session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
            session.OnTrackingLost();
            Call(runner, "UpdateHud");
            var pin = Field<Transform>(runner, "_pin");
            var hud = Field<IndoorHud>(runner, "_hud").transform;
            var beforeRootPosition = mapRoot.position;
            var beforeRootRotation = mapRoot.rotation;
            var beforePinPosition = pin.position;
            var beforePinRotation = pin.rotation;
            var beforeHudPosition = hud.position;
            var beforeHudRotation = hud.rotation;
            var rejectedPoseY = candidateKind == "signConflict" ? floor1Y + 1.5f
                : candidateKind == "below" ? floor1Y - 3f
                : candidateKind == "above" ? IndoorWalkGrid.ForFloor(gridJson, 5).FloorY + 3f
                : floor1Y + IndoorArSession.MaxEyeHeightM + .05f;
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=Sign .*outcome=Timeout"));
            LogAssert.Expect(LogType.Log, "Localization not supported in Unity Editor. Using simulation data to test localization.");
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            TickSignFallbackWithoutEditorFailure(signFirst, localizer, manager);
            Assert.IsFalse(localizer.SdkMapSpace.IsChildOf(mapRoot));
            Assert.IsFalse(mapRoot.IsChildOf(localizer.SdkMapSpace));
            localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(-12f, 8f, 19f), Quaternion.Euler(0f, 80f, 0f));
            camera.transform.position = localizer.SdkMapSpace.TransformPoint(0f, rejectedPoseY, 0f);
            var expectedEvidence = candidateKind == "signConflict" ? "sign=2 pose=1"
                : candidateKind == "gap" ? "sign=none pose=unknown" : "sign=none pose=unsupported";
            LogAssert.Expect(LogType.Log, $"[IndoorAr] floor conflict {expectedEvidence}");
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=MultiSet .*outcome=Rejected"));
            ReportLocalized(manager, true);

            var results = diagnostics.FindAll(line => line.StartsWith("[IndoorAr] localization result "));
            Assert.AreEqual(2, results.Count);
            StringAssert.Contains(candidateKind == "signConflict" ? "floor=1" : candidateKind == "gap" ? "floor=unknown" : "floor=unsupported", results[1]);
            StringAssert.Contains("deltaM=", results[1]);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
            Assert.IsTrue(session.WasPinned, "the earlier accepted pose remains recorded while this candidate is rejected");
            Assert.IsFalse(signFirst.FromSigns);
            Assert.IsFalse(Field<bool>(signFirst, "_innerPosed"));
            Assert.AreEqual(beforeRootPosition, mapRoot.position);
            Assert.AreEqual(beforeRootRotation, mapRoot.rotation);
            Assert.AreEqual(beforePinPosition, pin.position);
            Assert.AreEqual(beforePinRotation, pin.rotation);
            Assert.AreEqual(beforeHudPosition, hud.position);
            Assert.AreEqual(beforeHudRotation, hud.rotation);

            if (candidateKind == "signConflict")
            {
                LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=Sign .*outcome=Late"));
                Assert.AreEqual(IndoorSignPoseStatus.Ok, signFirst.ObservePose(sighting));
                Assert.AreEqual(IndoorArState.Pinned, session.State, "a validated sign can still localize after MultiSet rejection");
                Assert.IsTrue(signFirst.FromSigns);
                StringAssert.Contains("map=unavailable", diagnostics.FindLast(line => line.StartsWith("[IndoorAr] localization result ")));
                Assert.AreNotEqual(beforeRootPosition, mapRoot.position);
                var signAlignedPosition = mapRoot.position;
                var signAlignedRotation = mapRoot.rotation;
                localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(50f, 60f, 70f), Quaternion.Euler(0f, 120f, 0f));
                LogAssert.Expect(LogType.Log, "[IndoorAr] stale localization response ignored");
                ReportLocalized(manager, true);
                Assert.AreEqual(signAlignedPosition, mapRoot.position);
                Assert.AreEqual(signAlignedRotation, mapRoot.rotation);
            }
            else
            {
                LogAssert.Expect(LogType.Log, "Localization not supported in Unity Editor. Using simulation data to test localization.");
                LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
                WithoutEditorSimulationFailure(localizer, manager, () => session.Tick(IndoorArSession.LocalizeTimeoutS));
                localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(4f, 5f, 6f), Quaternion.identity);
                camera.transform.position = localizer.SdkMapSpace.TransformPoint(0f, floor1Y + 1.5f, 0f);
                LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization request attempt=\\d+ kind=MultiSet .*outcome=Ok"));
                ReportLocalized(manager, true);
                Assert.AreEqual(IndoorArState.Pinned, session.State, "a new accepted request commits after the rejected callback released the lock");
                Assert.AreEqual(new Vector3(4f, 5f, 6f), mapRoot.position);
            }
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            Call(runner, "OnDestroy");
            Call(localizer, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(localizer.SdkMapSpace.gameObject);
            UnityEngine.Object.DestroyImmediate(localizerObject);
            UnityEngine.Object.DestroyImmediate(managerObject);
            UnityEngine.Object.DestroyImmediate(sdkObject);
            UnityEngine.Object.DestroyImmediate(mapRoot.gameObject);
        }
    }

    static void TickSignFallbackWithoutEditorFailure(SignFirstLocalizer signFirst,
        MultiSetLocalizer localizer, SingleFrameLocalizationManager manager)
    {
        WithoutEditorSimulationFailure(localizer, manager, () => signFirst.Tick(SignFirstLocalizer.HoldS));
    }

    static void WithoutEditorSimulationFailure(MultiSetLocalizer localizer,
        SingleFrameLocalizationManager manager, Action action)
    {
        // The adapter subscribes through its completion relay, not its own method.
        var listener = typeof(MultiSetLocalizer).GetField("_completionListener", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(localizer);
        var failure = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), listener,
            listener.GetType().GetMethod("OnNotLocalized"));
        manager.LocalizationFailure.RemoveListener(failure);
        try { action(); }
        finally { manager.LocalizationFailure.AddListener(failure); }
    }

    static void ReportLocalized(SingleFrameLocalizationManager manager, bool poseFound)
    {
        var callback = typeof(SingleFrameLocalizationManager).GetMethod("LocalizationSuccessCallback");
        callback.Invoke(manager, callback.GetParameters().Length == 0 ? null
            : new object[] { new LocalizationSuccessResponse { poseFound = poseFound, confidence = 0.73f } });
    }

    [Test]
    public void CachedRouteRechecksClearanceAfterSmallCameraMovement()
    {
        var grid = IndoorWalkGrid.ForFloor(Resources.Load<TextAsset>("IndoorWalkGrid").text, 1);
        var directory = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,
            "IndoorNavigation/Data/vec_floors12345.json"));
        var door = IndoorRoomIdentityMatcher.MatchSign("VEC136", directory).Landmark.DoorPositionInMap;
        Assert.IsTrue(IndoorArContract.TryParseRequest(_request, out var request));
        request.room = "136";
        request.doorKey = "VEC136";
        request.position = new IndoorArVector3 { x = door.x, y = door.y, z = door.z };
        IndoorArBridge.EditorRequestJson = JsonUtility.ToJson(request);
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        localizer.DeferAnswer = true;
        var camera = new GameObject("AR camera").AddComponent<Camera>();
        camera.transform.SetParent(_host.transform, false);
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camera);
        runner.SetLocalizationMode(IndoorLocalizationMode.MultiSet);
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        typeof(IndoorArRunner).GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runner, 10f);
        var position = new Vector3(11.375f, grid.FloorY + 1.5f, 21.5025f);
        camera.transform.localPosition = position + new Vector3(0f, 0f, -.1f);
        Call(runner, "UpdateHud");
        var cached = Field<Vector3[]>(runner, "_route");
        Assert.AreEqual(2, cached.Length);
        var hud = Field<IndoorHud>(runner, "_hud");
        // The blocked cell snaps to the nearest walkable cell, so guidance continues instead of going blank.
        AssertGuiding(runner);
        Assert.IsNotNull(Field<IndoorArBaseline>(runner, "_baseline").ArrowShownMs);

        camera.transform.localPosition = position;
        Call(runner, "UpdateHud");
        Assert.AreSame(cached, Field<Vector3[]>(runner, "_route"));
        Assert.IsFalse(grid.TryNextWaypoint(cached, position, 1f, out _));
        Assert.IsNull(Field<RectTransform>(hud, "_arrow"));
        CollectionAssert.Contains(Labels(runner), "Route unavailable");
        Assert.IsNotNull(Field<IndoorArBaseline>(runner, "_baseline").ArrowShownMs);

        camera.transform.localPosition = position + new Vector3(0f, 0f, -.1f);
        Call(runner, "UpdateHud");
        Assert.AreSame(cached, Field<Vector3[]>(runner, "_route"));
        AssertGuiding(runner);
        CollectionAssert.DoesNotContain(Labels(runner), "Route unavailable");

        camera.transform.localPosition = position;
        Call(runner, "UpdateHud");
        Assert.AreSame(cached, Field<Vector3[]>(runner, "_route"));
        Assert.IsNull(Field<RectTransform>(hud, "_arrow"));
        CollectionAssert.Contains(Labels(runner), "Route unavailable");
        Assert.AreEqual(IndoorArState.Pinned, session.State);

        camera.transform.localPosition = new Vector3(11.375f, position.y, 21.625f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(.5f, 0f, -.25f));
        Call(runner, "UpdateHud");
        AssertPath(runner, false);
        AssertGuiding(runner);
        CollectionAssert.Contains(Labels(runner), "Follow the arrow");
    }

    [Test]
    public void StepFreeWrongFloorRequestReturnsUnavailableWithoutConnectorRoute()
    {
        IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"stepFree\":true");
        var runner = Runner();
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
        Assert.IsNull(Call(runner, "Route", Vector3.zero));
        Assert.AreEqual(IndoorArState.Done, session.State);
        StringAssert.Contains("\"status\":\"unavailable\"", session.ResultJson);
        StringAssert.Contains("\"errorCode\":\"STEP_FREE_UNVERIFIED\"", session.ResultJson);
        Assert.IsNull(Field<string>(runner, "_floorChange"));
    }

    [Test]
    public void StatusReachesTheHostOnEachChangeWithAPoseOnlyOnceLocalized()
    {
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        IndoorArBridge.LastStatusJson = null;
        try
        {
            var runner = Runner();
            Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
            Call(runner, "Update");
            var first = IndoorArBridge.LastStatusJson;
            StringAssert.Contains("\"type\":\"status\"", first);
            StringAssert.Contains("\"seq\":1,", first);
            StringAssert.Contains("\"localized\":false", first);
            StringAssert.DoesNotContain("\"pose\"", first);
            var sessionId = Regex.Match(first, "\"sessionId\":\"([0-9a-f]{32})\"").Groups[1].Value;
            Assert.IsNotEmpty(sessionId);

            Call(runner, "Update");
            Assert.AreSame(first, IndoorArBridge.LastStatusJson, "nothing changed, so nothing is posted");

            var session = Field<IndoorArSession>(runner, "_session");
            session.OnArSupport(true);
            session.OnAuthenticated(); // the fake MultiSet pins at once
            Call(runner, "Update");
            var pinned = IndoorArBridge.LastStatusJson;
            StringAssert.Contains($"\"sessionId\":\"{sessionId}\",\"seq\":2,", pinned);
            StringAssert.Contains("\"state\":\"pinned\",\"tracking\":true,", pinned);
            StringAssert.Contains("\"localized\":true,\"fallbackAvailable\":false,\"pose\":{\"source\":\"multiset\"", pinned); // the fake is no MultiSetLocalizer
        }
        finally
        {
            state.Invoke(null, new object[] { previous });
            IndoorArBridge.LastStatusJson = null;
        }
    }

    [Test]
    public void ASignPoseMustLandOnTheConfirmedSignsFloorDuringASearch()
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        _host.GetComponent<RunnerFakeLocalizer>().DeferAnswer = true;
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var grid = Resources.Load<TextAsset>("IndoorWalkGrid").text;
        var onFloor1 = new Vector3(0f, IndoorWalkGrid.ForFloor(grid, 1).FloorY + 1.5f, 0f);
        var onFloor2 = new Vector3(0f, IndoorWalkGrid.ForFloor(grid, 2).FloorY + 1.5f, 0f);
        Assert.IsFalse((bool)Call(runner, "IsCandidateSignPoseAllowed", onFloor1), "no search yet");
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
        Assert.IsFalse((bool)Call(runner, "IsCandidateSignPoseAllowed", onFloor1));
        Assert.IsTrue((bool)Call(runner, "IsCandidateSignPoseAllowed", onFloor2));
    }

    [Test]
    public void TrackedClimbAfterAFloorOneSignLetsAFloorTwoVpsPoseThroughUntilASignIsReadAgain()
    {
        var state = typeof(UnityEngine.XR.ARFoundation.ARSession).GetProperty("state").GetSetMethod(true);
        var previous = UnityEngine.XR.ARFoundation.ARSession.state;
        state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking });
        try
        {
            var localizer = _host.AddComponent<RunnerFakeLocalizer>();
            localizer.DeferAnswer = true;
            var camera = new GameObject("AR camera").AddComponent<Camera>();
            camera.transform.SetParent(_host.transform, false);
            var runner = _host.AddComponent<IndoorArRunner>();
            runner.Configure(localizer, _pinPrefab, camera);
            runner.SetLocalizationMode(IndoorLocalizationMode.MultiSet);
            Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
            var grid = Resources.Load<TextAsset>("IndoorWalkGrid").text;
            var floor1 = new Vector3(-5.7651f, IndoorWalkGrid.ForFloor(grid, 1).FloorY + 1.5f, 20f);
            var floor2 = new Vector3(-5.7651f, IndoorWalkGrid.ForFloor(grid, 2).FloorY + 1.5f, 20f);
            var session = Field<IndoorArSession>(runner, "_session");
            var sign = new IndoorRoomLandmark("VEC120", "VEC", "floor_1");
            session.OnArSupport(true);
            session.OnAuthenticated();
            session.OnSignConfirmed(sign);
            camera.transform.localPosition = floor1;
            localizer.CompleteLocalized(0.73f);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Call(runner, "Update");
            camera.transform.localPosition = floor2;
            Call(runner, "Update");
            Assert.IsNull(session.FloorSign, "the tracked climb outranks the floor-1 sign");
            Assert.IsTrue(session.IsWrongFloor(session.CurrentFloor(floor2.y, Field<(int, float)[]>(runner, "_floorHeights"))),
                "floor-1 destination from floor 2 needs the stairs or elevator");

            state.Invoke(null, new object[] { UnityEngine.XR.ARFoundation.ARSessionState.SessionInitializing });
            Call(runner, "Update");
            Assert.AreEqual(IndoorArState.Localizing, session.State);
            var candidate = new GameObject("VPS candidate").transform;
            candidate.SetParent(_host.transform, false);
            Assert.IsTrue((bool)Call(runner, "IsCandidateMultiSetPoseAllowed", candidate),
                "a floor-2 VPS pose after the climb is not a conflict");

            session.OnSignConfirmed(sign);
            LogAssert.Expect(LogType.Log, "[IndoorAr] floor conflict sign=1 pose=2");
            Assert.IsFalse((bool)Call(runner, "IsCandidateMultiSetPoseAllowed", candidate),
                "a sign read after the climb is fresh floor evidence");
        }
        finally { state.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void LocalizationModeDefaultsToMultiSet()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, _host.AddComponent<Camera>());
        runner.SetSignCalibration(IndoorSignPoseTests.ValidatedHere(out _, out _));
        localizer.DeferAnswer = true;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        Assert.AreSame(localizer, Field<IIndoorArLocalizer>(runner, "_localizer"));
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        Field<IIndoorArLocalizer>(runner, "_localizer").Authenticate();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(1, localizer.AttemptCount);
    }

    [TestCase(2, "VECstairsF2B", 3, "VEC301", "stairs")]
    [TestCase(2, "VECelev2B", 3, "VEC301", "elevator")]
    [TestCase(2, "VECelev2B", 1, "VEC119", "elevator")]
    [TestCase(1, "VECelev1B", 2, "VEC217", "elevator")]
    [TestCase(3, "VECelev3B", 2, "VEC217", "elevator")]
    [TestCase(3, "VECstairsF3A", 2, "VEC217", "stairs")]
    public void FloorThreeNavigationFollowsTheSelectedConnectorThroughItsLanding(
        int fromFloor, string startId, int toFloor, string room, string connectorType)
    {
        var directory = System.IO.File.ReadAllText(
            System.IO.Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json"));
        var ocr = _host.AddComponent<IndoorRoomMlKitOcr>();
        ocr.Configure(null, new TextAsset(directory));
        var runner = Runner();
        var request = JsonUtility.FromJson<IndoorArRequest>(_request);
        var door = IndoorRoomIdentityMatcher.MatchSign(room, directory).Landmark.DoorPositionInMap;
        request.floor = toFloor.ToString();
        request.room = room.Substring(3);
        request.doorKey = room;
        request.position = new IndoorArVector3 { x = door.x, y = door.y, z = door.z };
        IndoorArBridge.EditorRequestJson = JsonUtility.ToJson(request);
        var grids = IndoorWalkGrid.LoadFloors(Resources.Load<TextAsset>("IndoorWalkGrid").text);
        var camera = new GameObject("AR Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.SetParent(_host.transform, false);
        runner.Configure(_host.GetComponent<RunnerFakeLocalizer>(), _pinPrefab, camera);

        var connectors = IndoorRoomIdentityMatcher.FloorConnectors(directory, fromFloor);
        var origin = connectors.Single(c => c.Id == startId).PositionInMap;
        var x = grids[fromFloor].ColumnOf(origin.x);
        var z = grids[fromFloor].RowOf(origin.z);
        // Elevator markers sit in blocked cells: a person waits at the nearest walkable cell.
        Assert.IsTrue(grids[fromFloor].TryApproach(origin, out var startCell));
        var start = startCell + Vector3.up * 1.5f;
        camera.transform.position = start;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Assert.AreEqual(IndoorArState.Pinned, session.State);
        runner.GetType().GetField("_pinnedFor", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(runner, LiveDoorHud.LocationFoundHoldS);

        Call(runner, "UpdateHud");
        var route = Field<Vector3[]>(runner, "_route");
        Assert.IsNotNull(route);
        var pin = Field<Transform>(runner, "_pin");
        var selected = connectors.Single(c => c.PositionInMap == pin.position);
        if (connectorType == "elevator") // stairs may legitimately win beside both, so only an elevator case proves the landing check
            Assert.AreEqual(startId, selected.Id, "the elevator the user stands at is not skipped for the stairs");
        var connectorId = selected.Id.Replace(fromFloor.ToString(), toFloor.ToString());
        var hudArea = Field<IndoorHud>(runner, "_hud").transform.Find("HUD Canvas/Safe area");
        StringAssert.Contains(" · to Floor " + toFloor, hudArea.Find("Pin label")
            .GetComponentInChildren<UnityEngine.UI.Text>().text);
        AssertGuiding(runner);
        CollectionAssert.Contains(Labels(runner), "You're on Floor " + fromFloor);

        // Far off the scan the camera cannot snap to a walkable cell, so there is no route and no guidance.
        camera.transform.position = startCell + new Vector3(200f, 1.5f, 0f);
        Call(runner, "UpdateHud");
        Assert.IsNull(Field<Vector3[]>(runner, "_route"));
        Assert.IsNull(hudArea.Find("Guide arrow"));
        AssertPath(runner, false);
        Assert.IsFalse(pin.gameObject.activeSelf);
        Assert.IsFalse(hudArea.Find("Docked pin").gameObject.activeSelf);
        Assert.IsFalse(hudArea.Find("Pin label").gameObject.activeSelf);
        camera.transform.position = start;
        Call(runner, "UpdateHud");
        AssertGuiding(runner);
        Assert.IsTrue(hudArea.Find("Pin label").gameObject.activeSelf);

        var landing = IndoorRoomIdentityMatcher.FloorConnectors(directory, toFloor)
            .Single(c => c.Id == connectorId).PositionInMap;
        Assert.IsTrue(grids[toFloor].TryApproach(landing, out var landingCell));
        camera.transform.position = landingCell + Vector3.up * 1.5f;
        camera.transform.LookAt(door);
        Call(runner, "UpdateHud");
        Assert.IsNotNull(Field<Vector3[]>(runner, "_route"));
        Assert.IsNull(Field<string>(runner, "_floorChange"));
        Assert.AreEqual(door, pin.position);
        Assert.IsTrue(pin.gameObject.activeSelf);
        AssertGuiding(runner);
        CollectionAssert.Contains(Labels(runner), "You're on Floor " + toFloor);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadingDestinationTwiceStopsTheRouteAndReportsArrivalWithoutAPose(bool stepFree)
    {
        if (stepFree)
            IndoorArBridge.EditorRequestJson = _request.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"stepFree\":true");
        var runner = Runner();
        _host.GetComponent<RunnerFakeLocalizer>().DeferAnswer = true;
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        var directory = System.IO.File.ReadAllText(
            System.IO.Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json"));
        var started = Field<float>(runner, "_sessionStarted");
        const string occupancy = "MAXIMUM OCCUPANCY FOR THIS\nROOM\n119\nPERSONS";
        foreach (var text in new[] { occupancy, occupancy, "119", "119" })
        {
            Call(runner, "OnSignRead", text, IndoorRoomIdentityMatcher.MatchSign(text, directory), started, null);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
            Assert.IsNull(session.FloorSign);
        }
        var floors = Field<(int Floor, float Y)[]>(runner, "_floorHeights");
        var floorThreeEyeHeight = floors.Single(f => f.Floor == 3).Y + 1.5f;
        Assert.IsFalse(session.PoseConflictsWithSign(floorThreeEyeHeight, floors));
        Assert.AreEqual(3, session.CurrentFloor(floorThreeEyeHeight, floors));

        const string floorThreePlacard = "VEC 301\nMAXIMUM OCCUPANCY 119";
        var floorThreeMatch = IndoorRoomIdentityMatcher.MatchSign(floorThreePlacard, directory);
        Call(runner, "OnSignRead", floorThreePlacard, floorThreeMatch, started, null);
        Call(runner, "OnSignRead", floorThreePlacard, floorThreeMatch, started, null);
        Call(runner, "UpdateHud");
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual("VEC301", session.FloorSign?.Id);
        Assert.IsFalse(session.PoseConflictsWithSign(floorThreeEyeHeight, floors));
        Assert.IsTrue(session.PoseConflictsWithSign(floors.Single(f => f.Floor == 1).Y + 1.5f, floors));
        CollectionAssert.Contains(Labels(runner), "You're on Floor 3");
        CollectionAssert.Contains(Labels(runner), "VEC 119 is on Floor 1");

        const string firstRead = "VEC\n119";
        Call(runner, "OnSignRead", firstRead, IndoorRoomIdentityMatcher.MatchSign(firstRead, directory), started, null);
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Call(runner, "OnSignRead", "ROOM 119", IndoorRoomIdentityMatcher.MatchSign("ROOM 119", directory), started, null);
        Assert.AreEqual(IndoorArState.Arrived, session.State);
        Assert.IsNull(Field<Vector3[]>(runner, "_route"));
        var hud = Field<IndoorHud>(runner, "_hud");
        Assert.IsNull(hud.transform.Find("HUD Canvas/Safe area/Guide arrow"));
        AssertPath(runner, false);
        CollectionAssert.Contains(Labels(runner), "You've arrived");
        Call(runner, "PostStatus");
        StringAssert.Contains("\"localized\":false", IndoorArBridge.LastStatusJson);
        StringAssert.DoesNotContain("\"pose\"", IndoorArBridge.LastStatusJson);
        hud.transform.Find("HUD Canvas/Safe area/Arrival card/Done").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        StringAssert.Contains("\"status\":\"arrived\"", session.ResultJson);
    }

    static string[] Labels(IndoorArRunner runner) =>
        runner.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
}
