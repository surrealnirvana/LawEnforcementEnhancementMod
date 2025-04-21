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
using Il2CppInterop.Runtime.Injection;

namespace LawEnforcementEnhancementMod
{
    // Tag component to identify officers spawned by our mod
    [RegisterTypeInIl2Cpp]
    public class ModSpawnedOfficerTag : MonoBehaviour
    {
        // Empty component that serves as a tag
    }

    public class OfficerSpawnSystem
    {
        // IMPROVED INITIALIZATION VARIABLES
        private bool _gameReady = false;
        private float _gameReadyCheckTime = 0f;
        private float _gameReadyCheckInterval = 1.0f;
        private int _initFailCount = 0;
        private const int MAX_INIT_FAILURES = 5;
        private float _worldLoadedTime = 0f;
        private float _requiredStabilityDelay = 3.0f;
        private bool _playerMovementDetected = false;
        private Vector3 _lastCheckedPosition = Vector3.zero;

        // State tracking for district population
        private enum DistrictPopulationState
        {
            InitialCheck,        // Initial check to log needed officers
            InitialPopulation,   // First round of population in progress
            VerificationCheck,   // Verifying all districts have officers
            MaintenanceMode      // Districts are full, periodic maintenance checks
        }

        private DistrictPopulationState _populationState = DistrictPopulationState.InitialCheck;
        private bool _needsStatusUpdate = true; // Flag to indicate if we need to log status
        private DateTime _lastPopulationStateChangeTime = DateTime.Now;
        private DateTime _lastLogDisplayTime = DateTime.MinValue;
        private bool _enableDistrictDebugLogs = false; // Set to false by default to minimize logging

        // NEW: Console logging toggle (default to OFF)
        private bool _enableConsoleLogs = false;

        // CHANGE: Remove Officer Davis from spawn pool
        private readonly string[] OfficerTypes = new string[]
        {
            "OfficerBailey", "OfficerCooper",
            "OfficerGreen", "OfficerHoward", "OfficerJackson",
            "OfficerLee", "OfficerLeo", "OfficerLopez",
            "OfficerMurphy", "OfficerOakley", "OfficerSanchez"
        };

        // Officer Pool
        private readonly Queue<PooledOfficer> _officerPool = new Queue<PooledOfficer>();
        private const int MAX_POOLED_OFFICERS = 10;

        private readonly Core _core;
        private NetworkManager? _networkManager;
        private bool _initialized;
        private bool _gameFullyLoaded = false;

        // UPDATED DEFAULT VALUES per feedback
        private float _spawnCooldown = 4.5f; // Spawn one officer every 3.5 seconds
        private float _nextSpawnTime = 0f;
        private float _despawnCooldown = 12f; // Despawn one officer every 10 seconds
        private float _nextDespawnTime = 0f;

        // New limit for local officers
        private float _localOfficerRadius = 90f; // Consider officers within 100m of player "local"
        private int _maxLocalOfficers = 8; // Maximum officers near player

        // NEW: Per district officer maximums (configurable)
        private Dictionary<string, int> _districtMaxOfficers = new Dictionary<string, int>();

        // NEW: Day change detection for despawning (using same code as SentrySpawnSystem)
        private int _lastCheckedGameTime = -1; // Track game time to detect jumps
        private bool _dayDespawnCompleted = false; // Flag to track day despawn
        private float _lastTimeCheckTime = 0f;
        private float _timeCheckInterval = 10f; // Check game time every 10 seconds

        // Districts maintenance interval
        private float _normalDistrictMaintenanceInterval = 35f;  // Normal interval (30 seconds)
        private float _reducedDistrictMaintenanceInterval = 350f; // Reduced interval (5 minutes)
        private float _districtMaintenanceInterval = 30f; // Will be set in Initialize()
        private float _lastDistrictMaintenanceTime = 0f;
        private float _lastReconciliationTime = 0f;  // NEW: For tracking when we last reconciled officer counts
        private float _reconciliationInterval = 15f; // NEW: Reconcile every 15 seconds

        // District report tracking
        private DateTime _lastFullDistrictCheckTime = DateTime.MinValue;

        // Intervals
        private float _playerCheckInterval = 6f; // Check player position every 5 seconds
        private float _lastPlayerCheckTime = 0f;
        private float _districtTransitionCooldown = 20f; // Minimum time between district processing
        private float _lastDistrictTransitionTime = 0f;

        // Player tracking
        private Vector3 _lastPlayerPosition = Vector3.zero;
        private District? _currentPlayerDistrict = null;
        private District? _previousPlayerDistrict = null;

        // Officer tracking
        private readonly List<PoliceOfficer> _activeOfficers = new List<PoliceOfficer>();
        private readonly Queue<SpawnJob> _spawnQueue = new Queue<SpawnJob>();
        private readonly Queue<PoliceOfficer> _despawnQueue = new Queue<PoliceOfficer>();

        // NEW: Stuck officer detection
        private Dictionary<PoliceOfficer, Vector3> _lastOfficerPositions = new Dictionary<PoliceOfficer, Vector3>();
        private Dictionary<PoliceOfficer, int> _officerStuckCounter = new Dictionary<PoliceOfficer, int>();
        private float _lastStuckCheckTime = 0f;
        private float _stuckCheckInterval = 10f; // Check for stuck officers every 10 seconds
        private const int STUCK_THRESHOLD = 3; // Number of consecutive checks before considering an officer stuck

        // 9PM Settings
        private bool _isNinePMMaxSpawnTriggered = false;
        private float _lastNinePMCheckTime = 0f;

        // District management
        private readonly List<District> _districts = new List<District>();

        // Time-based officer count limits - UPDATED defaults
        private int _morningOfficerLimit = 8;   // 6:00 AM - 12:00 PM (was 10)
        private int _afternoonOfficerLimit = 14; // 12:00 PM - 6:00 PM (was 15)
        private int _eveningOfficerLimit = 20;   // 6:00 PM - 9:00 PM (was 30)
        private int _nightOfficerLimit = 24;     // 9:00 PM - 6:00 AM (was 60)

        // Time tracking for despawn during day
        private bool _isNightTime = false;
        private bool _officersShouldBeActive = false;

        // Config file path
        private readonly string _configFilePath;

        // District persistence - only despawn officers when absolutely necessary
        private bool _preserveDistrictOfficers = true;

        // Batch spawning - only 1 at a time to prevent hitching
        private int _maxSpawnsPerCycle = 1;
        private int _maxDespawnsPerCycle = 1;

        // District transitions
        private int _districtTransitionBuffer = 2; // Extra officers to spawn in new district

        // Patrol settings - updated defaults
        private float _minPatrolRadius = 20f;
        private float _maxPatrolRadius = 40f;
        private int _minWaypoints = 5;
        private int _maxWaypoints = 8;
        private float _waypointHeightOffset = 0.2f; // Small Y offset to avoid ground clipping

        // Baseline officers - spawn these first in every district
        private bool _populateAllDistrictsOnStartup = true;
        private bool _preloadAllDistrictOfficers = false; // Changed default to false - use delayed spawning instead
        private float _delayBeforeSpawning = 15f; // Wait 10 seconds before starting to spawn officers

        // Delayed spawn timer
        private float _startupDelayTimer = 0f;
        private bool _startupSpawnInitiated = false;

        // New variables for district population tracking and spawn timing
        private bool _districtsFullyPopulated = false;
        private float _lastGameSpawnCheck = 0f;
        private float _gameSpawnAvoidanceWindow = 0.5f; // Time in seconds to wait after detecting game spawning
        private bool _isGameCurrentlySpawning = false;
        private int _consecutiveSpawnFailures = 0;
        private const int MAX_SPAWN_FAILURES = 3;

        // Dictionary to track officer patrol route names
        private Dictionary<PoliceOfficer, string> _officerRouteNames = new Dictionary<PoliceOfficer, string>();

        // Frame-based throttling
        private int _frameCounter = 0;
        private const int SPAWN_FRAME_INTERVAL = 6;  // Only spawn every 6 frames
        private const int MAINTENANCE_FRAME_INTERVAL = 30;  // Check districts less frequently

        // New variables for single officer spawning and district tracking
        private bool _currentlySpawningOfficer = false; // Flag to ensure only one officer spawns at a time
        private int _districtOfficerCount = 0; // Count of officers spawned for district population
        private int _maxDistrictOfficers; // Maximum number of officers across all districts

        // Add a reference counter to help with memory cleanup
        private Dictionary<string, GameObject> _patrolRouteObjects = new Dictionary<string, GameObject>();

        // NEW: Add patrol pattern types for variety
        private enum PatrolPatternType
        {
            Circle,
            Grid,
            Zigzag,
            Star,
            Random
        }

        // Officer pooling class
        private class PooledOfficer
        {
            public GameObject? GameObject { get; set; }
            public PoliceOfficer? Officer { get; set; }
            public NetworkObject? NetworkObject { get; set; }
            public DateTime PooledTime { get; set; }
        }

        public class SpawnJob
        {
            public Vector3 Position { get; set; }
            public District? District { get; set; }
            public float Priority { get; set; } = 1f;
            public DateTime QueuedTime { get; set; } = DateTime.Now;
        }

        public class District
        {
            public string Name { get; set; } = string.Empty;
            public Vector3 Center { get; set; }
            public float Radius { get; set; }
            public List<Vector3> SpawnPoints { get; set; } = new List<Vector3>();
            public List<PoliceOfficer> OfficersSpawned { get; set; } = new List<PoliceOfficer>();
            public int TargetOfficers { get; set; }  // Simplified: consistent across all districts
            public DateTime LastVisited { get; set; } = DateTime.Now;

            public bool ContainsPoint(Vector3 point)
            {
                return Vector3.Distance(Center, point) <= Radius;
            }

            // Overlap check to handle border areas
            public bool OverlapsWith(District other, float buffer = 10f)
            {
                return Vector3.Distance(Center, other.Center) <= (Radius + other.Radius + buffer);
            }
        }

        // UPDATED: Simplified config class without redundant parameters
        public class SpawnSystemConfig
        {
            // Basic configuration
            public float SpawnCooldown { get; set; } = 4.5f;
            public float DespawnCooldown { get; set; } = 12f;
            public float PlayerCheckInterval { get; set; } = 6f;
            public float DistrictTransitionCooldown { get; set; } = 20f;
            public bool PreserveDistrictOfficers { get; set; } = true;
            public int MaxSpawnsPerCycle { get; set; } = 1;
            public int MaxDespawnsPerCycle { get; set; } = 1;
            public int MaxLocalOfficers { get; set; } = 8; // Default lowered
            public float LocalOfficerRadius { get; set; } = 90f;
            public float NormalDistrictMaintenanceInterval { get; set; } = 35f;
            public float ReducedDistrictMaintenanceInterval { get; set; } = 350f;
            public int DistrictTransitionBuffer { get; set; } = 2;
            public float MinPatrolRadius { get; set; } = 20f;
            public float MaxPatrolRadius { get; set; } = 40f;
            public int MinPatrolWaypoints { get; set; } = 5;
            public int MaxPatrolWaypoints { get; set; } = 8;
            public bool PopulateAllDistrictsOnStartup { get; set; } = true;
            public bool PreloadAllDistrictOfficers { get; set; } = false;
            public float DelayBeforeSpawning { get; set; } = 10f;

            // Time-based officer count limits (updated defaults)
            public int MorningOfficerLimit { get; set; } = 8;
            public int AfternoonOfficerLimit { get; set; } = 14;
            public int EveningOfficerLimit { get; set; } = 20;
            public int NightOfficerLimit { get; set; } = 24;

            // Per-district officer maximums
            public Dictionary<string, int> DistrictMaxOfficers { get; set; } = new Dictionary<string, int>
            {
                { "Downtown", 4 },
                { "Eastern District", 4 },
                { "Western District", 6 },
                { "Far Western District", 6 },
                { "Northern District", 4 },
                { "Southern District", 4 }
            };

            // NEW: Logging configuration
            public bool EnableConsoleLogs { get; set; } = false;
        }

        public OfficerSpawnSystem()
        {
            _core = Core.Instance;

            // Use a unique and specific name for the config file to prevent duplicates
            _configFilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "law_user_config.json");

            LoadConfig();
            InitializeDistricts();

            // Register to game loaded event - simpler approach than scene events
            MelonEvents.OnSceneWasLoaded.Subscribe(OnSceneWasLoaded);
        }

        // NEW: Helper methods for conditional logging
        private void Log(string message)
        {
            if (_enableConsoleLogs)
            {
                _core.LoggerInstance.Msg(message);
            }
        }

        private void LogWarning(string message)
        {
            if (_enableConsoleLogs)
            {
                _core.LoggerInstance.Warning(message);
            }
        }

        // Helper method to check if an officer was spawned by our mod
        private bool IsModSpawnedOfficer(PoliceOfficer officer)
        {
            if (officer == null || officer.gameObject == null) return false;
            return officer.gameObject.GetComponent<ModSpawnedOfficerTag>() != null;
        }

        private void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Log($"Scene loaded: {sceneName}, preparing initialization");

            // Reset initialization tracking
            _gameReady = false;
            _gameReadyCheckTime = 0f;
            _initFailCount = 0;
            _playerMovementDetected = false;
            _lastCheckedPosition = Vector3.zero;
            _worldLoadedTime = Time.time; // Record when the world was loaded

            // NEW: Reset time tracking like in SentrySpawnSystem
            _lastCheckedGameTime = -1;
            _dayDespawnCompleted = false;
            _isNightTime = false;
            _officersShouldBeActive = false;

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

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonConvert.DeserializeObject<SpawnSystemConfig>(json);

                    if (config != null)
                    {
                        // Load basic configuration
                        _spawnCooldown = config.SpawnCooldown;
                        _despawnCooldown = config.DespawnCooldown;
                        _playerCheckInterval = config.PlayerCheckInterval;
                        _districtTransitionCooldown = config.DistrictTransitionCooldown;
                        _preserveDistrictOfficers = config.PreserveDistrictOfficers;
                        _maxSpawnsPerCycle = config.MaxSpawnsPerCycle;
                        _maxDespawnsPerCycle = config.MaxDespawnsPerCycle;
                        _maxLocalOfficers = config.MaxLocalOfficers;
                        _localOfficerRadius = config.LocalOfficerRadius;
                        _normalDistrictMaintenanceInterval = config.NormalDistrictMaintenanceInterval;
                        _reducedDistrictMaintenanceInterval = config.ReducedDistrictMaintenanceInterval;
                        _districtTransitionBuffer = config.DistrictTransitionBuffer;
                        _minPatrolRadius = config.MinPatrolRadius;
                        _maxPatrolRadius = config.MaxPatrolRadius;
                        _minWaypoints = config.MinPatrolWaypoints;
                        _maxWaypoints = config.MaxPatrolWaypoints;
                        _populateAllDistrictsOnStartup = config.PopulateAllDistrictsOnStartup;
                        _preloadAllDistrictOfficers = config.PreloadAllDistrictOfficers;
                        _delayBeforeSpawning = config.DelayBeforeSpawning;

                        // Time-based officer limits
                        _morningOfficerLimit = config.MorningOfficerLimit;
                        _afternoonOfficerLimit = config.AfternoonOfficerLimit;
                        _eveningOfficerLimit = config.EveningOfficerLimit;
                        _nightOfficerLimit = config.NightOfficerLimit;

                        // Load per-district officer maximums
                        if (config.DistrictMaxOfficers != null && config.DistrictMaxOfficers.Count > 0)
                        {
                            _districtMaxOfficers = config.DistrictMaxOfficers;
                        }
                        else
                        {
                            // Initialize with defaults if not in config
                            _districtMaxOfficers["Downtown"] = 10;
                            _districtMaxOfficers["Eastern District"] = 5;
                            _districtMaxOfficers["Western District"] = 8;
                            _districtMaxOfficers["Far Western District"] = 8;
                            _districtMaxOfficers["Northern District"] = 10;
                            _districtMaxOfficers["Southern District"] = 6;
                        }

                        // NEW: Load console logging setting
                        _enableConsoleLogs = config.EnableConsoleLogs;

                        Log($"Loaded officer spawn system configuration from {_configFilePath}");
                    }
                }
                else
                {
                    SaveDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Failed to load config: {ex.Message}");
                SaveDefaultConfig();
            }
        }

        private void SaveDefaultConfig()
        {
            try
            {
                // Initialize default per-district officer maximums
                if (_districtMaxOfficers.Count == 0)
                {
                    _districtMaxOfficers["Downtown"] = 4;
                    _districtMaxOfficers["Eastern District"] = 4;
                    _districtMaxOfficers["Western District"] = 6;
                    _districtMaxOfficers["Far Western District"] = 6;
                    _districtMaxOfficers["Northern District"] = 4;
                    _districtMaxOfficers["Southern District"] = 4;
                }

                var config = new SpawnSystemConfig
                {
                    SpawnCooldown = _spawnCooldown,
                    DespawnCooldown = _despawnCooldown,
                    PlayerCheckInterval = _playerCheckInterval,
                    DistrictTransitionCooldown = _districtTransitionCooldown,
                    PreserveDistrictOfficers = _preserveDistrictOfficers,
                    MaxSpawnsPerCycle = _maxSpawnsPerCycle,
                    MaxDespawnsPerCycle = _maxDespawnsPerCycle,
                    MaxLocalOfficers = _maxLocalOfficers,
                    LocalOfficerRadius = _localOfficerRadius,
                    NormalDistrictMaintenanceInterval = _normalDistrictMaintenanceInterval,
                    ReducedDistrictMaintenanceInterval = _reducedDistrictMaintenanceInterval,
                    DistrictTransitionBuffer = _districtTransitionBuffer,
                    MinPatrolRadius = _minPatrolRadius,
                    MaxPatrolRadius = _maxPatrolRadius,
                    MinPatrolWaypoints = _minWaypoints,
                    MaxPatrolWaypoints = _maxWaypoints,
                    PopulateAllDistrictsOnStartup = _populateAllDistrictsOnStartup,
                    PreloadAllDistrictOfficers = _preloadAllDistrictOfficers,
                    DelayBeforeSpawning = _delayBeforeSpawning,

                    // Time-based officer limits (updated defaults)
                    MorningOfficerLimit = _morningOfficerLimit,
                    AfternoonOfficerLimit = _afternoonOfficerLimit,
                    EveningOfficerLimit = _eveningOfficerLimit,
                    NightOfficerLimit = _nightOfficerLimit,

                    // Per-district officer maximums
                    DistrictMaxOfficers = _districtMaxOfficers,

                    // NEW: Console logging setting
                    EnableConsoleLogs = _enableConsoleLogs
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                Log($"Created default configuration at {_configFilePath}");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Failed to save default config: {ex.Message}");
            }
        }

        private void InitializeDistricts()
        {
            // Clear any existing districts
            _districts.Clear();

            // Define Downtown District (Central area)
            var downtown = new District
            {
                Name = "Downtown",
                Center = new Vector3(0, 0, 40),
                Radius = 50,
                TargetOfficers = GetDistrictMaxOfficers("Downtown"),
                SpawnPoints = new List<Vector3>()
            };

            // Define Eastern District
            var eastern = new District
            {
                Name = "Eastern District",
                Center = new Vector3(100, 0, 0),
                Radius = 120,
                TargetOfficers = GetDistrictMaxOfficers("Eastern District"),
                SpawnPoints = new List<Vector3>()
            };

            // Define Western District
            var western = new District
            {
                Name = "Western District",
                Center = new Vector3(-80, 0, 0),
                Radius = 80,
                TargetOfficers = GetDistrictMaxOfficers("Western District"),
                SpawnPoints = new List<Vector3>()
            };

            // Define Far Western District
            var farWestern = new District
            {
                Name = "Far Western District",
                Center = new Vector3(-150, 0, 80),
                Radius = 80,
                TargetOfficers = GetDistrictMaxOfficers("Far Western District"),
                SpawnPoints = new List<Vector3>()
            };

            // Define Northern District
            var northern = new District
            {
                Name = "Northern District",
                Center = new Vector3(0, 0, 120),
                Radius = 80,
                TargetOfficers = GetDistrictMaxOfficers("Northern District"),
                SpawnPoints = new List<Vector3>()
            };

            // Define Southern District
            var southern = new District
            {
                Name = "Southern District",
                Center = new Vector3(0, 0, -80),
                Radius = 100,
                TargetOfficers = GetDistrictMaxOfficers("Southern District"),
                SpawnPoints = new List<Vector3>()
            };

            // Add districts to list for tracking
            _districts.Add(downtown);
            _districts.Add(eastern);
            _districts.Add(western);
            _districts.Add(farWestern);
            _districts.Add(northern);
            _districts.Add(southern);

            // UPDATED: New spawn points from user
            Vector3[] allSpawnPoints = new Vector3[]
            {
                new Vector3(-71.45f, 0.97f, 75.85f),
                new Vector3(-71.47f, -1.30f, 46.21f),
                new Vector3(-22.20f, 1.07f, 72.23f),
                new Vector3(-22.32f, 1.07f, 101.65f),
                new Vector3(-29.85f, -2.94f, 137.92f),
                new Vector3(-48.01f, -4.03f, 167.29f),
                new Vector3(-37.16f, -3.03f, 168.80f),
                new Vector3(-78.48f, -2.10f, 113.70f),
                new Vector3(-73.82f, -2.94f, 136.79f),
                new Vector3(-102.90f, -2.95f, 134.23f),
                new Vector3(-105.01f, -2.39f, 118.26f),
                new Vector3(-131.49f, -2.77f, 130.55f),
                new Vector3(-162.51f, -3.03f, 133.09f),
                new Vector3(-167.84f, -2.94f, 84.17f),
                new Vector3(-147.63f, -2.96f, 60.41f),
                new Vector3(-140.34f, -2.94f, 37.37f),
                new Vector3(-122.01f, -3.03f, 27.40f),
                new Vector3(-135.37f, -2.96f, 60.58f),
                new Vector3(-118.52f, -2.94f, 77.56f),
                new Vector3(-143.94f, -3.03f, 85.16f),
                new Vector3(-130.27f, -3.03f, 98.29f),
                new Vector3(-148.46f, -3.01f, 106.75f),
                new Vector3(-140.68f, -3.03f, 116.80f),
                new Vector3(-110.72f, -2.93f, 87.72f),
                new Vector3(-47.86f, 1.07f, 61.94f),
                new Vector3(10.40f, 0.97f, 78.10f),
                new Vector3(29.06f, 1.02f, 100.08f),
                new Vector3(56.68f, 1.07f, 83.01f),
                new Vector3(74.79f, 1.02f, 98.62f),
                new Vector3(111.30f, 1.07f, 72.78f),
                new Vector3(127.86f, 1.07f, 86.50f),
                new Vector3(137.50f, 1.02f, 63.43f),
                new Vector3(127.37f, 0.97f, 43.81f),
                new Vector3(101.93f, 1.06f, 29.70f),
                new Vector3(82.99f, 1.07f, 33.83f),
                new Vector3(99.24f, 0.98f, 2.68f),
                new Vector3(126.18f, 1.03f, 16.27f),
                new Vector3(128.73f, 1.14f, 3.58f),
                new Vector3(98.60f, 0.95f, -16.96f),
                new Vector3(75.62f, 1.07f, 0.43f),
                new Vector3(55.74f, 1.17f, -0.70f),
                new Vector3(49.81f, 1.07f, 18.16f),
                new Vector3(24.19f, 0.97f, 20.85f),
                new Vector3(-10.99f, 0.97f, 7.65f),
                new Vector3(-23.35f, 1.07f, -19.58f),
                new Vector3(-11.51f, 0.98f, -52.11f),
                new Vector3(-38.18f, -1.41f, -65.73f),
                new Vector3(-50.05f, -1.54f, -53.82f),
                new Vector3(-70.17f, -1.53f, -36.96f),
                new Vector3(-12.13f, 0.91f, -79.86f),
                new Vector3(62.28f, 4.96f, -94.93f),
                new Vector3(90.62f, 4.96f, -111.36f),
                new Vector3(105.26f, 5.05f, -111.22f),
                new Vector3(138.35f, 4.96f, -97.37f),
                new Vector3(97.69f, 4.96f, -78.33f)
            };

            // Assign each spawn point to the nearest district
            foreach (var spawnPoint in allSpawnPoints)
            {
                District? closestDistrict = null;
                float closestDistance = float.MaxValue;

                foreach (var district in _districts)
                {
                    float distance = Vector3.Distance(spawnPoint, district.Center);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestDistrict = district;
                    }
                }

                if (closestDistrict != null)
                {
                    closestDistrict.SpawnPoints.Add(spawnPoint);
                }
            }

            // Calculate total district officers needed
            _maxDistrictOfficers = _districts.Sum(d => d.TargetOfficers);

            Log($"Initialized {_districts.Count} districts with {_districts.Sum(d => d.SpawnPoints.Count)} total spawn points");

            // Log all districts and their spawn point counts
            foreach (var district in _districts)
            {
                Log($"  {district.Name}: {district.SpawnPoints.Count} spawn points, target {district.TargetOfficers} officers");
            }

            Log($"District population requires {_maxDistrictOfficers} officers");
        }

        // Helper method to get district maximum officers
        private int GetDistrictMaxOfficers(string districtName)
        {
            // If there's a specific setting for this district, use it
            if (_districtMaxOfficers.ContainsKey(districtName))
            {
                return _districtMaxOfficers[districtName];
            }

            // Otherwise use a safe default
            return 4;
        }

        // Method to update district target officers based on time
        private void UpdateDistrictTargetOfficers()
        {
            // Get current officer limit based on time
            int currentLimit = GetCurrentOfficerLimit();

            // Calculate how many officers per district (round robin distribution)
            int officersPerDistrict = currentLimit / _districts.Count;
            int remainingOfficers = currentLimit % _districts.Count;

            Log($"Updating district targets: {currentLimit} total officers, {officersPerDistrict} base per district, {remainingOfficers} remaining");

            // Update each district target
            for (int i = 0; i < _districts.Count; i++)
            {
                District district = _districts[i];

                // Base officers for all districts
                int targetForDistrict = officersPerDistrict;

                // Add one extra officer to each district until we've distributed all remaining
                if (i < remainingOfficers)
                {
                    targetForDistrict++;
                }

                // Check if we're exceeding district max
                int districtMax = GetDistrictMaxOfficers(district.Name);
                if (targetForDistrict > districtMax)
                {
                    targetForDistrict = districtMax;
                }

                // Update the district target
                district.TargetOfficers = targetForDistrict;

                Log($"  {district.Name}: Target set to {targetForDistrict} officers");
            }

            // Recalculate max district officers
            _maxDistrictOfficers = _districts.Sum(d => d.TargetOfficers);
            Log($"Updated district targets, total: {_maxDistrictOfficers}");
        }

        private float _lastUpdateTime;

        public void Initialize()
        {
            _lastUpdateTime = Time.time;
            _lastPlayerCheckTime = Time.time;
            _lastNinePMCheckTime = Time.time;
            _lastTimeCheckTime = Time.time;
            _lastDistrictTransitionTime = Time.time;
            _lastDistrictMaintenanceTime = Time.time;
            _lastReconciliationTime = Time.time;
            _lastStuckCheckTime = Time.time;
            _lastFullDistrictCheckTime = DateTime.Now;
            _lastLogDisplayTime = DateTime.Now;
            _initialized = true;
            _startupSpawnInitiated = false;
            _startupDelayTimer = 0f;
            _districtMaintenanceInterval = _normalDistrictMaintenanceInterval;
            _populationState = DistrictPopulationState.InitialCheck;
            _needsStatusUpdate = true;
            _lastPopulationStateChangeTime = DateTime.Now;
            _lastCheckedGameTime = -1;
            _dayDespawnCompleted = false;
            _isNightTime = false;
            _officersShouldBeActive = false;

            // Initially update district targets based on current time
            UpdateDistrictTargetOfficers();

            Log("District-based officer spawn system initialized");
        }

        // New method to safely queue officers after game is loaded
        private void InitiateStartupSpawning()
        {
            if (_startupSpawnInitiated) return;

            Log("Game has stabilized - beginning district population");
            _startupSpawnInitiated = true;

            if (_populateAllDistrictsOnStartup)
            {
                if (_preloadAllDistrictOfficers)
                {
                    // If we're preloading, spawn all district officers immediately during loading
                    Log("Preloading all district officers at once");
                    PreloadDistrictOfficers();
                }
                else
                {
                    // Otherwise, queue them as usual
                    QueueInitialDistrictPopulation();
                }
            }
        }

        // MODIFIED: Improved Update method with better time tracking for day changes
        public void Update()
        {
            try
            {
                // IMPROVED INITIALIZATION LOGIC - Only check when not yet ready
                if (!_gameReady)
                {
                    // Only check periodically to avoid overwhelming the system
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
                                if (_initFailCount % 3 == 0) // Log every 3rd attempt
                                    Log("Waiting for NetworkManager...");
                                return; // Wait until we have NetworkManager
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
                            if (_initFailCount % 3 == 0) // Log occasionally
                                Log($"Waiting for world to stabilize: {timeElapsed:F1}/{_requiredStabilityDelay} seconds");
                            _initFailCount++;
                            return; // Wait for stability period
                        }

                        // Check if we can detect player movement
                        GameObject? playerObj = GameObject.Find("Player_Local");
                        if (playerObj != null)
                        {
                            Vector3 playerPosition = playerObj.transform.position;

                            // If this is our first position check
                            if (_lastCheckedPosition == Vector3.zero)
                            {
                                _lastCheckedPosition = playerPosition;
                                if (_initFailCount % 3 == 0)
                                    Log("Found player, waiting for movement detection...");
                                _initFailCount++;
                                return; // Wait to check for movement
                            }

                            // Check if player has moved
                            if (!_playerMovementDetected)
                            {
                                float moveDistance = Vector3.Distance(_lastCheckedPosition, playerPosition);
                                if (moveDistance > 0.1f) // Player moved a small distance
                                {
                                    _playerMovementDetected = true;
                                    Log("Player movement detected, initializing officer system");
                                }
                                else
                                {
                                    _lastCheckedPosition = playerPosition; // Update for next check
                                    if (_initFailCount % 5 == 0) // Log less frequently
                                        Log("Waiting for player movement...");
                                    _initFailCount++;
                                    return; // Wait for player to move
                                }
                            }

                            // Store the player position for use by the system
                            _lastPlayerPosition = playerPosition;

                            // If we got here, we're good to go!
                            _gameReady = true;
                            _gameFullyLoaded = true;
                            Log("Game fully initialized, beginning officer spawning");
                        }
                        else
                        {
                            _initFailCount++;
                            if (_initFailCount % 3 == 0) // Log every 3rd attempt
                                Log("Waiting for player object...");

                            // Force initialization after too many failures, player might be in scene but not found
                            if (_initFailCount >= MAX_INIT_FAILURES)
                            {
                                _gameReady = true;
                                _gameFullyLoaded = true;
                                Log("Forcing initialization after multiple attempts");
                            }
                            return;
                        }
                    }
                    else
                    {
                        return; // Wait until next check interval
                    }
                }

                float currentTime = Time.time;
                _frameCounter++;

                try
                {
                    // Startup delay logic
                    if (_gameFullyLoaded && !_startupSpawnInitiated)
                    {
                        _startupDelayTimer += Time.deltaTime;
                        if (_startupDelayTimer >= _delayBeforeSpawning)
                        {
                            InitiateStartupSpawning();
                        }
                    }

                    // NEW: Check game time for day change detection - direct from SentrySpawnSystem
                    if (_gameReady && (currentTime - _lastTimeCheckTime >= _timeCheckInterval))
                    {
                        _lastTimeCheckTime = currentTime;
                        CheckGameTime();
                    }

                    // Only process spawn queue if officers should be active
                    if (_officersShouldBeActive)
                    {
                        // Reset the despawn flag when entering active time
                        _dayDespawnCompleted = false;

                        // Check player district on set interval - reduces frequency of district checks
                        if (currentTime - _lastPlayerCheckTime >= _playerCheckInterval)
                        {
                            _lastPlayerCheckTime = currentTime;
                            CheckPlayerDistrict();
                        }

                        // NEW: Reconcile district officer counts periodically to handle officers despawned by the game
                        if (currentTime - _lastReconciliationTime >= _reconciliationInterval)
                        {
                            _lastReconciliationTime = currentTime;
                            ReconcileDistrictOfficers();
                        }

                        // NEW: Check for stuck officers periodically
                        if (currentTime - _lastStuckCheckTime >= _stuckCheckInterval)
                        {
                            _lastStuckCheckTime = currentTime;
                            CheckForStuckOfficers();
                        }

                        // Periodically ensure all districts have target officers - HIGHER PRIORITY
                        // Only process on certain frames for better performance
                        if (_frameCounter % MAINTENANCE_FRAME_INTERVAL == 0 &&
                            currentTime - _lastDistrictMaintenanceTime >= _districtMaintenanceInterval)
                        {
                            _lastDistrictMaintenanceTime = currentTime;
                            EnsureAllDistrictsHaveTargetOfficers();
                        }

                        // Process spawn queue with longer cooldown
                        // Only process on certain frames for better performance
                        if (_frameCounter % SPAWN_FRAME_INTERVAL == 0 &&
                            currentTime >= _nextSpawnTime && _spawnQueue.Count > 0)
                        {
                            _nextSpawnTime = currentTime + _spawnCooldown;
                            ProcessSpawnQueue();
                        }
                    }
                    else if (!_dayDespawnCompleted && _activeOfficers.Count > 0)
                    {
                        // NEW: Only despawn once per day cycle when entering day time - like in SentrySpawnSystem
                        _core.LoggerInstance.Msg("Day time or early morning: Performing one-time officer cleanup");
                        DespawnAllOfficersImmediately();
                        _dayDespawnCompleted = true;
                    }

                    // Process despawn queue with longer cooldown
                    if (currentTime >= _nextDespawnTime && _despawnQueue.Count > 0)
                    {
                        _nextDespawnTime = currentTime + _despawnCooldown;
                        ProcessDespawnQueue();
                    }

                    // Periodically log officer counts
                    if (currentTime - _lastUpdateTime >= 15f)
                    {
                        _lastUpdateTime = currentTime;
                        LogOfficerCounts();

                        // Clean up pooled officers that have been in the pool too long
                        CleanOldPooledOfficers();
                    }

                    // Cleanup invalid officers less frequently
                    if (UnityEngine.Random.value < 0.01) // Only run ~1% of frames
                    {
                        CleanupInvalidOfficers();
                    }

                    // Add occasional GC collection to prevent gradual memory leaks
                    if (UnityEngine.Random.value < 0.001) // ~0.1% chance per frame
                    {
                        GC.Collect();
                    }
                }
                catch (Exception ex)
                {
                    _core.LoggerInstance.Error($"Error in Update: {ex.Message}\n{ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Critical error in Update: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // NEW: Check game time logic copied directly from SentrySpawnSystem for consistency
        private void CheckGameTime()
        {
            // Get game time from TimeManager
            TimeManager? timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
            if (timeManager == null) return;

            int currentTime = timeManager.CurrentTime;

            // Check for large time jumps (like sleeping)
            if (_lastCheckedGameTime != -1)
            {
                // If time jumps backward or jumps forward by more than 100 (1 hour)
                bool timeJumped = (currentTime < _lastCheckedGameTime) ||
                                  (currentTime > _lastCheckedGameTime + 100);

                if (timeJumped)
                {
                    Log($"Detected time jump: {_lastCheckedGameTime} -> {currentTime}. Refreshing officer status.");

                    // Force immediate reevaluation of time status based on new time of day
                    // Morning: 6:00 AM - 12:00 PM (600 - 1200)
                    // Afternoon: 12:00 PM - 6:00 PM (1200 - 1800)
                    // Evening: 6:00 PM - 9:00 PM (1800 - 2100)
                    // Night: 9:00 PM - 6:00 AM (2100 - 600)

                    // Check if it's night time (9 PM - 6 AM)
                    bool wasNight = _isNightTime;
                    _isNightTime = currentTime >= 2100 || currentTime < 600;

                    // If night status changed, update officers
                    if (wasNight != _isNightTime)
                    {
                        if (_isNightTime)
                        {
                            Log("Night time after time jump. Activating officers.");
                            _officersShouldBeActive = true;
                            _isNinePMMaxSpawnTriggered = true;
                            _dayDespawnCompleted = false; // Reset flag for next day cycle
                        }
                        else
                        {
                            Log("Day time after time jump. Deactivating officers.");
                            _officersShouldBeActive = false;
                            _isNinePMMaxSpawnTriggered = false;

                            if (!_dayDespawnCompleted && _activeOfficers.Count > 0)
                            {
                                // Immediately despawn all officers instead of queueing
                                DespawnAllOfficersImmediately();
                                _dayDespawnCompleted = true;
                            }
                        }
                    }
                }
            }

            _lastCheckedGameTime = currentTime;

            // Regular time of day check
            // Morning: 6:00 AM - 12:00 PM (600 - 1200)
            // Afternoon: 12:00 PM - 6:00 PM (1200 - 1800)
            // Evening: 6:00 PM - 9:00 PM (1800 - 2100)
            // Night: 9:00 PM - 6:00 AM (2100 - 600)

            // Check for current time period based on game time
            int timePeriod;
            bool shouldBeActive;

            if (currentTime >= 600 && currentTime < 1200)
            {
                // Morning
                timePeriod = 1;
                shouldBeActive = true;
            }
            else if (currentTime >= 1200 && currentTime < 1800)
            {
                // Afternoon
                timePeriod = 2;
                shouldBeActive = true;
            }
            else if (currentTime >= 1800 && currentTime < 2100)
            {
                // Evening
                timePeriod = 3;
                shouldBeActive = true;
            }
            else
            {
                // Night (9 PM - 6 AM)
                timePeriod = 4;
                shouldBeActive = true;

                // At 9 PM, trigger max spawning
                if (currentTime >= 2100 && !_isNinePMMaxSpawnTriggered)
                {
                    _isNinePMMaxSpawnTriggered = true;
                    Log("9 PM detected - Initiating night officer spawning protocol");
                }
            }

            // Check if active status changed
            if (shouldBeActive != _officersShouldBeActive)
            {
                _officersShouldBeActive = shouldBeActive;

                if (_officersShouldBeActive)
                {
                    Log($"Time period {timePeriod} detected. Activating officers.");
                    _dayDespawnCompleted = false;
                }
                else
                {
                    Log($"Inactive time period detected. Deactivating officers.");

                    if (!_dayDespawnCompleted && _activeOfficers.Count > 0)
                    {
                        // Immediately despawn all officers when time changes to inactive
                        DespawnAllOfficersImmediately();
                        _dayDespawnCompleted = true;
                    }
                }
            }

            // Check for day change (night->day transition)
            // This happens when going from late night (2100+) to early morning (< 600)
            bool isNightNow = currentTime >= 2100 || currentTime < 600;

            // Only log and process if there's a change in night/day status
            if (isNightNow != _isNightTime)
            {
                _isNightTime = isNightNow;
                if (!_isNightTime) // Day time transition
                {
                    Log("Day time detected (< 9 PM). Resetting night spawning flag.");
                    _isNinePMMaxSpawnTriggered = false;

                    if (!_dayDespawnCompleted && _activeOfficers.Count > 0)
                    {
                        // Immediately despawn all officers when day begins
                        DespawnAllOfficersImmediately();
                        _dayDespawnCompleted = true;
                    }
                }
            }
        }

        // NEW: Method to immediately despawn all officers on day change - copied from SentrySpawnSystem
        private void DespawnAllOfficersImmediately()
        {
            _core.LoggerInstance.Msg($"Day change detected - despawning all {_activeOfficers.Count} officers");

            // Make a copy of the list to avoid modification issues during iteration
            foreach (var officer in new List<PoliceOfficer>(_activeOfficers))
            {
                if (officer != null && officer.gameObject != null)
                {
                    DespawnOfficer(officer);
                }
            }

            // Clear despawn queue to avoid duplicates
            _despawnQueue.Clear();

            // Reset district populations
            foreach (var district in _districts)
            {
                district.OfficersSpawned.Clear();
            }

            // Reset population state to regenerate officers when active again
            _populationState = DistrictPopulationState.InitialCheck;
            _needsStatusUpdate = true;
            _districtOfficerCount = 0;
            _districtsFullyPopulated = false;

            // Log how many are left (should be 0)
            if (_activeOfficers.Count > 0)
            {
                _core.LoggerInstance.Msg($"WARNING: {_activeOfficers.Count} officers still active after immediate despawn!");
            }
            else
            {
                Log("All officers successfully despawned");
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // NEW: Reconcile district officers to fix counts when officers are despawned by the game
        private void ReconcileDistrictOfficers()
        {
            int officersRemoved = 0;

            // Validate all district officers
            foreach (var district in _districts)
            {
                for (int i = district.OfficersSpawned.Count - 1; i >= 0; i--)
                {
                    var officer = district.OfficersSpawned[i];
                    // Check if the officer has been despawned by the game
                    if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy)
                    {
                        district.OfficersSpawned.RemoveAt(i);
                        officersRemoved++;

                        // Adjust district officer count
                        if (!_districtsFullyPopulated)
                        {
                            _districtOfficerCount = Math.Max(0, _districtOfficerCount - 1);
                        }
                    }
                }
            }

            // Only log if we found and removed invalid officers
            if (officersRemoved > 0)
            {
                Log($"Reconciliation found and removed {officersRemoved} despawned officers from district counts");

                // If any were removed, it might be worth checking active officers list too
                CleanupInvalidOfficers();

                // Trigger a verification check to refill any understaffed districts
                if (_populationState == DistrictPopulationState.MaintenanceMode && officersRemoved > 5)
                {
                    _populationState = DistrictPopulationState.InitialCheck;
                    _needsStatusUpdate = true;
                    Log("Significant officer loss detected. Restarting district population process.");
                }
            }
        }

        // NEW: Check for stuck officers and attempt to fix them
        private void CheckForStuckOfficers()
        {
            int fixedCount = 0;
            var officersToCheck = new List<PoliceOfficer>(_activeOfficers);

            foreach (var officer in officersToCheck)
            {
                if (officer == null || officer.gameObject == null) continue;

                Vector3 currentPosition = officer.transform.position;

                // If this is our first check for this officer
                if (!_lastOfficerPositions.ContainsKey(officer))
                {
                    _lastOfficerPositions[officer] = currentPosition;
                    _officerStuckCounter[officer] = 0;
                    continue;
                }

                // Check if officer has moved
                Vector3 lastPosition = _lastOfficerPositions[officer];
                float moveDistance = Vector3.Distance(lastPosition, currentPosition);

                // If officer hasn't moved much, increment counter
                if (moveDistance < 0.1f)
                {
                    _officerStuckCounter[officer] = _officerStuckCounter.ContainsKey(officer) ?
                        _officerStuckCounter[officer] + 1 : 1;
                }
                else
                {
                    // Reset counter if officer moved
                    _officerStuckCounter[officer] = 0;
                }

                // Update last position
                _lastOfficerPositions[officer] = currentPosition;

                // If officer is stuck for multiple checks, try to fix
                if (_officerStuckCounter.ContainsKey(officer) && _officerStuckCounter[officer] >= STUCK_THRESHOLD)
                {
                    // Try to fix the officer
                    if (FixStuckOfficer(officer))
                    {
                        fixedCount++;
                    }

                    // Reset counter
                    _officerStuckCounter[officer] = 0;
                }
            }

            // Clean up tracking for officers that no longer exist
            var officersToRemove = new List<PoliceOfficer>();
            foreach (var officer in _lastOfficerPositions.Keys)
            {
                // FIX 2: Only add non-null officers to the remove list
                if (officer != null)
                {
                    if (officer.gameObject == null || !officer.gameObject.activeInHierarchy)
                    {
                        officersToRemove.Add(officer);
                    }
                }
            }

            foreach (var officer in officersToRemove)
            {
                _lastOfficerPositions.Remove(officer);
                _officerStuckCounter.Remove(officer);
            }

            // We no longer need to handle null keys separately
            // with non-nullable reference types

            if (fixedCount > 0)
            {
                Log($"Fixed {fixedCount} stuck officers");
            }
        }

        // NEW: Try to fix a stuck officer
        private bool FixStuckOfficer(PoliceOfficer officer)
        {
            try
            {
                if (officer == null || officer.gameObject == null) return false;

                // Make sure it's one of our officers
                if (!IsModSpawnedOfficer(officer)) return false;

                // Get the movement component and agent
                var movement = officer.Movement;
                if (movement == null) return false;

                var agent = movement.Agent;
                if (agent == null) return false;

                // Get current position
                Vector3 currentPos = officer.transform.position;

                // Try to find a valid NavMesh position within 10m
                NavMeshHit hit;
                if (NavMesh.SamplePosition(currentPos, out hit, 10f, NavMesh.AllAreas))
                {
                    // Try to warp the agent to the valid position
                    SafeInvokeMethod(agent, "Warp", new object[] { hit.position });

                    // Reset patrol route
                    string? routeName = null;
                    if (_officerRouteNames.TryGetValue(officer, out routeName) && routeName != null)
                    {
                        GameObject? routeGO = null;
                        if (_patrolRouteObjects.TryGetValue(routeName, out routeGO) && routeGO != null)
                        {
                            var route = routeGO.GetComponent<FootPatrolRoute>();
                            if (route != null)
                            {
                                var patrolGroup = new PatrolGroup(route);
                                officer.StartFootPatrol(patrolGroup, true);
                            }
                        }
                    }

                    return true;
                }

                // If we couldn't find a valid position, queue officer for despawn and respawn
                District? officerDistrict = null;
                foreach (var district in _districts)
                {
                    if (district.OfficersSpawned.Contains(officer))
                    {
                        officerDistrict = district;
                        break;
                    }
                }

                // Despawn and request a replacement
                if (officerDistrict != null)
                {
                    QueueDespawn(officer);
                    QueueSpawnInDistrict(officerDistrict, 2.0f);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error fixing stuck officer: {ex.Message}");
            }
            return false;
        }

        // Helper methods for safe property access using reflection
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

        private T SafeGetProperty<T>(object obj, string propertyName, T defaultValue)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanRead)
                {
                    return (T)prop.GetValue(obj)!;
                }
            }
            catch { }
            return defaultValue;
        }

        private void SafeInvokeMethod(object obj, string methodName, object[] args)
        {
            try
            {
                var method = obj.GetType().GetMethod(methodName);
                if (method != null)
                {
                    method.Invoke(obj, args);
                }
            }
            catch { }
        }

        // New method to immediately spawn all district officers during loading
        private void PreloadDistrictOfficers()
        {
            try
            {
                // Skip if network manager isn't ready
                if (_networkManager == null) return;

                Log($"Beginning immediate preload of {_maxDistrictOfficers} district officers");
                int spawnedCount = 0;

                foreach (var district in _districts)
                {
                    int officersNeeded = district.TargetOfficers;
                    Log($"Preloading {officersNeeded} officers for {district.Name}");

                    for (int i = 0; i < officersNeeded; i++)
                    {
                        // Choose a random spawn point
                        if (district.SpawnPoints.Count == 0) continue;
                        int spawnIndex = UnityEngine.Random.Range(0, district.SpawnPoints.Count);
                        Vector3 spawnPos = district.SpawnPoints[spawnIndex];

                        // Spawn officer directly, with isForDistrictPopulation=true
                        if (SpawnOfficer(spawnPos, district, true))
                        {
                            spawnedCount++;

                            // Force GC collection occasionally to prevent memory buildup
                            if (spawnedCount % 5 == 0)
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                    }
                }

                // Set districts as fully populated if we spawned all needed officers
                if (spawnedCount >= _maxDistrictOfficers)
                {
                    _districtsFullyPopulated = true;
                    _populationState = DistrictPopulationState.MaintenanceMode;
                    _districtMaintenanceInterval = _reducedDistrictMaintenanceInterval;
                    Log($"Successfully preloaded {spawnedCount} district officers");
                    Log($"All districts fully populated! Switching to maintenance mode.");
                }
                else
                {
                    _populationState = DistrictPopulationState.VerificationCheck;
                    Log($"Partially preloaded {spawnedCount}/{_maxDistrictOfficers} district officers, checking gaps...");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error during district officer preload: {ex.Message}");

                // Fall back to queued population
                QueueInitialDistrictPopulation();
            }
        }

        // FIXED: Updated to only queue needed officers
        private void QueueInitialDistrictPopulation()
        {
            _populationState = DistrictPopulationState.InitialCheck;
            _needsStatusUpdate = true;

            foreach (var district in _districts)
            {
                // Calculate officers needed first and only queue that many
                int officersNeeded = Math.Max(0, district.TargetOfficers - district.OfficersSpawned.Count);

                if (officersNeeded > 0)
                {
                    Log($"Queueing {officersNeeded} officers for initial population in {district.Name}");

                    for (int i = 0; i < officersNeeded; i++)
                    {
                        QueueSpawnInDistrict(district, 1.5f); // Higher priority for initial population
                    }
                }
                else
                {
                    // Don't queue any officers for this district
                    Log($"Skipping {district.Name} - already at target ({district.OfficersSpawned.Count}/{district.TargetOfficers})");
                }
            }
        }

        public int GetCurrentOfficerLimit()
        {
            // Get game time from TimeManager
            TimeManager? timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
            if (timeManager == null)
                return _nightOfficerLimit; // Default to night limit if TimeManager is not available

            int currentTime = timeManager.CurrentTime;

            // If it's 9 PM (2100) or later, or the 9 PM max spawn has been triggered, return the max
            if (currentTime >= 2100 || _isNinePMMaxSpawnTriggered)
            {
                return _nightOfficerLimit; // Return night limit after 9 PM
            }

            // Morning: 6:00 AM - 12:00 PM (600 - 1200)
            if (currentTime >= 600 && currentTime < 1200)
                return _morningOfficerLimit;

            // Afternoon: 12:00 PM - 6:00 PM (1200 - 1800)
            else if (currentTime >= 1200 && currentTime < 1800)
                return _afternoonOfficerLimit;

            // Evening: 6:00 PM - 9:00 PM (1800 - 2100)
            else if (currentTime >= 1800 && currentTime < 2100)
                return _eveningOfficerLimit;

            // Night: 12:00 AM - 6:00 AM (0 - 600)
            else
                return _nightOfficerLimit;
        }

        // Modified to exclude district officers when needed
        public int GetTotalOfficerCount(bool excludeDistrictPopulation = false)
        {
            if (excludeDistrictPopulation && !_districtsFullyPopulated)
            {
                // If districts aren't fully populated yet, exclude district population officers
                return Math.Max(0, _activeOfficers.Count - _districtOfficerCount);
            }
            return _activeOfficers.Count;
        }

        // Method to get local officer count - only of mod-spawned officers
        private int GetLocalOfficerCount()
        {
            if (_lastPlayerPosition == Vector3.zero) return 0;

            // Only count officers from our active list (which only contains mod-spawned officers)
            int count = 0;
            foreach (var officer in _activeOfficers)
            {
                if (officer != null && officer.gameObject != null &&
                    Vector3.Distance(officer.gameObject.transform.position, _lastPlayerPosition) <= _localOfficerRadius)
                {
                    count++;
                }
            }

            return count;
        }

        private void CheckForNinePM()
        {
            try
            {
                TimeManager? timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
                if (timeManager == null) return;

                int currentTime = timeManager.CurrentTime;

                // If it's 9 PM (2100) or later and we haven't triggered max spawning yet
                if (currentTime >= 2100 && !_isNinePMMaxSpawnTriggered)
                {
                    Log("9 PM detected - Initiating maximum officer spawning protocol");
                    _isNinePMMaxSpawnTriggered = true;

                    // Queue jobs to spawn officers to maximum, but with much longer spacing
                    QueueOfficersForNinePM();
                }
                // Reset the flag if it's before 9 PM and we've detected a day change
                else if (currentTime < 2100 && _isNinePMMaxSpawnTriggered)
                {
                    // Only reset if we've likely moved to a new day (time decreased significantly)
                    if (_lastCheckedGameTime > 2100 && currentTime < 600)
                    {
                        _isNinePMMaxSpawnTriggered = false;
                        Log("New day detected - Resetting maximum officer spawning protocol");
                    }
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error checking for 9 PM: {ex.Message}");
            }
        }

        private void QueueOfficersForNinePM()
        {
            int currentLimit = GetCurrentOfficerLimit();
            int totalOfficers = GetTotalOfficerCount();
            int officersNeeded = currentLimit - totalOfficers;

            if (officersNeeded <= 0)
                return;

            // Higher priority for player's current district
            if (_currentPlayerDistrict != null)
            {
                int districtSpace = Math.Max(0, _currentPlayerDistrict.TargetOfficers - _currentPlayerDistrict.OfficersSpawned.Count);
                int officersForCurrentDistrict = Math.Min(officersNeeded / 2, districtSpace);

                if (officersForCurrentDistrict > 0)
                {
                    for (int i = 0; i < officersForCurrentDistrict; i++)
                    {
                        QueueSpawnInDistrict(_currentPlayerDistrict, 2f);
                        officersNeeded--;
                    }
                }
            }

            // Distribute remaining officers across all districts
            // But only add a few at a time to avoid performance spikes
            int maxToQueueNow = Math.Min(officersNeeded, 4);

            // Distribute by finding districts below their target count
            var districtsNeedingOfficers = _districts
                .Where(d => d.OfficersSpawned.Count < d.TargetOfficers)
                .OrderBy(d => d.OfficersSpawned.Count - d.TargetOfficers)
                .ToList();

            foreach (var district in districtsNeedingOfficers)
            {
                if (maxToQueueNow <= 0) break;

                int districtNeed = Math.Max(0, district.TargetOfficers - district.OfficersSpawned.Count);
                int toQueue = Math.Min(districtNeed, maxToQueueNow);

                if (toQueue > 0)
                {
                    for (int i = 0; i < toQueue; i++)
                    {
                        QueueSpawnInDistrict(district, 1f);
                    }
                    maxToQueueNow -= toQueue;
                }
            }
        }

        private void CheckPlayerDistrict()
        {
            try
            {
                // Get player position
                var playerObj = GameObject.Find("Player_Local");
                if (playerObj == null) return;

                Vector3 playerPosition = playerObj.transform.position;
                _lastPlayerPosition = playerPosition;  // Update this first in case we return early

                // Find which district the player is in
                District? newDistrict = null;
                float closestDistance = float.MaxValue;

                foreach (var district in _districts)
                {
                    float distance = Vector3.Distance(playerPosition, district.Center);
                    if (district.ContainsPoint(playerPosition) && distance < closestDistance)
                    {
                        newDistrict = district;
                        closestDistance = distance;
                    }
                }

                // If we found a district and it's different from current
                if (newDistrict != null && newDistrict != _currentPlayerDistrict)
                {
                    Log($"Player entered {newDistrict.Name}");

                    // Store the previous district
                    _previousPlayerDistrict = _currentPlayerDistrict;

                    // Update current district
                    _currentPlayerDistrict = newDistrict;
                    _currentPlayerDistrict.LastVisited = DateTime.Now;

                    // Handle district transition after cooldown
                    if (Time.time - _lastDistrictTransitionTime >= _districtTransitionCooldown)
                    {
                        _lastDistrictTransitionTime = Time.time;
                        HandleDistrictTransition(_previousPlayerDistrict, _currentPlayerDistrict);
                    }
                }

                // Only occasionally check for distant districts to despawn officers from
                if (UnityEngine.Random.value < 0.1f && !_preserveDistrictOfficers)
                {
                    // Only do this if we're configured to not preserve district officers
                    DespawnOfficersFromFarDistricts();
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error checking player district: {ex.Message}");
            }
        }

        private void HandleDistrictTransition(District? oldDistrict, District newDistrict)
        {
            // When transitioning to a new district, ensure it has target officers
            // No longer add a buffer - just fill up to the target
            int officersToSpawn = Math.Max(0, newDistrict.TargetOfficers - newDistrict.OfficersSpawned.Count);

            if (officersToSpawn > 0 && !_districtsFullyPopulated)
            {
                // Only log in development mode
                if (_enableDistrictDebugLogs)
                {
                    Log($"Adding {officersToSpawn} officers to {newDistrict.Name} (transition)");
                }

                for (int i = 0; i < officersToSpawn; i++)
                {
                    QueueSpawnInDistrict(newDistrict, 2.0f);
                }
            }

            // Check for adjacent districts and ensure they're populated (only if in population phase)
            if (!_districtsFullyPopulated)
            {
                foreach (var district in _districts)
                {
                    if (district != newDistrict && district.OverlapsWith(newDistrict))
                    {
                        int adjacentOfficersNeeded = Math.Max(0, district.TargetOfficers - district.OfficersSpawned.Count);

                        if (adjacentOfficersNeeded > 0)
                        {
                            // Only log in development mode
                            if (_enableDistrictDebugLogs)
                            {
                                Log($"Adding {adjacentOfficersNeeded} officers to adjacent {district.Name}");
                            }

                            for (int i = 0; i < adjacentOfficersNeeded; i++)
                            {
                                QueueSpawnInDistrict(district, 1.5f);
                            }
                        }
                    }
                }
            }

            // Handle removing excess officers in the previous district
            if (oldDistrict != null && !newDistrict.OverlapsWith(oldDistrict) && !_preserveDistrictOfficers)
            {
                int excessOfficers = Math.Max(0, oldDistrict.OfficersSpawned.Count - oldDistrict.TargetOfficers);

                if (excessOfficers > 0)
                {
                    // Limit how many we despawn at once
                    int toDespawn = Math.Min(excessOfficers, 2);

                    // Only log in development mode
                    if (_enableDistrictDebugLogs)
                    {
                        Log($"Removing {toDespawn} excess officers from {oldDistrict.Name}");
                    }

                    for (int i = 0; i < toDespawn && i < oldDistrict.OfficersSpawned.Count; i++)
                    {
                        QueueDespawn(oldDistrict.OfficersSpawned[i]);
                    }
                }
            }
        }

        private void DespawnOfficersFromFarDistricts()
        {
            // This is now optional and only runs if we're not preserving district officers
            if (_currentPlayerDistrict == null || _preserveDistrictOfficers) return;

            // Find districts that are far away and haven't been visited recently
            foreach (var district in _districts)
            {
                // Skip player's current and previous district
                if (district == _currentPlayerDistrict || district == _previousPlayerDistrict) continue;

                // Skip districts with officers at or below target
                if (district.OfficersSpawned.Count <= district.TargetOfficers) continue;

                // Skip districts we visited in the last 5 minutes
                if ((DateTime.Now - district.LastVisited).TotalMinutes < 5) continue;

                // Skip districts that overlap with current district
                if (_currentPlayerDistrict != null && district.OverlapsWith(_currentPlayerDistrict)) continue;

                // FIX 3: Add null check before accessing _currentPlayerDistrict.Center
                if (_currentPlayerDistrict == null) continue;

                // Calculate physical distance to player's district
                float distanceBetweenDistricts = Vector3.Distance(
                    district.Center,
                    _currentPlayerDistrict.Center
                );

                // If far away, despawn excess officers
                if (distanceBetweenDistricts > district.Radius + _currentPlayerDistrict.Radius + 30f)
                {
                    int excessOfficers = district.OfficersSpawned.Count - district.TargetOfficers;

                    if (excessOfficers > 0)
                    {
                        int toDespawn = Math.Min(excessOfficers, 1); // Only despawn one at a time

                        if (_enableDistrictDebugLogs)
                        {
                            Log($"Despawning {toDespawn} excess officers from distant {district.Name}");
                        }

                        for (int i = 0; i < toDespawn; i++)
                        {
                            if (district.OfficersSpawned.Count > district.TargetOfficers && i < district.OfficersSpawned.Count)
                            {
                                QueueDespawn(district.OfficersSpawned[i]);
                            }
                        }
                    }
                }
            }
        }

        // FIXED: Always check district limits before queueing officers
        private void QueueSpawnInDistrict(District district, float priority = 1.0f)
        {
            if (district.SpawnPoints.Count == 0) return;

            // FIXED: Always check if district is at or above target limit before queueing any new officers
            // Don't spawn more than target officers for a district in ANY mode
            if (district.OfficersSpawned.Count >= district.TargetOfficers)
            {
                // Skip if district is already at or above target
                return;
            }

            // Choose a random spawn point
            int spawnIndex = UnityEngine.Random.Range(0, district.SpawnPoints.Count);
            Vector3 spawnPos = district.SpawnPoints[spawnIndex];

            // Create spawn job
            var job = new SpawnJob
            {
                Position = spawnPos,
                District = district,
                Priority = priority,
                QueuedTime = DateTime.Now
            };

            // Queue spawn job without logging
            _spawnQueue.Enqueue(job);
        }

        private void QueueDespawn(PoliceOfficer officer)
        {
            if (officer == null || !officer.gameObject.activeInHierarchy) return;

            // Only despawn officers spawned by our mod
            if (!IsModSpawnedOfficer(officer)) return;

            _despawnQueue.Enqueue(officer);
        }

        // FIXED: ProcessSpawnQueue with additional district limit checks
        private void ProcessSpawnQueue()
        {
            if (_spawnQueue.Count == 0) return;

            // If we're already spawning an officer, don't try to spawn another
            if (_currentlySpawningOfficer)
            {
                return;
            }

            // This is a district population job if we're not in maintenance mode
            bool isForDistrictPopulation = _populationState != DistrictPopulationState.MaintenanceMode;

            // Check if the game is currently spawning entities
            if (Time.time - _lastGameSpawnCheck > 0.1f) // Check every 0.1 seconds
            {
                _lastGameSpawnCheck = Time.time;
                _isGameCurrentlySpawning = IsGameSpawningEntities();

                if (_isGameCurrentlySpawning)
                {
                    // If game is spawning, delay our spawn to avoid hitching
                    return;
                }
            }

            // If we detected game spawning recently, wait for the avoidance window
            if (_isGameCurrentlySpawning && Time.time < _lastGameSpawnCheck + _gameSpawnAvoidanceWindow)
            {
                return;
            }
            else
            {
                _isGameCurrentlySpawning = false;
            }

            // Sort the queue by priority - but don't reassign the readonly queue
            var sortedQueue = new List<SpawnJob>(_spawnQueue);

            // Clear the original queue
            _spawnQueue.Clear();

            // Sort by priority (higher priority first)
            sortedQueue.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            // Re-queue all sorted jobs in order
            foreach (var spawnJob in sortedQueue)
            {
                // Skip stale jobs (older than 2 minutes)
                if ((DateTime.Now - spawnJob.QueuedTime).TotalMinutes > 2)
                {
                    continue;
                }

                // FIXED: Always check district limits before re-queueing, regardless of state
                if (spawnJob.District != null &&
                    spawnJob.District.OfficersSpawned.Count >= spawnJob.District.TargetOfficers)
                {
                    // Skip this job if the district is already at or above target
                    continue;
                }

                // Re-add to the queue
                _spawnQueue.Enqueue(spawnJob);
            }

            // Only spawn one officer at a time, regardless of _maxSpawnsPerCycle
            if (_spawnQueue.Count == 0) return;

            var nextJob = _spawnQueue.Dequeue();

            // Check for local density limit only if districts are fully populated
            // This ensures districts get filled first before player proximity matters
            if (_populationState == DistrictPopulationState.MaintenanceMode)
            {
                int localOfficers = GetLocalOfficerCount();

                // Don't spawn more local officers if we're exceeding limit
                if (localOfficers >= _maxLocalOfficers &&
                    Vector3.Distance(nextJob.Position, _lastPlayerPosition) <= _localOfficerRadius)
                {
                    return;
                }
            }

            // FIXED: One final district limit check before spawning
            if (nextJob.District != null &&
                nextJob.District.OfficersSpawned.Count >= nextJob.District.TargetOfficers)
            {
                // Skip this spawn if district is at or above its target
                return;
            }

            // Use singular spawning to reduce hitching
            _currentlySpawningOfficer = true;
            MelonCoroutines.Start(SpawnOfficerCoroutine(nextJob.Position, nextJob.District, isForDistrictPopulation));
        }

        // Single spawn coroutine
        private IEnumerator SpawnOfficerCoroutine(Vector3 position, District? district, bool isForDistrictPopulation)
        {
            // Wait a frame before spawning to allow the current frame to complete
            yield return null;

            bool success = SpawnOfficer(position, district, isForDistrictPopulation);

            if (!success)
            {
                _consecutiveSpawnFailures++;
                if (_consecutiveSpawnFailures >= MAX_SPAWN_FAILURES)
                {
                    LogWarning($"Warning: {_consecutiveSpawnFailures} consecutive spawn failures detected. Possible game state issue.");
                    _consecutiveSpawnFailures = 0;  // Reset to avoid spam
                }
            }
            else
            {
                _consecutiveSpawnFailures = 0;
            }

            // Wait several frames after spawning to allow the game to process the new officer
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            _currentlySpawningOfficer = false;
        }

        // Method to check if the game is currently spawning entities
        private bool IsGameSpawningEntities()
        {
            // This is a placeholder. You'll need to determine how to detect if the game is spawning
            // One approach: monitor frame time changes or check if specific game systems are active

            // Check if the frame time suddenly spiked:
            float currentFrameTime = Time.deltaTime;
            bool frameTimeSpiked = currentFrameTime > (Time.smoothDeltaTime * 1.5f);

            // Additional checks can be added if the game has detectable spawning patterns

            return frameTimeSpiked;
        }

        private void ProcessDespawnQueue()
        {
            if (_despawnQueue.Count == 0) return;

            // Limit how many officers we despawn per cycle
            int toDespawn = Math.Min(_maxDespawnsPerCycle, _despawnQueue.Count);

            // Process the specified number of jobs
            for (int i = 0; i < toDespawn; i++)
            {
                if (_despawnQueue.Count == 0) break;

                var officer = _despawnQueue.Dequeue();

                // Despawn the officer
                DespawnOfficer(officer);
            }
        }

        // Modified to track district vs non-district officers and better memory management
        private bool SpawnOfficer(Vector3 position, District? district, bool isForDistrictPopulation)
        {
            try
            {
                // Double-check district limits before spawning
                if (district != null && district.OfficersSpawned.Count >= district.TargetOfficers)
                {
                    return false; // Skip if district is already at or above target 
                }

                // Check if we have a pooled officer we can reuse
                PooledOfficer? pooled = null;
                if (_officerPool.Count > 0)
                {
                    pooled = _officerPool.Dequeue();
                }

                if (pooled != null && pooled.Officer != null && pooled.GameObject != null)
                {
                    // Reuse the pooled officer
                    var pooledOfficer = pooled.Officer;
                    var pooledOfficerGO = pooled.GameObject;

                    // Reposition
                    Vector3 positionOffset = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );
                    pooledOfficerGO.transform.position = position + positionOffset;
                    pooledOfficerGO.SetActive(true);

                    // Create a real patrol route with multiple waypoints
                    // Use a unique ID that doesn't rely on Time.time
                    string uniqueRouteId = Guid.NewGuid().ToString();
                    var routeName = $"PatrolRoute_{uniqueRouteId}";
                    var routeGO = new GameObject(routeName);
                    routeGO.transform.position = position;

                    // Store in our reference dictionary
                    _patrolRouteObjects[routeName] = routeGO;

                    var route = routeGO.AddComponent<FootPatrolRoute>();
                    route.RouteName = routeName;
                    route.StartWaypointIndex = 0;

                    // IMPROVED: Create enhanced patrol waypoints with better patterns
                    CreateEnhancedPatrolWaypoints(routeGO, position);

                    // Create patrol group
                    var patrolGroup = new PatrolGroup(route);

                    // Store the route name with this officer
                    _officerRouteNames[pooledOfficer] = routeName;

                    // Ensure officer still has our tag component
                    if (pooledOfficer.gameObject.GetComponent<ModSpawnedOfficerTag>() == null)
                    {
                        pooledOfficer.gameObject.AddComponent<ModSpawnedOfficerTag>();
                    }

                    // NEW: Enhance NavMeshAgent properties for better movement
                    EnhanceOfficerNavigation(pooledOfficer);

                    // Reactivate officer and start patrol
                    pooledOfficer.Activate();
                    pooledOfficer.StartFootPatrol(patrolGroup, true);

                    // Add to active officers
                    _activeOfficers.Add(pooledOfficer);

                    // Add to district
                    if (district != null)
                    {
                        district.OfficersSpawned.Add(pooledOfficer);
                    }

                    // Update district officer count if applicable
                    if (isForDistrictPopulation)
                    {
                        _districtOfficerCount++;

                        // Only log during initial population or if debug is enabled
                        if (_populationState == DistrictPopulationState.InitialPopulation || _enableDistrictDebugLogs)
                        {
                            Log($"District population: {_districtOfficerCount}/{_maxDistrictOfficers}");
                        }
                    }

                    // Only log in development mode
                    if (_enableDistrictDebugLogs && district != null)
                    {
                        Log($"Officer added to {district.Name}");
                    }
                    return true;
                }

                // Get a random officer type - REMOVING OFFICER DAVIS
                string officerType = OfficerTypes[UnityEngine.Random.Range(0, OfficerTypes.Length)];

                // Find the prefab
                NetworkObject? prefab = null;
                var spawnablePrefabs = _networkManager?.SpawnablePrefabs;

                if (spawnablePrefabs != null)
                {
                    for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++)
                    {
                        var obj = spawnablePrefabs.GetObject(true, i);
                        if (obj != null && obj.gameObject.name == officerType)
                        {
                            prefab = obj;
                            break;
                        }
                    }
                }

                if (prefab == null || _networkManager == null)
                {
                    _core.LoggerInstance.Error($"Could not find prefab for {officerType} or NetworkManager is null");
                    return false;
                }

                // Create a patrol route with unique ID
                string uniqueId = Guid.NewGuid().ToString();
                var patrolName = $"PatrolRoute_{uniqueId}";
                var patrolRouteGO = new GameObject(patrolName);
                patrolRouteGO.transform.position = position;

                // Store in our reference dictionary
                _patrolRouteObjects[patrolName] = patrolRouteGO;

                var patrolRoute = patrolRouteGO.AddComponent<FootPatrolRoute>();
                patrolRoute.RouteName = patrolName;
                patrolRoute.StartWaypointIndex = 0;

                // IMPROVED: Create enhanced patrol waypoints
                CreateEnhancedPatrolWaypoints(patrolRouteGO, position);

                // Create patrol group
                var newPatrolGroup = new PatrolGroup(patrolRoute);

                // Create the officer
                var newOfficerGO = UnityEngine.Object.Instantiate(prefab);
                newOfficerGO.gameObject.name = $"{officerType}_Spawned_{uniqueId}";

                // Add small random offset to prevent overlapping
                Vector3 spawnOffset = new Vector3(
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    0,
                    UnityEngine.Random.Range(-0.5f, 0.5f)
                );

                // Position and activate
                newOfficerGO.transform.position = position + spawnOffset;
                newOfficerGO.gameObject.SetActive(true);

                // Get officer component
                var spawnedOfficer = newOfficerGO.gameObject.GetComponent<PoliceOfficer>();
                if (spawnedOfficer != null)
                {
                    spawnedOfficer.Activate();

                    // NEW: Add our custom tag to identify mod-spawned officers
                    // FIXED: Call AddComponent on the gameObject, not the NetworkObject
                    newOfficerGO.gameObject.AddComponent<ModSpawnedOfficerTag>();

                    // Store the route name with this officer
                    _officerRouteNames[spawnedOfficer] = patrolName;

                    // NEW: Enhance NavMeshAgent properties before patrolling
                    EnhanceOfficerNavigation(spawnedOfficer);

                    // Start patrol
                    spawnedOfficer.StartFootPatrol(newPatrolGroup, true);

                    // Spawn on network
                    _networkManager.ServerManager.Spawn(newOfficerGO);

                    // Add to active officers
                    if (spawnedOfficer != null)
                    {
                        _activeOfficers.Add(spawnedOfficer);
                    }

                    // Add to district
                    if (district != null && spawnedOfficer != null)
                    {
                        district.OfficersSpawned.Add(spawnedOfficer);
                    }

                    // Update district officer count if applicable
                    if (isForDistrictPopulation)
                    {
                        _districtOfficerCount++;

                        // Only log during initial population or if debug is enabled
                        if (_populationState == DistrictPopulationState.InitialPopulation || _enableDistrictDebugLogs)
                        {
                            Log($"District population: {_districtOfficerCount}/{_maxDistrictOfficers}");
                        }
                    }

                    // Only log in development mode
                    if (_enableDistrictDebugLogs && district != null)
                    {
                        Log($"Officer added to {district.Name}");
                    }

                    return true;
                }
                else
                {
                    _core.LoggerInstance.Error("Failed to get PoliceOfficer component from instantiated officer");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error spawning officer: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        // NEW: Method to enhance officer navigation properties safely using reflection
        private void EnhanceOfficerNavigation(PoliceOfficer officer)
        {
            if (officer == null) return;

            try
            {
                // Get the officer's movement component
                var movement = officer.Movement;
                if (movement == null) return;

                // Get the NavMeshAgent
                var agent = movement.Agent;
                if (agent == null) return;

                // Set properties safely using reflection
                SafeSetProperty(agent, "speed", 3.5f); // Slightly faster movement
                SafeSetProperty(agent, "acceleration", 12f); // Better acceleration
                SafeSetProperty(agent, "angularSpeed", 180f); // Better turning
                SafeSetProperty(agent, "stoppingDistance", 0.5f); // Stop closer to destination
                SafeSetProperty(agent, "obstacleAvoidanceType", 1); // Quality level for obstacle avoidance

                // Try to warp to valid position if not on NavMesh
                NavMeshHit hit;
                bool isOnNavMesh = SafeGetProperty<bool>(agent, "isOnNavMesh", true);

                if (!isOnNavMesh)
                {
                    if (NavMesh.SamplePosition(officer.transform.position, out hit, 5f, NavMesh.AllAreas))
                    {
                        SafeInvokeMethod(agent, "Warp", new object[] { hit.position });
                    }
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error enhancing officer navigation: {ex.Message}");
            }
        }

        // IMPROVED: Enhanced waypoint creation for more realistic patrols
        private void CreateEnhancedPatrolWaypoints(GameObject routeGO, Vector3 centerPos)
        {
            try
            {
                // Choose a random patrol pattern
                PatrolPatternType patternType = (PatrolPatternType)UnityEngine.Random.Range(0, 5);

                // Create waypoints based on the pattern
                int waypointCount = UnityEngine.Random.Range(_minWaypoints, _maxWaypoints + 1);
                float patrolRadius = UnityEngine.Random.Range(_minPatrolRadius, _maxPatrolRadius);

                List<Transform> waypoints = new List<Transform>();

                // Always add center point as first waypoint
                var centerWaypointGO = new GameObject("Waypoint_0");
                centerWaypointGO.transform.position = centerPos;
                centerWaypointGO.transform.SetParent(routeGO.transform);
                waypoints.Add(centerWaypointGO.transform);

                // Generate waypoints based on pattern type
                switch (patternType)
                {
                    case PatrolPatternType.Circle:
                        GenerateCirclePattern(routeGO, centerPos, patrolRadius, waypointCount, waypoints);
                        break;

                    case PatrolPatternType.Grid:
                        GenerateGridPattern(routeGO, centerPos, patrolRadius, waypointCount, waypoints);
                        break;

                    case PatrolPatternType.Zigzag:
                        GenerateZigzagPattern(routeGO, centerPos, patrolRadius, waypointCount, waypoints);
                        break;

                    case PatrolPatternType.Star:
                        GenerateStarPattern(routeGO, centerPos, patrolRadius, waypointCount, waypoints);
                        break;

                    case PatrolPatternType.Random:
                    default:
                        GenerateRandomPattern(routeGO, centerPos, patrolRadius, waypointCount, waypoints);
                        break;
                }

                // Set up route with all waypoints
                var route = routeGO.GetComponent<FootPatrolRoute>();

                // Create a new Il2Cpp array with correct size (waypoints.Count)
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
                _core.LoggerInstance.Error($"Error creating enhanced patrol waypoints: {ex.Message}");

                // Fallback - create at least one waypoint
                try
                {
                    var singleWaypoint = new GameObject("Waypoint_0");
                    singleWaypoint.transform.position = centerPos;
                    singleWaypoint.transform.SetParent(routeGO.transform);

                    var route = routeGO.GetComponent<FootPatrolRoute>();
                    route.Waypoints = new Il2CppReferenceArray<Transform>(1);
                    route.Waypoints[0] = singleWaypoint.transform;
                    route.UpdateWaypoints();
                }
                catch { }
            }
        }

        // Generate waypoints in a circle pattern
        private void GenerateCirclePattern(GameObject routeGO, Vector3 center, float radius, int count, List<Transform> waypoints)
        {
            // Start at 1 since we already added the center point
            for (int i = 1; i < count; i++)
            {
                float angle = i * (2 * Mathf.PI / (count - 1)); // Distribute evenly
                Vector3 waypointPos = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0,
                    Mathf.Sin(angle) * radius
                );

                CreateValidWaypoint(routeGO, waypointPos, i, center.y, waypoints);
            }
        }

        // Generate waypoints in a grid pattern
        private void GenerateGridPattern(GameObject routeGO, Vector3 center, float radius, int count, List<Transform> waypoints)
        {
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(count - 1));
            float spacing = (radius * 2) / gridSize;

            int current = 1; // Start at 1 since we already added the center point
            for (int x = 0; x < gridSize && current < count; x++)
            {
                for (int z = 0; z < gridSize && current < count; z++)
                {
                    Vector3 waypointPos = center + new Vector3(
                        (x * spacing) - (radius * 0.9f),
                        0,
                        (z * spacing) - (radius * 0.9f)
                    );

                    CreateValidWaypoint(routeGO, waypointPos, current, center.y, waypoints);
                    current++;
                }
            }
        }

        // Generate waypoints in a zigzag pattern
        private void GenerateZigzagPattern(GameObject routeGO, Vector3 center, float radius, int count, List<Transform> waypoints)
        {
            float segmentLength = (2 * radius) / ((count - 1) / 2);

            for (int i = 1; i < count; i++)
            {
                float xOffset = ((i % 2 == 0) ? -1 : 1) * (radius * 0.8f);
                float zOffset = ((i / 2) * segmentLength) - radius;

                Vector3 waypointPos = center + new Vector3(xOffset, 0, zOffset);
                CreateValidWaypoint(routeGO, waypointPos, i, center.y, waypoints);
            }
        }

        // Generate waypoints in a star pattern
        private void GenerateStarPattern(GameObject routeGO, Vector3 center, float radius, int count, List<Transform> waypoints)
        {
            int points = Mathf.Min(count - 1, 8); // Max 8 points in star
            float innerRadius = radius * 0.4f;

            for (int i = 0; i < points; i++)
            {
                // Outer point
                float angle = i * (2 * Mathf.PI / points);
                Vector3 outerPos = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0,
                    Mathf.Sin(angle) * radius
                );
                CreateValidWaypoint(routeGO, outerPos, i * 2 + 1, center.y, waypoints);

                // Inner point (if we have enough waypoints)
                if ((i * 2 + 2) < count)
                {
                    float innerAngle = (i + 0.5f) * (2 * Mathf.PI / points);
                    Vector3 innerPos = center + new Vector3(
                        Mathf.Cos(innerAngle) * innerRadius,
                        0,
                        Mathf.Sin(innerAngle) * innerRadius
                    );
                    CreateValidWaypoint(routeGO, innerPos, i * 2 + 2, center.y, waypoints);
                }
            }
        }

        // Generate waypoints in a random pattern
        private void GenerateRandomPattern(GameObject routeGO, Vector3 center, float radius, int count, List<Transform> waypoints)
        {
            for (int i = 1; i < count; i++)
            {
                float angle = UnityEngine.Random.Range(0f, 2 * Mathf.PI);
                float distance = UnityEngine.Random.Range(radius * 0.3f, radius);

                Vector3 waypointPos = center + new Vector3(
                    Mathf.Cos(angle) * distance,
                    0,
                    Mathf.Sin(angle) * distance
                );

                CreateValidWaypoint(routeGO, waypointPos, i, center.y, waypoints);
            }
        }

        // Helper method to create a valid waypoint with NavMesh sampling
        private void CreateValidWaypoint(GameObject routeGO, Vector3 position, int index, float defaultY, List<Transform> waypoints)
        {
            // Try to find valid NavMesh position
            NavMeshHit hit;
            Vector3 finalPosition = position;
            bool foundValidPosition = false;

            // Try up to 5 positions with increasing search radius
            for (int attempt = 0; attempt < 5; attempt++)
            {
                float searchRadius = 2f + (attempt * 2f); // 2m, 4m, 6m, 8m, 10m
                if (NavMesh.SamplePosition(position, out hit, searchRadius, NavMesh.AllAreas))
                {
                    // Verify the position is actually walkable and not on edges
                    NavMeshPath path = new NavMeshPath();
                    Vector3 testPos1 = hit.position + new Vector3(1, 0, 0);
                    Vector3 testPos2 = hit.position + new Vector3(-1, 0, 0);
                    Vector3 testPos3 = hit.position + new Vector3(0, 0, 1);
                    Vector3 testPos4 = hit.position + new Vector3(0, 0, -1);

                    // Test if we can navigate in at least 3 directions from this point
                    int validDirections = 0;
                    NavMeshHit testHit;

                    if (NavMesh.SamplePosition(testPos1, out testHit, 2f, NavMesh.AllAreas)) validDirections++;
                    if (NavMesh.SamplePosition(testPos2, out testHit, 2f, NavMesh.AllAreas)) validDirections++;
                    if (NavMesh.SamplePosition(testPos3, out testHit, 2f, NavMesh.AllAreas)) validDirections++;
                    if (NavMesh.SamplePosition(testPos4, out testHit, 2f, NavMesh.AllAreas)) validDirections++;

                    // If we can move in at least 3 directions, this should be a safe position
                    if (validDirections >= 3)
                    {
                        finalPosition = hit.position;
                        finalPosition.y += _waypointHeightOffset; // Add a small height offset
                        foundValidPosition = true;
                        break;
                    }
                }
            }

            // Fallback if we couldn't find a valid position
            if (!foundValidPosition)
            {
                finalPosition = position;
                finalPosition.y = defaultY + _waypointHeightOffset;

                // Log warning for development purposes
                if (_enableDistrictDebugLogs)
                {
                    LogWarning($"Warning: Could not find valid NavMesh position for waypoint {index}, using fallback");
                }
            }

            var waypointGO = new GameObject($"Waypoint_{index}");
            waypointGO.transform.position = finalPosition;
            waypointGO.transform.SetParent(routeGO.transform);
            waypoints.Add(waypointGO.transform);
        }

        // Modified for improved memory management and better cleanup
        private void DespawnOfficer(PoliceOfficer officer)
        {
            if (officer == null) return;

            try
            {
                // Make sure it's one of our officers
                if (!IsModSpawnedOfficer(officer)) return;

                // Find which district this officer is in
                District? officerDistrict = null;
                foreach (var district in _districts)
                {
                    if (district.OfficersSpawned.Contains(officer))
                    {
                        officerDistrict = district;
                        break;
                    }
                }

                // Remove from district
                if (officerDistrict != null)
                {
                    officerDistrict.OfficersSpawned.Remove(officer);

                    // If this officer was part of district population, decrement the count
                    if (!_districtsFullyPopulated)
                    {
                        _districtOfficerCount = Math.Max(0, _districtOfficerCount - 1);
                    }
                }

                // Remove from active officers
                _activeOfficers.Remove(officer);

                // Remove from stuck tracking
                _lastOfficerPositions.Remove(officer);
                _officerStuckCounter.Remove(officer);

                // Instead of destroying, try to add to pool
                if (_officerPool.Count < MAX_POOLED_OFFICERS && officer.gameObject != null)
                {
                    try
                    {
                        // Get this specific officer's route
                        string? routeName = null;
                        if (_officerRouteNames.TryGetValue(officer, out routeName) && routeName != null)
                        {
                            // Use our reference dictionary for cleanup
                            GameObject? routeGO = null;
                            if (_patrolRouteObjects.TryGetValue(routeName, out routeGO) && routeGO != null)
                            {
                                try
                                {
                                    UnityEngine.Object.Destroy(routeGO);
                                }
                                catch (Exception ex)
                                {
                                    _core.LoggerInstance.Error($"Error destroying route: {ex.Message}");
                                }
                                _patrolRouteObjects.Remove(routeName);
                            }
                            else
                            {
                                // Fallback to the old method if not found in our dictionary
                                var patrolRouteObjects = GameObject.FindObjectsOfType<FootPatrolRoute>();
                                foreach (var route in patrolRouteObjects)
                                {
                                    if (route != null && route.name == routeName)
                                    {
                                        try
                                        {
                                            UnityEngine.Object.Destroy(route.gameObject);
                                        }
                                        catch (Exception ex)
                                        {
                                            _core.LoggerInstance.Error($"Error destroying route: {ex.Message}");
                                        }
                                        break;
                                    }
                                }
                            }

                            _officerRouteNames.Remove(officer);
                        }

                        // Deactivate
                        officer.Deactivate();
                        officer.gameObject.SetActive(false);

                        // Store in pool
                        var pooled = new PooledOfficer
                        {
                            GameObject = officer.gameObject,
                            Officer = officer,
                            NetworkObject = officer.gameObject.GetComponent<NetworkObject>(),
                            PooledTime = DateTime.Now
                        };

                        _officerPool.Enqueue(pooled);

                        if (officerDistrict != null && _enableDistrictDebugLogs)
                        {
                            Log($"Officer pooled from {officerDistrict.Name}");
                        }

                        return;
                    }
                    catch (Exception poolEx)
                    {
                        _core.LoggerInstance.Error($"Error pooling officer: {poolEx.Message}");
                    }
                }

                // If pooling fails or pool is full, destroy
                if (officer.gameObject != null)
                {
                    try
                    {
                        // Clean up route using unique ID
                        string? routeName = null;
                        if (_officerRouteNames.TryGetValue(officer, out routeName) && routeName != null)
                        {
                            // Use our reference dictionary for cleanup
                            GameObject? routeGO = null;
                            if (_patrolRouteObjects.TryGetValue(routeName, out routeGO) && routeGO != null)
                            {
                                try
                                {
                                    UnityEngine.Object.Destroy(routeGO);
                                }
                                catch (Exception ex)
                                {
                                    _core.LoggerInstance.Error($"Error destroying route: {ex.Message}");
                                }
                                _patrolRouteObjects.Remove(routeName);
                            }
                            else
                            {
                                // Fallback to the old method if not found in our dictionary
                                var patrolRouteObjects = GameObject.FindObjectsOfType<FootPatrolRoute>();
                                foreach (var route in patrolRouteObjects)
                                {
                                    if (route != null && route.name == routeName)
                                    {
                                        try
                                        {
                                            UnityEngine.Object.Destroy(route.gameObject);
                                        }
                                        catch (Exception ex)
                                        {
                                            _core.LoggerInstance.Error($"Error destroying route: {ex.Message}");
                                        }
                                        break;
                                    }
                                }
                            }
                            _officerRouteNames.Remove(officer);
                        }
                    }
                    catch (Exception ex)
                    {
                        _core.LoggerInstance.Error($"Error cleaning up officer route: {ex.Message}");
                    }

                    officer.Deactivate();
                    UnityEngine.Object.Destroy(officer.gameObject);
                }

                if (officerDistrict != null && _enableDistrictDebugLogs)
                {
                    Log($"Officer removed from {officerDistrict.Name}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error despawning officer: {ex.Message}");

                // Fallback destruction
                try
                {
                    if (officer != null && officer.gameObject != null)
                    {
                        officer.Deactivate();
                        UnityEngine.Object.Destroy(officer.gameObject);
                    }
                }
                catch { }
            }
        }

        private void CleanupInvalidOfficers()
        {
            // Clean up destroyed or null officers from all lists
            int removedCount = 0;

            // Clean active officers list
            for (int i = _activeOfficers.Count - 1; i >= 0; i--)
            {
                var officer = _activeOfficers[i];
                if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy)
                {
                    // Clean up route name dictionary
                    if (officer != null)
                    {
                        string? routeName;
                        if (_officerRouteNames.TryGetValue(officer, out routeName) && routeName != null)
                        {
                            // Use our reference dictionary for cleanup
                            GameObject? routeGO;
                            if (_patrolRouteObjects.TryGetValue(routeName, out routeGO) && routeGO != null)
                            {
                                try
                                {
                                    UnityEngine.Object.Destroy(routeGO);
                                }
                                catch { }
                                _patrolRouteObjects.Remove(routeName);
                            }
                            _officerRouteNames.Remove(officer);
                        }

                        // Also clean up stuck tracking
                        _lastOfficerPositions.Remove(officer);
                        _officerStuckCounter.Remove(officer);
                    }

                    _activeOfficers.RemoveAt(i);
                    removedCount++;
                }
            }

            // Clean district officer lists
            foreach (var district in _districts)
            {
                for (int i = district.OfficersSpawned.Count - 1; i >= 0; i--)
                {
                    var officer = district.OfficersSpawned[i];
                    if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy)
                    {
                        district.OfficersSpawned.RemoveAt(i);
                        removedCount++;

                        // If we're still populating districts, adjust the count
                        if (!_districtsFullyPopulated)
                        {
                            _districtOfficerCount = Math.Max(0, _districtOfficerCount - 1);
                        }
                    }
                }
            }

            // Log if we removed any officers
            if (removedCount > 0 && _enableDistrictDebugLogs)
            {
                Log($"Cleaned up {removedCount} invalid officers");

                // Force garbage collection after a significant cleanup
                if (removedCount >= 10)
                {
                    GC.Collect();
                }
            }
        }

        private void CleanOldPooledOfficers()
        {
            // Remove officers from the pool that have been there more than 5 minutes
            DateTime cutoff = DateTime.Now.AddMinutes(-5);
            int removed = 0;

            while (_officerPool.Count > 0 && _officerPool.Peek().PooledTime < cutoff)
            {
                var pooled = _officerPool.Dequeue();
                try
                {
                    if (pooled.GameObject != null)
                    {
                        UnityEngine.Object.Destroy(pooled.GameObject);
                    }
                }
                catch { }
                removed++;
            }

            if (removed > 0)
            {
                Log($"Cleaned {removed} old officers from the pool");
            }
        }

        // Modified to show district officer tracking with less logging
        private void LogOfficerCounts()
        {
            // Don't log at all in normal operation to avoid hitching
            if (!_enableConsoleLogs && !_enableDistrictDebugLogs && _populationState == DistrictPopulationState.MaintenanceMode)
            {
                return;
            }

            // Only log once every 30 seconds during population phase and every 2 minutes in maintenance
            double minTimeBetweenLogs = _populationState == DistrictPopulationState.MaintenanceMode ? 120 : 30;
            if ((DateTime.Now - _lastLogDisplayTime).TotalSeconds < minTimeBetweenLogs)
            {
                return;
            }

            _lastLogDisplayTime = DateTime.Now;

            // Only log basic statistics in maintenance mode to avoid hitching
            int currentLimit = GetCurrentOfficerLimit();
            int totalOfficers = GetTotalOfficerCount(false); // Get absolute total

            if (_populationState == DistrictPopulationState.MaintenanceMode)
            {
                // Only log this once every 2 minutes
                Log($"Maintenance status: {totalOfficers} officers - District: {_districtOfficerCount}/{_maxDistrictOfficers}");
            }
            else
            {
                // More verbose logging during population phases
                int localOfficers = GetLocalOfficerCount();
                Log($"Population progress: {_districtOfficerCount}/{_maxDistrictOfficers} district officers - Local: {localOfficers}/{_maxLocalOfficers}");

                // Only show district details during initial population
                foreach (var district in _districts)
                {
                    // Show population percentage
                    int percentage = district.TargetOfficers > 0 ?
                        (district.OfficersSpawned.Count * 100) / district.TargetOfficers : 0;
                    Log($"  {district.Name}: {district.OfficersSpawned.Count}/{district.TargetOfficers} officers ({percentage}%)");
                }
            }
        }

        // FIXED: Updated EnsureAllDistrictsHaveTargetOfficers to handle verification and cleanup better
        private void EnsureAllDistrictsHaveTargetOfficers()
        {
            // Update district targets first based on time of day
            UpdateDistrictTargetOfficers();

            // Only do anything if we're not in maintenance mode
            if (_populationState == DistrictPopulationState.MaintenanceMode)
            {
                // Check if any district is below target and if so, switch to population mode
                bool anyDistrictNeedingOfficers = false;
                foreach (var district in _districts)
                {
                    if (district.OfficersSpawned.Count < district.TargetOfficers)
                    {
                        anyDistrictNeedingOfficers = true;
                        break;
                    }
                }

                if (anyDistrictNeedingOfficers)
                {
                    _populationState = DistrictPopulationState.InitialCheck;
                    _needsStatusUpdate = true;
                    Log("Some districts need repopulation. Restarting district population process.");
                }
                else
                {
                    // FIXED: Clean up excess officers in maintenance mode
                    foreach (var district in _districts)
                    {
                        int excessOfficers = Math.Max(0, district.OfficersSpawned.Count - district.TargetOfficers);
                        if (excessOfficers > 0)
                        {
                            Log($"Maintenance: {district.Name} has {excessOfficers} excess officers, removing");

                            for (int i = 0; i < excessOfficers && i < district.OfficersSpawned.Count; i++)
                            {
                                QueueDespawn(district.OfficersSpawned[i]);
                            }
                        }
                    }
                }
                return;
            }

            // Handle population process based on current state
            switch (_populationState)
            {
                case DistrictPopulationState.InitialCheck:
                    // Log the initial status once
                    if (_needsStatusUpdate)
                    {
                        Log($"==== DISTRICT POPULATION INITIAL STATUS ====");

                        int totalNeeded = 0;
                        foreach (var district in _districts)
                        {
                            // FIXED: Check for both missing and excess officers
                            int officersNeeded = Math.Max(0, district.TargetOfficers - district.OfficersSpawned.Count);
                            int excessOfficers = Math.Max(0, district.OfficersSpawned.Count - district.TargetOfficers);

                            totalNeeded += officersNeeded;

                            if (officersNeeded > 0)
                            {
                                int populationPercentage = (district.OfficersSpawned.Count * 100) / Math.Max(1, district.TargetOfficers);
                                Log($"  {district.Name} needs {officersNeeded} more officers ({populationPercentage}% populated)");
                            }
                            else if (excessOfficers > 0)
                            {
                                int populationPercentage = (district.OfficersSpawned.Count * 100) / Math.Max(1, district.TargetOfficers);
                                Log($"  {district.Name} has {excessOfficers} excess officers ({populationPercentage}% populated)");

                                // Queue excess officers for removal
                                for (int i = 0; i < excessOfficers && i < district.OfficersSpawned.Count; i++)
                                {
                                    QueueDespawn(district.OfficersSpawned[i]);
                                }
                            }
                        }

                        Log($"Total officers to spawn: {totalNeeded}");

                        if (totalNeeded > 0)
                        {
                            Log($"Beginning initial population...");
                            // Move to population phase
                            _populationState = DistrictPopulationState.InitialPopulation;
                            _lastPopulationStateChangeTime = DateTime.Now;

                            // Queue up officers that are needed
                            QueueInitialDistrictPopulation();
                        }
                        else
                        {
                            // All districts are at or above target, switch to verification
                            _populationState = DistrictPopulationState.VerificationCheck;
                            _lastPopulationStateChangeTime = DateTime.Now;
                            _needsStatusUpdate = true;
                        }

                        _needsStatusUpdate = false;
                    }
                    break;

                case DistrictPopulationState.InitialPopulation:
                    // Don't do any checks here - just let the queue process
                    // Only switch to verification once enough time has passed to ensure all queued spawns processed
                    if ((DateTime.Now - _lastPopulationStateChangeTime).TotalSeconds > 60)
                    {
                        _populationState = DistrictPopulationState.VerificationCheck;
                        _lastPopulationStateChangeTime = DateTime.Now;
                        _needsStatusUpdate = true;
                        Log($"Initial population time elapsed. Verifying district status...");
                    }
                    break;

                case DistrictPopulationState.VerificationCheck:
                    // Verify that all districts are truly populated
                    if (_needsStatusUpdate)
                    {
                        Log($"==== DISTRICT POPULATION VERIFICATION ====");

                        // Count how many districts still need officers
                        int districtsNeedingOfficers = 0;
                        int officersStillNeeded = 0;

                        foreach (var district in _districts)
                        {
                            int officersNeeded = Math.Max(0, district.TargetOfficers - district.OfficersSpawned.Count);
                            int officersToRemove = Math.Max(0, district.OfficersSpawned.Count - district.TargetOfficers);

                            // Remove excess officers
                            if (officersToRemove > 0)
                            {
                                Log($"  {district.Name} has {officersToRemove} excess officers, queueing for removal");
                                for (int i = 0; i < officersToRemove && i < district.OfficersSpawned.Count; i++)
                                {
                                    QueueDespawn(district.OfficersSpawned[i]);
                                }
                            }

                            // Count districts needing more officers
                            if (officersNeeded > 0)
                            {
                                districtsNeedingOfficers++;
                                officersStillNeeded += officersNeeded;

                                int populationPercentage = (district.OfficersSpawned.Count * 100) / Math.Max(1, district.TargetOfficers);
                                Log($"  {district.Name} still needs {officersNeeded} more officers ({populationPercentage}% populated)");

                                // Queue the needed officers
                                for (int i = 0; i < officersNeeded; i++)
                                {
                                    QueueSpawnInDistrict(district, 2.0f);
                                }
                            }
                        }

                        if (districtsNeedingOfficers > 0)
                        {
                            Log($"Found {districtsNeedingOfficers} districts still needing {officersStillNeeded} officers.");
                            _populationState = DistrictPopulationState.InitialPopulation;
                            _lastPopulationStateChangeTime = DateTime.Now;
                        }
                        else
                        {
                            // All districts are populated, switch to maintenance mode
                            _populationState = DistrictPopulationState.MaintenanceMode;
                            _districtsFullyPopulated = true;

                            // Once districts are fully populated, switch to reduced maintenance interval
                            _districtMaintenanceInterval = _reducedDistrictMaintenanceInterval;
                            Log($"==== ALL DISTRICTS FULLY POPULATED ====");
                            Log($"District check interval reduced to {_districtMaintenanceInterval / 60f} minutes");

                            // Force garbage collection to clean up after initial population phase
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }

                        _needsStatusUpdate = false;
                    }
                    break;
            }
        }

        public void OnSceneUnloaded()
        {
            try
            {
                _core.LoggerInstance.Msg("Officer spawn system beginning cleanup on scene unload");

                _gameFullyLoaded = false;
                _startupSpawnInitiated = false;
                _populationState = DistrictPopulationState.InitialCheck;
                _needsStatusUpdate = true;
                _lastPopulationStateChangeTime = DateTime.Now;

                // Reset initialization variables
                _gameReady = false;
                _gameReadyCheckTime = 0f;
                _initFailCount = 0;
                _playerMovementDetected = false;
                _lastCheckedPosition = Vector3.zero;

                // Reset day change tracking
                _lastCheckedGameTime = -1;
                _dayDespawnCompleted = false;
                _isNightTime = false;
                _officersShouldBeActive = false;

                // Clear spawn and despawn queues first to prevent further operations
                _spawnQueue.Clear();
                _despawnQueue.Clear();

                // Reset internal state
                _currentlySpawningOfficer = false;

                // Clean up the officer pool
                while (_officerPool.Count > 0)
                {
                    var pooled = _officerPool.Dequeue();
                    if (pooled?.GameObject != null)
                    {
                        UnityEngine.Object.Destroy(pooled.GameObject);
                    }
                }

                // Clean up existing officers
                foreach (var officer in new List<PoliceOfficer>(_activeOfficers))
                {
                    try
                    {
                        if (officer?.gameObject != null)
                        {
                            officer.Deactivate();
                            UnityEngine.Object.Destroy(officer.gameObject);
                        }
                    }
                    catch (Exception ex)
                    {
                        _core.LoggerInstance.Error($"Error cleaning up officer: {ex.Message}");
                    }
                }

                // Clean up patrol routes directly
                foreach (var entry in new Dictionary<string, GameObject>(_patrolRouteObjects))
                {
                    if (entry.Value != null)
                    {
                        try
                        {
                            UnityEngine.Object.Destroy(entry.Value);
                        }
                        catch (Exception ex)
                        {
                            _core.LoggerInstance.Error($"Error destroying route: {ex.Message}");
                        }
                    }
                }
                _patrolRouteObjects.Clear();

                // EXTRA CAREFUL - Find and clean up any remaining patrol routes
                try
                {
                    var allPatrolRoutes = GameObject.FindObjectsOfType<FootPatrolRoute>();
                    foreach (var route in allPatrolRoutes)
                    {
                        if (route != null && route.gameObject != null)
                        {
                            try
                            {
                                UnityEngine.Object.Destroy(route.gameObject);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _core.LoggerInstance.Error($"Error finding patrol routes: {ex.Message}");
                }

                // Clear all dictionaries and lists
                _activeOfficers.Clear();
                _officerRouteNames.Clear();
                _lastOfficerPositions.Clear();
                _officerStuckCounter.Clear();

                foreach (var district in _districts)
                {
                    district.OfficersSpawned.Clear();
                }

                // Reset variables
                _initialized = false;
                _networkManager = null;
                _isGameCurrentlySpawning = false;
                _districtsFullyPopulated = false;
                _districtOfficerCount = 0;
                _isNinePMMaxSpawnTriggered = false;
                _currentPlayerDistrict = null;
                _previousPlayerDistrict = null;
                // FIX 1: Removed the invalid _cachedPlayerObject reference line

                // Double garbage collection to ensure complete cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Log("Officer spawn system completely reset on scene unload");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error in OnSceneUnloaded: {ex.Message}");
            }
        }
    }
}