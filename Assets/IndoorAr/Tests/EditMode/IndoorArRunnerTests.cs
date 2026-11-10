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
    public Transform MapSpace => transform;
    public string MapCode => "MSET_XTBY7AM7ZK77";
    public int AuthenticationCalls { get; private set; }
    public void Authenticate() { AuthenticationCalls++; Authenticated?.Invoke(); }
    public bool Succeed = true;
    public void Localize() { AttemptStarted?.Invoke(); if (Succeed) Localized?.Invoke(0.73f); }
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
    public void BuiltSceneWiresTheSameMapSpaceAndArCamera()
    {
        var scene = EditorSceneManager.OpenScene("Assets/IndoorAr/Scenes/IndoorArSpike.unity", OpenSceneMode.Additive);
        try
        {
            var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var runner = objects.Select(t=>t.GetComponent<IndoorArRunner>()).FirstOrDefault(c=>c!=null);
            var localizer = objects.Select(t=>t.GetComponent<MultiSetLocalizer>()).FirstOrDefault(c=>c!=null);
            var manager = objects.Select(t=>t.GetComponent<SingleFrameLocalizationManager>()).FirstOrDefault(c=>c!=null);
            Assert.IsNotNull(runner);
            Assert.IsNotNull(localizer);
            Assert.IsNotNull(manager);
            var serialized = new SerializedObject(manager);
            Assert.AreSame(localizer.MapSpace.gameObject, serialized.FindProperty("mapSpace").objectReferenceValue);
            var camera = (Camera)serialized.FindProperty("arCamera").objectReferenceValue;
            Assert.IsNotNull(camera);
            Assert.IsNotNull(camera.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>());
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
            var arrow = hud.transform.Find("HUD Canvas/Guide arrow");
            Assert.AreEqual(1, hud.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Guide arrow"));
            Assert.AreEqual(0, hud.GetComponentsInChildren<MeshRenderer>(true).Length);
            var yaw = GuidanceCue.ArrowYawDegrees(Vector3.forward, camObject.transform.position, door);
            Assert.AreEqual(GuidanceCue.ArrowScreenRotation(yaw), Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
            CollectionAssert.Contains(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(), "Direction to room");
            Assert.IsTrue(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("VEC 119")));

            camObject.transform.localRotation = Quaternion.LookRotation(Vector3.right);
            Call(runner, "Update");
            Assert.AreSame(arrow, hud.transform.Find("HUD Canvas/Guide arrow"));
            var turned = GuidanceCue.ArrowYawDegrees(Vector3.right, camObject.transform.position, door);
            Assert.AreEqual(GuidanceCue.ArrowScreenRotation(turned), Mathf.DeltaAngle(0f, arrow.GetComponent<RectTransform>().localEulerAngles.z), 0.5f);
            CollectionAssert.Contains(hud.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(), "Look this way");
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
        CollectionAssert.Contains(Labels(runner), "TO  VEC 119  ·  FLOOR 1");

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
        CollectionAssert.Contains(Labels(runner), "TO  VEC 127  ·  FLOOR 1");
        CollectionAssert.DoesNotContain(Labels(runner), "TO  VEC 119  ·  FLOOR 1");
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
        CollectionAssert.Contains(Labels(runner), "TO  VEC 119  ·  FLOOR 1");
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

    static string[] Labels(IndoorArRunner runner) =>
        runner.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
}
