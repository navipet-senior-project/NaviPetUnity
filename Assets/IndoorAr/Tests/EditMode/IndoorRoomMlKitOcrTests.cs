using System.IO;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorRoomMlKitOcrTests
{
    [Test]
    public void RgbaContractAcceptsOnlyBoundedCompleteFrames()
    {
        Assert.IsTrue(IndoorRoomMlKitOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 4));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(0, 360, 0));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(1921, 360, 1921 * 360 * 4));
        Assert.IsFalse(IndoorRoomMlKitOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 3));
    }

    [TestCase(ScreenOrientation.Portrait, 6)]
    [TestCase(ScreenOrientation.PortraitUpsideDown, 8)]
    [TestCase(ScreenOrientation.LandscapeLeft, 1)]
    [TestCase(ScreenOrientation.LandscapeRight, 3)]
    public void DisplayOrientationMapsToMlKitRotation(ScreenOrientation screen, int rotation)
    {
        Assert.AreEqual(rotation, IndoorRoomMlKitOcr.MlKitOrientation(screen));
    }

    [Test]
    public void MlKitTextUsesRoomMatcherAndReturnsIdentityOnly()
    {
        var host = new GameObject("OCR adapter test");
        try
        {
            var ocr = host.AddComponent<IndoorRoomMlKitOcr>();
            var directory = new TextAsset(File.ReadAllText(Path.Combine(
                Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json")));
            ocr.Configure(null, directory);
            string text = null;
            IndoorRoomMatch match = null;
            ocr.Result += (recognizedText, roomMatch, _, _) => { text = recognizedText; match = roomMatch; };

            ocr.OnMlKitText("VEC\n119");

            Assert.AreEqual("VEC\n119", text);
            Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
            Assert.AreEqual("VEC119", match.Landmark.Id);
            ocr.OnMlKitText("{\"text\":\"VEC 120\"}");
            Assert.AreEqual("VEC120", match.Landmark.Id);
            ocr.OnMlKitText("{\"text\":\"\"}");
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, match.Status);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "[IndoorAr OCR] ML Kit unavailable: recognizer_unavailable");
            ocr.OnMlKitError("recognizer_unavailable");
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, match.Status);
        }
        finally { Object.DestroyImmediate(host); }
    }

    [Test]
    public void BothPlatformAdaptersReportNoTextAndUnavailableWithoutAnotherRecognizer()
    {
        var host = new GameObject("OCR status test");
        try
        {
            IndoorRoomMatch iosMatch = null;
            var ios = host.AddComponent<IndoorRoomMlKitOcr>();
            ios.Result += (_, result, _, _) => iosMatch = result;
            ios.OnMlKitText("{\"text\":\"\"}");
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, iosMatch.Status);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                "[IndoorAr OCR] ML Kit unavailable: recognizer_unavailable");
            ios.OnMlKitError("recognizer_unavailable");
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, iosMatch.Status);

            IndoorRoomMatch androidMatch = null;
            var android = host.AddComponent<IndoorRoomOcrDemo>();
            android.Result += (_, result, _, _) => androidMatch = result;
            android.OnMlKitText("{\"text\":\"\"}");
            Assert.AreEqual(IndoorRoomMatchStatus.NoText, androidMatch.Status);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                "[IndoorAr OCR] recognition failed: recognizer_unavailable");
            android.OnMlKitError("recognizer_unavailable");
            Assert.AreEqual(IndoorRoomMatchStatus.RecognizerUnavailable, androidMatch.Status);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
