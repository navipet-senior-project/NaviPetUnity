using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class RoomDirectionsTests
{
    static readonly string Directory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"));
    static readonly string Scan = Resources.Load<TextAsset>("IndoorWalkGrid").text;

    static IndoorRoomLandmark Sign(string room) => IndoorRoomIdentityMatcher.MatchSign(room, Directory).Landmark;

    static IndoorArRequest To(string room, bool stepFree = false, string mode = null)
    {
        var door = Sign(room);
        var at = door.DoorPositionInMap;
        return new IndoorArRequest
        {
            mode = mode, room = room, doorKey = "VEC" + room, floor = door.Floor.ToString(), stepFree = stepFree,
            position = new IndoorArVector3 { x = at.x, y = at.y, z = at.z }
        };
    }

    static string Directions(string from, IndoorArRequest request)
    {
        var sign = Sign(from);
        return LiveDoorHud.RoomDirections(sign, request, IndoorWalkGrid.ForFloor(Scan, sign.Floor),
            IndoorRoomIdentityMatcher.FloorMarkers(Directory, sign.Floor));
    }

    // Heading is unknown in room-only mode: no turn, side or arrow words.
    static void AssertHeadingFree(string text)
    {
        StringAssert.EndsWith(LiveDoorHud.HeadingUnknown, text);
        Assert.IsFalse(Regex.IsMatch(text, @"\b(left|right|turn|behind|ahead|straight)\b", RegexOptions.IgnoreCase), text);
    }

    [Test]
    public void SameFloorRoomGetsScanGridDirectionsToTheDestination()
    {
        var text = Directions("127", To("119"));
        StringAssert.StartsWith("Walk", text);
        StringAssert.Contains(" to VEC 119.", text);
        AssertHeadingFree(text);
        // The distance is the scan-grid route length, not a straight line through walls.
        var grid = IndoorWalkGrid.ForFloor(Scan, 1);
        Assert.IsTrue(grid.TryRoute(Sign("127").DoorPositionInMap, Sign("119").DoorPositionInMap, out var route));
        StringAssert.Contains("about " + IndoorHud.DistanceLabel(IndoorWalkGrid.LengthM(route)) + " ", text);
    }

    [Test]
    public void EveryFloorOneRoomReachesEveryOtherWithoutInventedTurns()
    {
        var rooms = new List<string>();
        foreach (var (id, type, _) in IndoorRoomIdentityMatcher.FloorMarkers(Directory, 1))
            if (type == "door") rooms.Add(id.Substring(3));
        Assert.Greater(rooms.Count, 5);
        foreach (var from in rooms)
            foreach (var to in rooms)
            {
                if (from == to) continue;
                var text = Directions(from, To(to));
                Assert.IsNotNull(text, $"{from} to {to}");
                StringAssert.Contains(" to VEC " + to + ".", text);
                StringAssert.DoesNotContain("VEC " + from + ",", text, "the cue is never the room the user is at");
                AssertHeadingFree(text);
            }
    }

    [Test]
    public void OtherFloorRoomIsSentToAStairsOrElevatorFirst()
    {
        var text = Directions("215", To("119"));
        StringAssert.IsMatch(@" to the (stairs|elevator), then go to Floor 1\. ", text);
        AssertHeadingFree(text);
        text = Directions("127", To("215"));
        StringAssert.IsMatch(@" to the (stairs|elevator), then go to Floor 2\. ", text);
    }

    [Test]
    public void CueIsTheFirstMappedDoorBesideTheHallway()
    {
        var hall = IndoorWalkGrid.ForFloor(JsonUtility.ToJson(new IndoorWalkGridData
        {
            floors = new[] { new IndoorWalkFloor { floor = 1, cell = 1f, width = 11, height = 1, rows = new[] { "11111111111" } } }
        }), 1);
        var sign = new IndoorRoomLandmark("VEC101", "VEC", "floor_1", new Vector3(.5f, 0f, .5f));
        var request = new IndoorArRequest
        {
            room = "110", floor = "1", position = new IndoorArVector3 { x = 10.5f, z = .5f }
        };
        var markers = new List<(string, string, Vector3)>
        {
            ("VEC102", "door", new Vector3(1.5f, 0f, .5f)), // too close to the start to be walked past
            ("VEC103", "door", new Vector3(3f, 0f, 5f)), // too far off the hallway
            ("VEC104", "door", new Vector3(6.5f, 0f, 1.5f)),
            ("VEC105", "door", new Vector3(4.5f, 0f, 2f)), // first one passed
            ("elevator", "elevator", new Vector3(3.5f, 0f, .5f)), // not a room sign the user can read
            ("VEC110", "door", new Vector3(10.5f, 0f, .5f))
        };
        Assert.IsTrue(hall.TryRoute(sign.DoorPositionInMap, new Vector3(10.5f, 0f, .5f), out var route));
        Assert.AreEqual("Walk toward VEC 105, about " + IndoorHud.DistanceLabel(IndoorWalkGrid.LengthM(route))
            + " to VEC 110. " + LiveDoorHud.HeadingUnknown, LiveDoorHud.RoomDirections(sign, request, hall, markers));

        markers.RemoveAll(m => m.Item1 is "VEC104" or "VEC105");
        Assert.AreEqual("Walk about " + IndoorHud.DistanceLabel(IndoorWalkGrid.LengthM(route))
            + " to VEC 110. " + LiveDoorHud.HeadingUnknown, LiveDoorHud.RoomDirections(sign, request, hall, markers));
    }

    [Test]
    public void NoDirectionsWhenThereIsNothingHonestToSay()
    {
        Assert.IsNull(Directions("119", To("119")), "already at the destination's sign");
        Assert.IsNull(Directions("127", To("119", mode: "localize")));
        var noPosition = To("119");
        noPosition.position = null;
        Assert.IsNull(Directions("127", noPosition));
        var sign = Sign("127");
        Assert.IsNull(LiveDoorHud.RoomDirections(sign, To("119"), null,
            IndoorRoomIdentityMatcher.FloorMarkers(Directory, 1)), "no scan grid, no route");
        // A door the scan missed has no walkable cells: no route rather than a guess.
        var away = new IndoorRoomLandmark("VEC127", "VEC", "floor_1", new Vector3(500f, 0f, 500f));
        Assert.IsNull(LiveDoorHud.RoomDirections(away, To("119"), IndoorWalkGrid.ForFloor(Scan, 1),
            IndoorRoomIdentityMatcher.FloorMarkers(Directory, 1)));
    }

    [Test]
    public void StepFreeRequestGetsNoUnverifiedRoute()
    {
        Assert.AreEqual("A step-free route can't be confirmed from this room.", Directions("127", To("119", stepFree: true)));
    }

    [Test]
    public void RoomOnlyHudShowsDirectionsAndNoArrow()
    {
        var request = To("119");
        request.frame = IndoorArContract.Frame;
        request.mapCode = "MSET_XTBY7AM7ZK77";
        var session = new IndoorArSession(request, request.mapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnSignConfirmed(Sign("127"));
        var directions = Directions("127", request);
        var view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f, null, roomDirections: directions);
        Assert.AreEqual(IndoorHudState.Scanning, view.State, "Scanning never builds the guide arrow");
        Assert.AreEqual("Room VEC 127 detected · Floor 1", view.TitleOverride);
        Assert.AreEqual(directions, view.DetailOverride);

        // Without directions the existing sign copy stays.
        view = LiveDoorHud.Compose(session, Vector3.zero, Vector3.forward, 0f, null);
        Assert.AreNotEqual(directions, view.DetailOverride);
    }
}
