using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndoorAr
{
    /// <summary>What a run of room-sign sightings says about where the map is, and how much to trust it.</summary>
    public struct IndoorSignFix
    {
        /// <summary>Enough separated signs agree with the walked path to steer by. Never an exact pose.</summary>
        public bool Usable;
        public int Signs;
        public float BaselineM;
        public float ResidualM;
        /// <summary>Worst-case heading error if every sighting was ReadRangeM off, the assumed limit.</summary>
        public float YawBoundDeg;
        /// <summary>World pose for the MapSet origin. Only meaningful when Signs is at least 2.</summary>
        public Vector3 Origin;
        public float YawDeg;
    }

    /// <summary>
    /// Coarse map alignment from room signs read one after another while AR tracks the walk between them.
    /// One sign names a room, which is an area and not a position, so it never produces a fix. Two or more signs
    /// give the map's heading from the direction walked between their doors, and its position to within the
    /// distance a sign is read from. Nothing here measures the sign itself: an exact pose still needs the sign's
    /// corners in the image and its surveyed geometry (IndoorSignPose).
    /// </summary>
    public sealed class IndoorSignTrail
    {
        // shortcut: none of these three were measured on a device; tune them from the "sign trail" log lines.
        /// <summary>Assumed distance from the door marker, out into the hallway, where a reader stands.</summary>
        public const float StandoffM = 1.5f;
        /// <summary>Assumed limit of how far a sighting is from that standing point.</summary>
        public const float ReadRangeM = 1.5f;
        /// <summary>Assumed phone height above the floor. Only places the map vertically, for the floor check.</summary>
        public const float CameraHeightM = 1.4f;
        /// <summary>Shorter than this and the heading bound is worse than 21 degrees, so the fix is not used.</summary>
        public const float MinBaselineM = 8f;

        readonly List<(string room, Vector3 standInMap, Vector3 cameraInWorld)> _sightings =
            new List<(string, Vector3, Vector3)>();

        public int Signs => _sightings.Count;

        public void Clear() => _sightings.Clear();

        /// <summary>Records where the AR camera was when this room's sign was confirmed. A room read again replaces its sighting.</summary>
        public void Add(IndoorRoomLandmark room, Vector3 cameraInWorld)
        {
            var intoRoom = Quaternion.Euler(0f, room.DoorHeadingInMapDeg, 0f) * Vector3.forward;
            _sightings.RemoveAll(s => s.room == room.Id);
            _sightings.Add((room.Id, room.DoorPositionInMap - intoRoom * StandoffM, cameraInWorld));
        }

        /// <summary>Least-squares yaw and offset that carry the standing points in the map onto the camera positions.</summary>
        public IndoorSignFix Solve()
        {
            var fix = new IndoorSignFix { Signs = _sightings.Count };
            if (_sightings.Count < 2) return fix;
            Vector3 mapMean = Vector3.zero, worldMean = Vector3.zero;
            foreach (var s in _sightings)
            {
                mapMean += s.standInMap;
                worldMean += s.cameraInWorld;
            }
            mapMean /= _sightings.Count;
            worldMean /= _sightings.Count;
            float dot = 0f, cross = 0f;
            foreach (var s in _sightings)
            {
                var a = s.standInMap - mapMean;
                var b = s.cameraInWorld - worldMean;
                dot += b.x * a.x + b.z * a.z;
                cross += b.x * a.z - b.z * a.x;
                foreach (var other in _sightings)
                    fix.BaselineM = Mathf.Max(fix.BaselineM, Flat(s.standInMap - other.standInMap));
            }
            fix.YawDeg = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, fix.YawDeg, 0f);
            fix.Origin = worldMean - rotation * mapMean;
            fix.Origin.y = worldMean.y - (mapMean.y + CameraHeightM);
            var squared = 0f;
            foreach (var s in _sightings)
            {
                var miss = Flat(fix.Origin + rotation * s.standInMap - s.cameraInWorld);
                squared += miss * miss;
            }
            fix.ResidualM = Mathf.Sqrt(squared / _sightings.Count);
            fix.YawBoundDeg = Mathf.Atan2(2f * ReadRangeM, fix.BaselineM) * Mathf.Rad2Deg;
            fix.Usable = fix.BaselineM >= MinBaselineM && fix.ResidualM <= ReadRangeM;
            return fix;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }

    /// <summary>
    /// Room signs first, MultiSet last. A localization request starts a hold instead of a MultiSet query; a usable
    /// sign fix during or after the hold poses the map and reports Localized. MultiSet is asked only when the hold
    /// ends without one, and its pose then wins: a later sign fix no longer moves the map.
    /// </summary>
    public sealed class SignFirstLocalizer : IIndoorArLocalizer
    {
        // shortcut: 15 s is a guess at how long reaching a second sign takes; tune it from device runs.
        public const float HoldS = 15f;

        readonly IIndoorArLocalizer _inner;
        readonly IndoorSignTrail _trail = new IndoorSignTrail();
        bool _open;
        bool _innerPosed;
        float _holdLeft;

        public SignFirstLocalizer(IIndoorArLocalizer inner)
        {
            _inner = inner;
            _inner.Localized += OnInnerLocalized;
        }

        public event Action Authenticated { add => _inner.Authenticated += value; remove => _inner.Authenticated -= value; }
        public event Action<bool> AuthFailed { add => _inner.AuthFailed += value; remove => _inner.AuthFailed -= value; }
        public event Action<int> AttemptStarted { add => _inner.AttemptStarted += value; remove => _inner.AttemptStarted -= value; }
        public event Action<float?> Localized;
        public event Action<int> NotLocalized { add => _inner.NotLocalized += value; remove => _inner.NotLocalized -= value; }

        public Transform MapSpace => _inner.MapSpace;
        public string MapCode => _inner.MapCode;
        /// <summary>The map is posed from signs: steer by it, but it is too coarse to show a door pin.</summary>
        public bool FromSigns { get; private set; }

        public void Authenticate() => _inner.Authenticate();

        public void Localize()
        {
            if (_open)
            {
                if (_holdLeft <= 0f) _inner.Localize(); // the session's retry, after the hold already ran out
                return;
            }
            // A new search after tracking loss: the AR world may have jumped, so earlier sightings no longer line up.
            if (FromSigns || _innerPosed) _trail.Clear();
            FromSigns = _innerPosed = false;
            _open = true;
            _holdLeft = HoldS;
        }

        /// <summary>
        /// A new session runs in a new AR world: the previous session's sightings, an unfinished hold and
        /// whichever pose won no longer apply. The runner keeps this object across warm launches.
        /// </summary>
        public void Reset()
        {
            _trail.Clear();
            _open = _innerPosed = false;
            FromSigns = false;
            _holdLeft = 0f;
        }

        public void Tick(float dt)
        {
            if (!_open || _holdLeft <= 0f) return;
            _holdLeft -= dt;
            if (_holdLeft <= 0f) _inner.Localize();
        }

        /// <summary>Feeds one confirmed sign. Returns the trail's fix so the caller can log how good it is.</summary>
        public IndoorSignFix Observe(IndoorRoomLandmark room, Vector3 cameraInWorld)
        {
            _trail.Add(room, cameraInWorld);
            var fix = _trail.Solve();
            if (!fix.Usable || _innerPosed) return fix;
            MapSpace.SetPositionAndRotation(fix.Origin, Quaternion.Euler(0f, fix.YawDeg, 0f));
            FromSigns = true;
            if (!_open) return fix;
            _open = false;
            Localized?.Invoke(null);
            return fix;
        }

        void OnInnerLocalized(float? confidence)
        {
            _innerPosed = true;
            FromSigns = false;
            _open = false;
            Localized?.Invoke(confidence);
        }
    }
}
