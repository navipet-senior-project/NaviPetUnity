using IndoorAr;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
    public void FloorHasDoors(string floorId)
    {
        Assert.That(Markers.Any(m => m.floorId == floorId && m.type == "door"), Is.True);
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
    public void CorrectedSceneMarkersMatchDirectory()
    {
        var markerType = AssetDatabase.LoadAssetAtPath<MonoScript>(
            "Assets/IndoorNavigation/Scripts/IndoorMapMarker.cs").GetClass();
        var originType = AssetDatabase.LoadAssetAtPath<MonoScript>(
            "Assets/IndoorNavigation/Scripts/IndoorMapOrigin.cs").GetClass();
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        try
        {
            var roots = scene.GetRootGameObjects();
            var origins = roots.SelectMany(root => root.GetComponentsInChildren(originType, false))
                .Where(origin => origin.gameObject.activeInHierarchy)
                .Select(origin => origin.transform)
                .ToArray();
            Assert.That(origins, Is.Not.Empty);
            var sceneMarkers = roots.SelectMany(root => root.GetComponentsInChildren(markerType, true))
                .Select(marker => new SerializedObject(marker))
                .ToLookup(marker => marker.FindProperty("objectId").stringValue);

            foreach (var id in new[] { "VECelev5A", "VECelev5B", "VEC330", "VEC331", "VEC501" })
            {
                var exported = Find(id);
                Assert.That(sceneMarkers[id].Count(), Is.EqualTo(1), id);
                var marker = sceneMarkers[id].Single();
                Assert.AreEqual(exported.type, marker.FindProperty("objectType").stringValue, id);
                Assert.AreEqual(exported.floorId, marker.FindProperty("floorId").stringValue, id);
                var worldPosition = ((Component)marker.targetObject).transform.position;
                foreach (var origin in origins)
                {
                    var position = origin.InverseTransformPoint(worldPosition);
                    Assert.That(Vector3.Distance(exported.position, position), Is.LessThanOrEqualTo(0.001f),
                        $"{id} relative to {origin.name}");
                }
            }

            Assert.That(sceneMarkers["VEC510"], Is.Empty);
            Assert.That(Markers.Select(marker => marker.id), Does.Not.Contain("VEC510"));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void RoomFiveTenSignDoesNotMatch()
    {
        Assert.AreEqual(IndoorRoomMatchStatus.NoMatch, IndoorRoomIdentityMatcher.MatchSign("VEC 510", Json).Status);
    }

    [TestCase("ROOM 119", "VEC119", 1)]
    [TestCase("VEC 215", "VEC215", 2)]
    [TestCase("VEC 330", "VEC330", 3)]
    [TestCase("VEC 402", "VEC402", 4)]
    [TestCase("Room 501", "VEC501", 5)]
    public void MatchesRoomSignOnEachFloor(string text, string id, int floor)
    {
        var match = IndoorRoomIdentityMatcher.MatchSign(text, Json);
        Assert.AreEqual(IndoorRoomMatchStatus.Matched, match.Status);
        Assert.AreEqual(id, match.Landmark.Id);
        Assert.AreEqual(floor, match.Landmark.Floor);
    }
}
