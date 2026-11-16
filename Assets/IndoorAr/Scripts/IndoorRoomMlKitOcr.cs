using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace IndoorAr
{
    /// <summary>iOS room-sign OCR using AR Foundation's existing camera stream.</summary>
    public sealed class IndoorRoomMlKitOcr : MonoBehaviour
    {
        // shortcut: 0.3 s and 1920x1440 are untested guesses at what the phone sustains; tune from "ocrMs" in the log.
        const float FrameIntervalSeconds = .3f;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] TextAsset directory;
        float _nextFrameAt;
        readonly IndoorOcrJob _job = new IndoorOcrJob();
        byte[] _pixels;

        /// <summary>Text, its room match, when the frame was taken, and the sign's corners when the frame has them.</summary>
        public event Action<string, IndoorRoomMatch, float, IndoorSignObservation> Result;
        public string DirectoryJson => directory == null ? null : directory.text;
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int IndoorArRecognizeMlKitText(byte[] rgba, int width, int height,
            int orientation, string callbackObject, int captureId);
#endif

        public static bool IsValidRgbaFrame(int width, int height, int byteCount) =>
            width > 0 && height > 0 && width <= 1920 && height <= 1440
            && (long)width * height * 4 == byteCount;

        public static int MlKitOrientation(ScreenOrientation orientation) => orientation switch
        {
            ScreenOrientation.Portrait => 6,
            ScreenOrientation.PortraitUpsideDown => 8,
            ScreenOrientation.LandscapeLeft => 1,
            ScreenOrientation.LandscapeRight => 3,
            _ => 1
        };

        public void Configure(ARCameraManager cameraManager, TextAsset directory)
        {
            this.cameraManager = cameraManager;
            this.directory = directory;
        }

        void Update()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (cameraManager == null || _job.Busy || Time.unscaledTime < _nextFrameAt) return;
            _nextFrameAt = Time.unscaledTime + FrameIntervalSeconds;
            CaptureLatestFrame();
#endif
        }

        // A frame still converting or recognizing belongs to the session that was running; its reply is dropped.
        void OnDisable() => _job.Cancel();

        void CaptureLatestFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            var started = Time.realtimeSinceStartup;
            var orientation = MlKitOrientation(Screen.orientation);
            // Small digits on a sign across the hallway need the pixels: 640x480 only read signs at arm's length.
            var scale = Mathf.Min(1f, 1920f / image.width, 1440f / image.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));
            var id = _job.Start(started, IndoorOcrCapture.From(cameraManager, width, height));
            var parameters = new XRCpuImage.ConversionParams(
                image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
            {
                outputDimensions = new Vector2Int(width, height)
            };
            try
            {
                image.ConvertAsync(parameters,
                    (status, convertedParameters, data) =>
                        OnFrameConverted(id, status, convertedParameters, data, orientation));
            }
            catch
            {
                Fail(id, "frame_conversion_failed");
            }
            finally
            {
                image.Dispose();
            }
        }

        void OnFrameConverted(int id, XRCpuImage.AsyncConversionStatus status,
            XRCpuImage.ConversionParams parameters, NativeArray<byte> data, int orientation)
        {
            if (!_job.IsCurrent(id)) return;
            if (status != XRCpuImage.AsyncConversionStatus.Ready)
            {
                Fail(id, "frame_conversion_failed");
                return;
            }
            if (!IsValidRgbaFrame(parameters.outputDimensions.x,
                    parameters.outputDimensions.y, data.Length))
            {
                Fail(id, "invalid_frame");
                return;
            }
            // The plugin copies the pixels before it returns, so one buffer serves every frame.
            if (_pixels == null || _pixels.Length != data.Length) _pixels = new byte[data.Length];
            data.CopyTo(_pixels);
#if UNITY_IOS && !UNITY_EDITOR
            // 0: the recognizer is still busy with another frame and will not reply to this one.
            if (IndoorArRecognizeMlKitText(_pixels, parameters.outputDimensions.x,
                    parameters.outputDimensions.y, orientation, gameObject.name, id) == 0) _job.Cancel();
#endif
        }

        /// <param name="reply">"id|" then the ML Kit frame JSON, or plain recognized text.</param>
        public void OnMlKitText(string reply)
        {
            if (!_job.TryFinish(reply, out var message)) return;
            var frame = IndoorOcrFrame.Parse(message);
            var match = string.IsNullOrWhiteSpace(frame.text)
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoText)
                : IndoorRoomIdentityMatcher.MatchSign(frame.text, directory == null ? null : directory.text);
            Result?.Invoke(frame.text, match, _job.StartedAt,
                match.Landmark == null ? null : frame.Sighting(match.Landmark.Id, _job.Capture));
        }

        /// <param name="reply">"id|" then the failure reason.</param>
        public void OnMlKitError(string reply)
        {
            if (_job.TryFinish(reply, out var reason)) Report(reason);
        }

        void Fail(int id, string reason)
        {
            if (!_job.IsCurrent(id)) return;
            _job.Cancel();
            Report(reason);
        }

        void Report(string reason)
        {
            Debug.LogWarning($"[IndoorAr OCR] ML Kit unavailable: {reason}");
            Result?.Invoke("", new IndoorRoomMatch(IndoorRoomMatchStatus.RecognizerUnavailable),
                _job.StartedAt, null);
        }
    }
}
