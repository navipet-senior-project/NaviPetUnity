using IndoorAr;
using NUnit.Framework;

public class IndoorRoomIdentityMatcherTests
{
    static readonly IndoorRoomLandmark[] VecRooms =
    {
        new IndoorRoomLandmark("VEC119", "VEC", "floor_1"),
        new IndoorRoomLandmark("VEC120", "VEC", "floor_1"),
        new IndoorRoomLandmark("VEC201", "VEC", "floor_2")
    };

    [TestCase(" VEC 119 ")]
    [TestCase("Room 119")]
    [TestCase("119")]
    public void MatchesNormalizedVecRoomOnExplicitFloor(string text)
    {
        var result = IndoorRoomIdentityMatcher.Match(text, "VEC", "floor_1", VecRooms);

        Assert.AreEqual(IndoorRoomMatchStatus.Matched, result.Status);
        Assert.AreEqual("VEC119", result.Landmark.Id);
    }

    [Test]
    public void RequiresBuildingAndFloor()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.MissingBuilding,
            IndoorRoomIdentityMatcher.Match("119", " ", "floor_1", VecRooms).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.MissingFloor,
            IndoorRoomIdentityMatcher.Match("119", "VEC", null, VecRooms).Status);
    }

    [Test]
    public void RejectsNoMatchAndDuplicateRoomOnSameFloor()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("119", "VEC", "floor_2", VecRooms).Status);
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("119", "OTHER", "floor_1", VecRooms).Status);

        var ambiguous = new[]
        {
            VecRooms[0],
            new IndoorRoomLandmark("VEC 119", "VEC", "floor_1")
        };
        Assert.AreEqual(IndoorRoomMatchStatus.Ambiguous,
            IndoorRoomIdentityMatcher.Match("119", "VEC", "floor_1", ambiguous).Status);
    }

    [Test]
    public void RejectsUnknownRoomShownInReferenceImage()
    {
        // The supplied sign reads 118, which has no matching VEC directory entry here.
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch,
            IndoorRoomIdentityMatcher.Match("118", "VEC", "floor_1", VecRooms).Status);
    }

    [Test]
    public void RejectsTextThatIsNotOneRoomNumber()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.InvalidRoomNumber,
            IndoorRoomIdentityMatcher.Match("119 and 120", "VEC", "floor_1", VecRooms).Status);
    }
}
