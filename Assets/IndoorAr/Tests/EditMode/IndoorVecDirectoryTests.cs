using IndoorAr;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class IndoorVecDirectoryTests
{
    [Serializable]
    sealed class VecDirectory
    {
        public string buildingId;
        public List<Marker> objects;
    }

    [Serializable]
    sealed class Marker
    {
        public string id;
        public string type;
        public string floorId;
        public Vector3 position;
    }

    static readonly string Json = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors12345.json"));

    static readonly List<Marker> Markers = JsonUtility.FromJson<VecDirectory>(Json).objects;

    // Floor surface heights (exporter Y) the SampleScene markers sit on.
    static readonly float[] FloorSurfaceY = { 0.38f, 4.47f, 8.38f, 12.3f, 16.73f };

    static Marker Find(string id) => Markers.Single(m => m.id == id);

    [Test]
    public void EveryMarkerFloorIdMatchesItsHeight()
    {
        foreach (var marker in Markers)
        {
            var nearest = Enumerable.Range(0, FloorSurfaceY.Length)
                .OrderBy(i => Mathf.Abs(marker.position.y - FloorSurfaceY[i])).First() + 1;
            Assert.AreEqual($"floor_{nearest}", marker.floorId, marker.id);
        }
    }

    [TestCase("floor_1")]
    [TestCase("floor_2")]
    [TestCase("floor_3")]
    [TestCase("floor_4")]
    [TestCase("floor_5")]
    public void FloorHasDoorsTwoStairsTwoElevatorsAndTwoBathrooms(string floorId)
    {
        var onFloor = Markers.Where(m => m.floorId == floorId).ToList();
        Assert.That(onFloor.Count(m => m.type == "door"), Is.GreaterThan(0));
        Assert.AreEqual(2, onFloor.Count(m => m.type == "stairs"));
        Assert.AreEqual(2, onFloor.Count(m => m.type == "elevator"));
        Assert.AreEqual(2, onFloor.Count(m => m.type == "bathroom"));
    }

    [Test]
    public void MarkerIdsAreUnique()
    {
        Assert.AreEqual(Markers.Count, Markers.Select(m => m.id).Distinct().Count());
    }

    [TestCase("VECelev5A")]
    [TestCase("VECelev5B")]
    public void FloorFiveElevatorsAreTaggedFloorFive(string id)
    {
        Assert.AreEqual("floor_5", Find(id).floorId);
        Assert.That(IndoorRoomIdentityMatcher.FloorConnectors(Json, 5).Select(c => c.Id), Does.Contain(id));
        Assert.That(IndoorRoomIdentityMatcher.FloorConnectors(Json, 4).Select(c => c.Id), Does.Not.Contain(id));
    }

    [TestCase("VEC330", 16.907f)]
    [TestCase("VEC331", 19.81f)]
    public void RoomsThreeThirtyAndThreeThirtyOneUseConfirmedPositions(string id, float x)
    {
        var marker = Find(id);
        Assert.AreEqual("floor_3", marker.floorId);
        Assert.AreEqual(x, marker.position.x, 0.001f);
        Assert.AreEqual(8.38f, marker.position.y, 0.001f);
        Assert.AreEqual(21.53f, marker.position.z, 0.001f);
    }

    [Test]
    public void SignReadsRoomFiveOhOneNotFiveTen()
    {
        var match = IndoorRoomIdentityMatcher.MatchSign("VEC 501", Json);
        Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
        Assert.AreEqual("VEC501", match.Landmark.Id);
        Assert.AreEqual(5, match.Landmark.Floor);

        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch, IndoorRoomIdentityMatcher.MatchSign("VEC 510", Json).Status);
    }

    [TestCase("119", "floor_1")]
    [TestCase("VEC 215", "floor_2")]
    [TestCase("VEC 330", "floor_3")]
    [TestCase("402", "floor_4")]
    [TestCase("Room 501", "floor_5")]
    public void MatchesRoomOnEachFloor(string text, string floorId)
    {
        var match = IndoorRoomIdentityMatcher.Match(text, "VEC", floorId, Json);
        Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
        Assert.AreEqual(floorId, match.Landmark.FloorId);
    }
}
