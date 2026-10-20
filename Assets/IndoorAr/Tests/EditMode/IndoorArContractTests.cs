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
        Assert.AreEqual("VEC119", request.doorKey);
        Assert.AreEqual("1", request.floor);
        Assert.AreEqual(-5.7651f, request.position.x, 1e-4f);
        Assert.AreEqual(22.6689f, request.position.z, 1e-4f);
        Assert.AreEqual(185.36f, request.headingDeg, 1e-3f);
        Assert.AreEqual("MSET_XTBY7AM7ZK77", request.mapCode);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{\"schemaVersion\":2,\"doorKey\":\"VEC119\",\"floor\":\"1\",\"mapCode\":\"M\"}")]
    [TestCase("{\"schemaVersion\":1,\"floor\":\"1\",\"mapCode\":\"M\"}")]
    [TestCase("{\"schemaVersion\":1,\"doorKey\":\"VEC119\",\"floor\":\"1\"}")]
    public void RejectsABadRequest(string json)
    {
        Assert.IsFalse(IndoorArContract.TryParseRequest(json, out var request));
        Assert.IsNull(request);
    }

    [Test]
    public void BuildsArrived()
    {
        Assert.AreEqual(
            "{\"status\":\"arrived\",\"checkDistanceM\":0.42,\"localizationQueries\":0,\"durationMs\":1234}",
            IndoorArContract.Arrived(0.42f, 0, 1234));
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
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
