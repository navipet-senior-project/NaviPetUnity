using IndoorAr;
using NUnit.Framework;
using System.IO;
using UnityEngine;

public class IndoorRoomIdentityMatcherTests
{
    static readonly string VecDirectory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"));

    [TestCase(" VEC 119 ")]
    [TestCase("Room 119")]
    [TestCase("119")]
    public void MatchesNormalizedVecRoomOnExplicitFloor(string text)
    {
        var result = IndoorRoomIdentityMatcher.Match(text, "VEC", "floor_1", VecDirectory);

        Assert.AreEqual(IndoorRoomMatchStatus.Matched, result.Status);
        Assert.AreEqual("VEC119", result.Landmark.Id);
    }

    [Test]
    public void RequiresBuildingAndFloor()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.MissingBuilding,
            IndoorRoomIdentityMatcher.Match("119", " ", "floor_1", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.BuildingMismatch,
            IndoorRoomIdentityMatcher.Match("119", "OTHER", "floor_1", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.MissingFloor,
            IndoorRoomIdentityMatcher.Match("119", "VEC", null, VecDirectory).Status);
    }

    [Test]
    public void RejectsNoMatchAndDuplicateRoomOnSameFloor()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("119", "VEC", "floor_2", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("119", "VEC", "floor_3", VecDirectory).Status);

        const string ambiguous = "{\"buildingId\":\"VEC\",\"objects\":[{\"id\":\"VEC119\",\"type\":\"door\",\"floorId\":\"floor_1\"},{\"id\":\"VEC 119\",\"type\":\"door\",\"floorId\":\"floor_1\"}]}";
        Assert.AreEqual(IndoorRoomMatchStatus.Ambiguous,
            IndoorRoomIdentityMatcher.Match("119", "VEC", "floor_1", ambiguous).Status);
    }

    [Test]
    public void RejectsUnknownRoomShownInReferenceImage()
    {
        // The supplied sign reads 118, which has no matching VEC directory entry here.
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("118", "VEC", "floor_1", VecDirectory).Status);
    }

    [Test]
    public void RejectsTextThatIsNotOneRoomNumber()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.InvalidRoomNumber,
            IndoorRoomIdentityMatcher.Match("119 and 120", "VEC", "floor_1", VecDirectory).Status);
    }

    [TestCase("VEC 215", "VEC215", "floor_2")]
    [TestCase("ROOM\nVEC 120\nCLASSROOM", "VEC120", "floor_1")]
    [TestCase("120\n120", "VEC120", "floor_1")]
    public void SignMatchesAnyFloorFromAnyLine(string text, string id, string floor)
    {
        var result = IndoorRoomIdentityMatcher.MatchSign(text, VecDirectory);
        Assert.AreEqual(IndoorRoomMatchStatus.Matched, result.Status);
        Assert.AreEqual(id, result.Landmark.Id);
        Assert.AreEqual(floor, result.Landmark.FloorId);
        Assert.AreEqual("VEC", result.Landmark.BuildingId);
    }

    [Test]
    public void SignNamingTwoRoomsOrNoneIsNotAGuess()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.Ambiguous,
            IndoorRoomIdentityMatcher.MatchSign("VEC 119\nVEC 120", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.MatchSign("VEC 999", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.InvalidRoomNumber,
            IndoorRoomIdentityMatcher.MatchSign("EXIT", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.InvalidRoomNumber,
            IndoorRoomIdentityMatcher.MatchSign("  ", VecDirectory).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.MissingBuilding,
            IndoorRoomIdentityMatcher.MatchSign("119", "{}").Status);
    }

    [Test]
    public void SignWithUnknownThreeDigitRoomIsAmbiguous()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.Ambiguous,
            IndoorRoomIdentityMatcher.MatchSign("VEC 120\nVEC 999", VecDirectory).Status);
    }

    [Test]
    public void SignIgnoresNonThreeDigitNumbers()
    {
        var result = IndoorRoomIdentityMatcher.MatchSign("VEC 120\nVEC 12\nVEC 9999", VecDirectory);
        Assert.AreEqual(IndoorRoomMatchStatus.Matched, result.Status);
        Assert.AreEqual("VEC120", result.Landmark.Id);
    }

    [TestCase("VEC 119 VEC 120")]
    [TestCase("119 120")]
    public void SignWithTwoRoomsOnOneLineIsAmbiguous(string text)
    {
        Assert.AreEqual(IndoorRoomMatchStatus.Ambiguous,
            IndoorRoomIdentityMatcher.MatchSign(text, VecDirectory).Status);
    }

    [Test]
    public void SignDoesNotMatchDigitsEmbeddedInWords()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.InvalidRoomNumber,
            IndoorRoomIdentityMatcher.MatchSign("VEC119x", VecDirectory).Status);
    }
}
