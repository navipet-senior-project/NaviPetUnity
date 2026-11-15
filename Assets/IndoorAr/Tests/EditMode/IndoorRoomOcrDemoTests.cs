using System.IO;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class IndoorRoomOcrDemoTests
{
    [Test]
    public void RgbaContractAcceptsOnlyBoundedCompleteFrames()
    {
        Assert.IsTrue(IndoorRoomOcrDemo.IsValidRgbaFrame(640, 360, 640 * 360 * 4));
        Assert.IsFalse(IndoorRoomOcrDemo.IsValidRgbaFrame(0, 360, 0));
        Assert.IsFalse(IndoorRoomOcrDemo.IsValidRgbaFrame(1281, 360, 1281 * 360 * 4));
        Assert.IsFalse(IndoorRoomOcrDemo.IsValidRgbaFrame(640, 360, 640 * 360 * 3));
    }

    [Test]
    public void RecognizedRoomTextUsesExistingVECMatcherAndReturnsNoPose()
    {
        var directory = File.ReadAllText(Path.Combine(
            Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"));
        var host = new GameObject("OCR adapter test");
        try
        {
            var demo = host.AddComponent<IndoorRoomOcrDemo>();
            IndoorRoomMatch actual = null;
            string actualText = null;
            demo.Configure(null, File.ReadAllText(Path.Combine(
                Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json")));
            demo.Result += (text, match, _) => { actualText = text; actual = match; };

            LogAssert.Expect(LogType.Log, "[IndoorAr OCR] text='VEC 119' roomMatch=Matched");
            demo.OnMlKitText("VEC 119");

            Assert.AreEqual("VEC 119", actualText);
            Assert.AreEqual(IndoorRoomMatchStatus.Matched, actual.Status);
            Assert.AreEqual("VEC119", actual.Landmark.Id);
        }
        finally { Object.DestroyImmediate(host); }
    }
}
