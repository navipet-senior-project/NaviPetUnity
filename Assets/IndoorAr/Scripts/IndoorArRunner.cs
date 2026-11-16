using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace IndoorAr
{
    /// <summary>
    /// Runs one door verification: reads the launch request, drives
    /// IndoorArSession from AR Foundation and the localizer, shows the pin and
    /// HUD, and hands the result to the Android host.
    /// </summary>
    [DefaultExecutionOrder(100)] // after EventSystem, so an exit tap cancels before tracking loss rebuilds the HUD
    public class IndoorArRunner : MonoBehaviour
    {
        [SerializeField] MonoBehaviour localizerBehaviour; // must implement IIndoorArLocalizer
        [SerializeField] GameObject doorPinPrefab;
        [SerializeField] Camera arCamera;
        // Room-sign OCR while AR localizes. Off keeps the MultiSet-only flow and stops OCR.
        [SerializeField] bool signLocalization = true;
        // SignFirst names the room from signs and gives heading-free directions from it, poses the map only from a
        // sign validated on this device, and asks MultiSet only after the sign hold; no sign is validated yet.
        [SerializeField] IndoorLocalizationMode localizationMode = IndoorLocalizationMode.SignFirst;

        IIndoorArLocalizer _localizer;
        IndoorArSession _session;
        string _requestJson;
        bool _started;
        Transform _pin;
        IndoorHud _hud;
        float _pinnedFor;
        float _failureLeft = -1f;
        bool _failureRelease;
        bool _delivered;
        Vector3 _scanPos;
        Quaternion _scanRot;
        bool _scanPoseReady;
        float _slowHoldLeft;
        IndoorArState _loggedState;
        bool _stateLogged;
        bool _armingEntry;
        float _sessionStarted;
        IndoorArBaseline _baseline;
        string _baselineLog;
        IndoorSignPoseStatus? _signPoseStatus;
        IndoorSignCalibration _signCalibration;
        string _statusSessionId;
        int _statusSeq;
        string _statusSent;
        Material _pinAmberMaterial;
        Material _pinCoreMaterial;
        IndoorRoomOcrDemo _mlKitOcr;
        IndoorRoomMlKitOcr _iosOcr;
        IndoorSignTracker _signs;
        SignFirstLocalizer _signFirst;
        readonly HashSet<int> _sessionAttempts = new HashSet<int>();
        const float RerouteM = .5f;
        IndoorWalkGrid _walkGrid;
        Vector3[] _route;
        Vector3 _routeFrom;
        bool _routeTried;
        // The wrong-floor route ends at stairs or an elevator on the floor the user is on.
        int _routeFloor;
        bool _routeWrongFloor;
        string _floorChange;
        bool _floorUnknown;
        int _hereFloor;
        // Supported floors, parsed once per session: routing and both pose gates read them every frame.
        IndoorWalkGrid _floor1Grid;
        IndoorWalkGrid _floor2Grid;
        List<(string Type, Vector3 PositionInMap)> _connectors;
        // Heading-free directions from the last confirmed room sign, routed once per room.
        IndoorRoomLandmark _directionsRoom;
        string _roomDirections;

        public void Configure(MonoBehaviour localizer, GameObject pinPrefab, Camera camera)
        {
            localizerBehaviour = localizer;
            doorPinPrefab = pinPrefab;
            arCamera = camera;
        }

        public void SetLocalizationMode(IndoorLocalizationMode mode) => localizationMode = mode;

        /// <summary>Replaces the bundled sign calibration. Call before Start.</summary>
        public void SetSignCalibration(IndoorSignCalibration calibration) => _signCalibration = calibration;

        IEnumerator Start()
        {
            _localizer = (IIndoorArLocalizer)localizerBehaviour;
            if (localizerBehaviour is MultiSetLocalizer multiSet)
                multiSet.CandidatePoseValidator = IsCandidateMultiSetPoseAllowed;
            _signCalibration ??= IndoorSignCalibration.Parse(Resources.Load<TextAsset>("IndoorSignCalibration")?.text);
            if (localizationMode == IndoorLocalizationMode.SignFirst)
                _localizer = _signFirst = new SignFirstLocalizer(_localizer, _signCalibration, signLocalization)
                    { CandidatePoseValidator = IsCandidateSignPoseAllowed };
            ArCameraDiagnostics.Attach();
            _mlKitOcr = GetComponent<IndoorRoomOcrDemo>();
            _iosOcr = GetComponent<IndoorRoomMlKitOcr>();
            if (_mlKitOcr != null) _mlKitOcr.Result += OnSignRead;
            if (_iosOcr != null) _iosOcr.Result += OnSignRead;
            UpdateSignOcr();
            if (!Application.isEditor)
                Debug.Log($"[IndoorAr] mode={localizationMode} signLocalization={signLocalization}");
            if (ArSessionLifecycle.StartNeedsLoader(Application.isEditor, ArSessionLifecycle.LoaderActive,
                IndoorArRouterHold.Holds(IndoorArBridge.ReadRequestJson())))
            {
                // A scene reloaded after the last exit finds the loader torn down; nothing else would start it.
                _armingEntry = true;
                var init = ArSessionLifecycle.InitializeOnEntry();
                while (init.MoveNext()) yield return init.Current;
                _armingEntry = false;
                ArSessionLifecycle.RestartArComponents();
            }
            // A scene reloaded while a restarted loader still runs has a fresh pose driver with the same fault.
            ArSessionLifecycle.DriveCameraFromXrInput();
            BeginSession(IndoorArBridge.ReadRequestJson());
            if (_session == null || _session.State == IndoorArState.Done) yield break;
            var started = _session;
            yield return ARSession.CheckAvailability();
            if (_session != started) yield break; // a restart already authenticated the newer session
            // Editor smoke must never authenticate or consume VPS quota.
            _session.OnArSupport(!Application.isEditor && ARSession.state != ARSessionState.Unsupported);
            NoteArReady();
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        // A host that only unpauses the player keeps this object. The launch
        // JSON is the new session; the previous door and the delivered flag are not.
        void BeginSession(string json)
        {
            var restart = _started;
            _started = true;
            _sessionStarted = Time.realtimeSinceStartup;
            _stateLogged = false;
            ReleaseSession();
            _requestJson = json;
            _delivered = false;
            _pinnedFor = 0f;
            _failureLeft = -1f;
            _failureRelease = false;
            _scanPoseReady = false;
            _slowHoldLeft = 0f;
            _signs = new IndoorSignTracker();
            _signPoseStatus = null;
            _statusSessionId = System.Guid.NewGuid().ToString("N");
            _statusSeq = 0;
            _statusSent = null;
            _sessionAttempts.Clear();
            // Localizers outlive a warm launch; nothing they held belongs to this session's AR world.
            _signFirst?.Reset();
            if (restart && localizerBehaviour is MultiSetLocalizer multiSet) multiSet.AbandonPending();
            if (!IndoorArContract.TryParseRequest(json, out var request))
            {
                Deliver(IndoorArContract.Error(IndoorArContract.BadRequest, 0, 0));
                return;
            }
            if (request.OutdoorOnly)
            {
                // The outdoor scene reads the same request and answers it. Exports without that scene say so.
                if (Application.CanStreamedLevelBeLoaded(IndoorArContract.OutdoorScene))
                    SceneManager.LoadScene(IndoorArContract.OutdoorScene);
                else
                    Deliver(IndoorArContract.Unavailable(0, 0));
                return;
            }
            _session = new IndoorArSession(request, _localizer.MapCode);
            if (_session.State == IndoorArState.Done) return; // MAP_MISMATCH, delivered in Update
            var gridJson = Resources.Load<TextAsset>("IndoorWalkGrid")?.text;
            _floor1Grid = IndoorWalkGrid.ForFloor(gridJson, 1);
            _floor2Grid = IndoorWalkGrid.ForFloor(gridJson, 2);
            _walkGrid = int.TryParse(request.floor, out var floor) ? Grid(floor) : null;
            _route = null;
            _routeTried = false;
            _floorChange = null;
            _routeFloor = 0;
            _routeWrongFloor = false;
            _floorUnknown = false;
            _hereFloor = 0;
            _directionsRoom = null;
            _roomDirections = null;
            _baseline = new IndoorArBaseline();
            _baselineLog = null;
            _baseline.NoteSessionOpened(NowMs());
            AttachHud();
            _session.LocalizeRequested += RequestLocalization;
            _localizer.Authenticated += _session.OnAuthenticated;
            _localizer.AuthFailed += _session.OnAuthFailed;
            _localizer.AttemptStarted += OnAttemptStarted;
            _localizer.Localized += OnLocalizedObserved;
            _localizer.NotLocalized += OnNotLocalized;
            if (!restart || Application.isEditor) return;
            ArSessionLifecycle.SetArComponents(true);
            var ar = UnityEngine.Object.FindAnyObjectByType<ARSession>();
            if (ar != null) ar.Reset();
            _session.OnArSupport(ARSession.state != ARSessionState.Unsupported);
            NoteArReady();
            if (_session.State == IndoorArState.Auth) _localizer.Authenticate();
        }

        void Update()
        {
            if (_localizer != null && !_armingEntry)
            {
                var json = IndoorArBridge.ReadRequestJson();
                if (json != null && json != _requestJson)
                {
                    if (Application.isEditor || !ArSessionLifecycle.ShouldInitialize(ArSessionLifecycle.LoaderActive))
                        BeginSession(json);
                    else
                    {
                        _armingEntry = true;
                        StartCoroutine(ArmEntry(json));
                    }
                }
            }
            UpdateSignOcr();
            if (_session == null || _delivered) return;
            if (BackPressed()) OnHudExit();
            if (_session.State != IndoorArState.Init) NoteArReady();
            if (_session.State == IndoorArState.Pinned && ARSession.state != ARSessionState.SessionTracking)
            {
                _session.OnTrackingLost();
                _baseline?.NoteTrackingLost();
                LogBaseline();
            }
            if (_session.State == IndoorArState.Pinned)
            {
                _pinnedFor += Time.unscaledDeltaTime;
                _session.OnTrackedPose(CameraInMap().y, Floor1Y, Floor2Y);
            }
            else _pinnedFor = 0f;
            _session.Tick(Time.unscaledDeltaTime);
            _signFirst?.Tick(Time.unscaledDeltaTime);
            if (!_stateLogged || _loggedState != _session.State)
            {
                _loggedState = _session.State;
                _stateLogged = true;
                if (!Application.isEditor)
                    Debug.Log($"[IndoorAr] state={_loggedState} elapsedMs={(long)((Time.realtimeSinceStartup - _sessionStarted) * 1000)} attempts={_session.Queries} multisetFallback={MultiSetFallbackStatus()}");
            }
            UpdatePin();
            // shortcut: hold the failure card 4s on device so it can be read before the activity closes.
            if ((LiveDoorHud.AuthFailed(_session) || LiveDoorHud.StepFreeUnavailable(_session)) && _failureLeft < 0f)
                _failureLeft = Application.isEditor ? 0f : 4f;
            var holdingFailure = LiveDoorHud.LocalizationFailed(_session) || (_failureLeft > 0f && !_failureRelease);
            if (holdingFailure) _failureLeft -= Time.unscaledDeltaTime;
            UpdateHud();
            PostStatus();
            var keepFailure = LiveDoorHud.LocalizationFailed(_session) || (_failureLeft > 0f && !_failureRelease);
            if (_session.State == IndoorArState.Done && !keepFailure) Deliver(_session.ResultJson);
        }

        void AttachHud()
        {
            var hudObject = new GameObject("Indoor HUD");
            hudObject.transform.SetParent(_localizer.MapSpace, false);
            _hud = hudObject.AddComponent<IndoorHud>();
            _hud.Initialize(arCamera);
            _hud.ExitRequested += OnHudExit;
            _hud.HereRequested += OnHudHere;
            _hud.DoneRequested += OnHudDone;
            _hud.RetryRequested += OnHudRetry;
            if (EventSystem.current == null)
            {
                var events = new GameObject("HUD EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
        }

        void OnHudExit()
        {
            _failureRelease = true;
            _session?.Cancel();
            DropQueuedMultiSetFallback();
        }

        void RequestLocalization()
        {
            _baseline?.NoteAttemptStarted(NowMs());
            if (!Application.isEditor)
                Debug.Log($"[IndoorAr] localization attempt={_session.Queries + 1} elapsedMs={(long)((Time.realtimeSinceStartup - _sessionStarted) * 1000)}");
            _localizer.Localize();
            LogBaseline();
        }

        void OnAttemptStarted(int attemptId)
        {
            _sessionAttempts.Add(attemptId);
            _session?.OnAttemptStarted();
        }

        void OnNotLocalized(int attemptId)
        {
            if (_sessionAttempts.Remove(attemptId)) _session?.OnNotLocalized();
        }

        void OnLocalizedObserved(float? confidence)
        {
            if (RejectMultiSetFloorConflict(CameraInMap())) return;
            var wasPinned = _session.WasPinned;
            _session.OnLocalized(confidence);
            if (!wasPinned && _session.WasPinned)
                _baseline?.NotePoseAccepted(NowMs(), _signFirst != null && _signFirst.FromSigns);
            LogBaseline();
        }

        // OCR keeps reading while pinned so a confirmed reread can refresh floor evidence and recover sign alignment.
        void UpdateSignOcr()
        {
            var on = signLocalization && !_delivered && _session != null
                && _session.State is IndoorArState.Init or IndoorArState.Auth or IndoorArState.Localizing or IndoorArState.Pinned;
            if (_mlKitOcr != null) _mlKitOcr.enabled = on;
            if (_iosOcr != null) _iosOcr.enabled = on;
        }

        void OnSignRead(string text, IndoorRoomMatch match, float recognitionStarted, IndoorSignObservation observation)
        {
            if (!signLocalization || _session == null || _delivered || _signs == null
                || recognitionStarted < _sessionStarted) return;
            var now = Time.unscaledTime;
            var ocrMs = (long)((Time.realtimeSinceStartup - recognitionStarted) * 1000);
            _baseline?.NoteOcrRead(ocrMs);
            // Status, room id and timing only: the raw recognized text never reaches the log.
            if (!Application.isEditor)
                Debug.Log($"[IndoorAr] sign read status={match?.Status} room={match?.Landmark?.Id ?? "none"} ocrMs={ocrMs} corners={observation != null}");
            if (_signs.Observe(match, now))
            {
                if (!Application.isEditor) Debug.Log($"[IndoorAr] sign confirmed room={_signs.Confirmed.Id}");
                _baseline?.NoteSignConfirmed(NowMs());
                LogBaseline();
            }
            if (match?.Status == IndoorRoomMatchStatus.Matched && _signs.InView(now)
                && match.Landmark.Id == _signs.Confirmed.Id)
            {
                _session.OnSignConfirmed(_signs.Confirmed);
                if (_session.State == IndoorArState.Pinned && RejectMultiSetFloorConflict(CameraInMap()))
                    _session.OnTrackingLost();
                _session.OnSignInView();
                if (_signFirst != null && observation != null && observation.RoomId == _signs.Confirmed.Id)
                    ObserveSignPose(observation);
            }
        }

        bool RejectMultiSetFloorConflict(Vector3 cameraInMap)
        {
            var fromMultiSet = localizationMode == IndoorLocalizationMode.MultiSet
                || _signFirst != null && !_signFirst.FromSigns;
            if (!fromMultiSet) return false;
            if (!_session.PoseConflictsWithSign(cameraInMap.y, Floor1Y, Floor2Y)) return false;
            var poseFloor = _session.PoseFloor(cameraInMap.y, Floor1Y, Floor2Y);
            var pose = poseFloor == -1 ? "unsupported" : poseFloor == 0 ? "unknown" : poseFloor.ToString();
            Debug.Log($"[IndoorAr] floor conflict sign={(_session.FloorSign == null ? "none" : _session.FloorSign.Floor.ToString())} pose={pose}");
            if (_session.State == IndoorArState.Localizing) _session.OnNotLocalized();
            return true;
        }

        // A sign pose may pose the map only while the session wants or holds a pose, and only on the sign's floor.
        bool IsCandidateSignPoseAllowed(Vector3 cameraInMap)
        {
            if (_session == null || _session.State is not (IndoorArState.Localizing or IndoorArState.Pinned)) return false;
            return !_session.PoseConflictsWithSign(cameraInMap.y, Floor1Y, Floor2Y);
        }

        IndoorWalkGrid Grid(int floor) => floor == 1 ? _floor1Grid : floor == 2 ? _floor2Grid : null;
        float Floor1Y => _floor1Grid != null ? _floor1Grid.FloorY : float.NaN;
        float Floor2Y => _floor2Grid != null ? _floor2Grid.FloorY : float.NaN;

        bool IsCandidateMultiSetPoseAllowed(Transform candidateMapSpace)
        {
            if (_session == null || _session.State != IndoorArState.Localizing || arCamera == null) return false;
            return !RejectMultiSetFloorConflict(candidateMapSpace.InverseTransformPoint(arCamera.transform.position));
        }

        // Poses the map from a sign validated on this device. Logs the status and solve time, never the pose.
        void ObserveSignPose(IndoorSignObservation observation)
        {
            var status = _signFirst.ObservePose(observation);
            if (status == IndoorSignPoseStatus.Ok)
            {
                _baseline?.NotePoseEstimate(_signFirst.LastSolveUs);
                LogBaseline();
            }
            if (Application.isEditor) return;
            if (_signPoseStatus != status)
                Debug.Log($"[IndoorAr] sign pose room={observation.RoomId} status={status} solveUs={_signFirst.LastSolveUs}");
            _signPoseStatus = status;
            // Development builds only: survey input for the calibration importer. Intrinsics and corner pixels of the
            // image, no position and no text.
            if (Debug.isDebugBuild && status is IndoorSignPoseStatus.NoCalibration or IndoorSignPoseStatus.Unvalidated
                    or IndoorSignPoseStatus.OtherDevice)
                Debug.Log(IndoorSignSurvey.CaptureLine(observation));
        }

        void NoteArReady()
        {
            _baseline?.NoteArReady(NowMs());
            LogBaseline();
        }

        void LogBaseline()
        {
            if (Application.isEditor || _baseline == null) return;
            var line = _baseline.LogLine();
            if (line == _baselineLog) return;
            _baselineLog = line;
            Debug.Log(line);
        }

        string MultiSetFallbackStatus()
        {
            if (_signFirst != null) return _signFirst.FallbackStatus;
            return localizerBehaviour is MultiSetLocalizer multiSet ? multiSet.FallbackStatus : "unavailable";
        }

        // Tells the host what the session knows, once per change. The pose rides along only once localized; never log it.
        void PostStatus()
        {
            if (_session == null || _delivered) return;
            var localized = _session.State is IndoorArState.Pinned or IndoorArState.Arrived;
            var tracking = ARSession.state == ARSessionState.SessionTracking;
            var fallback = _signFirst != null
                ? _signFirst.FallbackAvailable : localizerBehaviour is MultiSetLocalizer multiSet && multiSet.FallbackAvailable;
            var room = _session.SignRoom;
            var key = $"{_session.State}|{tracking}|{room?.Id}|{localized}|{fallback}";
            if (key == _statusSent) return;
            _statusSent = key;
            var map = _localizer.MapSpace;
            var source = !localized ? null
                : _signFirst != null && _signFirst.FromSigns ? "sign" : "multiset";
            IndoorArBridge.PostStatus(IndoorArContract.Status(_statusSessionId, ++_statusSeq, _localizer.MapCode,
                _session.State, tracking, room?.Id, room?.Floor ?? 0, localized, fallback, source,
                CameraInMap(), Quaternion.Inverse(map.rotation) * arCamera.transform.rotation));
        }

        long NowMs() => (long)(Time.realtimeSinceStartup * 1000f);

        void OnHudHere() => _session?.ImHere(CameraInMap());
        void OnHudDone() => _session?.ConfirmArrival();
        void OnHudRetry() => _session?.Retry();

        void UpdateHud()
        {
            if (_hud == null || _delivered) return;
            if (_session.State == IndoorArState.Done
                && !LiveDoorHud.LocalizationFailed(_session)
                && !LiveDoorHud.AuthFailed(_session)
                && !LiveDoorHud.StepFreeUnavailable(_session)) return;
            var route = Route(CameraInMap());
            var view = LiveDoorHud.Compose(_session, CameraInMap(), CameraForwardInMap(), _pinnedFor, route,
                _floorChange, _floorUnknown, _routeWrongFloor, Grid(_routeFloor), RoomDirections());
            if (_pin != null && _pin.gameObject.activeSelf) view.LabelAnchor = PinLabelAnchor(_pin);
            // Names the sign the camera reads now. It is not the camera's position.
            if (view.State == IndoorHudState.Straight && !view.RouteBlocked && !view.FollowingRoute && _session.SignRoom != null
                && _signs != null && _signs.InView(Time.unscaledTime))
                view.DetailOverride = LiveDoorHud.SignDetected(_session.SignRoom);
            if (view.State == IndoorHudState.Scanning && _signFirst != null && _session.SignRoom == null)
                view.DetailOverride ??= "Point the camera at room signs as you walk";
            if (view.State == IndoorHudState.Scanning)
            {
                view.ScanTooFast = SampleScanMotion();
                // Slow-down wins, except over room directions the user is walking by.
                if (view.ScanTooFast && (_roomDirections == null || view.DetailOverride != _roomDirections))
                    view.TitleOverride = view.DetailOverride = null;
            }
            else
            {
                _scanPoseReady = false;
                _slowHoldLeft = 0f;
            }
            if (view.State == IndoorHudState.Straight && !view.RouteBlocked)
            {
                _baseline?.NoteArrowShown(NowMs());
                LogBaseline();
            }
            _hud.Show(view);
        }

        /// <summary>
        /// The hallway route from the camera to the door, or on the wrong floor to the nearest stairs or elevator,
        /// searched again only after the camera moved or the floor verdict changed.
        /// </summary>
        Vector3[] Route(Vector3 cameraInMap)
        {
            if (_session.State == IndoorArState.Pinned && !_session.Request.LocalizeOnly && _session.Request.stepFree)
            {
                _route = null;
                _session.OnStepFreeUnverified();
                _failureLeft = Application.isEditor ? 0f : 4f;
                return null;
            }
            if (_session.State != IndoorArState.Pinned || _session.Request.LocalizeOnly)
            {
                _routeTried = false;
                _floorChange = null;
                _floorUnknown = false;
                _routeWrongFloor = false;
                return _route = null;
            }
            var currentFloor = _session.CurrentFloor(cameraInMap.y, Floor1Y, Floor2Y);
            var wrongFloor = _session.IsWrongFloor(currentFloor);
            if (currentFloor != _routeFloor) _routeTried = false;
            var moved = new Vector2(cameraInMap.x - _routeFrom.x, cameraInMap.z - _routeFrom.z).magnitude;
            if (_routeTried && moved < RerouteM) return _route;
            var had = _route != null;
            var first = !_routeTried;
            _floorChange = null;
            _floorUnknown = false;
            if (currentFloor == 0)
            {
                _route = null;
                _floorUnknown = true;
                if (!Application.isEditor && first)
                    Debug.Log($"[IndoorAr] floor unknown sign={(_session.FloorSign == null ? "none" : _session.FloorSign.Floor.ToString())} pose=unknown destination={_session.Request.floor}");
            }
            else if (wrongFloor) RouteToFloorChange(cameraInMap, currentFloor);
            else
            {
                var door = _session.Request.position;
                if (_walkGrid == null || !_walkGrid.TryRoute(cameraInMap, new Vector3(door.x, door.y, door.z), out _route)) _route = null;
            }
            _routeFrom = cameraInMap;
            _routeTried = true;
            _routeFloor = currentFloor;
            _routeWrongFloor = wrongFloor;
            // Whether the grid connects the camera to the goal, and how far: no position, no waypoint.
            if (!Application.isEditor && (first || had != (_route != null)))
                Debug.Log(_route == null
                    ? $"[IndoorAr] route status=none wrongFloor={wrongFloor} stepFree={_session.Request.stepFree}"
                    : $"[IndoorAr] route status=found waypoints={_route.Length} lengthM={Mathf.Round(IndoorWalkGrid.LengthM(_route))} to={_floorChange ?? "door"} stepFree={_session.Request.stepFree}");
            return _route;
        }

        string RoomDirections()
        {
            var room = _session.SignRoom;
            if (room == _directionsRoom) return _roomDirections;
            _directionsRoom = room;
            _roomDirections = room == null ? null : LiveDoorHud.RoomDirections(room, _session.Request, Grid(room.Floor),
                IndoorRoomIdentityMatcher.FloorMarkers(DirectoryJson, room.Floor));
            // Room and whether a route was found: no position, no text.
            if (room != null && !Application.isEditor)
                Debug.Log($"[IndoorAr] room directions room={room.Id} status={(_roomDirections == null ? "none" : "found")}");
            return _roomDirections;
        }

        string DirectoryJson => _iosOcr != null ? _iosOcr.DirectoryJson : _mlKitOcr != null ? _mlKitOcr.DirectoryJson : null;

        /// <summary>Shortest scan-grid route on the user's floor to a directory stairs or elevator marker.</summary>
        void RouteToFloorChange(Vector3 cameraInMap, int here)
        {
            _route = null;
            if (here is not (1 or 2)) return;
            if (_hereFloor != here)
            {
                _hereFloor = here;
                _connectors = IndoorRoomIdentityMatcher.FloorConnectors(DirectoryJson, here);
            }
            var hereGrid = Grid(here);
            if (hereGrid == null) return;
            foreach (var (type, at) in _connectors)
                if (hereGrid.TryRoute(cameraInMap, at, out var route)
                    && (_route == null || IndoorWalkGrid.LengthM(route) < IndoorWalkGrid.LengthM(_route)))
                {
                    _route = route;
                    _floorChange = type;
                }
        }

        bool SampleScanMotion()
        {
            var pose = arCamera.transform;
            var tooFast = _scanPoseReady && GuidanceCue.ScanTooFast(
                Vector3.Distance(_scanPos, pose.position),
                Quaternion.Angle(_scanRot, pose.rotation),
                Time.unscaledDeltaTime);
            _scanPos = pose.position;
            _scanRot = pose.rotation;
            _scanPoseReady = true;
            _slowHoldLeft = GuidanceCue.HoldSlowScan(_slowHoldLeft, tooFast, Time.unscaledDeltaTime);
            return _slowHoldLeft > 0f;
        }

        void UpdatePin()
        {
            // The pin stays behind the arrival card.
            var pinned = (_session.State == IndoorArState.Pinned || _session.State == IndoorArState.Arrived) && !_session.Request.LocalizeOnly;
            if (pinned && _pin == null)
            {
                var door = _session.Request;
                _pin = CreatePin(_localizer.MapSpace, out _pinAmberMaterial, out _pinCoreMaterial);
                _pin.localPosition = new Vector3(door.position.x, door.position.y, door.position.z);
                _pin.localRotation = Quaternion.Euler(0f, door.headingDeg, 0f);
            }
            WatchMapSpace(pinned);
            if (_pin != null)
            {
                _pin.gameObject.SetActive(pinned);
                if (pinned)
                {
                    var direction = _pin.position - arCamera.transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 0.0001f) _pin.rotation = Quaternion.LookRotation(direction);
                }
            }
        }

        public const float MapShiftMeters = 0.02f;
        public const float MapShiftDegrees = 0.5f;

        /// <summary>The pin is a MapSpace child, so it only moves in the world if MapSpace itself is re-posed.</summary>
        public static bool MapShifted(Vector3 was, Quaternion wasRotation, Vector3 now, Quaternion nowRotation,
            out float meters, out float degrees)
        {
            meters = Vector3.Distance(was, now);
            degrees = Quaternion.Angle(wasRotation, nowRotation);
            return meters > MapShiftMeters || degrees > MapShiftDegrees;
        }

        Vector3 _mapPosition;
        Quaternion _mapRotation;
        bool _mapWatched;

        // Logs the size of any MapSpace move while the pin is shown, never where MapSpace is.
        void WatchMapSpace(bool pinned)
        {
            if (!pinned) { _mapWatched = false; return; }
            var map = _localizer.MapSpace;
            if (_mapWatched && MapShifted(_mapPosition, _mapRotation, map.position, map.rotation, out var meters, out var degrees)
                && !Application.isEditor)
                Debug.Log($"[IndoorAr] map space moved after pin shiftM={meters:0.00} turnDeg={degrees:0.0}");
            _mapPosition = map.position;
            _mapRotation = map.rotation;
            _mapWatched = true;
        }

        Vector3 CameraInMap() => _localizer.MapSpace.InverseTransformPoint(arCamera.transform.position);

        Vector3 CameraForwardInMap() => _localizer.MapSpace.InverseTransformDirection(arCamera.transform.forward);

        /// <summary>World point just above the pin head, where the HUD floats the room label.</summary>
        public static Vector3 PinLabelAnchor(Transform pin) => pin.TransformPoint(0f, .45f, 0f);

        // Static so the editor HUD preview shows the same pin. The caller owns both materials.
        public static Transform CreatePin(Transform parent, out Material amber, out Material coreMaterial)
        {
            var root = new GameObject("Destination pin").transform;
            root.SetParent(parent, false);
            amber = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            amber.color = new Color32(245, 166, 35, 255);
            coreMaterial = new Material(amber) { color = Color.white };
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Amber pin";
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0, .23f, 0);
            head.transform.localScale = Vector3.one * .3f;
            head.GetComponent<Renderer>().sharedMaterial = amber;
            DestroyOwned(head.GetComponent<Collider>());
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "White core";
            core.transform.SetParent(root, false);
            core.transform.localPosition = new Vector3(0, .23f, -.14f);
            core.transform.localScale = Vector3.one * .12f;
            core.GetComponent<Renderer>().sharedMaterial = coreMaterial;
            DestroyOwned(core.GetComponent<Collider>());
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tip.name = "Pin stem";
            tip.transform.SetParent(root, false);
            tip.transform.localPosition = new Vector3(0, .08f, 0);
            tip.transform.localScale = new Vector3(.07f, .08f, .07f);
            tip.GetComponent<Renderer>().sharedMaterial = amber;
            DestroyOwned(tip.GetComponent<Collider>());
            return root;
        }

        void OnDestroy()
        {
            if (_mlKitOcr != null) _mlKitOcr.Result -= OnSignRead;
            if (_iosOcr != null) _iosOcr.Result -= OnSignRead;
            ReleaseSession();
        }

        void ReleaseSession()
        {
            Unhook();
            if (_hud != null)
            {
                DestroyOwned(_hud.gameObject);
                _hud = null;
            }
            if (_pin != null)
            {
                DestroyOwned(_pin.gameObject);
                _pin = null;
            }
            DestroyOwned(_pinAmberMaterial);
            DestroyOwned(_pinCoreMaterial);
            _pinAmberMaterial = null;
            _pinCoreMaterial = null;
            _session = null;
            _baseline = null;
            _baselineLog = null;
        }

        void Unhook()
        {
            if (_hud != null)
            {
                _hud.ExitRequested -= OnHudExit;
                _hud.HereRequested -= OnHudHere;
                _hud.DoneRequested -= OnHudDone;
                _hud.RetryRequested -= OnHudRetry;
            }
            if (_session == null || _localizer == null) return;
            _session.LocalizeRequested -= RequestLocalization;
            _localizer.Authenticated -= _session.OnAuthenticated;
            _localizer.AuthFailed -= _session.OnAuthFailed;
            _localizer.AttemptStarted -= OnAttemptStarted;
            _localizer.Localized -= OnLocalizedObserved;
            _localizer.NotLocalized -= OnNotLocalized;
        }

        static void DestroyOwned(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        IEnumerator ArmEntry(string json)
        {
            var init = ArSessionLifecycle.InitializeOnEntry();
            while (init.MoveNext()) yield return init.Current;
            _armingEntry = false;
            BeginSession(json);
        }

        void Deliver(string resultJson)
        {
            if (_delivered) return;
            _delivered = true;
            _requestJson = null;
            DropQueuedMultiSetFallback();
            if (Application.isPlaying) StopAllCoroutines();
            ArSessionLifecycle.StopSessionsAndTearDown();
            IndoorArBridge.Deliver(resultJson);
        }

        void DropQueuedMultiSetFallback()
        {
            if (_localizer is MultiSetLocalizer multiSet) multiSet.DropQueuedFallback();
            else if (_localizer is SignFirstLocalizer signFirst) signFirst.DropQueuedFallback();
        }

        // Android's back button arrives as Escape.
        public static bool BackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
