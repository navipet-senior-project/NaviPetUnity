using System.Collections.Generic;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ArSessionLifecycleTests
{
    [Test]
    public void XrPoseTakesOverOnlyWhenTheXrSubsystemReportsAPose()
    {
        // The editor runs no XR loader: no device, so the camera keeps its pose driver and its transform.
        Assert.IsFalse(XrCameraPose.TryReadPose(new List<UnityEngine.XR.InputDevice>(), out _, out var rotation));
        Assert.AreEqual(Quaternion.identity, rotation);
        Assert.IsFalse(ArSessionLifecycle.LoaderRestarted, "no loader restart happens off device");
    }

    [Test]
    public void ExitStopsAndTearsDownWithoutStartingAnotherLoader()
    {
        var steps = new List<string>();
        ArSessionLifecycle.ApplyExit(true, () => steps.Add("stop"), () => steps.Add("tear"));
        CollectionAssert.AreEqual(new[] { "stop", "tear" }, steps);
    }

    [Test]
    public void ExitWithNoLoaderOnlyStopsTheSession()
    {
        var toreDown = false;
        ArSessionLifecycle.ApplyExit(false, () => { }, () => toreDown = true);
        Assert.IsFalse(toreDown);
    }

    [Test]
    public void NextEntryInitializesOnlyWhenTheLoaderIsDown()
    {
        Assert.IsTrue(ArSessionLifecycle.ShouldInitialize(false));
        Assert.IsFalse(ArSessionLifecycle.ShouldInitialize(true));
    }

    [TestCase(9.9f, false, false)]
    [TestCase(10f, false, true)]
    [TestCase(12f, true, false)]
    public void EntryLoaderWaitIsBounded(float elapsedS, bool loaderReady, bool expected)
    {
        Assert.AreEqual(expected, ArSessionLifecycle.InitializationTimedOut(elapsedS, loaderReady));
    }

    [TestCase(false, false, false, true)] // scene reloaded after exit: loader is down
    [TestCase(false, true, false, false)] // first launch: loader already running
    [TestCase(true, false, false, false)] // editor never touches XR
    [TestCase(false, false, true, false)] // outdoor scene arms itself
    public void ReloadedSceneStartInitializesAStoppedLoader(bool editor, bool loaderActive, bool outdoor, bool expected)
    {
        Assert.AreEqual(expected, ArSessionLifecycle.StartNeedsLoader(editor, loaderActive, outdoor));
    }

    [Test]
    public void ArComponentsIncludingTheCameraBackgroundFollowTheLoader()
    {
        var go = new GameObject("AR");
        try
        {
            go.AddComponent<Camera>();
            var manager = go.AddComponent<ARCameraManager>();
            var background = go.AddComponent<ARCameraBackground>();
            var session = go.AddComponent<ARSession>();
            ArSessionLifecycle.SetArComponents(false);
            Assert.IsFalse(manager.enabled || background.enabled || session.enabled);
            ArSessionLifecycle.SetArComponents(true);
            Assert.IsTrue(manager.enabled && background.enabled && session.enabled);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
