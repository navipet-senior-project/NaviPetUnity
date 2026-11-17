using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IndoorAr.Editor
{
    /// <summary>
    /// Rasterizes the building scan into Resources/IndoorWalkGrid.json. A cell is walkable when the scan has a floor
    /// surface at floor level under it and nothing in the body band above it.
    /// Re-run after the scan or the MapSet alignment changes.
    /// </summary>
    public static class IndoorWalkGridExport
    {
        public const string ScanPath = "Assets/building scans/floor 1 + 2.glb";
        public const string GridPath = "Assets/IndoorAr/Resources/IndoorWalkGrid.json";
        const string UpperScanPath = "Assets/building scans/floor 3 + 4.glb";
        const string DirectoryPath = "Assets/IndoorNavigation/Data/vec_floors123.json";
        const float Cell = .25f;
        const float FloorToleranceM = .35f;
        const float BodyLowM = .4f;
        const float BodyHighM = 1.4f;
        // shortcut: floor heights read off the door rows (vec_destination_points), re-check if a floor is added.
        static readonly (int floor, float y)[] LowerFloors = { (1, -1.9f), (2, 2.3f) };
        static readonly (int floor, float y)[] UpperFloors = { (3, 6f) };

        [MenuItem("Indoor AR/Rebuild Walk Grid From Scan")]
        public static void Export()
        {
            var data = Build();
            if (data != null)
            {
                File.WriteAllText(GridPath, JsonUtility.ToJson(data, true) + "\n");
                AssetDatabase.ImportAsset(GridPath);
                Report(File.ReadAllText(GridPath));
            }
            if (Application.isBatchMode) EditorApplication.Exit(data != null ? 0 : 1);
        }

        public static IndoorWalkGridData Build()
        {
            var floors = new List<IndoorWalkFloor>();
            // SampleScene places this scan relative to BuildingOrigin, in the same frame as the catalog.
            // Composing the floor 1 + 2 instance's scene pose with IndoorMapFrame gives identity.
            var upperPosition = IndoorMapFrame.ToMapSet(new Vector3(23.693075f, 5.62f, 21.074951f));
            var upperRotation = Quaternion.Euler(0f, IndoorMapFrame.HeadingToMapSet(2.671f), 0f);
            if (!AddFloors(floors, ScanPath, Vector3.zero, Quaternion.identity, LowerFloors)
                || !AddFloors(floors, UpperScanPath, upperPosition, upperRotation, UpperFloors)) return null;
            return new IndoorWalkGridData { floors = floors.ToArray() };
        }

        static bool AddFloors(List<IndoorWalkFloor> output, string path, Vector3 position, Quaternion rotation,
            (int floor, float y)[] floors)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[WalkGrid] {path} did not import as a GameObject.");
                return false;
            }
            var scan = Object.Instantiate(prefab, position, rotation);
            try
            {
                var bounds = new Bounds();
                var any = false;
                foreach (var filter in scan.GetComponentsInChildren<MeshFilter>())
                {
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    var b = filter.GetComponent<Renderer>().bounds;
                    if (any) bounds.Encapsulate(b);
                    else bounds = b;
                    any = true;
                }
                Physics.SyncTransforms();
                foreach (var (floor, y) in floors)
                {
                    var grid = Rasterize(floor, y, bounds);
                    if (grid == null)
                    {
                        Debug.LogError($"[WalkGrid] No walkable cells on floor {floor} of {path}.");
                        return false;
                    }
                    output.Add(grid);
                }
                return true;
            }
            finally
            {
                Object.DestroyImmediate(scan);
            }
        }

        static IndoorWalkFloor Rasterize(int floor, float nominalY, Bounds bounds)
        {
            var originX = Mathf.Floor(bounds.min.x / Cell) * Cell;
            var originZ = Mathf.Floor(bounds.min.z / Cell) * Cell;
            var width = Mathf.CeilToInt((bounds.max.x - originX) / Cell);
            var height = Mathf.CeilToInt((bounds.max.z - originZ) / Cell);
            var rows = new string[height];
            var heights = new List<float>();
            var body = new Vector3(Cell * .5f, (BodyHighM - BodyLowM) * .5f, Cell * .5f);
            for (var z = 0; z < height; z++)
            {
                var row = new char[width];
                for (var x = 0; x < width; x++)
                {
                    var at = new Vector3(originX + (x + .5f) * Cell, nominalY + 1f, originZ + (z + .5f) * Cell);
                    var free = Physics.Raycast(at, Vector3.down, out var hit, 1f + FloorToleranceM)
                        && Mathf.Abs(hit.point.y - nominalY) <= FloorToleranceM
                        && !Physics.CheckBox(hit.point + Vector3.up * (BodyLowM + BodyHighM) * .5f, body);
                    if (free) heights.Add(hit.point.y);
                    row[x] = free ? '1' : '0';
                }
                rows[z] = new string(row);
            }
            if (heights.Count == 0) return null;
            heights.Sort();
            return new IndoorWalkFloor
            {
                floor = floor,
                floorY = heights[heights.Count / 2],
                cell = Cell,
                originX = originX,
                originZ = originZ,
                width = width,
                height = height,
                rows = rows,
            };
        }

        /// <summary>Logs what the grid covers: which mapped doors sit on it and which pairs it connects.</summary>
        static void Report(string gridJson)
        {
            var directory = File.ReadAllText(DirectoryPath);
            foreach (var floorData in JsonUtility.FromJson<IndoorWalkGridData>(gridJson).floors)
            {
                var floor = floorData.floor;
                var grid = IndoorWalkGrid.ForFloor(gridJson, floor);
                var doors = new List<IndoorRoomLandmark>();
                for (var room = floor * 100; room < floor * 100 + 100; room++)
                {
                    var match = IndoorRoomIdentityMatcher.MatchSign("VEC" + room, directory);
                    if (match.Landmark != null) doors.Add(match.Landmark);
                }
                foreach (var door in doors)
                {
                    var reached = 0;
                    foreach (var other in doors)
                        if (other != door && grid.TryRoute(other.DoorPositionInMap, door.DoorPositionInMap, out _)) reached++;
                    Debug.Log($"[WalkGrid] floor={floor} door={door.Id} onGrid={grid.TrySnap(door.DoorPositionInMap, out _, out _)} routesFromOtherDoors={reached}/{doors.Count - 1}");
                }
            }
        }
    }
}
