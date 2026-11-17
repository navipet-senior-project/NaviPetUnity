using IndoorAr;
using NUnit.Framework;
using System.IO;
using UnityEngine;

public class IndoorMapFrameTests
{
    static readonly string VecDirectory = File.ReadAllText(
        Path.Combine(Application.dataPath, "IndoorNavigation/Data/vec_floors123.json"));

    // vec_destination_points, active doors, map_version 1: the same rows IndoorArDoorMeshTests copies.
    static readonly object[] BackendDoors =
    {
        new object[] { "VEC119", -5.7651f, -1.8186f, 22.6689f },
        new object[] { "VEC120", -8.5041f, -1.7956f, 22.9859f },
        new object[] { "VEC121", -22.0913f, -1.8136f, 24.4311f },
        new object[] { "VEC124", -21.8677f, -1.8286f, 27.4932f },
        new object[] { "VEC127", 0.4029f, -1.8616f, 30.2211f },
        new object[] { "VEC128", 23.6032f, -1.8326f, 22.9838f },
        new object[] { "VEC132", 21.425f, -1.7796f, 20.306f },
        new object[] { "VEC134", 16.0777f, -1.8346f, 20.806f },
        new object[] { "VEC136", 11.8828f, -1.7676f, 21.2616f },
        new object[] { "VEC201", -24.1111f, 2.3284f, 27.6193f },
        new object[] { "VEC202", -15.008f, 2.3284f, 26.7658f },
        new object[] { "VEC215", 8.2043f, 2.3284f, 28.7999f },
        new object[] { "VEC217", 14.2003f, 2.3284f, 23.8174f },
        new object[] { "VEC218", 21.0592f, 2.3284f, 23.1743f },
        new object[] { "VEC220", -3.2028f, 2.3284f, 22.7875f },
        new object[] { "VEC224", 9.5549f, 2.3284f, 21.513f },
        new object[] { "VEC227", 20.9229f, 2.3284f, 20.3065f },
    };

    [TestCaseSource(nameof(BackendDoors))]
    public void DirectoryDoorLandsOnTheBackendDoorPoint(string doorKey, float x, float y, float z)
    {
        var result = IndoorRoomIdentityMatcher.MatchSign(doorKey, VecDirectory);

        Assert.AreEqual(IndoorRoomMatchStatus.Matched, result.Status);
        Assert.Less(Vector3.Distance(new Vector3(x, y, z), result.Landmark.DoorPositionInMap), 0.01f, doorKey);
    }

    [Test]
    public void HeadingMatchesTheRequestHeadingAndPointsIntoTheRoom()
    {
        var vec119 = IndoorRoomIdentityMatcher.MatchSign("VEC 119", VecDirectory).Landmark;
        var vec128 = IndoorRoomIdentityMatcher.MatchSign("VEC 128", VecDirectory).Landmark;

        Assert.AreEqual(185.36f, vec119.DoorHeadingInMapDeg, 0.01f); // heading in the real VEC119 request
        Assert.AreEqual(5.36f, vec128.DoorHeadingInMapDeg, 0.01f);   // across the hallway, opposite way
    }

    [Test]
    public void HeadingWrapsIntoZeroTo360()
    {
        Assert.AreEqual(5.356265f, IndoorMapFrame.HeadingToMapSet(0f), 1e-4f);
        Assert.AreEqual(5.356265f, IndoorMapFrame.HeadingToMapSet(360f), 1e-3f);
        Assert.AreEqual(4.356265f, IndoorMapFrame.HeadingToMapSet(359f), 1e-3f);
    }
}
