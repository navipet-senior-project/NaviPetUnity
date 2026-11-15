using System.IO;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorOcrFrameTests
{
    // A portrait frame: the 1920x1440 camera image turned a quarter to stand upright.
    const string Json = "{\"text\":\"VEC\\n119\",\"width\":1440,\"height\":1920,\"rotation\":90,\"tokens\":["
        + "{\"text\":\"VEC\",\"corners\":[600,900,680,901,681,930,599,929]},"
        + "{\"text\":\"119\",\"corners\":[700,900,760,901,761,930,699,929]}]}";

    static IndoorOcrCapture Capture() => new IndoorOcrCapture
    {
        Fx = 1500f, Fy = 1490f, Cx = 950f, Cy = 730f, Width = 1920, Height = 1440,
        CameraWorldPosition = new Vector3(1f, 2f, 3f), CameraWorldRotation = Quaternion.Euler(0f, 30f, 0f)
    };

    static IndoorOcrFrame Frame(int rotation, int width, int height) => new IndoorOcrFrame
    {
        text = "119", rotation = rotation, width = width, height = height,
        tokens = new[] { new IndoorOcrToken { text = "119", corners = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 } } }
    };

    [Test]
    public void JsonCarriesTheTextAndTheRoomNumbersCorners()
    {
        var frame = IndoorOcrFrame.Parse(Json);
        Assert.AreEqual("VEC\n119", frame.text);
        Assert.IsNull(frame.engine, "a recognizer that does not name itself is not guessed");
        Assert.AreEqual("mlkit", IndoorOcrFrame.Parse(Json.Replace("{\"text\":\"VEC\\n", "{\"engine\":\"mlkit\",\"text\":\"VEC\\n")).engine);
        var corners = frame.CornersOf("VEC119");
        Assert.AreEqual(new Vector2(700f, 900f), corners[0]);
        Assert.AreEqual(new Vector2(699f, 929f), corners[3]);
        Assert.IsNull(frame.CornersOf("VEC120"));
    }

    [Test]
    public void PlainTextIsTextWithoutCorners()
    {
        var frame = IndoorOcrFrame.Parse("VEC 119");
        Assert.AreEqual("VEC 119", frame.text);
        Assert.IsNull(frame.CornersOf("VEC119"));
        Assert.IsNull(IndoorOcrFrame.Parse(null).text);
    }

    [Test]
    public void BrokenJsonNamesNoRoom()
    {
        Assert.AreEqual("", IndoorOcrFrame.Parse("{\"text\":\"119\",\"width\":14").text);
    }

    [Test]
    public void TwoTokensReadingTheRoomGiveNoCorners()
    {
        var frame = Frame(0, 1920, 1440);
        frame.tokens = new[] { frame.tokens[0], frame.tokens[0] };
        Assert.IsNull(frame.CornersOf("VEC119"), "which one is the sign is unknown");
        frame.tokens = new[] { new IndoorOcrToken { text = "119", corners = new float[] { 1, 2, 3, 4, 5, 6 } } };
        Assert.IsNull(frame.CornersOf("VEC119"), "three corners are not a box");
    }

    // A camera pixel (x, y) lands at (H - y, x) after a quarter turn clockwise, and so does the principal point.
    [TestCase(0, 1920, 1440, 1500f, 1490f, 950f, 730f)]
    [TestCase(90, 1440, 1920, 1490f, 1500f, 710f, 950f)]
    [TestCase(180, 1920, 1440, 1500f, 1490f, 970f, 710f)]
    [TestCase(270, 1440, 1920, 1490f, 1500f, 730f, 970f)]
    public void IntrinsicsTurnWithTheImage(int rotation, int width, int height, float fx, float fy, float cx, float cy)
    {
        var sighting = Frame(rotation, width, height).Sighting("VEC119", Capture());
        Assert.AreEqual("VEC119", sighting.RoomId);
        Assert.AreEqual(new Vector4(fx, fy, cx, cy), new Vector4(sighting.Fx, sighting.Fy, sighting.Cx, sighting.Cy));
        Assert.AreEqual(new Vector2(1f, 2f), sighting.CornersPx[0]);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), sighting.CameraWorldPosition);
        Assert.AreEqual(Quaternion.Euler(0f, 30f, 0f), sighting.CameraWorldRotation);
    }

    [Test]
    public void NoSightingWithoutIntrinsicsOrAMatchingImage()
    {
        Assert.IsNull(Frame(0, 1920, 1440).Sighting("VEC119", new IndoorOcrCapture { Width = 1920, Height = 1440 }),
            "no intrinsics");
        Assert.IsNull(Frame(90, 1920, 1440).Sighting("VEC119", Capture()), "a quarter turn swaps width and height");
        Assert.IsNull(Frame(0, 640, 480).Sighting("VEC119", Capture()), "corners from another image size");
        Assert.IsNull(Frame(45, 1920, 1440).Sighting("VEC119", Capture()));
        Assert.IsNull(Frame(0, 1920, 1440).Sighting("VEC120", Capture()), "no token reads that room");
    }

    [Test]
    public void AdapterMatchesTheJsonTextAndClaimsNoSightingWithoutACamera()
    {
        var host = new GameObject("OCR adapter test");
        try
        {
            var ocr = host.AddComponent<IndoorRoomVisionOcr>();
            ocr.Configure(null, new TextAsset(File.ReadAllText(Path.Combine(
                Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"))));
            IndoorRoomMatch match = null;
            var sighting = new IndoorSignObservation();
            ocr.Result += (_, roomMatch, _, observation) => { match = roomMatch; sighting = observation; };

            ocr.OnVisionText(Json);

            Assert.AreEqual("VEC119", match.Landmark.Id);
            Assert.IsNull(sighting, "the editor has no camera intrinsics, so the corners are not a sighting");
        }
        finally { Object.DestroyImmediate(host); }
    }
}
