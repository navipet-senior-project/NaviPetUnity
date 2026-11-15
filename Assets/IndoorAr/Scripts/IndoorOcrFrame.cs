using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace IndoorAr
{
    [Serializable]
    public sealed class IndoorOcrToken
    {
        public string text;
        /// <summary>x0,y0 .. x3,y3: top-left, top-right, bottom-right, bottom-left, in upright image pixels (y down).</summary>
        public float[] corners;
    }

    /// <summary>The camera behind one OCR frame: its intrinsics at the submitted image size, and where AR had it.</summary>
    public struct IndoorOcrCapture
    {
        /// <summary>Camera-image intrinsics (sensor orientation) scaled to Width x Height. Fx is 0 when unknown.</summary>
        public float Fx, Fy, Cx, Cy;
        public int Width, Height;
        public Vector3 CameraWorldPosition;
        public Quaternion CameraWorldRotation;

        /// <summary>Call in the frame the camera image is acquired, so the pose belongs to that image.</summary>
        public static IndoorOcrCapture From(ARCameraManager camera, int width, int height)
        {
            var capture = new IndoorOcrCapture
            {
                Width = width, Height = height,
                CameraWorldPosition = camera.transform.position, CameraWorldRotation = camera.transform.rotation
            };
            if (!camera.TryGetIntrinsics(out var k) || k.resolution.x <= 0 || k.resolution.y <= 0) return capture;
            float sx = width / (float)k.resolution.x, sy = height / (float)k.resolution.y;
            capture.Fx = k.focalLength.x * sx;
            capture.Fy = k.focalLength.y * sy;
            capture.Cx = k.principalPoint.x * sx;
            capture.Cy = k.principalPoint.y * sy;
            return capture;
        }
    }

    /// <summary>
    /// What a native recognizer returns for one frame: the text, and for each piece of it the four corners in
    /// the upright image. A plain string (no corners) is accepted as text only.
    /// </summary>
    [Serializable]
    public sealed class IndoorOcrFrame
    {
        public string text;
        /// <summary>Size of the upright image the corners are in.</summary>
        public int width, height;
        /// <summary>Clockwise degrees the camera image was turned to stand upright.</summary>
        public int rotation;
        public IndoorOcrToken[] tokens;

        public static IndoorOcrFrame Parse(string message)
        {
            if (message == null || !message.StartsWith("{")) return new IndoorOcrFrame { text = message };
            try
            {
                var frame = JsonUtility.FromJson<IndoorOcrFrame>(message);
                if (frame != null) return frame;
            }
            catch (ArgumentException) { }
            // Never match room numbers against broken JSON: its coordinates would read as rooms.
            return new IndoorOcrFrame { text = "" };
        }

        /// <summary>Corners of the one token that reads this room's number. Null when none or several do.</summary>
        public Vector2[] CornersOf(string roomId)
        {
            var wanted = Digits(roomId);
            if (tokens == null || wanted.Length == 0) return null;
            float[] found = null;
            foreach (var token in tokens)
            {
                if (token?.corners == null || token.corners.Length != 8 || Digits(token.text) != wanted) continue;
                if (found != null) return null;
                found = token.corners;
            }
            if (found == null) return null;
            var corners = new Vector2[4];
            for (var i = 0; i < 4; i++) corners[i] = new Vector2(found[2 * i], found[2 * i + 1]);
            return corners;
        }

        /// <summary>
        /// The room's corners paired with the camera that took the frame, with the intrinsics turned the same way
        /// the image was. Null when the corners, the intrinsics or a consistent image size are missing.
        /// </summary>
        public IndoorSignObservation Sighting(string roomId, IndoorOcrCapture capture)
        {
            var corners = CornersOf(roomId);
            if (corners == null || !(capture.Fx > 0f) || !(capture.Fy > 0f)) return null;
            var sighting = new IndoorSignObservation
            {
                RoomId = roomId, CornersPx = corners,
                CameraWorldPosition = capture.CameraWorldPosition, CameraWorldRotation = capture.CameraWorldRotation
            };
            var turned = rotation == 90 || rotation == 270;
            if (width != (turned ? capture.Height : capture.Width) || height != (turned ? capture.Width : capture.Height))
                return null;
            switch (rotation)
            {
                case 0:
                    (sighting.Fx, sighting.Fy, sighting.Cx, sighting.Cy) = (capture.Fx, capture.Fy, capture.Cx, capture.Cy);
                    break;
                case 90:
                    (sighting.Fx, sighting.Fy, sighting.Cx, sighting.Cy) = (capture.Fy, capture.Fx, capture.Height - capture.Cy, capture.Cx);
                    break;
                case 180:
                    (sighting.Fx, sighting.Fy, sighting.Cx, sighting.Cy) = (capture.Fx, capture.Fy, capture.Width - capture.Cx, capture.Height - capture.Cy);
                    break;
                case 270:
                    (sighting.Fx, sighting.Fy, sighting.Cx, sighting.Cy) = (capture.Fy, capture.Fx, capture.Cy, capture.Width - capture.Cx);
                    break;
                default:
                    return null;
            }
            return sighting;
        }

        static string Digits(string value) => value == null ? "" : Regex.Replace(value, "[^0-9]", "");
    }
}
