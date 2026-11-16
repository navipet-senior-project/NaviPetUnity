using System.IO;
using IndoorAr;
using NUnit.Framework;
using UnityEngine;

public class IndoorWalkGridTests
{
    static readonly string Directory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors1and2.json"));
    static readonly string Scan = Resources.Load<TextAsset>("IndoorWalkGrid").text;

    // 1 m cells, north is the last row. An L-shaped hallway with a wall between its two ends.
    static IndoorWalkGrid Corner(params string[] rows) => IndoorWalkGrid.ForFloor(JsonUtility.ToJson(new IndoorWalkGridData
    {
        floors = new[]
        {
            new IndoorWalkFloor { floor = 1, cell = 1f, width = rows[0].Length, height = rows.Length, rows = rows }
        }
    }), 1);

    static IndoorWalkGrid Hall() => Corner(
        "11111",
        "00001",
        "11101",
        "00001",
        "00001");

    static Vector3 Door(string room) => IndoorRoomIdentityMatcher.MatchSign(room, Directory).Landmark.DoorPositionInMap;

    [Test]
    public void RouteGoesAroundTheWallInsteadOfThroughIt()
    {
        var hall = Hall();
        Assert.IsTrue(hall.TryRoute(new Vector3(.5f, 0f, .5f), new Vector3(4.5f, 0f, 4.5f), out var route));
        Assert.AreEqual(3, route.Length, "start, the corner, the goal");
        Assert.AreEqual(new Vector3(4.5f, 0f, .5f), route[1]);
        Assert.AreEqual(8f, IndoorWalkGrid.LengthM(route), 1e-4f);
        for (var i = 1; i < route.Length; i++)
            for (var t = 0f; t <= 1f; t += .05f)
            {
                var p = Vector3.Lerp(route[i - 1], route[i], t);
                Assert.IsTrue(hall.IsWalkable(hall.ColumnOf(p.x), hall.RowOf(p.z)), $"leg {i} leaves the floor at {p}");
            }
    }

    [Test]
    public void NoRouteWhenTheFloorDoesNotConnectOrThePointIsOffIt()
    {
        var hall = Hall();
        Assert.IsFalse(hall.TryRoute(new Vector3(.5f, 0f, .5f), new Vector3(.5f, 0f, 2.5f), out _), "walled-off pocket");
        Assert.IsFalse(hall.TryRoute(new Vector3(-9f, 0f, .5f), new Vector3(4.5f, 0f, 4.5f), out _), "start is off the scan");
        Assert.IsNull(IndoorWalkGrid.ForFloor(JsonUtility.ToJson(new IndoorWalkGridData()), 1));
        Assert.IsNull(IndoorWalkGrid.ForFloor(null, 1));
    }

    [Test]
    public void ArrowSteersAtTheCornerNotTheDoor()
    {
        var hall = Hall();
        var camera = new Vector3(.5f, 0f, .5f);
        hall.TryRoute(camera, new Vector3(4.5f, 0f, 4.5f), out var route);
        Assert.AreEqual(route[1], hall.NextWaypoint(route, camera, 1f));
        // Past the corner the same route steers at the goal, and never back at a point already reached.
        Assert.AreEqual(route[2], hall.NextWaypoint(route, new Vector3(4.4f, 0f, .6f), 1f));

        IndoorArContract.TryParseRequest(
            "{\"schemaVersion\":1,\"room\":\"119\",\"doorKey\":\"VEC119\",\"floor\":\"1\"," +
            "\"position\":{\"x\":4.5,\"y\":0,\"z\":4.5},\"headingDeg\":0," +
            "\"mapCode\":\"MSET_XTBY7AM7ZK77\",\"frame\":\"multiset-mapset\"}", out var request);
        var session = new IndoorArSession(request, request.mapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        var straight = LiveDoorHud.Compose(session, camera, Vector3.forward, 10f);
        Assert.IsFalse(straight.FollowingRoute);
        Assert.AreEqual(45f, straight.ArrowYaw, 1e-3f, "no route: straight at the door, through the wall");
        var routed = LiveDoorHud.Compose(session, camera, Vector3.forward, 10f, route, routeGrid: hall);
        Assert.IsTrue(routed.FollowingRoute);
        Assert.AreEqual(90f, routed.ArrowYaw, 1e-3f, "route: along the hallway to the corner");
        Assert.AreEqual(8.0, routed.DistanceM, 1e-4);
        var upstairs = LiveDoorHud.Compose(session, camera + Vector3.up * 4f, Vector3.forward, 10f, route,
            floorUnknown: true);
        Assert.AreEqual(IndoorHudState.Localized, upstairs.State, "unverified height cannot produce a floor route");
        Assert.IsFalse(upstairs.FollowingRoute, "a route on this floor's grid is no help from another floor");
        var noConnector = LiveDoorHud.Compose(session, camera + Vector3.up * 4f, Vector3.forward, 10f,
            wrongFloorEvidence: true);
        Assert.IsTrue(noConnector.WrongFloor);
        Assert.IsFalse(noConnector.FollowingRoute, "no connector must not fall back to a direct arrow across floors");
        var toStairs = LiveDoorHud.Compose(session, camera + Vector3.up * 4f, Vector3.forward, 10f, route, "stairs",
            wrongFloorEvidence: true);
        Assert.IsTrue(toStairs.FollowingRoute, "on the wrong floor a route to the stairs is followed");
        Assert.AreEqual("stairs", toStairs.FloorChange);
        Assert.IsFalse(LiveDoorHud.Compose(session, camera, Vector3.forward, 10f, route, "stairs").FollowingRoute,
            "on the right floor a route that ends at the stairs is not to the door");
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void SmoothingKeepsBlockedDiagonalNeighbors(bool reverseX, bool reverseZ, bool otherNeighbor)
    {
        var sx = reverseX ? 1 : 0;
        var sz = reverseZ ? 1 : 0;
        var gx = 1 - sx;
        var gz = 1 - sz;
        var rows = new[] { new[] { '1', '1' }, new[] { '1', '1' } };
        rows[otherNeighbor ? sz : gz][otherNeighbor ? gx : sx] = '0';
        var grid = Corner(new string(rows[0]), new string(rows[1]));
        Assert.IsTrue(grid.TryRoute(grid.CenterOf(sx, sz), grid.CenterOf(gx, gz), out var route));
        Assert.AreEqual(3, route.Length);
        Assert.AreEqual(2f, IndoorWalkGrid.LengthM(route), 1e-4f);
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void SmoothingChecksEveryCrossedCell(bool reverseX, bool reverseZ, bool transpose)
    {
        var width = transpose ? 3 : 6;
        var height = transpose ? 6 : 3;
        Vector2Int Cell(int x, int z)
        {
            if (reverseX) x = 5 - x;
            if (reverseZ) z = 2 - z;
            return transpose ? new Vector2Int(z, x) : new Vector2Int(x, z);
        }
        var blocked = Cell(1, 1);
        var rows = new string[height];
        for (var z = 0; z < height; z++)
        {
            var row = new string('1', width).ToCharArray();
            if (z == blocked.y) row[blocked.x] = '0';
            rows[z] = new string(row);
        }
        var grid = Corner(rows);
        var start = Cell(0, 0);
        var goal = Cell(5, 2);
        Assert.IsTrue(grid.TryRoute(grid.CenterOf(start.x, start.y), grid.CenterOf(goal.x, goal.y), out var route));
        for (var i = 1; i < route.Length; i++)
            for (var sample = 0; sample <= 1000; sample++)
            {
                var point = Vector3.Lerp(route[i - 1], route[i], sample / 1000f);
                Assert.IsTrue(grid.IsWalkable(grid.ColumnOf(point.x), grid.RowOf(point.z)), point.ToString());
            }
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void LookAheadKeepsTheCornerUntilTheNextLegIsClear(bool reverseX, bool reverseZ, bool transpose)
    {
        Vector3 Point(float x, float z)
        {
            if (reverseX) x = 5f - x;
            if (reverseZ) z = 5f - z;
            return transpose ? new Vector3(z, 0f, x) : new Vector3(x, 0f, z);
        }
        var cells = new char[5][];
        for (var z = 0; z < 5; z++) cells[z] = new char[5];
        for (var z = 0; z < 5; z++)
            for (var x = 0; x < 5; x++)
            {
                var point = Point(x + .5f, z + .5f);
                cells[Mathf.FloorToInt(point.z)][Mathf.FloorToInt(point.x)] = z == 0 || x == 4 ? '1' : '0';
            }
        var rows = new string[5];
        for (var z = 0; z < 5; z++) rows[z] = new string(cells[z]);
        var grid = Corner(rows);
        var route = new[] { Point(.5f, .5f), Point(4.5f, .5f), Point(4.5f, 4.5f) };
        Assert.AreEqual(route[1], grid.NextWaypoint(route, Point(3.7f, .5f), 1f));
        Assert.AreEqual(route[2], grid.NextWaypoint(route, Point(4.4f, .6f), 1f));
    }

    // Every mapped door on a floor reaches stairs or an elevator on the scan, so a wrong-floor user gets a route.
    [TestCase(1, new[] { "VEC119", "VEC120", "VEC121", "VEC124", "VEC127", "VEC128", "VEC132", "VEC134", "VEC136" })]
    [TestCase(2, new[] { "VEC201", "VEC202", "VEC215", "VEC217", "VEC218", "VEC220", "VEC224", "VEC227" })]
    public void ScannedHallwayReachesAFloorChangeFromEveryDoor(int floor, string[] rooms)
    {
        var grid = IndoorWalkGrid.ForFloor(Scan, floor);
        var connectors = IndoorRoomIdentityMatcher.FloorConnectors(Directory, floor);
        Assert.AreEqual(4, connectors.Count, "two stairs and two elevators per floor");
        var reachable = "";
        foreach (var (type, at) in connectors)
            reachable += grid.TryRoute(Door(rooms[0]), at, out _) ? type + " " : "- ";
        TestContext.WriteLine($"floor {floor} from {rooms[0]}: {reachable}");
        foreach (var room in rooms)
        {
            var any = false;
            foreach (var (_, at) in connectors) any |= grid.TryRoute(Door(room), at, out _);
            Assert.IsTrue(any, room);
        }
    }

    // What the scan covers today. A door that stops connecting means the scan or the MapSet alignment changed.
    [TestCase(1, new[] { "VEC119", "VEC120", "VEC121", "VEC124", "VEC127", "VEC128", "VEC132", "VEC134", "VEC136" })]
    [TestCase(2, new[] { "VEC201", "VEC202", "VEC215", "VEC217", "VEC218", "VEC220", "VEC224", "VEC227" })]
    public void ScannedHallwayConnectsEveryMappedDoorOnAFloor(int floor, string[] rooms)
    {
        var grid = IndoorWalkGrid.ForFloor(Scan, floor);
        Assert.IsNotNull(grid);
        foreach (var from in rooms)
            foreach (var to in rooms)
                if (from != to) Assert.IsTrue(grid.TryRoute(Door(from), Door(to), out _), $"{from} to {to}");
    }

    [Test]
    public void HallwayRouteIsLongerThanTheStraightLineAndStaysOnTheFloor()
    {
        var grid = IndoorWalkGrid.ForFloor(Scan, 1);
        var from = Door("VEC134");
        var to = Door("VEC127");
        Assert.IsTrue(grid.TryRoute(from, to, out var route));
        Assert.Greater(route.Length, 2, "VEC 127 is around a corner from VEC 134");
        Assert.Greater(IndoorWalkGrid.LengthM(route), new Vector2(to.x - from.x, to.z - from.z).magnitude);
        for (var i = 1; i < route.Length; i++)
        {
            var steps = Mathf.CeilToInt(Vector3.Distance(route[i - 1], route[i]) / .1f);
            for (var s = 0; s <= steps; s++)
            {
                var p = Vector3.Lerp(route[i - 1], route[i], s / (float)steps);
                Assert.IsTrue(grid.TrySnap(p, out var x, out var z)
                    && Vector3.Distance(grid.CenterOf(x, z), new Vector3(p.x, grid.FloorY, p.z)) < .3f,
                    $"leg {i} leaves the scanned floor");
            }
        }
        Assert.IsFalse(grid.TryRoute(new Vector3(-30f, 0f, 5f), to, out _), "a point outside the scanned hallway has no route");
    }
}
