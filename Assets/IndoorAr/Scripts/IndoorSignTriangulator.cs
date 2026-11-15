using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    public enum IndoorSignFixStatus
    {
        Ok,
        FewFrames,
        LowParallax,
        HighResidual,
        BadGeometry
    }

    /// <summary>Where one sign's text box is in the AR world, and how well the frames agree on it.</summary>
    public struct IndoorSignTriangulation
    {
        public IndoorSignFixStatus Status;
        public int Frames;
        /// <summary>Largest angle between two frames' rays to the sign. Small means the camera barely moved sideways.</summary>
        public float ParallaxDeg;
        /// <summary>RMS distance between the solved corners and the rays that saw them.</summary>
        public float ResidualM;
        public Vector3 CenterInWorld;
        public float WidthM, HeightM;
        /// <summary>Unity yaw of the direction pointing from the reader into the wall the text is on.</summary>
        public float IntoWallYawDeg;
        /// <summary>Estimated one-sigma error of that yaw. An estimate from the fit, never checked against a survey.</summary>
        public float YawSigmaDeg;
    }

    /// <summary>
    /// Locates a sign's text box in the AR world from its four image corners in several frames, using the camera
    /// poses AR tracked between them. It needs no measured sign size: the size and the wall direction come out of
    /// the fit. It does need sideways camera motion; frames from one spot cannot give depth.
    /// </summary>
    public sealed class IndoorSignTriangulator
    {
        // shortcut: none of these gates were tuned on a device; tune them from the "sign fix" log lines.
        public const int Window = 12;
        public const int MinFrames = 3;
        public const float MinParallaxDeg = 6f;
        public const float MaxResidualM = .03f;
        /// <summary>A frame closer than this to the last one replaces it, so standing still keeps the older views.</summary>
        public const float MinStepM = .03f;

        readonly Dictionary<string, List<IndoorSignObservation>> _views =
            new Dictionary<string, List<IndoorSignObservation>>();

        public void Clear() => _views.Clear();

        public IndoorSignTriangulation Add(IndoorSignObservation obs)
        {
            if (obs?.RoomId == null || obs.CornersPx == null || obs.CornersPx.Length != 4
                || !(obs.Fx > 0f) || !(obs.Fy > 0f))
                return new IndoorSignTriangulation { Status = IndoorSignFixStatus.BadGeometry };
            if (!_views.TryGetValue(obs.RoomId, out var views))
                _views[obs.RoomId] = views = new List<IndoorSignObservation>();
            if (views.Count > 0
                && Vector3.Distance(views[views.Count - 1].CameraWorldPosition, obs.CameraWorldPosition) < MinStepM)
                views[views.Count - 1] = obs;
            else views.Add(obs);
            if (views.Count > Window) views.RemoveAt(0);
            return Solve(views);
        }

        /// <summary>World direction of the ray through a pixel of an upright image (origin top-left, y down).</summary>
        public static Vector3 Ray(IndoorSignObservation obs, Vector2 px) =>
            obs.CameraWorldRotation * new Vector3((px.x - obs.Cx) / obs.Fx, -(px.y - obs.Cy) / obs.Fy, 1f).normalized;

        static IndoorSignTriangulation Solve(List<IndoorSignObservation> views)
        {
            var result = new IndoorSignTriangulation { Frames = views.Count, Status = IndoorSignFixStatus.FewFrames };
            if (views.Count < MinFrames) return result;

            for (var i = 0; i < views.Count; i++)
                for (var j = i + 1; j < views.Count; j++)
                    result.ParallaxDeg = Mathf.Max(result.ParallaxDeg, Vector3.Angle(CenterRay(views[i]), CenterRay(views[j])));
            result.Status = IndoorSignFixStatus.LowParallax;
            if (result.ParallaxDeg < MinParallaxDeg) return result;

            result.Status = IndoorSignFixStatus.BadGeometry;
            var corners = new Vector3[4];
            double squared = 0;
            for (var c = 0; c < 4; c++)
                if (!Intersect(views, c, out corners[c], ref squared)) return result;
            result.ResidualM = (float)Math.Sqrt(squared / (4 * views.Count));
            if (result.ResidualM > MaxResidualM)
            {
                result.Status = IndoorSignFixStatus.HighResidual;
                return result;
            }

            var right = corners[1] - corners[0] + corners[2] - corners[3];
            var down = corners[3] - corners[0] + corners[2] - corners[1];
            result.WidthM = right.magnitude / 2f;
            result.HeightM = down.magnitude / 2f;
            result.CenterInWorld = (corners[0] + corners[1] + corners[2] + corners[3]) / 4f;
            right.y = 0f;
            // Text on a wall is upright and reads left to right from the hallway; anything else is a bad fit or
            // a wrong image orientation.
            if (result.WidthM < .02f || result.WidthM > 1.5f || result.HeightM < .01f || result.HeightM > 1f
                || right.sqrMagnitude < 1e-8f || Vector3.Dot(down.normalized, Vector3.down) < .7f) return result;
            var into = Vector3.Cross(right.normalized, Vector3.up);
            var range = Vector3.Distance(result.CenterInWorld, views[0].CameraWorldPosition);
            if (Vector3.Dot(into, result.CenterInWorld - views[0].CameraWorldPosition) <= 0f) return result;
            result.IntoWallYawDeg = Mathf.Atan2(into.x, into.z) * Mathf.Rad2Deg;
            // shortcut: heading error estimated from the fit, assuming at least one pixel of corner noise; replace
            // it with the measured spread once paired device runs exist.
            var noiseM = Mathf.Max(result.ResidualM, range / views[0].Fx);
            var depthSigmaM = noiseM / Mathf.Sin(result.ParallaxDeg * Mathf.Deg2Rad) / Mathf.Sqrt(views.Count);
            result.YawSigmaDeg = Mathf.Atan2(2f * depthSigmaM, result.WidthM) * Mathf.Rad2Deg;
            result.Status = IndoorSignFixStatus.Ok;
            return result;
        }

        static Vector3 CenterRay(IndoorSignObservation obs) =>
            Ray(obs, (obs.CornersPx[0] + obs.CornersPx[1] + obs.CornersPx[2] + obs.CornersPx[3]) / 4f);

        // Least-squares point closest to every frame's ray through one corner: sum(I - d d^T) p = sum(I - d d^T) o.
        static bool Intersect(List<IndoorSignObservation> views, int corner, out Vector3 point, ref double squared)
        {
            point = default;
            var origin = views[0].CameraWorldPosition; // keeps the sums small
            double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0, b0 = 0, b1 = 0, b2 = 0;
            foreach (var view in views)
            {
                var d = Ray(view, view.CornersPx[corner]);
                var o = view.CameraWorldPosition - origin;
                double along = Vector3.Dot(d, o);
                a00 += 1 - d.x * d.x; a01 -= d.x * d.y; a02 -= d.x * d.z;
                a11 += 1 - d.y * d.y; a12 -= d.y * d.z; a22 += 1 - d.z * d.z;
                b0 += o.x - d.x * along; b1 += o.y - d.y * along; b2 += o.z - d.z * along;
            }
            var c00 = a11 * a22 - a12 * a12;
            var c01 = a02 * a12 - a01 * a22;
            var c02 = a01 * a12 - a02 * a11;
            var det = a00 * c00 + a01 * c01 + a02 * c02;
            if (!(Math.Abs(det) > 1e-9)) return false;
            var local = new Vector3(
                (float)((c00 * b0 + c01 * b1 + c02 * b2) / det),
                (float)((c01 * b0 + (a00 * a22 - a02 * a02) * b1 + (a01 * a02 - a00 * a12) * b2) / det),
                (float)((c02 * b0 + (a01 * a02 - a00 * a12) * b1 + (a00 * a11 - a01 * a01) * b2) / det));
            point = origin + local;
            foreach (var view in views)
            {
                var d = Ray(view, view.CornersPx[corner]);
                var to = point - view.CameraWorldPosition;
                var along = Vector3.Dot(to, d);
                if (along <= 0f) return false; // behind a camera that saw it
                squared += (to - d * along).sqrMagnitude;
            }
            return true;
        }
    }
}
