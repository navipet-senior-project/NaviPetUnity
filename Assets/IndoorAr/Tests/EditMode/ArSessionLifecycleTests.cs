using System.Collections.Generic;
using IndoorAr;
using NUnit.Framework;

public class ArSessionLifecycleTests
{
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
}
