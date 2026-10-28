using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using IndoorAr;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class IndoorArEditorSmokeTests
{
    [UnityTest]
    public IEnumerator SceneStartsAndReturnsOneResult()
    {
        yield return new EnterPlayMode();
        LogAssert.Expect(LogType.Log, new Regex("\\[IndoorAr\\] result "));
        EditorSceneManager.LoadSceneInPlayMode("Assets/IndoorAr/Scenes/IndoorArSpike.unity",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var runner = Object.FindFirstObjectByType<IndoorArRunner>();
        Assert.IsNotNull(runner);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var session = (IndoorArSession)typeof(IndoorArRunner).GetField("_session", flags).GetValue(runner);
        Assert.IsNotNull(session);
        if (session.State != IndoorArState.Done) session.Cancel();
        yield return null;
        Assert.IsTrue((bool)typeof(IndoorArRunner).GetField("_delivered", flags).GetValue(runner));
        Assert.IsTrue(session.ResultJson.Contains("cancelled") || session.ResultJson.Contains("ARCORE_UNSUPPORTED"));
        yield return new ExitPlayMode();
    }
}
