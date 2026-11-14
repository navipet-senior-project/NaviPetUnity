using System.Collections;
using System.Text.RegularExpressions;
using IndoorAr;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;

public class IndoorArRouterHoldTests
{
    const string OutdoorRequest = "{\"schemaVersion\":2,\"mode\":\"outdoor\"," +
        "\"destination\":{\"name\":\"VEC\",\"latitude\":33.7829,\"longitude\":-118.1108},\"dataUseAccepted\":true}";

    EditorBuildSettingsScene[] _listedScenes;

    // A failed assertion must not leave the build settings emptied.
    [TearDown]
    public void RestoreBuildSettings()
    {
        if (_listedScenes != null) EditorBuildSettings.scenes = _listedScenes;
        _listedScenes = null;
    }

    [TestCase(OutdoorRequest, true)]
    [TestCase("{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"}", false)]
    [TestCase("bad json", false)]
    [TestCase(null, false)]
    public void OnlyAnOutdoorRequestHoldsTheRouterScene(string json, bool holds) =>
        Assert.AreEqual(holds, IndoorArRouterHold.Holds(json));

    [UnityTest]
    public IEnumerator OutdoorRequestLeavesTheRouterSceneSessionAndCameraIdle()
    {
        var previousRequest = IndoorArBridge.EditorRequestJson;
        _listedScenes = EditorBuildSettings.scenes;
        EditorBuildSettings.scenes = new EditorBuildSettingsScene[0]; // no outdoor scene: the router answers and stays loaded
        yield return new EnterPlayMode();
        IndoorArBridge.EditorRequestJson = OutdoorRequest; // after the domain reload, which resets statics
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result .*unavailable"));
        EditorSceneManager.LoadSceneInPlayMode("Assets/IndoorAr/Scenes/IndoorArSpike.unity",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        Assert.IsFalse(Object.FindFirstObjectByType<ARSession>().enabled);
        Assert.IsFalse(Object.FindFirstObjectByType<ARCameraManager>().enabled);
        yield return new ExitPlayMode();
        IndoorArBridge.EditorRequestJson = previousRequest;
    }

    [UnityTest]
    public IEnumerator IndoorRequestKeepsTheRouterSceneSessionAndCameraRunning()
    {
        var previousRequest = IndoorArBridge.EditorRequestJson;
        yield return new EnterPlayMode();
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result "));
        EditorSceneManager.LoadSceneInPlayMode("Assets/IndoorAr/Scenes/IndoorArSpike.unity",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        Assert.IsTrue(Object.FindFirstObjectByType<ARSession>().enabled);
        Assert.IsTrue(Object.FindFirstObjectByType<ARCameraManager>().enabled);
        Object.FindFirstObjectByType<IndoorArRunner>().SendMessage("OnHudExit");
        yield return null;
        yield return new ExitPlayMode();
        IndoorArBridge.EditorRequestJson = previousRequest;
    }
}
