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
        /// <summary>Worst-case heading error if every sighting was off by its assumed limit (ReadRangeM or SignOffsetM).</summary>
        public float YawBoundDeg;
        /// <summary>World pose for the MapSet origin. Only meaningful when Signs is at least 2.</summary>
        public Vector3 Origin;
        public float YawDeg;
        /// <summary>Posed from one located sign: YawBoundDeg is then that sign's estimated heading error, not a limit.</summary>
        public bool OneSign;
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
        // shortcut: no VEC sign was measured; replace both with per-sign values in IndoorSignCalibration.json.
        /// <summary>Assumed limit of how far a sign's text is from the point SignHeightM above its door marker.</summary>
        public const float SignOffsetM = 1f;
        /// <summary>Assumed height of a sign's text above the door marker. Only places the map vertically.</summary>
        public const float SignHeightM = 1.5f;

        readonly List<(string room, Vector3 inMap, Vector3 inWorld, float boundM)> _sightings =
            new List<(string, Vector3, Vector3, float)>();

        public int Signs => _sightings.Count;

        public void Clear() => _sightings.Clear();

        /// <summary>Records where the AR camera was when this room's sign was confirmed. A room read again replaces its sighting.</summary>
        public void Add(IndoorRoomLandmark room, Vector3 cameraInWorld)
        {
            // A sign located from its corners says more than where its reader is assumed to stand.
            if (_sightings.Exists(s => s.room == room.Id && s.boundM < ReadRangeM)) return;
            var intoRoom = Quaternion.Euler(0f, room.DoorHeadingInMapDeg, 0f) * Vector3.forward;
            Put(room.Id, room.DoorPositionInMap - intoRoom * StandoffM + Vector3.up * CameraHeightM, cameraInWorld, ReadRangeM);
        }

        /// <summary>Records where this room's sign text is in the AR world (IndoorSignTriangulator).</summary>
        public void AddSign(IndoorRoomLandmark room, Vector3 signInWorld) =>
            Put(room.Id, room.DoorPositionInMap + Vector3.up * SignHeightM, signInWorld, SignOffsetM);

        void Put(string room, Vector3 inMap, Vector3 inWorld, float boundM)
        {
            _sightings.RemoveAll(s => s.room == room);
            _sightings.Add((room, inMap, inWorld, boundM));
        }

        /// <summary>Least-squares yaw and offset that carry the sighted points in the map onto where AR saw them.</summary>
        public IndoorSignFix Solve()
        {
            var fix = new IndoorSignFix { Signs = _sightings.Count };
            if (_sightings.Count < 2) return fix;
            Vector3 mapMean = Vector3.zero, worldMean = Vector3.zero;
            var bound = 0f;
            foreach (var s in _sightings)
            {
                mapMean += s.inMap;
                worldMean += s.inWorld;
                bound = Mathf.Max(bound, s.boundM);
            }
            mapMean /= _sightings.Count;
            worldMean /= _sightings.Count;
            float dot = 0f, cross = 0f;
            foreach (var s in _sightings)
            {
                var a = s.inMap - mapMean;
                var b = s.inWorld - worldMean;
                dot += b.x * a.x + b.z * a.z;
                cross += b.x * a.z - b.z * a.x;
                foreach (var other in _sightings)
                    fix.BaselineM = Mathf.Max(fix.BaselineM, Flat(s.inMap - other.inMap));
            }
            fix.YawDeg = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, fix.YawDeg, 0f);
            fix.Origin = worldMean - rotation * mapMean;
            fix.Origin.y = worldMean.y - mapMean.y;
            var squared = 0f;
            foreach (var s in _sightings)
            {
                var miss = Flat(fix.Origin + rotation * s.inMap - s.inWorld);
                squared += miss * miss;
            }
            fix.ResidualM = Mathf.Sqrt(squared / _sightings.Count);
            fix.YawBoundDeg = Mathf.Atan2(2f * bound, fix.BaselineM) * Mathf.Rad2Deg;
            // The same 21 degree limit as MinBaselineM gives assumed readers; located signs reach it sooner.
            fix.Usable = fix.BaselineM * ReadRangeM >= MinBaselineM * bound && fix.ResidualM <= bound;
            return fix;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }

    /// <summary>
    /// Room signs first, MultiSet last, at two quality levels. A confirmed sign is room-recognized evidence: its trail
    /// and one-sign fixes are returned for logging and never move the map. Only an IndoorSignPose from a sign
    /// validated on this device is camera-pose-localized and may pose the map. With such a sign, authentication does
    /// not wait for MultiSet and a localization request starts a hold; MultiSet is asked only when the hold ends
    /// without a pose. Without one, MultiSet is asked at once, as in MultiSet mode. Once MultiSet poses the map its
    /// pose wins: a later sign pose no longer moves the map.
    /// </summary>
    public sealed class SignFirstLocalizer : IIndoorArLocalizer
    {
        // shortcut: 15 s is a guess at how long reaching a calibrated sign takes; tune it from device runs.
        public const float HoldS = 15f;
        // shortcut: untested limit on one sign's estimated heading error; tune it from the "sign fix" log lines.
        public const float MaxOneSignYawSigmaDeg = 20f;

        readonly IIndoorArLocalizer _inner;
        readonly IndoorSignCalibration _calibration;
        readonly IndoorSignTrail _trail = new IndoorSignTrail();
        readonly IndoorSignTriangulator _triangulator = new IndoorSignTriangulator();
        bool _open;
        bool _innerPosed;
        bool _innerReady;
        bool _innerWanted;
        bool _innerAuthFailed;
        float _holdLeft;

        public SignFirstLocalizer(IIndoorArLocalizer inner, IndoorSignCalibration calibration = null)
        {
            _inner = inner;
            _calibration = calibration ?? new IndoorSignCalibration();
            SignsCanPose = _calibration.CanPose(SystemInfo.deviceModel);
            _inner.Localized += OnInnerLocalized;
            _inner.Authenticated += OnInnerAuthenticated;
            _inner.AuthFailed += OnInnerAuthFailed;
        }

        public event Action Authenticated;
        public event Action<bool> AuthFailed;
        public event Action<int> AttemptStarted { add => _inner.AttemptStarted += value; remove => _inner.AttemptStarted -= value; }
        public event Action<float?> Localized;
        public event Action<int> NotLocalized { add => _inner.NotLocalized += value; remove => _inner.NotLocalized -= value; }

        /// <summary>Checks a solved sign pose's camera position in the map, such as its floor, before it poses the map.</summary>
        public Func<Vector3, bool> CandidatePoseValidator;

        public Transform MapSpace => _inner.MapSpace;
        public string MapCode => _inner.MapCode;
        /// <summary>Some sign is validated on this device model, so signs can pose the camera here.</summary>
        public bool SignsCanPose { get; }
        public bool FallbackAvailable => !_innerAuthFailed && (_inner is not MultiSetLocalizer multiSet || multiSet.FallbackAvailable);
        public string FallbackStatus => _innerAuthFailed ? "unavailable: authentication failed"
            : _inner is MultiSetLocalizer multiSet ? multiSet.FallbackStatus : "available";
        /// <summary>The map is posed from a validated sign pose, not from MultiSet.</summary>
        public bool FromSigns { get; private set; }
        /// <summary>Microseconds the last ObservePose spent solving, for the pose-estimation metric.</summary>
        public long LastSolveUs { get; private set; }

        public void Authenticate()
        {
            _innerReady = _innerWanted = _innerAuthFailed = false;
            _inner.Authenticate();
            // A validated sign needs no network round trip; MultiSet keeps authenticating behind it.
            if (SignsCanPose) Authenticated?.Invoke();
        }

        public void Localize()
        {
            if (_open)
            {
                if (_holdLeft <= 0f) AskInner(); // the session's retry, after the hold already ran out
                return;
            }
            // A new search after tracking loss: the AR world may have jumped, so earlier sightings no longer line up.
            if (FromSigns || _innerPosed)
            {
                _trail.Clear();
                _triangulator.Clear();
            }
            FromSigns = _innerPosed = false;
            if (!SignsCanPose)
            {
                _inner.Localize(); // no sign can pose here: a hold would only delay MultiSet
                return;
            }
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
            _triangulator.Clear();
            _open = _innerPosed = _innerWanted = false;
            FromSigns = false;
            _holdLeft = 0f;
        }

        public void Tick(float dt)
        {
            if (!_open || _holdLeft <= 0f) return;
            _holdLeft -= dt;
            if (_holdLeft <= 0f) AskInner();
        }

        /// <summary>Feeds one confirmed sign. Room-recognized evidence: the trail's fix is for the log only.</summary>
        public IndoorSignFix Observe(IndoorRoomLandmark room, Vector3 cameraInWorld)
        {
            _trail.Add(room, cameraInWorld);
            return _trail.Solve();
        }

        /// <summary>
        /// Feeds one frame of a confirmed sign's image corners. Once enough frames locate the sign it joins the
        /// trail, and alone it gives a coarse fix: heading from the wall the text is on, taken as the wall its door
        /// is in, and position from the door marker. Both are room-recognized evidence for the log only.
        /// </summary>
        public IndoorSignTriangulation ObserveCorners(IndoorRoomLandmark room, IndoorSignObservation observation,
            out IndoorSignFix fix)
        {
            fix = default;
            var sign = _triangulator.Add(observation);
            if (sign.Status != IndoorSignFixStatus.Ok) return sign;
            _trail.AddSign(room, sign.CenterInWorld);
            fix = _trail.Solve();
            if (!fix.Usable && sign.YawSigmaDeg <= MaxOneSignYawSigmaDeg)
            {
                fix.YawDeg = Mathf.DeltaAngle(room.DoorHeadingInMapDeg, sign.IntoWallYawDeg);
                fix.Origin = sign.CenterInWorld - Quaternion.Euler(0f, fix.YawDeg, 0f)
                    * (room.DoorPositionInMap + Vector3.up * IndoorSignTrail.SignHeightM);
                fix.YawBoundDeg = sign.YawSigmaDeg;
                fix.OneSign = fix.Usable = true;
            }
            return sign;
        }

        /// <summary>
        /// Feeds one frame of a confirmed sign. A pose from a sign validated on this device that
        /// CandidatePoseValidator accepts poses the map, unless MultiSet already did this search.
        /// </summary>
        public IndoorSignPoseStatus ObservePose(IndoorSignObservation observation)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var pose = IndoorSignPose.Solve(observation, _calibration.Find(observation?.RoomId));
            LastSolveUs = watch.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
            if (pose.Status != IndoorSignPoseStatus.Ok || _innerPosed) return pose.Status;
            if (CandidatePoseValidator != null && !CandidatePoseValidator(pose.CameraPositionInMap))
                return IndoorSignPoseStatus.Implausible;
            if (_inner is MultiSetLocalizer multiSet) multiSet.DropQueuedFallback();
            _innerWanted = false;
            SignPoseLocalizer.Place(MapSpace, observation.CameraWorldPosition, observation.CameraWorldRotation,
                pose.CameraPositionInMap, pose.CameraRotationInMap);
            FromSigns = true;
            if (!_open) return pose.Status;
            _open = false;
            Localized?.Invoke(null);
            return pose.Status;
        }

        // With a sign able to pose, authentication was not awaited, so MultiSet waits for its own.
        void AskInner()
        {
            if (SignsCanPose && !_innerReady)
            {
                _innerWanted = !_innerAuthFailed;
                return;
            }
            _innerWanted = false;
            _inner.Localize();
        }

        void OnInnerAuthenticated()
        {
            _innerReady = true;
            if (!SignsCanPose) Authenticated?.Invoke();
            else if (_innerWanted) AskInner();
        }

        void OnInnerAuthFailed(bool network)
        {
            if (!SignsCanPose) AuthFailed?.Invoke(network);
            else _innerAuthFailed = true; // signs can still pose; only the fallback is gone
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
