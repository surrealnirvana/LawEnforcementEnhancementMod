using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppFishNet.Object;
using MelonLoader;
using Il2CppFishNet.Managing;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.IO;
using Il2CppScheduleOne.GameTime;
using Newtonsoft.Json;
using MelonLoader.Utils;

namespace LawEnforcementEnhancementMod
{
    public class SentrySpawnSystem
    {
        // Core references
        private readonly Core _core;
        private NetworkManager? _networkManager; // FIX: Made _networkManager nullable
        private bool _initialized;

        // Sentry types
        private readonly string[] SentryTypes = new string[]
        {
            "OfficerBailey", "OfficerCooper", "OfficerDavis",
            "OfficerGreen", "OfficerHoward", "OfficerJackson",
            "OfficerLee", "OfficerLeo", "OfficerLopez",
            "OfficerMurphy", "OfficerOakley", "OfficerSanchez"
        };

        // Sentry tracking - using StringComparer for better performance
        private readonly List<PoliceOfficer> _activeSentries = new List<PoliceOfficer>();
        private readonly Queue<SentrySpawnJob> _spawnQueue = new Queue<SentrySpawnJob>();
        private readonly Queue<PoliceOfficer> _despawnQueue = new Queue<PoliceOfficer>();

        // Timing and cooldowns
        private float _spawnCooldown = 3.5f;
        private float _nextSpawnTime = 0f;
        private float _despawnCooldown = 10f;
        private float _nextDespawnTime = 0f;

        // Frame-based processing
        private int _frameCounter = 0;
        private const int SPAWN_FRAME_INTERVAL = 6;  // Process spawning every 6 frames
        private const int DESPAWN_FRAME_INTERVAL = 10; // Process despawning every 10 frames

        // Specific time checking for 6AM and 9PM only
        private int _lastGameHour = -1;
        private bool _isNinePMCheckDone = false;
        private bool _isSixAMCheckDone = false;

        // Flag to track day despawn
        private bool _dayDespawnCompleted = false;

        // Limits and counts - MODIFIED: reduced from 24 to 10
        private int _maxTotalSentries = 10; // Reduced to 10 sentries
        private int _targetSentryCount = 10; // Target 10 sentries

        // Dictionary to track sentry patrol route names - using StringComparer.Ordinal for better performance
        private readonly Dictionary<PoliceOfficer, string> _sentryRouteNames = new Dictionary<PoliceOfficer, string>(new ObjectReferenceComparer<PoliceOfficer>());
        private readonly Dictionary<string, GameObject> _patrolRouteObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        // Reusable collections to avoid GC pressure
        private readonly List<PoliceOfficer> _tempSentryList = new List<PoliceOfficer>();

        // Initialization tracking
        private bool _gameReady = false;
        private float _gameReadyCheckTime = 0f;
        private float _gameReadyCheckInterval = 1.0f;
        private int _initFailCount = 0;
        private const int MAX_INIT_FAILURES = 5;
        private float _worldLoadedTime = 0f;
        private float _requiredStabilityDelay = 3.0f;

        // Time-based spawning
        private bool _isNightTime = false;
        private bool _sentryShouldBeActive = false;

        // Debug logging
        private bool _enableDebugLogs = false;

        // Performance tracking
        private float _lastUpdateTime = 0f;

        // Sentry spawn points
        private readonly List<SentryPosition> _sentryPositions = new List<SentryPosition>();

        // Class to hold sentry position and facing direction
        public class SentryPosition
        {
            public Vector3 Position { get; set; }
            public Direction FacingDirection { get; set; }
            public bool IsOccupied { get; set; } = false;
            public PoliceOfficer? CurrentSentry { get; set; } = null; // FIX: Made CurrentSentry nullable
            public Quaternion CalculatedRotation { get; set; }
            public string District { get; set; } = "Unknown";

            // FIX: Initialize PatrolWaypoints with empty array to fix CS8618
            public Vector3[] PatrolWaypoints { get; set; } = Array.Empty<Vector3>();
        }

        public enum Direction
        {
            North,
            South,
            East,
            West
        }

        public class SentrySpawnJob
        {
            public Vector3 SpawnPosition { get; set; }
            // FIX: Initialize TargetPosition in constructor to fix CS8618
            public SentryPosition TargetPosition { get; set; } = new SentryPosition();
            public float Priority { get; set; } = 1f;
            public DateTime QueuedTime { get; set; } = DateTime.Now;
        }

        // Custom equality comparer for PoliceOfficer objects
        private class ObjectReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => obj == null ? 0 : obj.GetHashCode();
        }

        public SentrySpawnSystem()
        {
            _core = Core.Instance;
            InitializeSentryPositions();

            // Register to game loaded event
            MelonEvents.OnSceneWasLoaded.Subscribe(OnSceneWasLoaded);
        }

        private void InitializeSentryPositions()
        {
            // Add all sentry positions with their facing directions and district
            // For each position, we'll create a set of exact patrol points

            // Position 1
            Vector3 pos1 = new Vector3(96.42f, 5.07f, -100.70f);
            AddSentryPosition(pos1, Direction.West, "Southern District", new Vector3[]
            {
                pos1,
                new Vector3(96.42f, 5.07f, -100.70f), // Same position
                new Vector3(96.42f, 5.07f, -100.71f), // Barely moved
                new Vector3(96.41f, 5.07f, -100.70f)  // Barely moved sideways
            });

            // Position 2
            Vector3 pos2 = new Vector3(150.54f, 5.07f, -107.42f);
            AddSentryPosition(pos2, Direction.South, "Eastern District", new Vector3[]
            {
                pos2,
                new Vector3(150.54f, 5.07f, -107.42f),
                new Vector3(150.54f, 5.07f, -107.43f),
                new Vector3(150.55f, 5.07f, -107.42f)
            });

            // Position 3
            Vector3 pos3 = new Vector3(34.82f, 3.38f, -109.06f);
            AddSentryPosition(pos3, Direction.East, "Southern District", new Vector3[]
            {
                pos3,
                new Vector3(34.82f, 3.38f, -109.06f),
                new Vector3(34.83f, 3.38f, -109.06f),
                new Vector3(34.82f, 3.38f, -109.05f)
            });

            // Position 4
            Vector3 pos4 = new Vector3(87.28f, 5.07f, -61.64f);
            AddSentryPosition(pos4, Direction.North, "Southern District", new Vector3[]
            {
                pos4,
                new Vector3(87.28f, 5.07f, -61.64f),
                new Vector3(87.28f, 5.07f, -61.63f),
                new Vector3(87.27f, 5.07f, -61.64f)
            });

            // Position 5
            Vector3 pos5 = new Vector3(95.21f, 1.06f, 16.79f);
            AddSentryPosition(pos5, Direction.South, "Eastern District", new Vector3[]
            {
                pos5,
                new Vector3(95.21f, 1.06f, 16.79f),
                new Vector3(95.21f, 1.06f, 16.78f),
                new Vector3(95.22f, 1.06f, 16.79f)
            });

            // Position 6
            Vector3 pos6 = new Vector3(97.67f, 1.05f, 15.72f);
            AddSentryPosition(pos6, Direction.West, "Eastern District", new Vector3[]
            {
                pos6,
                new Vector3(97.67f, 1.05f, 15.72f),
                new Vector3(97.66f, 1.05f, 15.72f),
                new Vector3(97.67f, 1.05f, 15.73f)
            });

            // Position 7
            Vector3 pos7 = new Vector3(127.73f, 1.07f, 55.09f);
            AddSentryPosition(pos7, Direction.North, "Eastern District", new Vector3[]
            {
                pos7,
                new Vector3(127.73f, 1.07f, 55.09f),
                new Vector3(127.73f, 1.07f, 55.10f),
                new Vector3(127.74f, 1.07f, 55.09f)
            });

            // Position 8
            Vector3 pos8 = new Vector3(124.90f, 1.07f, 55.01f);
            AddSentryPosition(pos8, Direction.West, "Eastern District", new Vector3[]
            {
                pos8,
                new Vector3(124.90f, 1.07f, 55.01f),
                new Vector3(124.89f, 1.07f, 55.01f),
                new Vector3(124.90f, 1.07f, 55.02f)
            });

            // Position 9
            Vector3 pos9 = new Vector3(14.70f, 1.07f, 34.11f);
            AddSentryPosition(pos9, Direction.North, "Downtown", new Vector3[]
            {
                pos9,
                new Vector3(14.70f, 1.07f, 34.11f),
                new Vector3(14.70f, 1.07f, 34.12f),
                new Vector3(14.71f, 1.07f, 34.11f)
            });

            // Position 10
            Vector3 pos10 = new Vector3(13.89f, 1.06f, 42.42f);
            AddSentryPosition(pos10, Direction.North, "Downtown", new Vector3[]
            {
                pos10,
                new Vector3(13.89f, 1.06f, 42.42f),
                new Vector3(13.89f, 1.06f, 42.43f),
                new Vector3(13.90f, 1.06f, 42.42f)
            });

            // Position 11
            Vector3 pos11 = new Vector3(37.68f, 1.06f, 47.09f);
            AddSentryPosition(pos11, Direction.East, "Downtown", new Vector3[]
            {
                pos11,
                new Vector3(37.68f, 1.06f, 47.09f),
                new Vector3(37.69f, 1.06f, 47.09f),
                new Vector3(37.68f, 1.06f, 47.10f)
            });

            // Position 12
            Vector3 pos12 = new Vector3(35.37f, 1.07f, 44.97f);
            AddSentryPosition(pos12, Direction.South, "Downtown", new Vector3[]
            {
                pos12,
                new Vector3(35.37f, 1.07f, 44.97f),
                new Vector3(35.37f, 1.07f, 44.96f),
                new Vector3(35.38f, 1.07f, 44.97f)
            });

            // Position 13
            Vector3 pos13 = new Vector3(27.93f, 1.06f, 16.40f);
            AddSentryPosition(pos13, Direction.North, "Downtown", new Vector3[]
            {
                pos13,
                new Vector3(27.93f, 1.06f, 16.40f),
                new Vector3(27.93f, 1.06f, 16.41f),
                new Vector3(27.94f, 1.06f, 16.40f)
            });

            // Position 14
            Vector3 pos14 = new Vector3(-22.46f, 1.07f, -42.51f);
            AddSentryPosition(pos14, Direction.North, "Southern District", new Vector3[]
            {
                pos14,
                new Vector3(-22.46f, 1.07f, -42.51f),
                new Vector3(-22.46f, 1.07f, -42.50f),
                new Vector3(-22.45f, 1.07f, -42.51f)
            });

            // Position 15
            Vector3 pos15 = new Vector3(-82.13f, -1.44f, -27.66f);
            AddSentryPosition(pos15, Direction.West, "Western District", new Vector3[]
            {
                pos15,
                new Vector3(-82.13f, -1.44f, -27.66f),
                new Vector3(-82.14f, -1.44f, -27.66f),
                new Vector3(-82.13f, -1.44f, -27.65f)
            });

            // Position 16
            Vector3 pos16 = new Vector3(-75.07f, 1.07f, 69.90f);
            AddSentryPosition(pos16, Direction.West, "Western District", new Vector3[]
            {
                pos16,
                new Vector3(-75.07f, 1.07f, 69.90f),
                new Vector3(-75.08f, 1.07f, 69.90f),
                new Vector3(-75.07f, 1.07f, 69.91f)
            });

            // Position 17
            Vector3 pos17 = new Vector3(-130.84f, -2.93f, 82.56f);
            AddSentryPosition(pos17, Direction.East, "Far Western District", new Vector3[]
            {
                pos17,
                new Vector3(-130.84f, -2.93f, 82.56f),
                new Vector3(-130.83f, -2.93f, 82.56f),
                new Vector3(-130.84f, -2.93f, 82.57f)
            });

            // Position 18
            Vector3 pos18 = new Vector3(-129.47f, -2.94f, 78.82f);
            AddSentryPosition(pos18, Direction.North, "Far Western District", new Vector3[]
            {
                pos18,
                new Vector3(-129.47f, -2.94f, 78.82f),
                new Vector3(-129.47f, -2.94f, 78.83f),
                new Vector3(-129.46f, -2.94f, 78.82f)
            });

            // Position 19
            Vector3 pos19 = new Vector3(-149.99f, -2.94f, 82.46f);
            AddSentryPosition(pos19, Direction.South, "Far Western District", new Vector3[]
            {
                pos19,
                new Vector3(-149.99f, -2.94f, 82.46f),
                new Vector3(-149.99f, -2.94f, 82.45f),
                new Vector3(-149.98f, -2.94f, 82.46f)
            });

            // Position 20
            Vector3 pos20 = new Vector3(-95.81f, -2.76f, 119.93f);
            AddSentryPosition(pos20, Direction.East, "Northern District", new Vector3[]
            {
                pos20,
                new Vector3(-95.81f, -2.76f, 119.93f),
                new Vector3(-95.80f, -2.76f, 119.93f),
                new Vector3(-95.81f, -2.76f, 119.94f)
            });

            // Position 21
            Vector3 pos21 = new Vector3(-77.84f, -2.94f, 119.60f);
            AddSentryPosition(pos21, Direction.East, "Northern District", new Vector3[]
            {
                pos21,
                new Vector3(-77.84f, -2.94f, 119.60f),
                new Vector3(-77.83f, -2.94f, 119.60f),
                new Vector3(-77.84f, -2.94f, 119.61f)
            });

            // Position 22
            Vector3 pos22 = new Vector3(-32.44f, -2.95f, 135.06f);
            AddSentryPosition(pos22, Direction.West, "Northern District", new Vector3[]
            {
                pos22,
                new Vector3(-32.44f, -2.95f, 135.06f),
                new Vector3(-32.45f, -2.95f, 135.06f),
                new Vector3(-32.44f, -2.95f, 135.07f)
            });

            // Position 23
            Vector3 pos23 = new Vector3(-33.82f, -2.94f, 136.70f);
            AddSentryPosition(pos23, Direction.South, "Northern District", new Vector3[]
            {
                pos23,
                new Vector3(-33.82f, -2.94f, 136.70f),
                new Vector3(-33.82f, -2.94f, 136.69f),
                new Vector3(-33.81f, -2.94f, 136.70f)
            });

            // Position 24
            Vector3 pos24 = new Vector3(-14.43f, 1.07f, 88.94f);
            AddSentryPosition(pos24, Direction.South, "Northern District", new Vector3[]
            {
                pos24,
                new Vector3(-14.43f, 1.07f, 88.94f),
                new Vector3(-14.43f, 1.07f, 88.93f),
                new Vector3(-14.42f, 1.07f, 88.94f)
            });

            // MODIFIED: The max and target are now set to 10 instead of total positions count
            _maxTotalSentries = 10;
            _targetSentryCount = 10;

            // Calculate rotations based on direction
            foreach (var pos in _sentryPositions)
            {
                switch (pos.FacingDirection)
                {
                    case Direction.North:
                        pos.CalculatedRotation = Quaternion.Euler(0, 0, 0);
                        break;
                    case Direction.South:
                        pos.CalculatedRotation = Quaternion.Euler(0, 180, 0);
                        break;
                    case Direction.East:
                        pos.CalculatedRotation = Quaternion.Euler(0, 90, 0);
                        break;
                    case Direction.West:
                        pos.CalculatedRotation = Quaternion.Euler(0, 270, 0);
                        break;
                }
            }

            if (_enableDebugLogs)
            {
                _core.LoggerInstance.Msg($"Initialized {_sentryPositions.Count} sentry positions across all districts");
            }
        }

        private void AddSentryPosition(Vector3 position, Direction direction, string district, Vector3[] patrolPoints)
        {
            _sentryPositions.Add(new SentryPosition
            {
                Position = position,
                FacingDirection = direction,
                IsOccupied = false,
                District = district,
                PatrolWaypoints = patrolPoints
            });
        }

        private void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (_enableDebugLogs)
            {
                _core.LoggerInstance.Msg($"Scene loaded: {sceneName}, preparing sentry initialization");
            }

            // Reset initialization tracking
            _gameReady = false;
            _gameReadyCheckTime = 0f;
            _initFailCount = 0;
            _worldLoadedTime = Time.time;
            _isNightTime = false;
            _sentryShouldBeActive = false;
            _lastGameHour = -1;
            _isNinePMCheckDone = false;
            _isSixAMCheckDone = false;
            _dayDespawnCompleted = false; // Reset the despawn flag
            _frameCounter = 0; // Reset frame counter

            // Clean up from previous scene
            try
            {
                OnSceneUnloaded();
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error cleaning up previous scene: {ex.Message}");
            }
        }

        public void Initialize()
        {
            _nextSpawnTime = Time.time;
            _nextDespawnTime = Time.time;
            _initialized = true;
            _frameCounter = 0;
            _lastUpdateTime = Time.time;

            if (_enableDebugLogs)
            {
                _core.LoggerInstance.Msg("Sentry spawn system initialized");
            }
        }

        public void Update()
        {
            try
            {
                // Increment frame counter
                _frameCounter++;

                // Initialization check
                if (!_gameReady)
                {
                    if (Time.time > _gameReadyCheckTime)
                    {
                        _gameReadyCheckTime = Time.time + _gameReadyCheckInterval;

                        // Find network manager first
                        if (_networkManager == null)
                        {
                            _networkManager = UnityEngine.Object.FindObjectOfType<NetworkManager>();
                            if (_networkManager == null)
                            {
                                _initFailCount++;
                                if (_initFailCount % 3 == 0 && _enableDebugLogs)
                                    _core.LoggerInstance.Msg("Waiting for NetworkManager...");
                                return;
                            }
                        }

                        // Initialize if not already done
                        if (!_initialized)
                        {
                            Initialize();
                        }

                        // Check if required time has passed since world loaded
                        float timeElapsed = Time.time - _worldLoadedTime;
                        if (timeElapsed < _requiredStabilityDelay)
                        {
                            if (_initFailCount % 3 == 0 && _enableDebugLogs)
                                _core.LoggerInstance.Msg($"Waiting for world to stabilize: {timeElapsed:F1}/{_requiredStabilityDelay} seconds");
                            _initFailCount++;
                            return;
                        }

                        // Force initialization after too many failures or delay
                        if (_initFailCount >= MAX_INIT_FAILURES || timeElapsed > 20f)
                        {
                            _gameReady = true;
                            if (_enableDebugLogs) _core.LoggerInstance.Msg("Forcing sentry system initialization");
                        }
                        else
                        {
                            _gameReady = true;
                            if (_enableDebugLogs) _core.LoggerInstance.Msg("Sentry system initialized");
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                float currentTime = Time.time;

                try
                {
                    // Performance tracking
                    float startTime = Time.realtimeSinceStartup;

                    // Check time only once every 60 frames (~1 second at 60fps)
                    if (_frameCounter % 60 == 0)
                    {
                        // NEW APPROACH: Check time only once per game hour to detect 6AM and 9PM
                        CheckSpecificGameHours();
                    }

                    // Only process sentries if it's night time
                    if (_sentryShouldBeActive)
                    {
                        // Reset the despawn flag when entering night time
                        _dayDespawnCompleted = false;

                        // Fill any empty sentry positions - only check every 120 frames (~2 seconds)
                        if (_frameCounter % 120 == 0)
                        {
                            PopulateEmptySentryPositions();
                        }

                        // Process spawn queue - frame-based throttling
                        if (_frameCounter % SPAWN_FRAME_INTERVAL == 0 && _spawnQueue.Count > 0)
                        {
                            ProcessSpawnQueue();
                        }
                    }
                    else if (!_dayDespawnCompleted && _activeSentries.Count > 0)
                    {
                        // Only despawn once per day cycle when sentries should not be active
                        _core.LoggerInstance.Msg("Day time: Performing one-time sentry despawn");
                        DespawnAllSentriesImmediately();
                        _dayDespawnCompleted = true;
                    }

                    // Process despawn queue - frame-based throttling
                    if (_frameCounter % DESPAWN_FRAME_INTERVAL == 0 && _despawnQueue.Count > 0)
                    {
                        ProcessDespawnQueue();
                    }

                    // Log performance if needed - only every 600 frames (~10 seconds)
                    if (_frameCounter % 600 == 0 && _enableDebugLogs)
                    {
                        float updateTime = Time.realtimeSinceStartup - startTime;
                        if (updateTime > 0.01f) // Only log if it took more than 10ms
                        {
                            _core.LoggerInstance.Msg($"Sentry update took {updateTime * 1000:F2}ms with {_activeSentries.Count} active sentries");
                        }
                        _lastUpdateTime = Time.realtimeSinceStartup;
                    }
                }
                catch (Exception ex)
                {
                    _core.LoggerInstance.Error($"Error in sentry update: {ex.Message}\n{ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Critical error in sentry update: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // NEW: Check only for specific hours (6AM and 9PM) once per game hour
        private void CheckSpecificGameHours()
        {
            // Get game time from TimeManager
            TimeManager? timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
            if (timeManager == null) return;

            int currentTime = timeManager.CurrentTime;

            // Extract hour from game time (format is HHMM)
            int currentHour = currentTime / 100;

            // Detect day change by checking if we've wrapped around from late night to early morning
            bool dayChanged = false;
            if (_lastGameHour != -1)
            {
                // If we went from late hours (20-23) to early hours (0-6), we've changed days
                if (_lastGameHour >= 20 && currentHour <= 6)
                {
                    dayChanged = true;
                }
            }

            // Only check if hour has changed
            if (currentHour != _lastGameHour)
            {
                _lastGameHour = currentHour;

                // Reset checks when day changes
                if (dayChanged)
                {
                    _isNinePMCheckDone = false;
                    _isSixAMCheckDone = false;
                }

                // Specific 9 PM check (once per day)
                if (currentHour == 21 && !_isNinePMCheckDone)
                {
                    _core.LoggerInstance.Msg("9 PM detected. Activating sentries.");
                    _isNinePMCheckDone = true;
                    _isNightTime = true;
                    _sentryShouldBeActive = true;
                    _dayDespawnCompleted = false;
                }
                // Specific 6 AM check (once per day)
                else if (currentHour == 6 && !_isSixAMCheckDone)
                {
                    _core.LoggerInstance.Msg("6 AM detected. Deactivating sentries.");
                    _isSixAMCheckDone = true;
                    _isNightTime = false;
                    _sentryShouldBeActive = false;

                    if (!_dayDespawnCompleted && _activeSentries.Count > 0)
                    {
                        DespawnAllSentriesImmediately();
                        _dayDespawnCompleted = true;
                    }
                }

                // Special case: Game starts during night hours
                if ((_lastGameHour >= 21 || _lastGameHour < 6) && !_isNinePMCheckDone && !_isNightTime)
                {
                    _core.LoggerInstance.Msg("Night time detected on startup. Activating sentries.");
                    _isNinePMCheckDone = true;
                    _isNightTime = true;
                    _sentryShouldBeActive = true;
                    _dayDespawnCompleted = false;
                }
            }
        }

        // Method to immediately despawn all sentries (not just queue them)
        private void DespawnAllSentriesImmediately()
        {
            // Use the temporary list to avoid collection modification issues
            _tempSentryList.Clear();
            _tempSentryList.AddRange(_activeSentries);

            foreach (var sentry in _tempSentryList)
            {
                if (sentry != null && sentry.gameObject != null)
                {
                    DespawnSentry(sentry);
                }
            }

            // Clear despawn queue to avoid duplicates
            _despawnQueue.Clear();

            // Log how many are left (should be 0)
            if (_activeSentries.Count > 0)
            {
                _core.LoggerInstance.Msg($"WARNING: {_activeSentries.Count} sentries still active after immediate despawn!");
            }
            else if (_enableDebugLogs)
            {
                _core.LoggerInstance.Msg("All sentries successfully despawned");
            }
        }

        // MODIFIED: Updated to ensure 1 sentry per district with remaining random
        // Also now properly uses _maxTotalSentries to prevent warning
        private void PopulateEmptySentryPositions()
        {
            // Check if we need to spawn more sentries
            int currentCount = _activeSentries.Count;

            // Add check for maximum sentries - use _maxTotalSentries here
            if (currentCount >= _targetSentryCount || currentCount >= _maxTotalSentries)
                return;

            // Get list of unique districts
            HashSet<string> districts = new HashSet<string>();
            foreach (var position in _sentryPositions)
            {
                districts.Add(position.District);
            }

            // Step 1: Ensure at least one sentry per district
            foreach (string district in districts)
            {
                // Stop if we've reached the hard maximum
                if (_activeSentries.Count >= _maxTotalSentries)
                    break;

                // Check if this district already has a sentry
                bool hasActiveSentry = false;
                foreach (var pos in _sentryPositions)
                {
                    if (pos.District == district && pos.IsOccupied)
                    {
                        hasActiveSentry = true;
                        break;
                    }
                }

                // If no active sentry in this district, spawn one
                if (!hasActiveSentry)
                {
                    // Find first available position in this district
                    foreach (var pos in _sentryPositions)
                    {
                        if (pos.District == district && !pos.IsOccupied)
                        {
                            // Queue this position to be filled
                            Vector3 spawnPos = FindSpawnPositionNear(pos.Position);
                            QueueSentrySpawn(spawnPos, pos);

                            // Break after finding one position
                            break;
                        }
                    }
                }
            }

            // Step 2: Fill remaining slots randomly up to the target count
            int remainingToSpawn = Math.Min(_targetSentryCount, _maxTotalSentries) - Math.Max(currentCount, districts.Count);

            if (remainingToSpawn > 0)
            {
                // Get all unoccupied positions
                List<SentryPosition> availablePositions = _sentryPositions
                    .Where(p => !p.IsOccupied)
                    .ToList();

                // Shuffle the list using Fisher-Yates algorithm
                for (int i = availablePositions.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    var temp = availablePositions[i];
                    availablePositions[i] = availablePositions[j];
                    availablePositions[j] = temp;
                }

                // Queue positions to fill remaining slots
                for (int i = 0; i < Math.Min(remainingToSpawn, availablePositions.Count); i++)
                {
                    Vector3 spawnPos = FindSpawnPositionNear(availablePositions[i].Position);
                    QueueSentrySpawn(spawnPos, availablePositions[i]);
                }
            }
        }

        private Vector3 FindSpawnPositionNear(Vector3 targetPos)
        {
            // Try to find a spawn point 15-20 units away from the target position
            float distance = UnityEngine.Random.Range(15f, 20f);
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);

            Vector3 spawnOffset = new Vector3(
                Mathf.Cos(angle) * distance,
                0,
                Mathf.Sin(angle) * distance
            );

            Vector3 spawnPos = targetPos + spawnOffset;

            // Try to find valid NavMesh position
            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 10f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            // Fallback - use original position with slight offset
            return targetPos + new Vector3(
                UnityEngine.Random.Range(-3f, 3f),
                0,
                UnityEngine.Random.Range(-3f, 3f)
            );
        }

        private void QueueSentrySpawn(Vector3 spawnPosition, SentryPosition targetPosition)
        {
            var job = new SentrySpawnJob
            {
                SpawnPosition = spawnPosition,
                TargetPosition = targetPosition,
                Priority = 1f,
                QueuedTime = DateTime.Now
            };

            _spawnQueue.Enqueue(job);
        }

        private void ProcessSpawnQueue()
        {
            if (_spawnQueue.Count == 0) return;

            var nextJob = _spawnQueue.Dequeue();

            // Check if the position is still available
            if (nextJob.TargetPosition.IsOccupied)
                return;

            // Spawn the sentry
            SpawnSentry(nextJob.SpawnPosition, nextJob.TargetPosition);

            // Update spawn time - use frame-based approach
            _nextSpawnTime = Time.time + _spawnCooldown;
        }

        private bool SpawnSentry(Vector3 spawnPosition, SentryPosition targetPosition)
        {
            try
            {
                if (_networkManager == null) return false;

                // Mark position as occupied
                targetPosition.IsOccupied = true;

                // Get a random officer type for the sentry
                string sentryType = SentryTypes[UnityEngine.Random.Range(0, SentryTypes.Length)];

                // Find the prefab
                NetworkObject? prefab = null;
                var spawnablePrefabs = _networkManager.SpawnablePrefabs;

                for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++)
                {
                    var obj = spawnablePrefabs.GetObject(true, i);
                    if (obj != null && obj.gameObject.name == sentryType)
                    {
                        prefab = obj;
                        break;
                    }
                }

                if (prefab == null)
                {
                    _core.LoggerInstance.Error($"Could not find prefab for {sentryType}");
                    targetPosition.IsOccupied = false;
                    return false;
                }

                // Create a patrol route with unique ID for this sentry
                string uniqueId = Guid.NewGuid().ToString();
                var patrolName = $"SentryRoute_{uniqueId}";
                var patrolRouteGO = new GameObject(patrolName);
                patrolRouteGO.transform.position = spawnPosition;

                // Store in our reference dictionary
                _patrolRouteObjects[patrolName] = patrolRouteGO;

                var patrolRoute = patrolRouteGO.AddComponent<FootPatrolRoute>();
                patrolRoute.RouteName = patrolName;
                patrolRoute.StartWaypointIndex = 0;

                // Create waypoints using + shaped pattern (MODIFIED)
                CreateSentryPatrolWithFixedPoints(patrolRouteGO, spawnPosition, targetPosition);

                // Create patrol group
                var newPatrolGroup = new PatrolGroup(patrolRoute);

                // Create the sentry
                var newOfficerGO = UnityEngine.Object.Instantiate(prefab);
                newOfficerGO.gameObject.name = $"{sentryType}_Sentry_{uniqueId}";

                // Position and activate
                newOfficerGO.transform.position = spawnPosition;

                // Set rotation according to facing direction
                newOfficerGO.transform.rotation = targetPosition.CalculatedRotation;

                newOfficerGO.gameObject.SetActive(true);

                // Get officer component
                var spawnedSentry = newOfficerGO.gameObject.GetComponent<PoliceOfficer>();
                if (spawnedSentry == null)
                {
                    _core.LoggerInstance.Error("Failed to get PoliceOfficer component from instantiated sentry");
                    UnityEngine.Object.Destroy(patrolRouteGO);
                    UnityEngine.Object.Destroy(newOfficerGO);
                    targetPosition.IsOccupied = false;
                    return false;
                }

                spawnedSentry.Activate();

                // Store the route name with this sentry
                _sentryRouteNames[spawnedSentry] = patrolName;

                // Start patrol
                spawnedSentry.StartFootPatrol(newPatrolGroup, true); // true = loop the patrol

                // Spawn on network
                _networkManager.ServerManager.Spawn(newOfficerGO);

                // Add to active sentries
                _activeSentries.Add(spawnedSentry);

                // Set as current sentry for this position
                targetPosition.CurrentSentry = spawnedSentry;

                if (_enableDebugLogs)
                {
                    _core.LoggerInstance.Msg($"Spawned sentry at {targetPosition.Position} in {targetPosition.District}");
                }
                return true;
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error spawning sentry: {ex.Message}");
                targetPosition.IsOccupied = false;
                return false;
            }
        }

        private void SafeSetProperty(object obj, string propertyName, object value)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(obj, value);
                }
            }
            catch { }
        }

        // MODIFIED: Use + shaped patrol pattern with 1 meter in each direction
        private void CreateSentryPatrolWithFixedPoints(GameObject routeGO, Vector3 spawnPos, SentryPosition targetPosition)
        {
            try
            {
                List<Transform> waypoints = new List<Transform>();

                // Add spawn point as first waypoint (approach path)
                var spawnWaypointGO = new GameObject("Waypoint_0");
                spawnWaypointGO.transform.position = spawnPos;
                spawnWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(spawnWaypointGO.transform);

                // Get the target position (center of the + pattern)
                Vector3 centerPos = targetPosition.Position;

                // Create + shaped pattern, with each point 1 meter from center
                var centerWaypointGO = new GameObject("Waypoint_Center");
                centerWaypointGO.transform.position = centerPos;
                centerWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(centerWaypointGO.transform);

                // Forward point (+Z)
                var forwardWaypointGO = new GameObject("Waypoint_Forward");
                forwardWaypointGO.transform.position = centerPos + new Vector3(0, 0, 1f);
                forwardWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(forwardWaypointGO.transform);

                // Back to center
                waypoints.Add(centerWaypointGO.transform);

                // Backward point (-Z)
                var backwardWaypointGO = new GameObject("Waypoint_Backward");
                backwardWaypointGO.transform.position = centerPos + new Vector3(0, 0, -1f);
                backwardWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(backwardWaypointGO.transform);

                // Back to center
                waypoints.Add(centerWaypointGO.transform);

                // Left point (-X)
                var leftWaypointGO = new GameObject("Waypoint_Left");
                leftWaypointGO.transform.position = centerPos + new Vector3(-1f, 0, 0);
                leftWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(leftWaypointGO.transform);

                // Back to center
                waypoints.Add(centerWaypointGO.transform);

                // Right point (+X)
                var rightWaypointGO = new GameObject("Waypoint_Right");
                rightWaypointGO.transform.position = centerPos + new Vector3(1f, 0, 0);
                rightWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(rightWaypointGO.transform);

                // Set up route with waypoints
                var route = routeGO.GetComponent<FootPatrolRoute>();
                route.Waypoints = new Il2CppReferenceArray<Transform>(waypoints.Count);

                // Assign waypoints to the array
                for (int i = 0; i < waypoints.Count; i++)
                {
                    route.Waypoints[i] = waypoints[i];
                }

                route.UpdateWaypoints();
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error creating sentry waypoints: {ex.Message}");
            }
        }

        private void ProcessDespawnQueue()
        {
            if (_despawnQueue.Count == 0) return;

            // Process a batch of despawns to avoid spiking the frame
            int count = Math.Min(_despawnQueue.Count, 2); // Process up to 2 at a time

            for (int i = 0; i < count; i++)
            {
                var sentry = _despawnQueue.Dequeue();
                DespawnSentry(sentry);
            }

            // Update despawn time - use frame-based approach
            _nextDespawnTime = Time.time + _despawnCooldown;
        }

        private void DespawnSentry(PoliceOfficer sentry)
        {
            if (sentry == null) return;

            try
            {
                // Find which position this sentry is occupying
                SentryPosition? sentryPosition = null;
                foreach (var pos in _sentryPositions)
                {
                    if (pos.CurrentSentry == sentry)
                    {
                        sentryPosition = pos;
                        break;
                    }
                }

                if (sentryPosition != null)
                {
                    sentryPosition.IsOccupied = false;
                    sentryPosition.CurrentSentry = null; // FIX: Can now set to null since it's nullable
                }

                // Remove from active sentries
                _activeSentries.Remove(sentry);

                // Clean up route
                if (_sentryRouteNames.TryGetValue(sentry, out string? routeName) && routeName != null)
                {
                    if (_patrolRouteObjects.TryGetValue(routeName, out GameObject? routeGO) && routeGO != null)
                    {
                        UnityEngine.Object.Destroy(routeGO);
                        _patrolRouteObjects.Remove(routeName);
                    }
                    _sentryRouteNames.Remove(sentry);
                }

                // Deactivate and destroy
                sentry.Deactivate();
                UnityEngine.Object.Destroy(sentry.gameObject);
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error despawning sentry: {ex.Message}");

                // Fallback destruction
                try
                {
                    if (sentry != null && sentry.gameObject != null)
                    {
                        sentry.Deactivate();
                        UnityEngine.Object.Destroy(sentry.gameObject);
                    }
                }
                catch { }
            }
        }

        // Enable this only for debug purposes, keeping it for future reference
        private void LogSentryCounts()
        {
            if (!_enableDebugLogs) return;

            // Group sentries by district
            var districtCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var districtTotals = new Dictionary<string, int>(StringComparer.Ordinal);

            // Count occupied positions by district
            foreach (var position in _sentryPositions)
            {
                if (!districtTotals.ContainsKey(position.District))
                {
                    districtTotals[position.District] = 0;
                    districtCounts[position.District] = 0;
                }

                districtTotals[position.District]++;

                if (position.IsOccupied)
                {
                    districtCounts[position.District]++;
                }
            }

            int totalOccupied = _sentryPositions.Count(p => p.IsOccupied);
            int totalPositions = _sentryPositions.Count;

            // Log the summary
            _core.LoggerInstance.Msg($"Sentry population: {totalOccupied}/{totalPositions} positions filled");

            // Log each district
            foreach (var district in districtTotals.Keys.OrderBy(d => d))
            {
                int percentage = (districtCounts[district] * 100) / Math.Max(1, districtTotals[district]);
                _core.LoggerInstance.Msg($"  {district}: {districtCounts[district]}/{districtTotals[district]} sentries ({percentage}%)");
            }
        }

        public void OnSceneUnloaded()
        {
            try
            {
                if (_enableDebugLogs)
                {
                    _core.LoggerInstance.Msg("Sentry spawn system beginning cleanup");
                }

                // Reset initialization variables
                _gameReady = false;
                _gameReadyCheckTime = 0f;
                _initFailCount = 0;
                _isNightTime = false;
                _sentryShouldBeActive = false;
                _lastGameHour = -1;
                _isNinePMCheckDone = false;
                _isSixAMCheckDone = false;
                _dayDespawnCompleted = false; // Reset the despawn flag

                // Clear queues
                _spawnQueue.Clear();
                _despawnQueue.Clear();

                // Clean up existing sentries - use temp list to avoid collection modification
                _tempSentryList.Clear();
                _tempSentryList.AddRange(_activeSentries);

                foreach (var sentry in _tempSentryList)
                {
                    try
                    {
                        if (sentry?.gameObject != null)
                        {
                            sentry.Deactivate();
                            UnityEngine.Object.Destroy(sentry.gameObject);
                        }
                    }
                    catch (Exception ex)
                    {
                        _core.LoggerInstance.Error($"Error cleaning up sentry: {ex.Message}");
                    }
                }

                // Clean up patrol routes
                foreach (var entry in new Dictionary<string, GameObject>(_patrolRouteObjects))
                {
                    if (entry.Value != null)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(entry.Value);
                        }
                        catch { }
                    }
                }
                _patrolRouteObjects.Clear();

                // Clear dictionaries and lists
                _activeSentries.Clear();
                _sentryRouteNames.Clear();
                _tempSentryList.Clear();

                // Reset sentry position occupied status
                foreach (var position in _sentryPositions)
                {
                    position.IsOccupied = false;
                    position.CurrentSentry = null; // Now properly nullable
                }

                // Reset variables
                _initialized = false;
                _networkManager = null; // FIX: Can now set to null since it's nullable

                // Garbage collection
                GC.Collect();

                if (_enableDebugLogs)
                {
                    _core.LoggerInstance.Msg("Sentry spawn system completely reset");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error in OnSceneUnloaded: {ex.Message}");
            }
        }

        // Public method to enable/disable debug logs
        public void SetDebugLogging(bool enable)
        {
            _enableDebugLogs = enable;
            _core.LoggerInstance.Msg($"Sentry debug logging {(enable ? "enabled" : "disabled")}");
        }
    }
}