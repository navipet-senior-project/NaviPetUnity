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

    // What the phone's OpenCV solver (tools/sign-pnp) returns for the poses above: camera in the sign frame, yaw and
    // pitch relative to the sign, the corners it was given, its solution count and output. Printed by
    // tools/sign-pnp/ippe-fixtures.py; the first three have exact corners, the last two whole-pixel ones.
    static readonly (Vector3 Camera, float Yaw, float Pitch, float[] Corners, int Count, double[] Solutions)[] Ippe =
    {
        (new Vector3(0f, 0.1f, -1.2f), 0f, 0f,
            new[] { 772.5f, 571.25f, 1147.5f, 571.25f, 1147.5f, 758.75f, 772.5f, 758.75f }, 2, new[]
            { 1, -3.7320422e-17, -3.19510016e-16, 1.60787589e-17, -0.986206897, 0.165517241, 0, 0.0992241379, 1.2, 0.999608537, -0.0256213086, 0.0112392614, 0.0256213086, 0.676918918, -0.735611533, 0, 0.1, 1.2, 1, -1.7515646e-16, -1.25283986e-16, -1.7515646e-16, -1, -6.09478426e-15, 8.4013986e-18, 0.1, 1.2, 1, -9.56103782e-17, -1.3398312e-15, -9.56103782e-17, -1, -8.57252759e-16, -3.69345461e-17, 0.1, 1.2 }),
        (new Vector3(-0.3f, -0.1f, -1.2f), 15f, -5f,
            new[] { 754.361f, 453.215f, 1104.73f, 466.328f, 1106.22f, 641.66f, 752.112f, 639.747f }, 2, new[]
            { 0.965927195, 0.022556319, 0.257829142, 2.55931624e-06, -0.996195802, 0.0871431292, -0.0208052709, 0.00817091362, 1.24076698, 0.974032972, 0.0193729317, -0.225575838, 0.00270412132, -0.997256788, -0.0739701755, -0.0292628122, 0.00869878918, 1.23793955, 0.96592728, 0.0225564012, 0.257828817, 2.26697825e-06, -0.996195669, 0.0871446401, -0.0208052774, 0.00817090793, 1.24076702, 0.96592728, 0.0225564012, 0.257828817, 2.26697966e-06, -0.996195669, 0.0871446401, -0.0208052774, 0.00817090793, 1.24076702 }),
        (new Vector3(0.4f, 0.3f, -1.2f), -20f, 8f,
            new[] { 840.375f, 584.625f, 1173.57f, 605.681f, 1170.01f, 782.335f, 842.218f, 748.358f }, 2, new[]
            { 0.939691654, 0.0476001773, -0.338694284, 3.02892697e-07, -0.990268265, -0.139171703, 0.0345473154, 0.12110417, 1.29388467, 0.956045602, 0.104928124, 0.273800832, 0.0122655867, -0.947272926, 0.320193002, 0.0445339017, 0.119556925, 1.28922788, 0.93969197, 0.0476003257, -0.338693388, 2.34479523e-07, -0.990268128, -0.139172676, 0.0345473371, 0.121104197, 1.29388492, 0.97654779, 0.0932508136, 0.194058495, 0.03732589, -0.961015472, 0.273963576, 0.0442239041, 0.12245435, 1.31944828 }),
        (new Vector3(0f, 0.1f, -1.2f), 0f, 0f,
            new[] { 772f, 571f, 1148f, 571f, 1148f, 759f, 772f, 759f }, 2, new[]
            { 1, -9.17662744e-18, -7.85635377e-17, 3.95356676e-18, -0.986206897, 0.165517241, 0, 0.0989561115, 1.19680851, 0.999992071, -0.00387695339, 0.000909047438, 0.00387695339, 0.895765933, -0.444509126, 0, 0.0997340426, 1.19680851, 1, -1.36184569e-16, -1.07796309e-15, -1.36184569e-16, -1, -5.65069505e-15, 3.69455841e-18, 0.0997340426, 1.19680851, 1, -3.39798581e-16, 1.256167e-14, -3.39798581e-16, -1, 1.69063156e-14, 2.51184343e-16, 0.0997340426, 1.19680851 }),
        (new Vector3(0f, 0.1f, -4f), 10f, 0f,
            new[] { 637f, 550f, 753f, 549f, 753f, 606f, 637f, 607f }, 2, new[]
            { 0.992285124, -0.00560527184, 0.123849961, -0.0191290327, -0.993936645, 0.108277544, -0.694334602, 0.0995074796, 3.93426188, 0.974617396, -0.00307178626, 0.22385597, 0.010483042, -0.998182904, -0.0593379733, -0.693754567, 0.0997413314, 3.93426188, 0.994321825, -0.00606291249, 0.106241941, -0.0154357207, -0.996034136, 0.0876227044, -0.69407027, 0.0994845949, 3.932184, 0.994321825, -0.00606291229, 0.106241937, -0.0154357176, -0.996034138, 0.0876226806, -0.694070271, 0.099484595, 3.932184 }),
    };

    static IndoorSignObservation WithCorners(float[] px)
    {
        var corners = new Vector2[4];
        for (var i = 0; i < 4; i++) corners[i] = new Vector2(px[2 * i], px[2 * i + 1]);
        return new IndoorSignObservation { RoomId = "VEC120", CornersPx = corners, Fx = Fx, Fy = Fy, Cx = Cx, Cy = Cy, DeviceModel = Device };
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TheNativeSolverOutputGivesTheCameraPose(int fixture)
    {
        var sign = Sign();
        var (camera, yaw, pitch, corners, count, solutions) = Ippe[fixture];
        var toMap = Quaternion.Euler(0f, sign.intoWallHeadingDeg, 0f);
        var result = IndoorSignPose.FromIppe(WithCorners(corners), sign, solutions, count);
        Assert.AreEqual(IndoorSignPoseStatus.Ok, result.Status);
        Assert.Less(Vector3.Distance(sign.centerInMap + toMap * camera, result.CameraPositionInMap), 0.01f);
        Assert.Less(Quaternion.Angle(toMap * Quaternion.Euler(pitch, yaw, 0f), result.CameraRotationInMap), 0.5f);
    }

    [Test]
    public void TheNativeSolverIsAmbiguousWhenItsTwoSolutionsBothFitButDisagree()
    {
        var sign = Sign();
        var (_, _, _, corners, count, solutions) = Ippe[4];
        // Refinement pulls both solutions to one pose here; only the raw pair shows the flip.
        Assert.AreEqual(IndoorSignPoseStatus.Ambiguous, IndoorSignPose.FromIppe(WithCorners(corners), sign, solutions, count).Status);
        Assert.AreEqual(IndoorSignPoseStatus.BadGeometry, IndoorSignPose.FromIppe(WithCorners(corners), sign, new double[36], 0).Status);
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
