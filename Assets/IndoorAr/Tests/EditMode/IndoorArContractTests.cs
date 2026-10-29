using System.Globalization;
using IndoorAr;
using NUnit.Framework;

public class IndoorArContractTests
{
    const string Vec119 =
        "{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"," +
        "\"position\":{\"x\":-5.7651,\"y\":-1.8186,\"z\":22.6689},\"headingDeg\":185.36," +
        "\"mapCode\":\"MSET_XTBY7AM7ZK77\",\"frame\":\"multiset-mapset\"}";

    [Test]
    public void ParsesAValidRequest()
    {
        Assert.IsTrue(IndoorArContract.TryParseRequest(Vec119, out var request));
        Assert.AreEqual("119", request.room);
        Assert.AreEqual("VEC119", request.doorKey);
        Assert.AreEqual("1", request.floor);
        Assert.AreEqual(-5.7651f, request.position.x, 1e-4f);
        Assert.AreEqual(22.6689f, request.position.z, 1e-4f);
        Assert.AreEqual(185.36f, request.headingDeg, 1e-3f);
        Assert.AreEqual("MSET_XTBY7AM7ZK77", request.mapCode);
        Assert.AreEqual("multiset-mapset", request.frame);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{\"schemaVersion\":2,\"doorKey\":\"VEC119\",\"floor\":\"1\",\"mapCode\":\"M\",\"frame\":\"multiset-mapset\"}")]
    [TestCase("{\"schemaVersion\":1,\"floor\":\"1\",\"mapCode\":\"M\",\"frame\":\"multiset-mapset\"}")]
    [TestCase("{\"schemaVersion\":1,\"doorKey\":\"\",\"floor\":\"1\",\"mapCode\":\"M\",\"frame\":\"multiset-mapset\"}")]
    [TestCase("{\"schemaVersion\":1,\"doorKey\":\"VEC119\",\"mapCode\":\"M\",\"frame\":\"multiset-mapset\"}")]
    [TestCase("{\"schemaVersion\":1,\"doorKey\":\"VEC119\",\"floor\":\"1\",\"frame\":\"multiset-mapset\"}")]
    public void RejectsABadRequest(string json)
    {
        Assert.IsFalse(IndoorArContract.TryParseRequest(json, out var request));
        Assert.IsNull(request);
    }

    [Test]
    public void ParsesAForeignFrameSoTheSessionCanReportMapMismatch()
    {
        var json = Vec119.Replace("multiset-mapset", "unity-world");
        Assert.IsTrue(IndoorArContract.TryParseRequest(json, out var request));
        Assert.AreEqual("unity-world", request.frame);
    }

    [TestCase("multiset-mapset", "MSET_XTBY7AM7ZK77", true)]
    [TestCase("unity-world", "MSET_XTBY7AM7ZK77", false)]
    [TestCase("multiset-mapset", "MSET_OTHER", false)]
    public void ChecksFrameAndMapCode(string frame, string mapCode, bool expected)
    {
        var request = new IndoorArRequest { frame = frame, mapCode = mapCode };
        Assert.AreEqual(expected, IndoorArContract.CheckMap(request, "MSET_XTBY7AM7ZK77"));
    }

    [Test]
    public void BuildsLocalizationFailed()
    {
        Assert.AreEqual(
            "{\"status\":\"localizationFailed\",\"localizationQueries\":4,\"durationMs\":60000}",
            IndoorArContract.LocalizationFailed(4, 60000));
    }

    [Test]
    public void ErrorCodesMatchTheDartContract()
    {
        Assert.AreEqual("MAP_MISMATCH", IndoorArContract.MapMismatch);
        Assert.AreEqual("ARCORE_UNSUPPORTED", IndoorArContract.ArcoreUnsupported);
        Assert.AreEqual("MULTISET_AUTH", IndoorArContract.MultisetAuth);
        Assert.AreEqual("NETWORK", IndoorArContract.Network);
    }

    [Test]
    public void BuildsArrived()
    {
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"checkDistanceM\":0.42,\"localizationQueries\":0,\"durationMs\":1234}",
            IndoorArContract.Arrived(0.42f, 0, 1234));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-0.1f)]
    public void ArrivedWithAnInvalidDistanceBecomesAnError(float distance)
    {
        Assert.AreEqual(
            "{\"status\":\"error\",\"errorCode\":\"BAD_REQUEST\",\"localizationQueries\":2,\"durationMs\":7}",
            IndoorArContract.Arrived(distance, 2, 7));
    }

    [Test]
    public void BuildsArrivedWithConfidence()
    {
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"checkDistanceM\":0.42,\"confidence\":0.73,\"localizationQueries\":1,\"durationMs\":1234}",
            IndoorArContract.Arrived(0.42f, 1, 1234, 0.73f));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void InvalidConfidenceBecomesAnError(float confidence)
    {
        StringAssert.Contains("\"errorCode\":\"BAD_REQUEST\"",
            IndoorArContract.Arrived(1f, 1, 3, confidence));
    }

    [Test]
    public void BuildsCancelledAndError()
    {
        Assert.AreEqual(
            "{\"status\":\"cancelled\",\"localizationQueries\":1,\"durationMs\":5}",
            IndoorArContract.Cancelled(1, 5));
        Assert.AreEqual(
            "{\"status\":\"error\",\"errorCode\":\"BAD_REQUEST\",\"localizationQueries\":0,\"durationMs\":5}",
            IndoorArContract.Error(IndoorArContract.BadRequest, 0, 5));
    }

    [Test]
    public void WritesDecimalPointsInAnyCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            StringAssert.Contains("\"checkDistanceM\":1.5,", IndoorArContract.Arrived(1.5f, 0, 1));
            StringAssert.Contains("\"confidence\":0.1234567,", IndoorArContract.Arrived(1.5f, 0, 1, 0.1234567f));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
