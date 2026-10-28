using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Proves the Supabase door export and the imported scan agree: the scan at
/// identity (the MapSet origin) has a floor just under each door point. It does
/// not prove the live MultiSet frame; only the device check does.
/// </summary>
public class IndoorArDoorMeshTests
{
    const string ScanPath = "Assets/building scans/floor 1 + 2.glb";
    const float MaxDropM = 0.5f;
    const float StartAboveM = 0.1f;

    // vec_destination_points, active doors, map_version 1, copied 2026-10-28.
    static readonly object[] Doors =
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

    GameObject _scan;

    [OneTimeSetUp]
    public void LoadScan()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScanPath);
        Assert.IsNotNull(prefab, $"{ScanPath} did not import as a GameObject. Is glTFast installed?");
        _scan = Object.Instantiate(prefab); // identity transform = MapSet origin
        foreach (var filter in _scan.GetComponentsInChildren<MeshFilter>())
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        Physics.SyncTransforms();
    }

    [OneTimeTearDown]
    public void Unload() => Object.DestroyImmediate(_scan);

    [TestCaseSource(nameof(Doors))]
    public void DoorSitsOnTheScannedFloor(string doorKey, float x, float y, float z)
    {
        var start = new Vector3(x, y + StartAboveM, z);
        Assert.IsTrue(
            Physics.Raycast(start, Vector3.down, out var hit, MaxDropM + StartAboveM),
            $"{doorKey}: no surface within {MaxDropM} m below ({x}, {y}, {z})");
        Debug.Log($"[IndoorAr] {doorKey} floor drop {y - hit.point.y:0.000} m");
    }
}
