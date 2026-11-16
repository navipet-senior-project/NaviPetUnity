using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace IndoorAr
{
    /// <summary>Android room-sign OCR using AR Foundation's existing camera stream.</summary>
    public sealed class IndoorRoomOcrDemo : MonoBehaviour
    {
        const float FrameIntervalSeconds = .5f;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] TextAsset directory;
        float _nextFrameAt;
        readonly IndoorOcrJob _job = new IndoorOcrJob();
        byte[] _pixels;

        /// <summary>Text, its room match, when the frame was taken, and the sign's corners when the frame has them.</summary>
        public event Action<string, IndoorRoomMatch, float, IndoorSignObservation> Result;

        public static bool IsValidRgbaFrame(int width, int height, int byteCount) =>
            width > 0 && height > 0 && width <= 1280 && height <= 720
            && (long)width * height * 4 == byteCount;

        public void Configure(ARCameraManager cameraManager, string directoryJson)
        {
            this.cameraManager = cameraManager;
            directory = null;
            _directoryJson = directoryJson;
        }

        string _directoryJson;
        public string DirectoryJson => _directoryJson ?? (directory == null ? null : directory.text);

        public void Configure(ARCameraManager cameraManager, TextAsset directory)
        {
            this.cameraManager = cameraManager;
            this.directory = directory;
            _directoryJson = directory == null ? null : directory.text;
        }

        void Update()
        {
            if (cameraManager == null || _job.Busy) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < _nextFrameAt) return;
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
            // shortcut: 1280x720 is an untested guess at what Android phones sustain; tune from "ocrMs" in the log.
            var scale = Mathf.Min(1f, 1280f / image.width, 720f / image.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));
            var id = _job.Start(started, IndoorOcrCapture.From(cameraManager, width, height));
            var parameters = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32,
                XRCpuImage.Transformation.None);
            parameters.outputDimensions = new Vector2Int(width, height);
            try { image.ConvertAsync(parameters, (status, converted, data) => OnFrameConverted(id, status, converted, data)); }
            catch { Fail(id, "frame_conversion_failed"); }
            finally { image.Dispose(); }
        }

        void OnFrameConverted(int id, XRCpuImage.AsyncConversionStatus status,
            XRCpuImage.ConversionParams parameters, NativeArray<byte> data)
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
            // JNI copies the array into Java on the call, so one buffer serves every frame.
            if (_pixels == null || _pixels.Length != data.Length) _pixels = new byte[data.Length];
            data.CopyTo(_pixels);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var plugin = new AndroidJavaClass("com.navipet.indoorar.ocr.IndoorArOcrPlugin"))
                {
                    // false: the recognizer is still busy with another frame and will not reply to this one.
                    if (!plugin.CallStatic<bool>("recognize", _pixels,
                            parameters.outputDimensions.x, parameters.outputDimensions.y,
                            gameObject.name, id)) _job.Cancel();
                }
            }
            catch (AndroidJavaException) { Fail(id, "recognizer_unavailable"); }
#endif
        }

        /// <param name="reply">"id|" then the plugin's JSON (IndoorOcrFrame), or plain recognized text.</param>
        public void OnMlKitText(string reply)
        {
            if (!_job.TryFinish(reply, out var message)) return;
            var frame = IndoorOcrFrame.Parse(message);
            var match = string.IsNullOrWhiteSpace(frame.text)
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoText)
                : IndoorRoomIdentityMatcher.MatchSign(frame.text, _directoryJson ?? (directory == null ? null : directory.text));
            // The recognized text is never logged; the runner logs the match status.
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
            Debug.LogWarning($"[IndoorAr OCR] recognition failed: {reason}");
            Result?.Invoke("", new IndoorRoomMatch(IndoorRoomMatchStatus.RecognizerUnavailable),
                _job.StartedAt, null);
        }
    }
}
