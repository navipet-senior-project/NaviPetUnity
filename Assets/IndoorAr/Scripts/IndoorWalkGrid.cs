using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    [Serializable]
    public class IndoorWalkFloor
    {
        public int floor;
        public float floorY;
        public float cell;
        public float originX;
        public float originZ;
        public int width;
        public int height;
        /// <summary>One string per z row, '1' walkable, '0' not. Built from the scan mesh by IndoorWalkGridExport.</summary>
        public string[] rows;
    }

    [Serializable]
    public class IndoorWalkGridData
    {
        public IndoorWalkFloor[] floors;
    }

    /// <summary>
    /// A provisional scan-derived route grid in the MapSet frame, not a navigation graph. Routes are only as good
    /// as the scan: a doorway or hallway the scan missed has no walkable cells, so TryRoute fails instead of guessing.
    /// </summary>
    public sealed class IndoorWalkGrid
    {
        public const float SnapRadiusM = 1.6f;
        readonly IndoorWalkFloor _floor;

        IndoorWalkGrid(IndoorWalkFloor floor) { _floor = floor; }

        public float FloorY => _floor.floorY;

        public static IndoorWalkGrid ForFloor(string json, int floor)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var data = JsonUtility.FromJson<IndoorWalkGridData>(json);
            if (data?.floors == null) return null;
            foreach (var f in data.floors)
                if (f.floor == floor && f.rows != null && f.rows.Length == f.height) return new IndoorWalkGrid(f);
            return null;
        }

        public bool IsWalkable(int x, int z) =>
            x >= 0 && z >= 0 && x < _floor.width && z < _floor.height && _floor.rows[z][x] == '1';

        public int ColumnOf(float worldX) => Mathf.FloorToInt((worldX - _floor.originX) / _floor.cell);
        public int RowOf(float worldZ) => Mathf.FloorToInt((worldZ - _floor.originZ) / _floor.cell);

        public Vector3 CenterOf(int x, int z) => new Vector3(
            _floor.originX + (x + .5f) * _floor.cell, _floor.floorY, _floor.originZ + (z + .5f) * _floor.cell);

        /// <summary>Nearest walkable cell within the snap radius, or false when the point is not near the scanned floor.</summary>
        public bool TrySnap(Vector3 world, out int cx, out int cz)
        {
            var x0 = ColumnOf(world.x);
            var z0 = RowOf(world.z);
            var reach = Mathf.CeilToInt(SnapRadiusM / _floor.cell);
            var best = float.MaxValue;
            cx = cz = -1;
            for (var dz = -reach; dz <= reach; dz++)
                for (var dx = -reach; dx <= reach; dx++)
                {
                    if (!IsWalkable(x0 + dx, z0 + dz)) continue;
                    var c = CenterOf(x0 + dx, z0 + dz);
                    var d = new Vector2(c.x - world.x, c.z - world.z).sqrMagnitude;
                    if (d >= best || d > SnapRadiusM * SnapRadiusM) continue;
                    best = d;
                    cx = x0 + dx;
                    cz = z0 + dz;
                }
            return cx >= 0;
        }

        /// <summary>
        /// A* over 8-connected walkable cells, no corner cutting, then straightened by line of sight.
        /// The first point is where the camera snapped onto the floor and the last is where the goal snapped.
        /// </summary>
        public bool TryRoute(Vector3 from, Vector3 to, out Vector3[] waypoints)
        {
            waypoints = null;
            if (!TrySnap(from, out var sx, out var sz) || !TrySnap(to, out var gx, out var gz)) return false;
            var cells = Search(sx, sz, gx, gz);
            if (cells == null) return false;
            var kept = new List<Vector3> { CenterOf(cells[0].x, cells[0].y) };
            var anchor = 0;
            for (var i = 2; i < cells.Count; i++)
            {
                if (LineIsClear(CenterOf(cells[anchor].x, cells[anchor].y), CenterOf(cells[i].x, cells[i].y))) continue;
                anchor = i - 1;
                kept.Add(CenterOf(cells[anchor].x, cells[anchor].y));
            }
            kept.Add(CenterOf(cells[cells.Count - 1].x, cells[cells.Count - 1].y));
            waypoints = kept.ToArray();
            return true;
        }

        List<Vector2Int> Search(int sx, int sz, int gx, int gz)
        {
            var w = _floor.width;
            var cost = new Dictionary<int, float> { [sz * w + sx] = 0f };
            var from = new Dictionary<int, int>();
            var open = new SortedSet<(float f, int id)> { (Heuristic(sx, sz, gx, gz), sz * w + sx) };
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                var (_, id) = open.Min;
                open.Remove(open.Min);
                var x = id % w;
                var z = id / w;
                if (x == gx && z == gz) return Rebuild(from, id);
                if (!closed.Add(id)) continue;
                for (var dz = -1; dz <= 1; dz++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var nx = x + dx;
                        var nz = z + dz;
                        if (!IsWalkable(nx, nz)) continue;
                        if (dx != 0 && dz != 0 && (!IsWalkable(x + dx, z) || !IsWalkable(x, z + dz))) continue;
                        var next = nz * w + nx;
                        var g = cost[id] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        if (cost.TryGetValue(next, out var known) && known <= g) continue;
                        cost[next] = g;
                        from[next] = id;
                        open.Add((g + Heuristic(nx, nz, gx, gz), next));
                    }
            }
            return null;
        }

        static float Heuristic(int x, int z, int gx, int gz) =>
            Mathf.Sqrt((x - gx) * (x - gx) + (z - gz) * (z - gz));

        List<Vector2Int> Rebuild(Dictionary<int, int> from, int id)
        {
            var path = new List<Vector2Int>();
            while (true)
            {
                path.Add(new Vector2Int(id % _floor.width, id / _floor.width));
                if (!from.TryGetValue(id, out id)) break;
            }
            path.Reverse();
            return path;
        }

        bool LineIsClear(Vector3 a, Vector3 b)
        {
            var x = ColumnOf(a.x);
            var z = RowOf(a.z);
            var gx = ColumnOf(b.x);
            var gz = RowOf(b.z);
            if (!IsWalkable(x, z) || !IsWalkable(gx, gz)) return false;
            var dx = (double)b.x - a.x;
            var dz = (double)b.z - a.z;
            var stepX = Math.Sign(dx);
            var stepZ = Math.Sign(dz);
            var deltaX = stepX == 0 ? double.PositiveInfinity : _floor.cell / Math.Abs(dx);
            var deltaZ = stepZ == 0 ? double.PositiveInfinity : _floor.cell / Math.Abs(dz);
            var nextX = stepX == 0 ? double.PositiveInfinity
                : (_floor.originX + (x + (stepX > 0 ? 1 : 0)) * (double)_floor.cell - a.x) / dx;
            var nextZ = stepZ == 0 ? double.PositiveInfinity
                : (_floor.originZ + (z + (stepZ > 0 ? 1 : 0)) * (double)_floor.cell - a.z) / dz;
            while (x != gx || z != gz)
            {
                if (Math.Abs(nextX - nextZ) < 1e-10)
                {
                    if (!IsWalkable(x + stepX, z) || !IsWalkable(x, z + stepZ)) return false;
                    x += stepX;
                    z += stepZ;
                    nextX += deltaX;
                    nextZ += deltaZ;
                }
                else if (nextX < nextZ)
                {
                    x += stepX;
                    nextX += deltaX;
                }
                else
                {
                    z += stepZ;
                    nextZ += deltaZ;
                }
                if (!IsWalkable(x, z)) return false;
            }
            return true;
        }

        public static float LengthM(Vector3[] route)
        {
            var meters = 0f;
            for (var i = 1; i < route.Length; i++)
                meters += new Vector2(route[i].x - route[i - 1].x, route[i].z - route[i - 1].z).magnitude;
            return meters;
        }

        /// <summary>
        /// The point to steer at: the first waypoint more than lookAhead metres from the camera, so the arrow does not
        /// flip as the user passes a corner point. The last waypoint when everything is nearer than that.
        /// </summary>
        public Vector3 NextWaypoint(Vector3[] route, Vector3 camera, float lookAhead)
        {
            for (var i = 1; i < route.Length - 1; i++)
                if (new Vector2(route[i].x - camera.x, route[i].z - camera.z).magnitude > lookAhead
                    || !LineIsClear(camera, route[i + 1])) return route[i];
            return route[route.Length - 1];
        }
    }
}
