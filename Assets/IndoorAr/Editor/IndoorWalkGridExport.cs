using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IndoorAr.Editor
{
    /// <summary>
    /// Rasterizes the building scan into Resources/IndoorWalkGrid.json. A cell is walkable when the scan has a floor
    /// surface at floor level under it and nothing in the body band above it. The scan sits at identity, which is
    /// the MapSet origin, so the grid is in the MapSet frame. Re-run after the scan or the MapSet alignment changes.
    /// </summary>
    public static class IndoorWalkGridExport
    {
        public const string ScanPath = "Assets/building scans/floor 1 + 2.glb";
        public const string GridPath = "Assets/IndoorAr/Resources/IndoorWalkGrid.json";
        const string DirectoryPath = "Assets/IndoorNavigation/Data/vec_floors1and2.json";
        const float Cell = .25f;
        const float FloorToleranceM = .35f;
        const float BodyLowM = .4f;
        const float BodyHighM = 1.4f;
        // shortcut: floor heights read off the door rows (vec_destination_points), re-check if a floor is added.
        static readonly (int floor, float y)[] Floors = { (1, -1.9f), (2, 2.3f) };

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
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScanPath);
            if (prefab == null)
            {
                Debug.LogError($"[WalkGrid] {ScanPath} did not import as a GameObject.");
                return null;
            }
            var scan = Object.Instantiate(prefab); // identity transform = MapSet origin
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
                var floors = new List<IndoorWalkFloor>();
                foreach (var (floor, y) in Floors) floors.Add(Rasterize(floor, y, bounds));
                return new IndoorWalkGridData { floors = floors.ToArray() };
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
            heights.Sort();
            return new IndoorWalkFloor
            {
                floor = floor,
                floorY = heights.Count == 0 ? nominalY : heights[heights.Count / 2],
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
            foreach (var (floor, _) in Floors)
            {
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
