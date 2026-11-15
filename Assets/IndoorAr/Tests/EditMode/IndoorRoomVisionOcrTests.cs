using System.IO;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorRoomVisionOcrTests
{
    [Test]
    public void RgbaContractAcceptsOnlyBoundedCompleteFrames()
    {
        Assert.IsTrue(IndoorRoomVisionOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 4));
        Assert.IsFalse(IndoorRoomVisionOcr.IsValidRgbaFrame(0, 360, 0));
        Assert.IsFalse(IndoorRoomVisionOcr.IsValidRgbaFrame(1921, 360, 1921 * 360 * 4));
        Assert.IsFalse(IndoorRoomVisionOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 3));
    }

    [TestCase(ScreenOrientation.Portrait, 6)]
    [TestCase(ScreenOrientation.PortraitUpsideDown, 8)]
    [TestCase(ScreenOrientation.LandscapeLeft, 1)]
    [TestCase(ScreenOrientation.LandscapeRight, 3)]
    public void DisplayOrientationMapsToVisionExifOrientation(ScreenOrientation screen, int vision)
    {
        Assert.AreEqual(vision, IndoorRoomVisionOcr.VisionOrientation(screen));
    }

    [Test]
    public void VisionTextUsesRoomMatcherAndReturnsIdentityOnly()
    {
        var host = new GameObject("OCR adapter test");
        try
        {
            var ocr = host.AddComponent<IndoorRoomVisionOcr>();
            var directory = new TextAsset(File.ReadAllText(Path.Combine(
                Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json")));
            ocr.Configure(null, directory);
            string text = null;
            IndoorRoomMatch match = null;
            ocr.Result += (recognizedText, roomMatch, _, _) => { text = recognizedText; match = roomMatch; };

            ocr.OnVisionText("VEC\n119");

            Assert.AreEqual("VEC\n119", text);
            Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
            Assert.AreEqual("VEC119", match.Landmark.Id);
            Assert.IsNull(ocr.Engine);

            ocr.OnVisionText("{\"engine\":\"mlkit\",\"text\":\"VEC 120\"}");
            Assert.AreEqual("mlkit", ocr.Engine);
            Assert.AreEqual("VEC120", match.Landmark.Id);
            ocr.OnVisionText("{\"engine\":\"vision\",\"text\":\"\"}");
            Assert.AreEqual("vision", ocr.Engine, "a fallback to Apple Vision shows");
        }
        finally { Object.DestroyImmediate(host); }
    }
}
