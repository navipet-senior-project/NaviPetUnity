using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

[assembly: InternalsVisibleTo("IndoorAr.Tests.EditMode")]

namespace IndoorAr
{
    public enum IndoorSignPoseStatus
    {
        Ok,
        NoCorners,
        NoCalibration,
        Unvalidated,
        BadGeometry,
        HighReprojectionError,
        /// <summary>The camera would be behind the sign, outside reading distance, or on a floor the sign is not on.</summary>
        Implausible,
        /// <summary>A second, mirrored pose fits the corners as well but puts the camera elsewhere (IPPE flip).</summary>
        Ambiguous,
        /// <summary>The entry was validated on another device model; its lens distortion is not modelled.</summary>
        OtherDevice
    }

    /// <summary>
    /// One measured sign. WidthM x HeightM is the physical rectangle the four OCR image corners outline (the text
    /// block, not necessarily the plate). Validated stays false until IndoorSignPose.Validate passes on surveyed
    /// camera poses (see OCR-POC-prerequisites.md); an unvalidated entry never produces a pose.
    /// </summary>
    [Serializable]
    public sealed class IndoorSignCalibrationEntry
    {
        public string doorKey;
        public float widthM;
        public float heightM;
        /// <summary>Centre of that rectangle in the MapSet frame.</summary>
        public Vector3 centerInMap;
        /// <summary>Unity yaw (0 = +Z, clockwise from above) of the direction pointing from the hallway into the sign.</summary>
        public float intoWallHeadingDeg;
        public bool validated;
        /// <summary>SystemInfo.deviceModel the validation samples came from. Poses are trusted on that model only.</summary>
        public string validatedDevice;
    }

    [Serializable]
    public sealed class IndoorSignCalibration
    {
        public IndoorSignCalibrationEntry[] signs = new IndoorSignCalibrationEntry[0];

        public static IndoorSignCalibration Parse(string json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json) ? new IndoorSignCalibration()
                    : JsonUtility.FromJson<IndoorSignCalibration>(json) ?? new IndoorSignCalibration();
            }
            catch (ArgumentException)
            {
                return new IndoorSignCalibration();
            }
        }

        public IndoorSignCalibrationEntry Find(string roomId)
        {
            if (signs == null || string.IsNullOrEmpty(roomId)) return null;
            var wanted = LiveDoorHud.RoomLabel(roomId);
            foreach (var sign in signs)
                if (sign != null && LiveDoorHud.RoomLabel(sign.doorKey) == wanted) return sign;
            return null;
        }

        /// <summary>Whether any sign can pose the camera on this device model.</summary>
        public bool CanPose(string deviceModel)
        {
            if (signs == null || string.IsNullOrEmpty(deviceModel)) return false;
            foreach (var sign in signs)
                if (sign != null && sign.validated && sign.validatedDevice == deviceModel) return true;
            return false;
        }
    }

    /// <summary>One OCR sighting of a sign, with everything the pose needs captured at the same frame.</summary>
    public sealed class IndoorSignObservation
    {
        public string RoomId;
        /// <summary>Top-left, top-right, bottom-right, bottom-left in pixels of the upright image, origin top-left, y down.</summary>
        public Vector2[] CornersPx;
        public float Fx, Fy, Cx, Cy;
        /// <summary>SystemInfo.deviceModel of the phone that took the frame.</summary>
        public string DeviceModel;
        public Vector3 CameraWorldPosition;
        public Quaternion CameraWorldRotation;
    }

    public readonly struct IndoorSignPoseResult
    {
        public readonly IndoorSignPoseStatus Status;
        public readonly Vector3 CameraPositionInMap;
        public readonly Quaternion CameraRotationInMap;
        public readonly float ReprojectionErrorPx;

        public IndoorSignPoseResult(IndoorSignPoseStatus status, Vector3 position = default,
            Quaternion rotation = default, float errorPx = 0f)
        {
            Status = status;
            CameraPositionInMap = position;
            CameraRotationInMap = rotation;
            ReprojectionErrorPx = errorPx;
        }
    }

    /// <summary>
    /// Camera pose in the MapSet frame from the four image corners of one measured sign. Everything is in
    /// Unity's left-handed convention (camera x right, y up, z forward). The sign's local frame is x right,
    /// y up, z into the wall, so its transform to the map is a pure yaw.
    /// On the phone OpenCV's solvePnP IPPE solves it (tools/sign-pnp, FromIppe). The Editor and EditMode tests have no
    /// OpenCV build, so they use a planar homography and its mirrored IPPE candidate (plane normal reflected about the
    /// line of sight). Either way the sighting is rejected when another candidate fits the corners but moves the camera.
    /// shortcut: no lens distortion model (AR Foundation gives none), and survey validation runs in the Editor, so it
    /// checks the managed solve, not OpenCV's; build OpenCV for the Editor if real captures show the two disagree.
    /// </summary>
    public static class IndoorSignPose
    {
        public const float MaxReprojectionErrorPx = 3f;
        // shortcut: untuned limits; set them from the device validation runs.
        public const float MinDistanceM = 0.3f;
        public const float MaxDistanceM = 5f;
        public const float AmbiguityYawDeg = 5f;
        public const float AmbiguityM = 0.25f;
        public const int MinValidationSamples = 3;
        public const float MaxValidationErrorM = 0.3f;
        public const float MaxValidationYawDeg = 5f;

        /// <summary>
        /// Moves the MapSet origin so the AR camera, at its world pose, sits at the solved map pose. Yaw only:
        /// the AR world is gravity aligned, so the sign's pitch and roll estimate must not tilt the map.
        /// </summary>
        public static void Place(Transform mapSpace, Vector3 cameraWorldPosition, Quaternion cameraWorldRotation,
            Vector3 cameraPositionInMap, Quaternion cameraRotationInMap)
        {
            var yaw = Yaw(cameraWorldRotation) - Yaw(cameraRotationInMap);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            mapSpace.SetPositionAndRotation(cameraWorldPosition - rotation * cameraPositionInMap, rotation);
        }

        /// <summary>A pose only from a sign validated on this device model.</summary>
        public static IndoorSignPoseResult Solve(IndoorSignObservation obs, IndoorSignCalibrationEntry sign)
        {
            if (obs == null || obs.CornersPx == null || obs.CornersPx.Length != 4)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.NoCorners);
            if (sign == null) return new IndoorSignPoseResult(IndoorSignPoseStatus.NoCalibration);
            if (!sign.validated) return new IndoorSignPoseResult(IndoorSignPoseStatus.Unvalidated);
            if (string.IsNullOrEmpty(sign.validatedDevice) || sign.validatedDevice != obs.DeviceModel)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.OtherDevice);
            return Estimate(obs, sign);
        }

        /// <summary>
        /// True when every sample of this sign solves to its surveyed camera pose, from one device, with at least
        /// MinValidationSamples of them. The survey is measured on site, never taken from a MultiSet estimate.
        /// </summary>
        public static bool Validate(IndoorSignCalibrationEntry sign,
            IReadOnlyList<(IndoorSignObservation Frame, Vector3 CameraInMap, float HeadingDeg)> samples)
        {
            if (sign == null || samples == null || samples.Count < MinValidationSamples) return false;
            var device = samples[0].Frame?.DeviceModel;
            if (string.IsNullOrEmpty(device)) return false;
            foreach (var (frame, cameraInMap, headingDeg) in samples)
            {
                if (frame == null || frame.DeviceModel != device) return false;
                var pose = Estimate(frame, sign);
                if (pose.Status != IndoorSignPoseStatus.Ok
                    || Vector3.Distance(pose.CameraPositionInMap, cameraInMap) > MaxValidationErrorM
                    || Mathf.Abs(Mathf.DeltaAngle(Yaw(pose.CameraRotationInMap), headingDeg)) > MaxValidationYawDeg)
                    return false;
            }
            return true;
        }

        /// <summary>Geometry only, with no validation gate: Solve and Validate both use it.</summary>
        static IndoorSignPoseResult Estimate(IndoorSignObservation obs, IndoorSignCalibrationEntry sign)
        {
            if (obs.CornersPx == null || obs.CornersPx.Length != 4) return new IndoorSignPoseResult(IndoorSignPoseStatus.NoCorners);
            if (!(sign.widthM > 0f) || !(sign.heightM > 0f) || !(obs.Fx > 0f) || !(obs.Fy > 0f)
                || !AllFinite(obs.CornersPx))
                return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
            return SolveNative(obs, sign);
#else
            return SolveManaged(obs, sign, Plane(sign));
#endif
        }

        static Vector2[] Plane(IndoorSignCalibrationEntry sign)
        {
            float hw = sign.widthM / 2f, hh = sign.heightM / 2f;
            return new[] { new Vector2(-hw, hh), new Vector2(hw, hh), new Vector2(hw, -hh), new Vector2(-hw, -hh) };
        }

#if !UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
#if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")]
#else
        [System.Runtime.InteropServices.DllImport("indoorsignpnp")]
#endif
        static extern int IndoorSignSolveIppe(double[] plane8, double[] image8, double[] k4, double[] out36);

        static IndoorSignPoseResult SolveNative(IndoorSignObservation obs, IndoorSignCalibrationEntry sign)
        {
            var plane = Plane(sign);
            var planeXy = new double[8];
            var image = new double[8];
            for (var i = 0; i < 4; i++)
            {
                planeXy[2 * i] = plane[i].x;
                planeXy[2 * i + 1] = plane[i].y;
                image[2 * i] = obs.CornersPx[i].x;
                image[2 * i + 1] = obs.CornersPx[i].y;
            }
            var solutions = new double[36];
            var count = IndoorSignSolveIppe(planeXy, image, new double[] { obs.Fx, obs.Fy, obs.Cx, obs.Cy }, solutions);
            return FromIppe(obs, sign, solutions, count);
        }
#endif

        /// <summary>
        /// The pose from tools/sign-pnp's output: OpenCV's IPPE solution s at 9 * s and its refinement at 9 * (2 + s),
        /// each as rotation columns 1 and 2 then the translation, in OpenCV's y-down camera frame. The better-fitting
        /// refinement is the pose and the other three are what the ambiguity check weighs it against.
        /// </summary>
        internal static IndoorSignPoseResult FromIppe(IndoorSignObservation obs, IndoorSignCalibrationEntry sign,
            double[] solutions, int count)
        {
            if (count < 1 || count > 2 || solutions == null || solutions.Length < 36)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            var plane = Plane(sign);
            var candidates = new List<(Vector3 R1, Vector3 R2, Vector3 T)>();
            foreach (var slot in new[] { 2, 3, 0, 1 })
            {
                if (slot % 2 >= count) continue;
                var i = 9 * slot;
                // Negating y turns OpenCV's camera frame into Unity's.
                candidates.Add((new Vector3((float)solutions[i], -(float)solutions[i + 1], (float)solutions[i + 2]),
                    new Vector3((float)solutions[i + 3], -(float)solutions[i + 4], (float)solutions[i + 5]),
                    new Vector3((float)solutions[i + 6], -(float)solutions[i + 7], (float)solutions[i + 8])));
            }
            var best = count > 1 && Fit(obs, plane, candidates[1]) < Fit(obs, plane, candidates[0]) ? 1 : 0;
            var pose = candidates[best];
            candidates.RemoveAt(best);
            return Gate(obs, sign, plane, pose, candidates);
        }

        static float Fit(IndoorSignObservation obs, Vector2[] plane, (Vector3 R1, Vector3 R2, Vector3 T) c)
        {
            var error = Reprojection(obs, plane, c.R1, c.R2, Vector3.Cross(c.R1, c.R2), c.T);
            return float.IsNaN(error) ? float.PositiveInfinity : error;
        }

        static IndoorSignPoseResult SolveManaged(IndoorSignObservation obs, IndoorSignCalibrationEntry sign, Vector2[] plane)
        {
            var a = new double[8, 9];
            for (var i = 0; i < 4; i++)
            {
                double x = plane[i].x, y = plane[i].y;
                double u = (obs.CornersPx[i].x - obs.Cx) / obs.Fx;
                double v = -(obs.CornersPx[i].y - obs.Cy) / obs.Fy;
                Fill(a, 2 * i, x, y, u, 1, 0);
                Fill(a, 2 * i + 1, x, y, v, 0, 1);
            }
            if (!SolveLinear(a, 8, out var h)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);

            var h1 = new Vector3((float)h[0], (float)h[3], (float)h[6]);
            var h2 = new Vector3((float)h[1], (float)h[4], (float)h[7]);
            var h3 = new Vector3((float)h[2], (float)h[5], 1f);
            var scale = (h1.magnitude + h2.magnitude) / 2f;
            if (!(scale > 1e-6f)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            var r1 = h1.normalized;
            var r2 = h2 - Vector3.Dot(h2, r1) * r1;
            if (!(r2.sqrMagnitude > 1e-12f)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            r2.Normalize();
            var t = h3 / scale;
            var mirror = Mirror(obs, plane, r1, r2, t);
            return Gate(obs, sign, plane, (r1, r2, t),
                mirror.HasValue ? new[] { mirror.Value } : Array.Empty<(Vector3, Vector3, Vector3)>());
        }

        // A candidate pose must reproject onto the corners and put the camera where a reader could stand, and no other
        // candidate that also fits may move the camera elsewhere.
        static IndoorSignPoseResult Gate(IndoorSignObservation obs, IndoorSignCalibrationEntry sign, Vector2[] plane,
            (Vector3 R1, Vector3 R2, Vector3 T) pose, IEnumerable<(Vector3 R1, Vector3 R2, Vector3 T)> others)
        {
            var (r1, r2, t) = pose;
            var r3 = Vector3.Cross(r1, r2);
            var error = Reprojection(obs, plane, r1, r2, r3, t);
            if (float.IsNaN(error)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            if (error > MaxReprojectionErrorPx)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.HighReprojectionError, errorPx: error);

            // Camera pose in the sign frame, then the sign's yaw and centre carry it into the map.
            var cameraInSign = CameraInSign(r1, r2, r3, t);
            var distance = cameraInSign.magnitude;
            // Sign z points into the wall, so a camera that can read it has negative z.
            if (!(cameraInSign.z < 0f) || distance < MinDistanceM || distance > MaxDistanceM)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.Implausible, errorPx: error);
            foreach (var other in others)
                if (Disagrees(obs, plane, r1, r3, cameraInSign, other))
                    return new IndoorSignPoseResult(IndoorSignPoseStatus.Ambiguous, errorPx: error);
            var forward = new Vector3(r1.z, r2.z, r3.z);
            var up = new Vector3(r1.y, r2.y, r3.y);
            var signToMap = Quaternion.Euler(0f, sign.intoWallHeadingDeg, 0f);
            return new IndoorSignPoseResult(IndoorSignPoseStatus.Ok,
                sign.centerInMap + signToMap * cameraInSign,
                signToMap * Quaternion.LookRotation(forward, up), error);
        }

        static Vector3 CameraInSign(Vector3 r1, Vector3 r2, Vector3 r3, Vector3 t) =>
            -new Vector3(Vector3.Dot(r1, t), Vector3.Dot(r2, t), Vector3.Dot(r3, t));

        static float Yaw(Quaternion q)
        {
            var forward = q * Vector3.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        // The other IPPE candidate for the managed solve: rotation reflected about the line of sight to the sign,
        // translation refitted by least squares. Null when that fit is degenerate.
        static (Vector3 R1, Vector3 R2, Vector3 T)? Mirror(IndoorSignObservation obs, Vector2[] plane, Vector3 r1,
            Vector3 r2, Vector3 t)
        {
            var d = t.normalized;
            var m1 = r1 - 2f * Vector3.Dot(r1, d) * d;
            var m2 = r2 - 2f * Vector3.Dot(r2, d) * d;
            var normal = new double[3, 4];
            for (var i = 0; i < 4; i++)
            {
                double u = (obs.CornersPx[i].x - obs.Cx) / obs.Fx, v = -(obs.CornersPx[i].y - obs.Cy) / obs.Fy;
                var p = m1 * plane[i].x + m2 * plane[i].y;
                // Pinhole rows: t.x - u t.z = u p.z - p.x, and the same for y with v.
                AddNormalRow(normal, 1, 0, -u, u * p.z - p.x);
                AddNormalRow(normal, 0, 1, -v, v * p.z - p.y);
            }
            if (!SolveLinear(normal, 3, out var s)) return null;
            return (m1, m2, new Vector3((float)s[0], (float)s[1], (float)s[2]));
        }

        // Another candidate matters only if it also reprojects well and moves the camera's yaw or plan position.
        static bool Disagrees(IndoorSignObservation obs, Vector2[] plane, Vector3 r1, Vector3 r3, Vector3 cameraInSign,
            (Vector3 R1, Vector3 R2, Vector3 T) other)
        {
            var (m1, m2, mt) = other;
            var m3 = Vector3.Cross(m1, m2);
            var otherError = Reprojection(obs, plane, m1, m2, m3, mt);
            if (float.IsNaN(otherError) || otherError > MaxReprojectionErrorPx) return false;
            var camera = CameraInSign(m1, m2, m3, mt);
            var yawGap = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(r1.z, r3.z) * Mathf.Rad2Deg, Mathf.Atan2(m1.z, m3.z) * Mathf.Rad2Deg));
            var gapM = new Vector2(camera.x - cameraInSign.x, camera.z - cameraInSign.z).magnitude;
            return yawGap > AmbiguityYawDeg || gapM > AmbiguityM;
        }

        static void AddNormalRow(double[,] normal, double a, double b, double c, double rhs)
        {
            var row = new[] { a, b, c };
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++) normal[i, j] += row[i] * row[j];
                normal[i, 3] += row[i] * rhs;
            }
        }

        static bool AllFinite(Vector2[] points)
        {
            foreach (var p in points)
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)) return false;
            return true;
        }

        // Row for h11 h12 h13 h21 h22 h23 h31 h32 | rhs, with h33 fixed at 1.
        static void Fill(double[,] a, int row, double x, double y, double image, double uSelect, double vSelect)
        {
            a[row, 0] = x * uSelect; a[row, 1] = y * uSelect; a[row, 2] = uSelect;
            a[row, 3] = x * vSelect; a[row, 4] = y * vSelect; a[row, 5] = vSelect;
            a[row, 6] = -image * x; a[row, 7] = -image * y;
            a[row, 8] = image;
        }

        // Gaussian elimination on an n x (n+1) augmented matrix.
        static bool SolveLinear(double[,] m, int n, out double[] x)
        {
            x = new double[n];
            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                for (var row = col + 1; row < n; row++)
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
                if (Math.Abs(m[pivot, col]) < 1e-12) return false;
                if (pivot != col)
                    for (var k = 0; k <= n; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                for (var row = col + 1; row < n; row++)
                {
                    var f = m[row, col] / m[col, col];
                    for (var k = col; k <= n; k++) m[row, k] -= f * m[col, k];
                }
            }
            for (var row = n - 1; row >= 0; row--)
            {
                var sum = m[row, n];
                for (var k = row + 1; k < n; k++) sum -= m[row, k] * x[k];
                x[row] = sum / m[row, row];
            }
            return true;
        }

        static float Reprojection(IndoorSignObservation obs, Vector2[] plane, Vector3 r1, Vector3 r2, Vector3 r3, Vector3 t)
        {
            double sum = 0;
            for (var i = 0; i < 4; i++)
            {
                var p = r1 * plane[i].x + r2 * plane[i].y + t;
                if (!(p.z > 1e-6f)) return float.NaN;
                var px = obs.Cx + obs.Fx * p.x / p.z;
                var py = obs.Cy - obs.Fy * p.y / p.z;
                sum += Math.Pow(px - obs.CornersPx[i].x, 2) + Math.Pow(py - obs.CornersPx[i].y, 2);
            }
            return (float)Math.Sqrt(sum / 4);
        }
    }

    [Serializable]
    public sealed class IndoorSignSurveySample
    {
        /// <summary>Camera position measured on site in the MapSet frame, never a MultiSet estimate.</summary>
        public Vector3 cameraInMap;
        /// <summary>Camera heading measured on site, Unity yaw.</summary>
        public float headingDeg;
        /// <summary>The "sign capture" log line the development build printed at that spot.</summary>
        public string capture;
    }

    /// <summary>Surveyed sign geometry and the sightings that check it, from one walk with one device.</summary>
    [Serializable]
    public sealed class IndoorSignSurvey
    {
        public IndoorSignCalibrationEntry[] signs = new IndoorSignCalibrationEntry[0];
        public IndoorSignSurveySample[] samples = new IndoorSignSurveySample[0];

        const string CaptureTag = "[IndoorAr] sign capture ";

        /// <summary>One sighting as a log line: image intrinsics and corner pixels only. Device goes last: it may hold spaces.</summary>
        public static string CaptureLine(IndoorSignObservation o)
        {
            var corners = new string[8];
            for (var i = 0; i < 4; i++)
            {
                corners[2 * i] = o.CornersPx[i].x.ToString("0.#", CultureInfo.InvariantCulture);
                corners[2 * i + 1] = o.CornersPx[i].y.ToString("0.#", CultureInfo.InvariantCulture);
            }
            return CaptureTag + FormattableString.Invariant(
                $"room={o.RoomId} fx={o.Fx} fy={o.Fy} cx={o.Cx} cy={o.Cy} corners={string.Join(",", corners)} device={o.DeviceModel}");
        }

        /// <summary>The sighting a capture line describes, or null when the line is not one.</summary>
        public static IndoorSignObservation ParseCapture(string line)
        {
            var m = Regex.Match(line ?? "", @"room=(\S+) fx=(\S+) fy=(\S+) cx=(\S+) cy=(\S+) corners=(\S+) device=(.+)$");
            if (!m.Success) return null;
            var c = m.Groups[6].Value.Split(',');
            var f = new float[12];
            for (var i = 0; i < 12; i++)
                if (!float.TryParse(i < 4 ? m.Groups[i + 2].Value : i - 4 < c.Length ? c[i - 4] : "",
                        NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return null;
            if (c.Length != 8) return null;
            return new IndoorSignObservation
            {
                RoomId = m.Groups[1].Value, Fx = f[0], Fy = f[1], Cx = f[2], Cy = f[3], DeviceModel = m.Groups[7].Value.Trim(),
                CornersPx = new[] { new Vector2(f[4], f[5]), new Vector2(f[6], f[7]), new Vector2(f[8], f[9]), new Vector2(f[10], f[11]) }
            };
        }

        /// <summary>
        /// Calibration from a survey: every sign keeps its surveyed geometry, and is marked validated for the
        /// survey's device only when its samples pass IndoorSignPose.Validate. Whatever the input claims about
        /// validation is ignored.
        /// </summary>
        public static IndoorSignCalibration ToCalibration(string surveyJson, List<string> report)
        {
            var survey = JsonUtility.FromJson<IndoorSignSurvey>(surveyJson) ?? new IndoorSignSurvey();
            var calibration = new IndoorSignCalibration { signs = survey.signs ?? new IndoorSignCalibrationEntry[0] };
            foreach (var sign in calibration.signs)
            {
                if (sign == null) continue;
                sign.validated = false;
                sign.validatedDevice = null;
                var label = LiveDoorHud.RoomLabel(sign.doorKey);
                var samples = new List<(IndoorSignObservation, Vector3, float)>();
                var unreadable = 0;
                foreach (var sample in survey.samples ?? new IndoorSignSurveySample[0])
                {
                    var frame = ParseCapture(sample?.capture);
                    if (frame == null) unreadable++;
                    else if (LiveDoorHud.RoomLabel(frame.RoomId) == label)
                        samples.Add((frame, sample.cameraInMap, sample.headingDeg));
                }
                if (unreadable == 0 && IndoorSignPose.Validate(sign, samples))
                {
                    sign.validated = true;
                    sign.validatedDevice = samples[0].Item1.DeviceModel;
                }
                report?.Add($"{sign.doorKey}: samples={samples.Count} validated={sign.validated}"
                    + (unreadable > 0 ? $" unreadableCaptures={unreadable}" : "")
                    + (sign.validated ? $" device={sign.validatedDevice}" : ""));
            }
            return calibration;
        }
    }
}
