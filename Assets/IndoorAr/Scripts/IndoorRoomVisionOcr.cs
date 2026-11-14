using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace IndoorAr
{
    /// <summary>Optional iOS Vision OCR using AR Foundation's existing camera stream.</summary>
    public sealed class IndoorRoomVisionOcr : MonoBehaviour
    {
        const float FrameIntervalSeconds = .5f;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] TextAsset directory;
        float _nextFrameAt;
        bool _converting;
        bool _recognizing;
        float _recognitionStarted;

        public event Action<string, IndoorRoomMatch, float> Result;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int IndoorArRecognizeText(byte[] rgba, int width, int height,
            int orientation, string callbackObject);
#endif

        public static bool IsValidRgbaFrame(int width, int height, int byteCount) =>
            width > 0 && height > 0 && width <= 640 && height <= 480
            && (long)width * height * 4 == byteCount;

        public static int VisionOrientation(ScreenOrientation orientation) => orientation switch
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
            if (cameraManager == null || _converting || _recognizing
                || Time.unscaledTime < _nextFrameAt) return;
            _nextFrameAt = Time.unscaledTime + FrameIntervalSeconds;
            CaptureLatestFrame();
#endif
        }

        void CaptureLatestFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            _recognitionStarted = Time.realtimeSinceStartup;
            var orientation = VisionOrientation(Screen.orientation);
            var scale = Mathf.Min(1f, 640f / image.width, 480f / image.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(image.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(image.height * scale));
            var parameters = new XRCpuImage.ConversionParams(
                image, TextureFormat.RGBA32, XRCpuImage.Transformation.None)
            {
                outputDimensions = new Vector2Int(width, height)
            };
            _converting = true;
            try
            {
                image.ConvertAsync(parameters,
                    (status, convertedParameters, data) =>
                        OnFrameConverted(status, convertedParameters, data, orientation));
            }
            catch
            {
                _converting = false;
            }
            finally
            {
                image.Dispose();
            }
        }

        void OnFrameConverted(XRCpuImage.AsyncConversionStatus status,
            XRCpuImage.ConversionParams parameters, NativeArray<byte> data, int orientation)
        {
            _converting = false;
            if (status != XRCpuImage.AsyncConversionStatus.Ready) return;
            var pixels = data.ToArray();
            if (!IsValidRgbaFrame(parameters.outputDimensions.x,
                    parameters.outputDimensions.y, pixels.Length)) return;
#if UNITY_IOS && !UNITY_EDITOR
            _recognizing = IndoorArRecognizeText(pixels, parameters.outputDimensions.x,
                parameters.outputDimensions.y, orientation, gameObject.name) != 0;
#endif
        }

        public void OnVisionText(string recognizedText)
        {
            _recognizing = false;
            var match = IndoorRoomIdentityMatcher.MatchSign(
                recognizedText, directory == null ? null : directory.text);
            Result?.Invoke(recognizedText, match, _recognitionStarted);
        }

        public void OnVisionError(string ignored)
        {
            _recognizing = false;
        }
    }
}
