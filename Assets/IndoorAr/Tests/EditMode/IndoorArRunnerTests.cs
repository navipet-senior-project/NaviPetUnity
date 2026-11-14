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
using UnityEngine.TestTools;

public sealed class RunnerFakeLocalizer : MonoBehaviour, IIndoorArLocalizer
{
    public event Action Authenticated;
    public event Action<bool> AuthFailed;
    public event Action AttemptStarted;
    public event Action<float?> Localized;
    public event Action NotLocalized;
    public Transform MapSpace => transform;
    public string MapCode => "MSET_XTBY7AM7ZK77";
    public int AuthenticationCalls { get; private set; }
    public void Authenticate() { AuthenticationCalls++; Authenticated?.Invoke(); }
    public bool Succeed = true;
    public void Localize() { AttemptStarted?.Invoke(); if (Succeed) Localized?.Invoke(0.73f); else NotLocalized?.Invoke(); }
}

public class IndoorArRunnerTests
{
    GameObject _host;
    GameObject _pinPrefab;
    string _request;

    static object Call(object target, string name) => target.GetType().GetMethod(name,
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
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

    IndoorArRunner Runner()
    {
        var localizer = _host.AddComponent<RunnerFakeLocalizer>();
        var runner = _host.AddComponent<IndoorArRunner>();
        runner.Configure(localizer, _pinPrefab, _host.AddComponent<Camera>());
        return runner;
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
        camObject.transform.localPosition = new Vector3(-5.7651f, -1.8186f, 21.6689f);
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
            Assert.AreSame(localizer.MapSpace.gameObject, serialized.FindProperty("mapSpace").objectReferenceValue);
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
        camObject.transform.localPosition = new Vector3(1f, 1.6f, 3f);
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
            // The test camera stands 3.4 m above the saved door height.
            CollectionAssert.Contains(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(), "VEC 119 is on Floor 1");
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

    static string[] Labels(IndoorArRunner runner) =>
        runner.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
}
