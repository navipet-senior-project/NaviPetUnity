using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorSignTriangulatorTests
{
    const float F = 1450f, Cx = 960f, Cy = 720f;
    const float WidthM = .12f, HeightM = .05f;

    static readonly IndoorRoomLandmark West = new IndoorRoomLandmark("VEC1", "VEC", "floor_1", new Vector3(0f, -2f, 20f), 180f);
    static readonly IndoorRoomLandmark East = new IndoorRoomLandmark("VEC2", "VEC", "floor_1", new Vector3(10f, -2f, 20f), 180f);
    // Where a sign's text sits relative to its door marker in these tests: beside the door, at eye height.
    static readonly Vector3 SignFromDoor = new Vector3(.6f, IndoorSignTrail.SignHeightM, 0f);

    /// <summary>What a pinhole camera at this spot, aimed at the sign, sees of its four text corners.</summary>
    static IndoorSignObservation See(string room, Vector3 camera, Vector3 sign, float intoWallYawDeg)
    {
        var onWall = Quaternion.Euler(0f, intoWallYawDeg, 0f);
        var rotation = Quaternion.LookRotation(sign - camera);
        var corners = new Vector2[4];
        var plane = new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1) };
        for (var i = 0; i < 4; i++)
        {
            var world = sign + onWall * new Vector3(plane[i].x * WidthM / 2f, plane[i].y * HeightM / 2f, 0f);
            var p = Quaternion.Inverse(rotation) * (world - camera);
            corners[i] = new Vector2(Cx + F * p.x / p.z, Cy - F * p.y / p.z);
        }
        return new IndoorSignObservation
        {
            RoomId = room, CornersPx = corners, Fx = F, Fy = F, Cx = Cx, Cy = Cy,
            CameraWorldPosition = camera, CameraWorldRotation = rotation
        };
    }

    /// <summary>A reader 1.5 m out from the wall, this far along it from the sign.</summary>
    static Vector3 Reader(Vector3 sign, float intoWallYawDeg, float alongM)
    {
        var onWall = Quaternion.Euler(0f, intoWallYawDeg, 0f);
        return sign + onWall * new Vector3(alongM, -.1f, -1.5f);
    }

    static IndoorSignTriangulation WalkPast(IndoorSignTriangulator triangulator, string room, Vector3 sign,
        float intoWallYawDeg, float stepM = .3f, int frames = 5)
    {
        var result = default(IndoorSignTriangulation);
        for (var i = 0; i < frames; i++)
            result = triangulator.Add(See(room, Reader(sign, intoWallYawDeg, (i - (frames - 1) / 2f) * stepM), sign, intoWallYawDeg));
        return result;
    }

    [Test]
    public void WalkingPastASignLocatesItWithoutKnowingItsSize()
    {
        var sign = new Vector3(3f, 1.5f, 8f);
        var result = WalkPast(new IndoorSignTriangulator(), "VEC1", sign, 40f);

        Assert.AreEqual(IndoorSignFixStatus.Ok, result.Status);
        Assert.AreEqual(5, result.Frames);
        Assert.AreEqual(0f, Vector3.Distance(sign, result.CenterInWorld), 2e-3f);
        Assert.AreEqual(WidthM, result.WidthM, 2e-3f);
        Assert.AreEqual(HeightM, result.HeightM, 2e-3f);
        Assert.AreEqual(0f, Mathf.DeltaAngle(40f, result.IntoWallYawDeg), 1f);
        Assert.Greater(result.ParallaxDeg, 30f);
        Assert.Greater(result.YawSigmaDeg, 0f, "a heading from a fit is never claimed exact");
        Assert.Less(result.YawSigmaDeg, 5f);
    }

    [Test]
    public void StandingStillIsNotAFix()
    {
        var sign = new Vector3(3f, 1.5f, 8f);
        var still = WalkPast(new IndoorSignTriangulator(), "VEC1", sign, 40f, stepM: 0f);
        Assert.AreEqual(IndoorSignFixStatus.FewFrames, still.Status);
        Assert.AreEqual(1, still.Frames, "frames from one spot replace each other");

        var swaying = WalkPast(new IndoorSignTriangulator(), "VEC1", sign, 40f, stepM: .04f, frames: 3);
        Assert.AreEqual(IndoorSignFixStatus.LowParallax, swaying.Status);
        Assert.Less(swaying.ParallaxDeg, IndoorSignTriangulator.MinParallaxDeg);
    }

    [Test]
    public void FramesThatDisagreeWithTheTrackedPathAreRejected()
    {
        var sign = new Vector3(3f, 1.5f, 8f);
        var triangulator = new IndoorSignTriangulator();
        WalkPast(triangulator, "VEC1", sign, 40f, frames: 4);
        // AR reports the camera 0.3 m from where the frame was really taken.
        var late = See("VEC1", Reader(sign, 40f, .9f), sign, 40f);
        late.CameraWorldPosition += Quaternion.Euler(0f, 40f, 0f) * new Vector3(.3f, 0f, 0f);
        Assert.AreEqual(IndoorSignFixStatus.HighResidual, triangulator.Add(late).Status);
    }

    [Test]
    public void CornersInTheWrongImageOrientationAreRejected()
    {
        var sign = new Vector3(3f, 1.5f, 8f);
        var triangulator = new IndoorSignTriangulator();
        var result = default(IndoorSignTriangulation);
        for (var i = 0; i < 5; i++)
        {
            var view = See("VEC1", Reader(sign, 40f, (i - 2) * .3f), sign, 40f);
            var c = view.CornersPx;
            view.CornersPx = new[] { c[3], c[0], c[1], c[2] }; // the text box labelled a quarter turn off
            result = triangulator.Add(view);
        }
        Assert.AreEqual(IndoorSignFixStatus.BadGeometry, result.Status);
    }

    [Test]
    public void OneLocatedSignIsACoarseFixThatNeverPosesTheMap()
    {
        var host = new GameObject("map");
        var truth = new GameObject("truth").transform;
        try
        {
            truth.SetPositionAndRotation(new Vector3(2f, 0f, 5f), Quaternion.Euler(0f, 40f, 0f));
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            inner.DeferAnswer = true;
            var first = new SignFirstLocalizer(inner);
            var localized = 0;
            first.Localized += _ => localized++;
            first.Localize();
            Assert.AreEqual(1, inner.AttemptCount, "no validated sign: MultiSet is asked at once");

            var fix = default(IndoorSignFix);
            var sign = truth.TransformPoint(West.DoorPositionInMap + SignFromDoor);
            for (var i = 0; i < 5; i++)
                first.ObserveCorners(West, See(West.Id, Reader(sign, 220f, (i - 2) * .3f), sign, 220f), out fix);

            Assert.IsTrue(fix.Usable);
            Assert.IsTrue(fix.OneSign);
            Assert.AreEqual(0f, Mathf.DeltaAngle(40f, fix.YawDeg), 1f);
            Assert.AreEqual(SignFromDoor.x, Vector3.Distance(truth.position, fix.Origin), .02f,
                "off by how far the sign really is from the assumed point above its door marker");
            Assert.AreEqual(0, localized, "a recognized room is not a camera pose");
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(Vector3.zero, host.transform.position);

            // A second located sign 10 m on replaces the one-sign fix with the trail's.
            sign = truth.TransformPoint(East.DoorPositionInMap + SignFromDoor);
            for (var i = 0; i < 5; i++)
                first.ObserveCorners(East, See(East.Id, Reader(sign, 220f, (i - 2) * .3f), sign, 220f), out fix);
            Assert.IsTrue(fix.Usable);
            Assert.IsFalse(fix.OneSign);
            Assert.AreEqual(2, fix.Signs);
            Assert.AreEqual(11.3f, fix.YawBoundDeg, .1f, "atan(2 * 1 m / 10 m)");
            Assert.AreEqual(0, localized);

            // Where the reader stood says less than the located sign, so it does not replace it.
            var coarse = first.Observe(West, truth.TransformPoint(new Vector3(50f, 0f, 50f)));
            Assert.AreEqual(2, coarse.Signs);
            Assert.AreEqual(0f, coarse.ResidualM, .01f);
        }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(truth.gameObject);
        }
    }

    [Test]
    public void WithoutParallaxMultiSetIsStillTheFallbackAndItsPoseWins()
    {
        var host = new GameObject("map");
        try
        {
            var inner = host.AddComponent<RunnerFakeLocalizer>();
            var first = new SignFirstLocalizer(inner);
            first.Localize();
            var sign = new Vector3(3f, 1.5f, 8f);
            for (var i = 0; i < 4; i++)
            {
                var read = first.ObserveCorners(West, See(West.Id, Reader(sign, 40f, 0f), sign, 40f), out var none);
                Assert.AreNotEqual(IndoorSignFixStatus.Ok, read.Status);
                Assert.IsFalse(none.Usable);
            }
            Assert.IsFalse(first.FromSigns);
            Assert.AreEqual(1, inner.AttemptCount, "no validated sign: MultiSet is asked at once");

            host.transform.SetPositionAndRotation(new Vector3(9f, 9f, 9f), Quaternion.identity);
            var fix = default(IndoorSignFix);
            for (var i = 0; i < 5; i++)
                first.ObserveCorners(West, See(West.Id, Reader(sign, 40f, (i - 2) * .3f), sign, 40f), out fix);
            Assert.IsTrue(fix.Usable, "the sign fix is still reported, for comparison");
            Assert.AreEqual(new Vector3(9f, 9f, 9f), host.transform.position, "but MultiSet's pose is not moved");
            Assert.IsFalse(first.FromSigns);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
