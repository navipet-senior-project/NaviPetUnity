using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorSignPoseTests
{
    const float Fx = 1500f, Fy = 1500f, Cx = 960f, Cy = 540f;

    static IndoorSignCalibrationEntry Sign(bool validated = true) => new IndoorSignCalibrationEntry
    {
        doorKey = "VEC120", widthM = 0.30f, heightM = 0.15f, centerInMap = new Vector3(2f, 1.5f, 8f),
        intoWallHeadingDeg = 90f, validated = validated
    };

    // Projects the sign corners the way a camera at (position, rotation) in map space would see them.
    static IndoorSignObservation Observe(IndoorSignCalibrationEntry sign, Vector3 position, Quaternion rotation)
    {
        var toMap = Quaternion.Euler(0f, sign.intoWallHeadingDeg, 0f);
        var plane = new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1) };
        var corners = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var map = sign.centerInMap + toMap * new Vector3(plane[i].x * sign.widthM / 2f, plane[i].y * sign.heightM / 2f, 0f);
            var cam = Quaternion.Inverse(rotation) * (map - position);
            corners[i] = new Vector2(Cx + Fx * cam.x / cam.z, Cy - Fy * cam.y / cam.z);
        }
        return new IndoorSignObservation
        {
            RoomId = "VEC120", CornersPx = corners, Fx = Fx, Fy = Fy, Cx = Cx, Cy = Cy,
            CameraWorldPosition = new Vector3(10f, 0f, 3f), CameraWorldRotation = Quaternion.Euler(0f, 40f, 0f)
        };
    }

    [TestCase(0f, 0f, 1.6f, 0f)]
    [TestCase(15f, -5f, 1.4f, 0.3f)]
    [TestCase(-20f, 8f, 1.8f, -0.4f)]
    public void SolvesTheCameraPoseFromSyntheticCorners(float yaw, float pitch, float height, float lateral)
    {
        var sign = Sign();
        // The hallway side is -X of the sign (it faces +X, into the wall is +X rotated 90 deg from +Z).
        var position = sign.centerInMap + new Vector3(-1.2f, height - sign.centerInMap.y, lateral);
        var rotation = Quaternion.Euler(pitch, 90f + yaw, 0f);
        var result = IndoorSignPose.Solve(Observe(sign, position, rotation), sign);
        Assert.AreEqual(IndoorSignPoseStatus.Ok, result.Status);
        Assert.Less(Vector3.Distance(position, result.CameraPositionInMap), 0.01f);
        Assert.Less(Quaternion.Angle(rotation, result.CameraRotationInMap), 0.5f);
        Assert.Less(result.ReprojectionErrorPx, 0.5f);
    }

    [Test]
    public void NoPoseWithoutCornersCalibrationOrValidation()
    {
        var sign = Sign();
        var good = Observe(sign, sign.centerInMap + new Vector3(-1.2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        Assert.AreEqual(IndoorSignPoseStatus.NoCorners, IndoorSignPose.Solve(new IndoorSignObservation { RoomId = "VEC120" }, sign).Status);
        Assert.AreEqual(IndoorSignPoseStatus.NoCalibration, IndoorSignPose.Solve(good, null).Status);
        Assert.AreEqual(IndoorSignPoseStatus.Unvalidated, IndoorSignPose.Solve(good, Sign(false)).Status);
        var unusable = Sign();
        unusable.widthM = 0f;
        Assert.AreEqual(IndoorSignPoseStatus.BadGeometry, IndoorSignPose.Solve(good, unusable).Status);
        good.Fx = 0f;
        Assert.AreEqual(IndoorSignPoseStatus.BadGeometry, IndoorSignPose.Solve(good, sign).Status);
    }

    [Test]
    public void CornersThatDoNotFormTheMeasuredRectangleAreRejected()
    {
        var sign = Sign();
        var obs = Observe(sign, sign.centerInMap + new Vector3(-1.2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        obs.CornersPx[2] += new Vector2(40f, 25f);
        Assert.AreEqual(IndoorSignPoseStatus.HighReprojectionError, IndoorSignPose.Solve(obs, sign).Status);
    }

    [Test]
    public void CalibrationParsesAndFindsByRoomLabel()
    {
        var parsed = IndoorSignCalibration.Parse(
            "{\"signs\":[{\"doorKey\":\"VEC120\",\"widthM\":0.3,\"heightM\":0.15,\"validated\":false}]}");
        Assert.IsNotNull(parsed.Find("VEC120"));
        Assert.IsNotNull(parsed.Find("120"));
        Assert.IsNull(parsed.Find("VEC119"));
        Assert.IsFalse(parsed.Find("VEC120").validated);
        Assert.AreEqual(0, IndoorSignCalibration.Parse("not json").signs.Length);
        Assert.AreEqual(0, IndoorSignCalibration.Parse(null).signs.Length);
    }

    [Test]
    public void ShippedCalibrationHasNoValidatedSigns()
    {
        var shipped = Resources.Load<TextAsset>("IndoorSignCalibration");
        Assert.IsNotNull(shipped);
        foreach (var sign in IndoorSignCalibration.Parse(shipped.text).signs) Assert.IsFalse(sign.validated);
    }

    [Test]
    public void LocalizerNeedsAnOpenAttemptAndAPoseThenMovesTheMapSoTheCameraLandsOnTheSolvedPose()
    {
        var sign = Sign();
        var space = new GameObject("MapSpace").transform;
        try
        {
            var localizer = new SignPoseLocalizer(space, "MSET", new IndoorSignCalibration { signs = new[] { sign } });
            var authenticated = 0;
            var localized = 0;
            localizer.Authenticated += () => authenticated++;
            localizer.Localized += _ => localized++;
            localizer.Authenticate();
            Assert.AreEqual(1, authenticated, "no network round trip");

            var camera = sign.centerInMap + new Vector3(-1.2f, 0f, 0.2f);
            var obs = Observe(sign, camera, Quaternion.Euler(0f, 100f, 0f));
            Assert.AreEqual(IndoorSignPoseStatus.Ok, localizer.Observe(obs));
            Assert.AreEqual(0, localized, "no localization attempt is open yet");

            localizer.Localize();
            Assert.AreEqual(IndoorSignPoseStatus.NoCorners, localizer.Observe(new IndoorSignObservation { RoomId = "VEC120" }));
            Assert.AreEqual(0, localized);
            localizer.Observe(obs);
            Assert.AreEqual(1, localized);
            Assert.Less(Vector3.Distance(obs.CameraWorldPosition, space.TransformPoint(camera)), 0.01f);
            var forwardInMap = space.InverseTransformDirection(obs.CameraWorldRotation * Vector3.forward);
            Assert.Less(Vector3.Angle(forwardInMap, Quaternion.Euler(0f, 100f, 0f) * Vector3.forward), 0.5f);
            localizer.Observe(obs);
            Assert.AreEqual(1, localized, "one attempt, one pose");
        }
        finally { Object.DestroyImmediate(space.gameObject); }
    }
}
