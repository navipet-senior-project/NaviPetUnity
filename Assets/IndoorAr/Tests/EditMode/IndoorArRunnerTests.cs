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
    GameObject _host;
    GameObject _pinPrefab;
    string _request;

    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    [SetUp]
    public void SetUp()
    {
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
            : _request.Replace("multiset-mapset", "unity-world");
        var runner = Runner();
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*" + code));
        Assert.IsFalse(((IEnumerator)Call(runner, "Start")).MoveNext());
        Call(runner, "Update");
        Call(runner, "Update");
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void ExitDuringAvailabilityDeliversCancelledAndPreventsLaterAuth()
    {
        var runner = Runner();
        var start = (IEnumerator)Call(runner, "Start");
        Assert.IsTrue(start.MoveNext()); // availability is still pending
        var session = Field<IndoorArSession>(runner, "_session");
        session.Cancel();
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*cancelled"));
        Call(runner, "Update");
        Assert.IsFalse(start.MoveNext());
        Assert.AreEqual(IndoorArState.Done, session.State);
        Call(runner, "Update");
        LogAssert.NoUnexpectedReceived();
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
            .Invoke(runner, new object[] { _request });
        var currentSession = Field<IndoorArSession>(runner, "_session");
        currentSession.OnArSupport(true);
        currentSession.OnAuthenticated();
        var currentAttempt = localizer.LastAttemptId;
        localizer.CompleteWithoutPose(oldAttempt);

        var match = IndoorRoomIdentityMatcher.MatchSign("119", "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"VEC119\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        var started = Field<float>(runner, "_sessionStarted");
        var onSignRead = runner.GetType().GetMethod("OnSignRead", BindingFlags.Instance | BindingFlags.NonPublic);
        onSignRead.Invoke(runner, new object[] { "119", match, started, null });
        onSignRead.Invoke(runner, new object[] { "119", match, started, null });
        Assert.AreEqual(2, localizer.AttemptCount, "old failure cannot spend current session retry");

        localizer.CompleteWithoutPose(currentAttempt);
        onSignRead.Invoke(runner, new object[] { "119", match, started, null });
        Assert.AreEqual(3, localizer.AttemptCount, "current session failure retains early sign retry");
    }

    [Test]
    public void StaleSignReadCannotSetNewSessionRoom()
    {
        var runner = Runner();
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        var match = IndoorRoomIdentityMatcher.MatchSign("119", "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"119\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}");
        runner.GetType().GetMethod("OnSignRead", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(runner, new object[] { "119", match, Field<float>(runner, "_sessionStarted") - 1f, null });
        Assert.IsNull(session.SignRoom);
    }

    [Test]
    public void PinUsesMapLocalPoseAndHidesOnTrackingLoss()
    {
        var runner = Runner();
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        session.OnAuthenticated();
        Call(runner, "UpdatePin");
        var pin = Field<Transform>(runner, "_pin");
        Assert.AreSame(_host.transform, pin.parent);
        Assert.AreEqual(new Vector3(-5.7651f, -1.8186f, 22.6689f), pin.localPosition);
        Assert.IsNotNull(pin.Find("Amber pin"));
        Assert.IsNotNull(pin.Find("White core"));
        Assert.IsTrue(pin.gameObject.activeSelf);
        _host.GetComponent<RunnerFakeLocalizer>().Succeed = false;
        session.OnTrackingLost();
        Call(runner, "UpdatePin");
        Assert.IsFalse(pin.gameObject.activeSelf);
    }

    [Test]
    public void PinAndItsRoomLabelStayBehindTheArrivalCard()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        // One meter short of the door, facing it.
        camObject.transform.localPosition = new Vector3(-5.7651f, -0.4f, 21.6689f);
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
            Assert.AreEqual("VEC 119", label.GetComponentInChildren<UnityEngine.UI.Text>().text);
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
    public void BuiltSceneWiresTheSameMapSpaceAndArCamera()
    {
        var scene = EditorSceneManager.OpenScene("Assets/IndoorAr/Scenes/IndoorArSpike.unity", OpenSceneMode.Additive);
        try
        {
            var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var runner = objects.Select(t=>t.GetComponent<IndoorArRunner>()).FirstOrDefault(c=>c!=null);
            var visionOcr = objects.Select(t=>t.GetComponent<IndoorRoomVisionOcr>()).FirstOrDefault(c=>c!=null);
            var androidOcr = objects.Select(t=>t.GetComponent<IndoorRoomOcrDemo>()).FirstOrDefault(c=>c!=null);
            var localizer = objects.Select(t=>t.GetComponent<MultiSetLocalizer>()).FirstOrDefault(c=>c!=null);
            var manager = objects.Select(t=>t.GetComponent<SingleFrameLocalizationManager>()).FirstOrDefault(c=>c!=null);
            Assert.IsNotNull(runner);
            Assert.IsNotNull(visionOcr);
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
            var visionSerialized = new SerializedObject(visionOcr);
            Assert.AreSame(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(),
                visionSerialized.FindProperty("cameraManager").objectReferenceValue);
            Assert.IsNotNull(visionSerialized.FindProperty("directory").objectReferenceValue);
            var androidSerialized = new SerializedObject(androidOcr);
            Assert.AreSame(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>(),
                androidSerialized.FindProperty("cameraManager").objectReferenceValue);
            Assert.AreEqual(visionSerialized.FindProperty("directory").objectReferenceValue,
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
    public void PinnedHudPointsOneArrowFromTheCameraTowardTheDoor()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var camObject = new GameObject("AR Camera", typeof(Camera));
        camObject.transform.SetParent(_host.transform, false);
        camObject.transform.localPosition = new Vector3(1f, -0.4f, 3f);
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
            var door = new Vector3(-5.7651f, -1.8186f, 22.6689f);
            var arrow = hud.transform.Find("HUD Canvas/Safe area/Guide arrow");
            Assert.AreEqual(1, hud.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Guide arrow"));
            Assert.AreEqual(0, hud.GetComponentsInChildren<MeshRenderer>(true).Length);
            var yaw = GuidanceCue.ArrowYawDegrees(Vector3.forward, camObject.transform.position, door);
            Assert.AreEqual(GuidanceCue.ArrowScreenRotation(yaw), Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
            // Camera height is in the supported eye-height band above Floor 1.
            CollectionAssert.Contains(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(), "VEC 119 is ahead");
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("VEC 119")));

            camObject.transform.localRotation = Quaternion.LookRotation(Vector3.right);
            Call(runner, "Update");
            Assert.AreSame(arrow, hud.transform.Find("HUD Canvas/Safe area/Guide arrow"));
            var turned = GuidanceCue.ArrowYawDegrees(Vector3.right, camObject.transform.position, door);
            Assert.AreEqual(GuidanceCue.ArrowScreenRotation(turned), Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
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

    // X and the back key both call OnHudExit; arrival is "I'm here" then Done.
    [TestCase("cancel", "cancelled")]
    [TestCase("arrive", "arrived")]
    public void EveryExitDeliversOnceThenTheNextLaunchStartsClean(string exit, string status)
    {
        var runner = Runner(IndoorLocalizationMode.SignFirst);
        runner.SetSignCalibration(IndoorSignTrailTests.ValidatedHere(out _, out _));
        Assert.IsTrue(((IEnumerator)Call(runner, "Start")).MoveNext());
        var first = Field<IndoorArSession>(runner, "_session");
        var signFirst = Field<SignFirstLocalizer>(runner, "_signFirst");
        first.OnArSupport(true);
        signFirst.Authenticate(); // opens the sign hold
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
            Assert.AreEqual(2, baseline.Attempts);
            Assert.AreEqual(1, baseline.Retries);
            Assert.AreEqual(1, baseline.UnansweredAttempts);
            Assert.AreEqual(IndoorArState.Localizing, session.State);
        }
        finally { state.Invoke(null, new object[] { previous }); }
    }

    [Test]
    public void SignPoseModeLocalizesWithoutTouchingMultiSet()
    {
        var runner = Runner(IndoorLocalizationMode.SignPose);
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        var session = Field<IndoorArSession>(runner, "_session");
        var fake = _host.GetComponent<RunnerFakeLocalizer>();
        Assert.IsInstanceOf<SignPoseLocalizer>(Field<IIndoorArLocalizer>(runner, "_localizer"));
        session.OnArSupport(true);
        Field<IIndoorArLocalizer>(runner, "_localizer").Authenticate();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(0, fake.AuthenticationCalls);
        Assert.AreEqual(0, fake.AttemptCount);
        Assert.AreEqual(0, session.Queries, "no VPS attempt is counted");
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
        var floor2 = IndoorWalkGrid.ForFloor(gridJson, 2);
        cameraObject.transform.localPosition = Vector3.up * (belowFloorOne ? floor1.FloorY - 3f : floor2.FloorY + 4f);
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

    [TestCase("signConflict")]
    [TestCase("below")]
    [TestCase("above")]
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
        var mapRoot = new GameObject("Navigation Map Space").transform;
        mapRoot.SetPositionAndRotation(new Vector3(7f, 2f, 9f), Quaternion.Euler(0f, 20f, 0f));
        localizer.Configure(sdk, manager, mapRoot);
        localizerObject.SetActive(true);
        Call(localizer, "Awake");
        var camera = new GameObject("AR Camera", typeof(Camera)).GetComponent<Camera>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, camera);
        runner.SetLocalizationMode(IndoorLocalizationMode.SignFirst);
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

            LogAssert.Expect(LogType.Log, "Localization not supported in Unity Editor. Using simulation data to test localization.");
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            TickSignFallbackWithoutEditorFailure(signFirst, localizer, manager);
            localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.identity);
            camera.transform.position = localizer.SdkMapSpace.TransformPoint(0f, floor1Y + 1.5f, 0f);
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization mapCodes=.*"));
            ReportLocalized(manager, true);
            Assert.AreEqual(IndoorArState.Pinned, session.State);
            Call(runner, "UpdatePin");

            if (candidateKind == "signConflict")
                session.OnSignConfirmed(new IndoorRoomLandmark("VEC215", "VEC", "floor_2"));
            session.OnTrackingLost();
            Call(runner, "UpdatePin");
            var pin = Field<Transform>(runner, "_pin");
            var hud = Field<IndoorHud>(runner, "_hud").transform;
            var beforeRootPosition = mapRoot.position;
            var beforeRootRotation = mapRoot.rotation;
            var beforePinPosition = pin.position;
            var beforePinRotation = pin.rotation;
            var beforeHudPosition = hud.position;
            var beforeHudRotation = hud.rotation;
            var rejectedPoseY = candidateKind == "signConflict" ? floor1Y + 1.5f
                : candidateKind == "below" ? floor1Y - 3f : floor2Y + 3f;
            LogAssert.Expect(LogType.Log, "Localization not supported in Unity Editor. Using simulation data to test localization.");
            LogAssert.Expect(LogType.Error, "SimulationDataManager not found in scene!");
            TickSignFallbackWithoutEditorFailure(signFirst, localizer, manager);
            Assert.IsFalse(localizer.SdkMapSpace.IsChildOf(mapRoot));
            Assert.IsFalse(mapRoot.IsChildOf(localizer.SdkMapSpace));
            localizer.SdkMapSpace.SetPositionAndRotation(new Vector3(-12f, 8f, 19f), Quaternion.Euler(0f, 80f, 0f));
            camera.transform.position = localizer.SdkMapSpace.TransformPoint(0f, rejectedPoseY, 0f);
            var expectedEvidence = candidateKind == "signConflict" ? "sign=2 pose=1" : "sign=none pose=unsupported";
            LogAssert.Expect(LogType.Log, $"[IndoorAr] floor conflict {expectedEvidence}");
            LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization mapCodes=.*"));
            ReportLocalized(manager, true);

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
                Assert.AreEqual(IndoorSignPoseStatus.Ok, signFirst.ObservePose(sighting));
                Assert.AreEqual(IndoorArState.Pinned, session.State, "a validated sign can still localize after MultiSet rejection");
                Assert.IsTrue(signFirst.FromSigns);
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
                LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] localization mapCodes=.*"));
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
        var failure = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), localizer,
            typeof(MultiSetLocalizer).GetMethod("OnNotLocalized", BindingFlags.Instance | BindingFlags.NonPublic));
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
    public void LocalizationModeDefaultsToSignsFirstWithMultiSetBehindIt()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, _host.AddComponent<Camera>());
        runner.SetSignCalibration(IndoorSignTrailTests.ValidatedHere(out _, out _));
        ((IEnumerator)Call(runner, "Start")).MoveNext();
        Assert.IsInstanceOf<SignFirstLocalizer>(Field<IIndoorArLocalizer>(runner, "_localizer"));
        var session = Field<IndoorArSession>(runner, "_session");
        session.OnArSupport(true);
        Field<IIndoorArLocalizer>(runner, "_localizer").Authenticate();
        Assert.AreEqual(IndoorArState.Localizing, session.State);
        Assert.AreEqual(0, localizer.AttemptCount, "MultiSet is not the first thing asked where a sign can pose");
    }

    static string[] Labels(IndoorArRunner runner) =>
        runner.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
}
