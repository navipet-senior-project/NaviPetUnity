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
    public event Action QuerySent;
    public event Action Localized;
    public Transform MapSpace => transform;
    public string MapCode => "MSET_XTBY7AM7ZK77";
    public int AuthenticationCalls { get; private set; }
    public void Authenticate() { AuthenticationCalls++; Authenticated?.Invoke(); }
    public bool Succeed = true;
    public void Localize() { QuerySent?.Invoke(); if (Succeed) Localized?.Invoke(); }
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
        _request = IndoorArBridge.EditorRequestJson;
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
        Assert.That(Quaternion.Angle(Quaternion.Euler(0,185.36f,0),pin.localRotation), Is.LessThan(.001f));
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
            var manager = objects.Select(t=>t.GetComponent<MapLocalizationManager>()).FirstOrDefault(c=>c!=null);
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
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}
