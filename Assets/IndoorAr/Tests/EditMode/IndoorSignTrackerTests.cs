using IndoorAr;
using NUnit.Framework;
using System.IO;
using UnityEngine;

public class IndoorSignTrackerTests
{
    static readonly string VecDirectory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"));

    static IndoorRoomMatch Read(string text) => IndoorRoomIdentityMatcher.MatchSign(text, VecDirectory);

    [Test]
    public void ConfirmsAfterTwoReadsInTheWindowOnly()
    {
        var tracker = new IndoorSignTracker();
        Assert.IsFalse(tracker.Observe(Read("VEC 120"), 0f));
        Assert.IsNull(tracker.Confirmed);
        Assert.IsFalse(tracker.InView(0f));
        Assert.IsFalse(tracker.Observe(Read("VEC 120"), IndoorSignTracker.ReadWindowS + 1f), "stale first read");
        Assert.IsTrue(tracker.Observe(Read("VEC 120"), IndoorSignTracker.ReadWindowS + 2f));
        Assert.AreEqual("VEC120", tracker.Confirmed.Id);
        Assert.IsTrue(tracker.InView(IndoorSignTracker.ReadWindowS + 2f));
        Assert.IsFalse(tracker.InView(IndoorSignTracker.ReadWindowS * 3f));
        Assert.IsFalse(tracker.Observe(Read("VEC 120"), IndoorSignTracker.ReadWindowS + 3f), "same room is not new");
    }

    [Test]
    public void AmbiguousReadResetsAndNoiseIsIgnored()
    {
        var tracker = new IndoorSignTracker();
        tracker.Observe(Read("VEC 119"), 0f);
        tracker.Observe(Read("VEC 119\nVEC 120"), .5f);
        Assert.IsFalse(tracker.Observe(Read("VEC 119"), 1f), "ambiguous frame reset the count");
        Assert.IsFalse(tracker.Observe(Read("EXIT"), 1.2f));
        Assert.IsFalse(tracker.Observe(null, 1.3f));
        Assert.IsTrue(tracker.Observe(Read("VEC 119"), 1.5f), "noise does not reset the count");
    }

    [Test]
    public void SameLineAmbiguityResetsConfirmation()
    {
        var tracker = new IndoorSignTracker();
        tracker.Observe(Read("VEC 119"), 0f);
        Assert.IsFalse(tracker.Observe(Read("VEC 119 VEC 120"), .5f));
        Assert.IsFalse(tracker.Observe(Read("VEC 119"), 1f));
        Assert.IsTrue(tracker.Observe(Read("VEC 119"), 1.5f));
    }

    [Test]
    public void ANewRoomReplacesTheConfirmedOne()
    {
        var tracker = new IndoorSignTracker();
        tracker.Observe(Read("VEC 119"), 0f);
        tracker.Observe(Read("VEC 119"), .5f);
        Assert.IsFalse(tracker.Observe(Read("VEC 215"), 1f));
        Assert.AreEqual("VEC119", tracker.Confirmed.Id);
        Assert.IsFalse(tracker.InView(1f), "the confirmed sign is no longer the one in view");
        Assert.IsTrue(tracker.Observe(Read("VEC 215"), 1.5f));
        Assert.AreEqual("floor_2", tracker.Confirmed.FloorId);
    }
}
