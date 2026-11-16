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
        bool _converting;
        bool _recognizing;
        float _recognitionStarted;
        IndoorOcrCapture _capture;
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
            if (cameraManager == null || _converting || _recognizing) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < _nextFrameAt) return;
            _nextFrameAt = Time.unscaledTime + FrameIntervalSeconds;
            CaptureLatestFrame();
#endif
        }

        void CaptureLatestFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            _recognitionStarted = Time.realtimeSinceStartup;
            // shortcut: 1280x720 is an untested guess at what Android phones sustain; tune from "ocrMs" in the log.
            var scale = Mathf.Min(1f, 1280f / image.width, 720f / image.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));
            _capture = IndoorOcrCapture.From(cameraManager, width, height);
            var parameters = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32,
                XRCpuImage.Transformation.None);
            parameters.outputDimensions = new Vector2Int(width, height);
            _converting = true;
            try { image.ConvertAsync(parameters, OnFrameConverted); }
            catch
            {
                _converting = false;
                OnMlKitError("frame_conversion_failed");
            }
            finally { image.Dispose(); }
        }

        void OnFrameConverted(XRCpuImage.AsyncConversionStatus status,
            XRCpuImage.ConversionParams parameters, NativeArray<byte> data)
        {
            _converting = false;
            if (status != XRCpuImage.AsyncConversionStatus.Ready)
            {
                OnMlKitError("frame_conversion_failed");
                return;
            }
            if (!IsValidRgbaFrame(parameters.outputDimensions.x,
                    parameters.outputDimensions.y, data.Length))
            {
                OnMlKitError("invalid_frame");
                return;
            }
            // JNI copies the array into Java on the call, so one buffer serves every frame.
            if (_pixels == null || _pixels.Length != data.Length) _pixels = new byte[data.Length];
            data.CopyTo(_pixels);
            _recognizing = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var plugin = new AndroidJavaClass("com.navipet.indoorar.ocr.IndoorArOcrPlugin"))
                {
                    _recognizing = plugin.CallStatic<bool>("recognize", _pixels,
                        parameters.outputDimensions.x, parameters.outputDimensions.y,
                        gameObject.name);
                }
            }
            catch (AndroidJavaException) { OnMlKitError("recognizer_unavailable"); }
#endif
        }

        /// <param name="message">The plugin's JSON (IndoorOcrFrame), or plain recognized text.</param>
        public void OnMlKitText(string message)
        {
            _recognizing = false;
            var frame = IndoorOcrFrame.Parse(message);
            var match = string.IsNullOrWhiteSpace(frame.text)
                ? new IndoorRoomMatch(IndoorRoomMatchStatus.NoText)
                : IndoorRoomIdentityMatcher.MatchSign(frame.text, _directoryJson ?? (directory == null ? null : directory.text));
            // The recognized text is never logged; the runner logs the match status.
            Result?.Invoke(frame.text, match, _recognitionStarted,
                match.Landmark == null ? null : frame.Sighting(match.Landmark.Id, _capture));
        }

        public void OnMlKitError(string message)
        {
            _recognizing = false;
            Debug.LogWarning($"[IndoorAr OCR] recognition failed: {message}");
            Result?.Invoke("", new IndoorRoomMatch(IndoorRoomMatchStatus.RecognizerUnavailable),
                _recognitionStarted, null);
        }
    }
}
