using System.Collections.Generic;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorSignPoseTests
{
    const float Fx = 1500f, Fy = 1500f, Cx = 960f, Cy = 540f;
    const string Device = "TestPhone1,1";

    internal static IndoorSignCalibrationEntry Sign(bool validated = true, string device = Device) => new IndoorSignCalibrationEntry
    {
        doorKey = "VEC120", widthM = 0.30f, heightM = 0.15f, centerInMap = new Vector3(2f, 1.5f, 8f),
        intoWallHeadingDeg = 90f, validated = validated, validatedDevice = device
    };

    // Projects the sign corners the way a camera at (position, rotation) in map space would see them.
    internal static IndoorSignObservation Observe(IndoorSignCalibrationEntry sign, Vector3 position, Quaternion rotation,
        string device = Device)
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
            RoomId = "VEC120", CornersPx = corners, Fx = Fx, Fy = Fy, Cx = Cx, Cy = Cy, DeviceModel = device,
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
    public void APoseIsTrustedOnlyOnTheDeviceItWasValidatedOn()
    {
        var sign = Sign();
        var obs = Observe(sign, sign.centerInMap + new Vector3(-1.2f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        Assert.AreEqual(IndoorSignPoseStatus.Ok, IndoorSignPose.Solve(obs, sign).Status);
        obs.DeviceModel = "OtherPhone2,1";
        Assert.AreEqual(IndoorSignPoseStatus.OtherDevice, IndoorSignPose.Solve(obs, sign).Status);
        obs.DeviceModel = Device;
        sign.validatedDevice = null;
        Assert.AreEqual(IndoorSignPoseStatus.OtherDevice, IndoorSignPose.Solve(obs, sign).Status);

        var calibration = new IndoorSignCalibration { signs = new[] { Sign(), Sign(false) } };
        Assert.IsTrue(calibration.CanPose(Device));
        Assert.IsFalse(calibration.CanPose("OtherPhone2,1"));
        Assert.IsFalse(calibration.CanPose(null));
        Assert.IsFalse(new IndoorSignCalibration { signs = new[] { Sign(false) } }.CanPose(Device));
    }

    [Test]
    public void ACameraBeyondReadingDistanceIsImplausible()
    {
        var sign = Sign();
        var obs = Observe(sign, sign.centerInMap + new Vector3(-5.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        Assert.AreEqual(IndoorSignPoseStatus.Implausible, IndoorSignPose.Solve(obs, sign).Status);
    }

    [Test]
    public void AFarObliqueSightingThatAlsoFitsItsMirrorIsAmbiguous()
    {
        var sign = Sign();
        var obs = Observe(sign, sign.centerInMap + new Vector3(-4f, 0.1f, 0f), Quaternion.Euler(0f, 100f, 0f));
        // Whole-pixel corners, as OCR reports them: at this range the flipped pose fits them as well.
        for (var i = 0; i < 4; i++)
            obs.CornersPx[i] = new Vector2(Mathf.Round(obs.CornersPx[i].x), Mathf.Round(obs.CornersPx[i].y));
        Assert.AreEqual(IndoorSignPoseStatus.Ambiguous, IndoorSignPose.Solve(obs, sign).Status);
    }

    static List<(IndoorSignObservation, Vector3, float)> Samples(IndoorSignCalibrationEntry sign)
    {
        var samples = new List<(IndoorSignObservation, Vector3, float)>();
        foreach (var (offset, heading) in new[] { (new Vector3(-1.2f, 0f, 0f), 90f), (new Vector3(-1.5f, -0.1f, 0.3f), 80f),
                     (new Vector3(-1f, 0.1f, -0.2f), 100f) })
        {
            var at = sign.centerInMap + offset;
            samples.Add((Observe(sign, at, Quaternion.Euler(0f, heading, 0f)), at, heading));
        }
        return samples;
    }

    [Test]
    public void ValidationNeedsEnoughSamplesFromOneDeviceThatLandOnTheSurveyedPoses()
    {
        var sign = Sign(false);
        Assert.IsTrue(IndoorSignPose.Validate(sign, Samples(sign)), "validation runs before the entry is validated");
        Assert.IsFalse(IndoorSignPose.Validate(sign, Samples(sign).GetRange(0, 2)));
        var moved = Samples(sign);
        moved[1] = (moved[1].Item1, moved[1].Item2 + new Vector3(0.5f, 0f, 0f), moved[1].Item3);
        Assert.IsFalse(IndoorSignPose.Validate(sign, moved));
        var turned = Samples(sign);
        turned[2] = (turned[2].Item1, turned[2].Item2, turned[2].Item3 + 10f);
        Assert.IsFalse(IndoorSignPose.Validate(sign, turned));
        var mixed = Samples(sign);
        mixed[0].Item1.DeviceModel = "OtherPhone2,1";
        Assert.IsFalse(IndoorSignPose.Validate(sign, mixed));
    }

    [Test]
    public void SurveyImportValidatesOnlyWhatItsCapturesProve()
    {
        var sign = Sign(false);
        var samples = Samples(sign);
        samples[0].Item1.DeviceModel = samples[1].Item1.DeviceModel = samples[2].Item1.DeviceModel = "samsung SM-G991B";
        var line = IndoorSignSurvey.CaptureLine(samples[0].Item1);
        StringAssert.StartsWith("[IndoorAr] sign capture room=VEC120 fx=1500 fy=1500 cx=960 cy=540 corners=", line);
        var parsed = IndoorSignSurvey.ParseCapture(line);
        Assert.AreEqual("samsung SM-G991B", parsed.DeviceModel);
        Assert.Less(Vector2.Distance(samples[0].Item1.CornersPx[2], parsed.CornersPx[2]), 0.06f);
        Assert.IsNull(IndoorSignSurvey.ParseCapture("[IndoorAr] sign capture room=VEC120 fx=1500"));

        var survey = new IndoorSignSurvey { signs = new[] { sign, Sign() } };
        survey.signs[1].doorKey = "VEC121"; // claims validation with no samples
        survey.samples = samples.ConvertAll(s => new IndoorSignSurveySample
            { capture = IndoorSignSurvey.CaptureLine(s.Item1), cameraInMap = s.Item2, headingDeg = s.Item3 }).ToArray();
        var report = new List<string>();
        var calibration = IndoorSignSurvey.ToCalibration(JsonUtility.ToJson(survey), report);
        Assert.IsTrue(calibration.Find("VEC120").validated);
        Assert.AreEqual("samsung SM-G991B", calibration.Find("VEC120").validatedDevice);
        Assert.IsFalse(calibration.Find("VEC121").validated);
        Assert.IsNull(calibration.Find("VEC121").validatedDevice);
        Assert.AreEqual(new[] { "VEC120: samples=3 validated=True device=samsung SM-G991B", "VEC121: samples=0 validated=False" }, report);

        survey.samples[1].cameraInMap += new Vector3(0f, 0f, 1f);
        Assert.IsFalse(IndoorSignSurvey.ToCalibration(JsonUtility.ToJson(survey), null).Find("VEC120").validated);
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

            localizer.Localize();
            localizer.CandidatePoseValidator = _ => false;
            Assert.AreEqual(IndoorSignPoseStatus.Implausible, localizer.Observe(obs), "e.g. on another floor");
            Assert.AreEqual(1, localized);
        }
        finally { Object.DestroyImmediate(space.gameObject); }
    }
}
