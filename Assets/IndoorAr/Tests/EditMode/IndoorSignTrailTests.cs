using System.Collections;
using System.Reflection;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorSignTrailTests
{
    // Two doors on the south wall (heading 180 points into the room), 10 m apart.
    static readonly IndoorRoomLandmark West = new IndoorRoomLandmark("VEC1", "VEC", "floor_1", new Vector3(0f, -2f, 20f), 180f);
    static readonly IndoorRoomLandmark East = new IndoorRoomLandmark("VEC2", "VEC", "floor_1", new Vector3(10f, -2f, 20f), 180f);
    static readonly IndoorRoomLandmark Next = new IndoorRoomLandmark("VEC3", "VEC", "floor_1", new Vector3(4f, -2f, 20f), 180f);

    // Where a reader's phone is, in the AR world, when the map sits at this pose.
    static Vector3 Reader(Transform map, IndoorRoomLandmark room, Vector3 offInMap = default) => map.TransformPoint(
        room.DoorPositionInMap + new Vector3(0f, IndoorSignTrail.CameraHeightM, IndoorSignTrail.StandoffM) + offInMap);

    [Test]
    public void OneSignIsAnAreaNotAPosition()
    {
        var trail = new IndoorSignTrail();
        trail.Add(West, new Vector3(3f, 0f, 3f));
        var fix = trail.Solve();
        Assert.AreEqual(1, fix.Signs);
        Assert.IsFalse(fix.Usable);
    }

    [Test]
    public void TwoSeparatedSignsRecoverTheMapPoseAndSayHowCoarseItIs()
    {
        var map = new GameObject("truth").transform;
        try
        {
            map.SetPositionAndRotation(new Vector3(7f, 1.2f, -3f), Quaternion.Euler(0f, 130f, 0f));
            var trail = new IndoorSignTrail();
            trail.Add(West, Reader(map, West));
            trail.Add(East, Reader(map, East));
            var fix = trail.Solve();
            Assert.IsTrue(fix.Usable);
            Assert.AreEqual(10f, fix.BaselineM, 1e-3f);
            Assert.AreEqual(0f, fix.ResidualM, 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(130f, fix.YawDeg), 1e-2f);
            Assert.AreEqual(0f, Vector3.Distance(map.position, fix.Origin), 1e-3f);
            Assert.AreEqual(16.7f, fix.YawBoundDeg, .1f, "atan(2 * 1.5 m / 10 m): the heading is never claimed tighter");

            // A reader 1 m off the assumed spot at each door turns the fix, but inside the stated bound.
            trail.Add(West, Reader(map, West, new Vector3(0f, 0f, 1f)));
            trail.Add(East, Reader(map, East, new Vector3(0f, 0f, -1f)));
            var skewed = trail.Solve();
            Assert.AreEqual(2, skewed.Signs, "a room read again replaces its sighting");
            var error = Mathf.Abs(Mathf.DeltaAngle(130f, skewed.YawDeg));
            Assert.Greater(error, 5f);
            Assert.Less(error, skewed.YawBoundDeg);
        }
        finally { Object.DestroyImmediate(map.gameObject); }
    }

    [Test]
    public void CloseSignsOrAPathThatDisagreesAreNotUsable()
    {
        var map = new GameObject("truth").transform;
        try
        {
            var near = new IndoorSignTrail();
            near.Add(West, Reader(map, West));
            near.Add(Next, Reader(map, Next));
            Assert.IsFalse(near.Solve().Usable, "4 m apart: the heading bound is too wide");

            // The doors are 10 m apart but AR says the reader walked 16 m: a sign was read from far down the hall.
            var far = new IndoorSignTrail();
            far.Add(West, Reader(map, West));
            far.Add(East, Reader(map, East, new Vector3(6f, 0f, 0f)));
            var fix = far.Solve();
            Assert.Greater(fix.ResidualM, IndoorSignTrail.ReadRangeM);
            Assert.IsFalse(fix.Usable);
        }
        finally { Object.DestroyImmediate(map.gameObject); }
    }

    [Test]
    public void SignsLocalizeFirstAndMultiSetIsOnlyAskedAfterTheHold()
    {
        var host = new GameObject("fake");
        var truth = new GameObject("truth").transform;
        try
        {
            truth.SetPositionAndRotation(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 40f, 0f));
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            var first = new SignFirstLocalizer(inner);
            var localized = 0;
            first.Localized += _ => localized++;

            first.Localize();
            first.Tick(SignFirstLocalizer.HoldS - 1f);
            Assert.AreEqual(0, inner.AttemptCount, "no MultiSet query while signs may still localize");
            Assert.IsFalse(first.Observe(West, Reader(truth, West)).Usable);
            Assert.AreEqual(0, localized, "one sign is not a fix");
            Assert.IsTrue(first.Observe(East, Reader(truth, East)).Usable);
            Assert.AreEqual(1, localized);
            Assert.IsTrue(first.FromSigns);
            Assert.AreEqual(0f, Vector3.Distance(truth.position, host.transform.position), 1e-3f);
            first.Tick(5f);
            Assert.AreEqual(0, inner.AttemptCount, "a sign fix ends the hold");

            // Tracking loss: a new search starts a new hold and drops sightings from the old AR world.
            first.Localize();
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(1, first.Observe(West, Reader(truth, West)).Signs);
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, inner.AttemptCount, "the hold ran out without a sign fix: MultiSet is the fallback");
            Assert.AreEqual(2, localized);
            Assert.IsFalse(first.FromSigns);

            // MultiSet's pose wins from here: a later sign fix is reported but does not move the map.
            host.transform.SetPositionAndRotation(new Vector3(9f, 9f, 9f), Quaternion.identity);
            Assert.IsTrue(first.Observe(East, Reader(truth, East)).Usable);
            Assert.AreEqual(new Vector3(9f, 9f, 9f), host.transform.position);
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(2, localized);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(truth.gameObject);
        }
    }

    [Test]
    public void ResetDropsTheLastSessionsSightingsAndPose()
    {
        var host = new GameObject("fake");
        var truth = new GameObject("truth").transform;
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            var first = new SignFirstLocalizer(inner);
            first.Localize();
            Assert.AreEqual(1, first.Observe(West, Reader(truth, West)).Signs);

            // The next launch's AR world starts elsewhere: an old sighting paired with a new one is no fix.
            first.Reset();
            truth.SetPositionAndRotation(new Vector3(30f, 0f, -12f), Quaternion.Euler(0f, 90f, 0f));
            first.Localize();
            var fix = first.Observe(East, Reader(truth, East));
            Assert.AreEqual(1, fix.Signs);
            Assert.IsFalse(first.FromSigns);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(truth.gameObject);
        }
    }

    [Test]
    public void SessionRetryDuringTheHoldDoesNotQueryMultiSetEarly()
    {
        var host = new GameObject("fake");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.Succeed = false;
            var first = new SignFirstLocalizer(inner);
            first.Localize();
            first.Localize();
            Assert.AreEqual(0, inner.AttemptCount);
            first.Tick(SignFirstLocalizer.HoldS);
            Assert.AreEqual(1, inner.AttemptCount);
            first.Localize();
            Assert.AreEqual(2, inner.AttemptCount, "after the hold the session's retry goes straight to MultiSet");
        }
        finally { Object.DestroyImmediate(host); }
    }
}
