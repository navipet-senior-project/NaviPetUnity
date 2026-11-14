using System;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorSignPoseStatus
    {
        Ok,
        NoCorners,
        NoCalibration,
        Unvalidated,
        BadGeometry,
        HighReprojectionError
    }

    /// <summary>
    /// One measured sign. WidthM x HeightM is the physical rectangle the four OCR image corners outline (the text
    /// block, not necessarily the plate). Validated stays false until a paired comparison against CameraInMap
    /// exists (see OCR-POC-prerequisites.md); an unvalidated entry never produces a pose.
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
    }

    /// <summary>One OCR sighting of a sign, with everything the pose needs captured at the same frame.</summary>
    public sealed class IndoorSignObservation
    {
        public string RoomId;
        /// <summary>Top-left, top-right, bottom-right, bottom-left in sensor-image pixels, origin top-left, y down.</summary>
        public Vector2[] CornersPx;
        public float Fx, Fy, Cx, Cy;
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
    /// shortcut: planar homography decomposition with no IPPE ambiguity check; use IPPE before trusting
    /// oblique views, and only after the paired validation in OCR-POC-prerequisites.md.
    /// </summary>
    public static class IndoorSignPose
    {
        public const float MaxReprojectionErrorPx = 3f;

        public static IndoorSignPoseResult Solve(IndoorSignObservation obs, IndoorSignCalibrationEntry sign)
        {
            if (obs == null || obs.CornersPx == null || obs.CornersPx.Length != 4)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.NoCorners);
            if (sign == null) return new IndoorSignPoseResult(IndoorSignPoseStatus.NoCalibration);
            if (!sign.validated) return new IndoorSignPoseResult(IndoorSignPoseStatus.Unvalidated);
            if (!(sign.widthM > 0f) || !(sign.heightM > 0f) || !(obs.Fx > 0f) || !(obs.Fy > 0f)
                || !AllFinite(obs.CornersPx))
                return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);

            float hw = sign.widthM / 2f, hh = sign.heightM / 2f;
            var plane = new[] { new Vector2(-hw, hh), new Vector2(hw, hh), new Vector2(hw, -hh), new Vector2(-hw, -hh) };
            var a = new double[8, 9];
            for (var i = 0; i < 4; i++)
            {
                double x = plane[i].x, y = plane[i].y;
                double u = (obs.CornersPx[i].x - obs.Cx) / obs.Fx;
                double v = -(obs.CornersPx[i].y - obs.Cy) / obs.Fy;
                Fill(a, 2 * i, x, y, u, 1, 0);
                Fill(a, 2 * i + 1, x, y, v, 0, 1);
            }
            if (!Solve8(a, out var h)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);

            var h1 = new Vector3((float)h[0], (float)h[3], (float)h[6]);
            var h2 = new Vector3((float)h[1], (float)h[4], (float)h[7]);
            var h3 = new Vector3((float)h[2], (float)h[5], 1f);
            var scale = (h1.magnitude + h2.magnitude) / 2f;
            if (!(scale > 1e-6f)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            var r1 = h1.normalized;
            var r2 = h2 - Vector3.Dot(h2, r1) * r1;
            if (!(r2.sqrMagnitude > 1e-12f)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            r2.Normalize();
            var r3 = Vector3.Cross(r1, r2);
            var t = h3 / scale;

            var error = Reprojection(obs, plane, r1, r2, r3, t);
            if (float.IsNaN(error)) return new IndoorSignPoseResult(IndoorSignPoseStatus.BadGeometry);
            if (error > MaxReprojectionErrorPx)
                return new IndoorSignPoseResult(IndoorSignPoseStatus.HighReprojectionError, errorPx: error);

            // Camera pose in the sign frame, then the sign's yaw and centre carry it into the map.
            var cameraInSign = -new Vector3(Vector3.Dot(r1, t), Vector3.Dot(r2, t), Vector3.Dot(r3, t));
            var forward = new Vector3(r1.z, r2.z, r3.z);
            var up = new Vector3(r1.y, r2.y, r3.y);
            var signToMap = Quaternion.Euler(0f, sign.intoWallHeadingDeg, 0f);
            return new IndoorSignPoseResult(IndoorSignPoseStatus.Ok,
                sign.centerInMap + signToMap * cameraInSign,
                signToMap * Quaternion.LookRotation(forward, up), error);
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

        static bool Solve8(double[,] m, out double[] x)
        {
            x = new double[8];
            for (var col = 0; col < 8; col++)
            {
                var pivot = col;
                for (var row = col + 1; row < 8; row++)
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
                if (Math.Abs(m[pivot, col]) < 1e-12) return false;
                if (pivot != col)
                    for (var k = 0; k < 9; k++) (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                for (var row = col + 1; row < 8; row++)
                {
                    var f = m[row, col] / m[col, col];
                    for (var k = col; k < 9; k++) m[row, k] -= f * m[col, k];
                }
            }
            for (var row = 7; row >= 0; row--)
            {
                var sum = m[row, 8];
                for (var k = row + 1; k < 8; k++) sum -= m[row, k] * x[k];
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
}
