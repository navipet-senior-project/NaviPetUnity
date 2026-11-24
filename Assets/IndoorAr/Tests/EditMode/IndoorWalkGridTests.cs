using System.Collections.Generic;
using System.IO;
using IndoorAr;
using IndoorAr.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class IndoorWalkGridTests
{
    static readonly string Directory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors123.json"));
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
        Assert.IsTrue(hall.TryNextWaypoint(route, camera, 1f, out var target));
        Assert.AreEqual(route[1], target);
        // Past the corner the same route steers at the goal, and never back at a point already reached.
        Assert.IsTrue(hall.TryNextWaypoint(route, new Vector3(4.4f, 0f, .6f), 1f, out target));
        Assert.AreEqual(route[2], target);

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
        Assert.IsTrue(straight.RouteBlocked);
        Assert.AreEqual(0f, straight.ArrowYaw);
        Assert.AreEqual("Route unavailable", straight.TitleOverride);
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
        Assert.IsTrue(noConnector.RouteBlocked);
        var toStairs = LiveDoorHud.Compose(session, camera + Vector3.up * 4f, Vector3.forward, 10f, route, "stairs",
            wrongFloorEvidence: true, routeGrid: hall);
        Assert.IsTrue(toStairs.FollowingRoute, "on the wrong floor a route to the stairs is followed");
        Assert.AreEqual("stairs", toStairs.FloorChange);
        Assert.IsFalse(LiveDoorHud.Compose(session, camera, Vector3.forward, 10f, route, "stairs").FollowingRoute,
            "on the right floor a route that ends at the stairs is not to the door");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrUncheckedRouteHidesArrow(bool uncheckedRoute)
    {
        var grid = Hall();
        var camera = new Vector3(.5f, 0f, 2.5f);
        var door = new Vector3(4.5f, 0f, 4.5f);
        Assert.IsFalse(grid.TryRoute(camera, door, out var route));
        var session = new IndoorArSession(new IndoorArRequest
        {
            frame = IndoorArContract.Frame, mapCode = "test", floor = "1", room = "119",
            position = new IndoorArVector3 { x = door.x, y = door.y, z = door.z }
        }, "test");
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        if (uncheckedRoute) route = new[] { camera, door };
        var view = LiveDoorHud.Compose(session, camera, Vector3.forward, 10f, route);
        Assert.IsTrue(view.RouteBlocked);
        Assert.IsFalse(view.FollowingRoute);
        Assert.AreEqual("Route unavailable", view.TitleOverride);
        Assert.AreEqual("No clear hallway route from your position.", view.DetailOverride);
        var host = new GameObject("Unreachable route HUD");
        try
        {
            var hud = host.AddComponent<IndoorHud>();
            hud.Initialize(host.AddComponent<Camera>());
            hud.Show(view);
            Assert.IsNull(host.transform.Find("HUD Canvas/Safe area/Guide arrow"));
            Assert.IsNotNull(host.transform.Find("HUD Canvas/Safe area/Here"));
        }
        finally { Object.DestroyImmediate(host); }
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
        Assert.IsTrue(grid.TryNextWaypoint(route, route[0], 1f, out var target));
        Assert.AreEqual(route[1], target);
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
        Assert.IsTrue(grid.TryNextWaypoint(route, Point(3.7f, .5f), 1f, out var target));
        Assert.AreEqual(route[1], target);
        Assert.IsTrue(grid.TryNextWaypoint(route, Point(4.4f, .6f), 1f, out target));
        Assert.AreEqual(route[2], target);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ShippedGridSteeringTargetsHaveCameraClearance(int floor)
    {
        var grid = IndoorWalkGrid.ForFloor(Scan, floor);
        var camera = new Vector3(11.375f, grid.FloorY + 1.5f, 21.5025f);
        if (floor == 2)
        {
            Assert.IsTrue(grid.TrySnap(Door("VEC201"), out var startX, out var startZ));
            camera = grid.CenterOf(startX, startZ) + new Vector3(.05f, 1.5f, .05f);
        }
        Assert.IsTrue(grid.TryRoute(camera, Door(floor == 1 ? "VEC136" : "VEC227"), out var route));
        Assert.IsTrue(grid.TrySnap(camera, out var x, out var z));
        if (floor == 1)
        {
            Assert.AreEqual(178, x);
            Assert.AreEqual(85, z);
            Assert.AreEqual(2, route.Length);
            var crossesBlockedCell = false;
            for (var i = 0; i <= 10000; i++)
            {
                var point = Vector3.Lerp(camera, route[1], i / 10000f);
                crossesBlockedCell |= grid.ColumnOf(point.x) == 178 && grid.RowOf(point.z) == 84;
            }
            Assert.IsTrue(crossesBlockedCell);
            Assert.IsFalse(grid.IsWalkable(178, 84));
        }
        if (floor == 1)
        {
            Assert.IsFalse(grid.TryNextWaypoint(route, camera, 1f, out _));
            return;
        }
        Assert.IsTrue(grid.TryNextWaypoint(route, camera, 1f, out var target));
        for (var i = 0; i <= 1000; i++)
        {
            var point = Vector3.Lerp(camera, target, i / 1000f);
            Assert.IsTrue(grid.IsWalkable(grid.ColumnOf(point.x), grid.RowOf(point.z)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnreachableFinalAndIntermediateTargetsInvalidateGuidance(bool intermediate)
    {
        var grid = Hall();
        Assert.IsTrue(grid.TryRoute(new Vector3(.5f, 0f, .5f), new Vector3(4.5f, 0f, 4.5f), out var route));
        if (!intermediate) route = new[] { route[0], route[route.Length - 1] };
        var camera = new Vector3(.5f, 1.5f, 2.5f);
        Assert.IsTrue(grid.IsWalkable(grid.ColumnOf(camera.x), grid.RowOf(camera.z)));
        Assert.IsFalse(grid.TryNextWaypoint(route, camera, 1f, out _));
        var request = new IndoorArRequest
        {
            schemaVersion = 1, room = "119", floor = "1", mapCode = "test", frame = "multiset-mapset",
            position = new IndoorArVector3 { x = 4.5f, z = 4.5f }
        };
        var session = new IndoorArSession(request, request.mapCode);
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnLocalized();
        var view = LiveDoorHud.Compose(session, camera, Vector3.forward, 10f, route, routeGrid: grid);
        Assert.AreEqual(IndoorHudState.Straight, view.State);
        Assert.IsTrue(view.RouteBlocked);
        Assert.IsFalse(view.FollowingRoute);
        Assert.AreEqual(0f, view.ArrowYaw);
        Assert.AreEqual("Route unavailable", view.TitleOverride);

        view = LiveDoorHud.Compose(session, route[0], Vector3.forward, 10f, route);
        Assert.AreEqual(IndoorHudState.Straight, view.State);
        Assert.IsTrue(view.RouteBlocked);
        Assert.IsFalse(view.FollowingRoute);
    }

    [Test]
    public void BlockedWaypointAheadNeverFallsBackToOneBehindTheUser()
    {
        var grid = Corner(
            "11111",
            "00001",
            "00001",
            "00000",
            "00001");
        var route = new[] { new Vector3(.5f, 0f, .5f), new Vector3(4.5f, 0f, .5f), new Vector3(4.5f, 0f, 4.5f) };
        // Past the corner on the way to the goal, which a blocked cell cuts off: the corner is clear but behind.
        Assert.IsFalse(grid.TryNextWaypoint(route, new Vector3(4.5f, 1.5f, 2.5f), 1f, out _));
        Assert.IsFalse(grid.TryNextWaypoint(route, new Vector3(4.5f, 1.5f, 1.5f), 1f, out _));
        // Still on the first leg, the corner is ahead and is chosen.
        Assert.IsTrue(grid.TryNextWaypoint(route, new Vector3(2.5f, 1.5f, .5f), 1f, out var target));
        Assert.AreEqual(route[1], target);
    }

    [Test]
    public void CameraInABlockedCellSteersFromTheNearestWalkableCell()
    {
        var hall = Hall();
        hall.TryRoute(new Vector3(.5f, 0f, .5f), new Vector3(4.5f, 0f, 4.5f), out var route);
        var camera = new Vector3(1.5f, 1.5f, 1.2f);
        Assert.IsFalse(hall.IsWalkable(hall.ColumnOf(camera.x), hall.RowOf(camera.z)));
        Assert.IsTrue(hall.TryNextWaypoint(route, camera, 1f, out var target));
        Assert.AreEqual(route[1], target);
        Assert.IsFalse(hall.TryNextWaypoint(route, new Vector3(-9f, 1.5f, .5f), 1f, out _), "off the scan stays unrouted");
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
        foreach (var (_, type, at) in connectors)
            reachable += grid.TryRoute(Door(rooms[0]), at, out _) ? type + " " : "- ";
        TestContext.WriteLine($"floor {floor} from {rooms[0]}: {reachable}");
        foreach (var room in rooms)
        {
            var any = false;
            foreach (var (_, _, at) in connectors) any |= grid.TryRoute(Door(room), at, out _);
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

    // 1 m cells, origin 0, floor 1. Two walkable blocks split by a blocked column at x = 3; the left block is bigger.
    static IndoorWalkFloor Split() => new IndoorWalkFloor
    {
        floor = 1, cell = 1f, width = 6, height = 3,
        rows = new[] { "111011", "111011", "111011" }
    };

    // Directory JSON with one marker per MapSet point, written in the exporter frame the file really uses.
    static string Markers(params (string id, float x, float z)[] markers)
    {
        var objects = "";
        foreach (var (id, x, z) in markers)
        {
            var p = Quaternion.Inverse(Quaternion.Euler(0f, IndoorMapFrame.YawDeg, 0f)) * (new Vector3(x, 0f, z) - IndoorMapFrame.Offset);
            objects += (objects == "" ? "" : ",") + "{\"id\":\"" + id + "\",\"type\":\"door\",\"floorId\":\"floor_1\"," +
                "\"position\":{\"x\":" + p.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ",\"y\":0,\"z\":" + p.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "},\"rotationY\":0}";
        }
        return "{\"buildingId\":\"T\",\"objects\":[" + objects + "]}";
    }

    [Test]
    public void ExportGateFlagsAMarkerThatSnapsOntoAnIslandOrOffTheFloor()
    {
        var data = new IndoorWalkGridData { floors = new[] { Split() } };
        var directory = Markers(("MAIN", .5f, 1.5f), ("ISLAND", 5.5f, 1.5f), ("OFF", 40f, 40f));
        var flagged = IndoorWalkGridExport.OffRegionMarkers(data, directory);
        CollectionAssert.AreEquivalent(new[] { "ISLAND", "OFF" }, flagged);
        LogAssert.Expect(LogType.Warning, "[WalkGrid] Markers off their floor's main walk region: ISLAND, OFF.");
        IndoorWalkGridExport.WarnOffRegionMarkers(data, directory);
    }

    [Test]
    public void DiagonalOnlyContactDoesNotJoinARegionBecauseRoutingCannotCutCorners()
    {
        var floor = Split();
        floor.width = 4;
        floor.rows = new[] { "1100", "1100", "0011" };
        var data = new IndoorWalkGridData { floors = new[] { floor } };
        CollectionAssert.AreEquivalent(new[] { "SMALL" },
            IndoorWalkGridExport.OffRegionMarkers(data, Markers(("BIG", .5f, 1.5f), ("SMALL", 3.5f, 2.5f))));
        Assert.IsFalse(IndoorWalkGrid.ForFloor(JsonUtility.ToJson(data), 1).TryRoute(
            new Vector3(.5f, 0f, 1.5f), new Vector3(3.5f, 0f, 2.5f), out _));
    }

    [Test]
    public void DoorwayOverrideJoinsTheIslandAndTheGateClears()
    {
        var floor = Split();
        var data = new IndoorWalkGridData { floors = new[] { floor } };
        var directory = Markers(("MAIN", .5f, 1.5f), ("ISLAND", 5.5f, 1.5f));
        Assert.IsFalse(IndoorWalkGrid.ForFloor(JsonUtility.ToJson(data), 1).TryRoute(
            new Vector3(.5f, 0f, 1.5f), new Vector3(5.5f, 0f, 1.5f), out _), "blocked doorway");
        // A doorway one cell wide at row z = 1, and an override for another floor that must not touch this one.
        IndoorWalkGridExport.ApplyOverrides(floor, new[]
        {
            new IndoorWalkGridExport.DoorwayOverride(1, 3.4f, 1.4f, 3.6f, 1.6f, "test doorway"),
            new IndoorWalkGridExport.DoorwayOverride(2, 0f, 0f, 9f, 9f, "other floor"),
        });
        CollectionAssert.AreEqual(new[] { "111011", "111111", "111011" }, floor.rows, "only the doorway cell changed");
        Assert.IsEmpty(IndoorWalkGridExport.OffRegionMarkers(data, directory));
        Assert.IsTrue(IndoorWalkGrid.ForFloor(JsonUtility.ToJson(data), 1).TryRoute(
            new Vector3(.5f, 0f, 1.5f), new Vector3(5.5f, 0f, 1.5f), out _));
    }

    [Test]
    public void ShippedGridFlagsStairsAAndEntranceCMarkers()
    {
        var data = JsonUtility.FromJson<IndoorWalkGridData>(Scan);
        CollectionAssert.AreEquivalent(new[] { "VECstairsF2A", "VECentr1C" },
            IndoorWalkGridExport.OffRegionMarkers(data, Directory));
    }

    // ---- Hint walk simulation: REAL floor-2 directory, walk grid and floor heights; MOCKED barometer evidence and server.
    // It shows how the Y band relates to a walk on the scanned floor. It is not proof of MultiSet server or device
    // behavior: the stand-in server below is a pure Y filter, which is the documented hintFloorHeight rule, nothing more.
    const float StandingEyeM = 1.5f, SeatedEyeM = 1.0f, WalkStepM = 0.7f;

    static IndoorArRequest HostMoved(float deltaHpa) => new IndoorArRequest
    {
        schemaVersion = 2, room = "215", doorKey = "VEC215", floor = "2", mapCode = "MSET_X", frame = "multiset-mapset",
        floorEvidenceVersion = 1, startFloor = "1", startFloorAgeS = 60f,
        pressureSinceStart = new IndoorArPressureSummary { settled = true, moved = true, sampleAgeS = 1f, deltaHpa = deltaHpa, deltaValid = true },
    };

    static List<Vector3> Walk(Vector3[] route)
    {
        var points = new List<Vector3> { route[0] };
        var carry = 0f;
        for (var i = 1; i < route.Length; i++)
        {
            var leg = Vector3.Distance(route[i - 1], route[i]);
            for (var at = WalkStepM - carry; at <= leg; at += WalkStepM)
                points.Add(Vector3.Lerp(route[i - 1], route[i], at / leg));
            carry = (carry + leg) % WalkStepM;
        }
        points.Add(route[route.Length - 1]);
        return points;
    }

    // 1 m squares, north up. S start, G pin, * walked, . scanned walkable.
    static string Sketch(IndoorWalkGrid grid, List<Vector3> walk)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in walk)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
        }
        minX = Mathf.Floor(minX) - 2; minZ = Mathf.Floor(minZ) - 2; maxX = Mathf.Ceil(maxX) + 2; maxZ = Mathf.Ceil(maxZ) + 2;
        var sb = new System.Text.StringBuilder();
        for (var z = maxZ; z >= minZ; z--)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var c = new Vector3(x + .5f, grid.FloorY, z + .5f);
                bool Within(Vector3 p) => Mathf.Abs(p.x - c.x) <= .5f && Mathf.Abs(p.z - c.z) <= .5f;
                sb.Append(Within(walk[0]) ? 'S' : Within(walk[walk.Count - 1]) ? 'G' : walk.Exists(Within) ? '*' : grid.IsWalkableAt(c) ? '.' : ' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    [Test]
    public void WalkFromTheFloor2ArrivalToRoom215StaysInsideThePressureHintBand()
    {
        var grids = IndoorWalkGrid.LoadFloors(Scan);
        var heights = new List<(int Floor, float Y)>();
        foreach (var entry in grids) heights.Add((entry.Key, entry.Value.FloorY));
        var floor2 = grids[2];

        // Start: the first floor-2 stairs or elevator that reaches the room. A real directory point, not an invented one.
        var goal = Door("VEC215");
        Vector3[] route = null;
        foreach (var (_, _, at) in IndoorRoomIdentityMatcher.FloorConnectors(Directory, 2))
            if (floor2.TryRoute(at, goal, out route)) break;
        Assert.IsNotNull(route, "a floor-2 connector reaches VEC215 on the scan");
        var walk = Walk(route);
        TestContext.WriteLine($"walk {IndoorWalkGrid.LengthM(route):0.0} m, {walk.Count} steps of {WalkStepM} m, floor 2 Y {floor2.FloorY}\n{Sketch(floor2, walk)}");

        // Mocked barometer: confirmed floor 1, then one storey of pressure drop before launch.
        var session = new IndoorArSession(HostMoved(-0.47f), "MSET_X");
        var floors = session.PressureCandidateFloors(heights);
        Assert.AreEqual((2, 2), floors);
        var yOf = new Dictionary<int, float>();
        foreach (var (floor, y) in heights) yOf[floor] = y;
        var hint = new MultiSetHint(floors.Value.Min, floors.Value.Max, yOf[floors.Value.Min], yOf[floors.Value.Max]);
        TestContext.WriteLine($"hint band Y [{hint.MinY:0.00}, {hint.MaxY:0.00}]");

        // Stand-in server: keeps frames whose Y is in the band. Camera or floor height, standing or seated, all pass.
        bool Passes(float y) => y >= hint.MinY && y <= hint.MaxY;
        var surface = floor2.FloorY; // every step is on this floor, so its Y is the same along the walk
        Assert.IsTrue(Passes(surface), "floor surface");
        Assert.IsTrue(Passes(surface + StandingEyeM), "standing camera");
        Assert.IsTrue(Passes(surface + SeatedEyeM), "seated camera");

        // The same band keeps the neighbouring floors out, so a cross-floor look-alike is not searched.
        foreach (var (floor, y) in heights)
        {
            if (floor == 2) continue;
            Assert.IsFalse(Passes(y) || Passes(y + StandingEyeM) || Passes(y + SeatedEyeM), $"floor {floor} is outside the band");
        }

        // A wrong estimate is only a failed first hint: the retry carries no hint and the unhinted server accepts it.
        session.OnArSupport(true);
        session.OnAuthenticated();
        session.OnAttemptStarted();
        Assert.IsNull(session.PressureCandidateFloors(heights));
        Assert.AreEqual(0, session.HintFloor);
    }

    [Test]
    public void WithoutAUsableDeltaTheWalkIsLocalizedUnhinted()
    {
        var heights = new List<(int Floor, float Y)>();
        foreach (var entry in IndoorWalkGrid.LoadFloors(Scan)) heights.Add((entry.Key, entry.Value.FloorY));
        foreach (var delta in new[] { 0f, -0.2f, -0.67f, -3.5f })
            Assert.IsNull(new IndoorArSession(HostMoved(delta), "MSET_X").PressureCandidateFloors(heights), $"delta {delta}");
    }
}
