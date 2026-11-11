using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace IndoorAr
{
    /// <summary>Optional Android OCR demo using AR Foundation's existing camera stream.</summary>
    public sealed class IndoorRoomOcrDemo : MonoBehaviour
    {
        const float FrameIntervalSeconds = 1f;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] TextAsset directory;
        float _nextFrameAt;
        bool _converting;
        bool _recognizing;

        public event Action<string, IndoorRoomMatch> Result;

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

        public void Configure(ARCameraManager cameraManager, TextAsset directory)
        {
            this.cameraManager = cameraManager;
            this.directory = directory;
            _directoryJson = directory == null ? null : directory.text;
        }

        void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (cameraManager == null || _converting || _recognizing
                || Time.unscaledTime < _nextFrameAt) return;
            _nextFrameAt = Time.unscaledTime + FrameIntervalSeconds;
            CaptureLatestFrame();
#endif
        }

        void CaptureLatestFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            var scale = Mathf.Min(1f, 640f / image.width, 480f / image.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));
            var parameters = new XRCpuImage.ConversionParams(image,
                new Vector2Int(width, height), TextureFormat.RGBA32,
                XRCpuImage.Transformation.None);
            _converting = true;
            image.ConvertAsync(parameters, OnFrameConverted);
            image.Dispose();
        }

        void OnFrameConverted(XRCpuImage.AsyncConversionStatus status,
            XRCpuImage.ConversionParams parameters, NativeArray<byte> data)
        {
            _converting = false;
            if (status != XRCpuImage.AsyncConversionStatus.Ready) return;
            var pixels = data.ToArray();
            if (!IsValidRgbaFrame(parameters.outputDimensions.x,
                    parameters.outputDimensions.y, pixels.Length)) return;
            _recognizing = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var plugin = new AndroidJavaClass("com.navipet.indoorar.ocr.IndoorArOcrPlugin"))
            {
                _recognizing = plugin.CallStatic<bool>("recognize", pixels,
                    parameters.outputDimensions.x, parameters.outputDimensions.y,
                    gameObject.name);
            }
#endif
        }

        public void OnMlKitText(string recognizedText)
        {
            _recognizing = false;
            var match = IndoorRoomIdentityMatcher.Match(
                recognizedText, "VEC", "floor_1",
                _directoryJson ?? (directory == null ? null : directory.text));
            Debug.Log($"[IndoorAr OCR] text='{recognizedText}' roomMatch={match.Status}");
            Result?.Invoke(recognizedText, match);
        }

        public void OnMlKitError(string message)
        {
            _recognizing = false;
            Debug.LogWarning($"[IndoorAr OCR] recognition failed: {message}");
        }
    }
}
