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
        Assert.IsFalse(IndoorRoomVisionOcr.IsValidRgbaFrame(641, 360, 641 * 360 * 4));
        Assert.IsFalse(IndoorRoomVisionOcr.IsValidRgbaFrame(640, 360, 640 * 360 * 3));
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
            ocr.Result += (recognizedText, roomMatch) => { text = recognizedText; match = roomMatch; };

            ocr.OnVisionText("VEC\n119");

            Assert.AreEqual("VEC\n119", text);
            Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
            Assert.AreEqual("VEC119", match.Landmark.Id);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
