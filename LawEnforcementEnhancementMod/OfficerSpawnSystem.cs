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

        private readonly string[] OfficerTypes = new string[]
        {
            "OfficerBailey", "OfficerCooper", "OfficerDavis",
            "OfficerGreen", "OfficerHoward", "OfficerJackson",
            "OfficerLee", "OfficerLeo", "OfficerLopez",
            "OfficerMurphy", "OfficerOakley", "OfficerSanchez"
        };

        // Officer Pool
        private readonly Queue<PooledOfficer> _officerPool = new Queue<PooledOfficer>();
        private const int MAX_POOLED_OFFICERS = 10;

        private readonly Core _core;
        private NetworkManager _networkManager;
        private bool _initialized;
        private bool _gameFullyLoaded = false;

        // UPDATED DEFAULT VALUES per feedback
        private float _spawnCooldown = 3.5f; // Spawn one officer every 3.5 seconds
        private float _nextSpawnTime = 0f;
        private float _despawnCooldown = 10f; // Despawn one officer every 10 seconds
        private float _nextDespawnTime = 0f;

        // Officer limits - updated defaults
        private int _maxTotalOfficers = 60;
        private int _officersPerDistrict = 10;

        // New limit for officers close to the player
        private float _localOfficerRadius = 100f; // Consider officers within 100m of player "local"
        private int _maxLocalOfficers = 30; // Maximum officers near player (updated)

        // Districts maintenance interval
        private float _normalDistrictMaintenanceInterval = 30f;  // Normal interval (30 seconds)
        private float _reducedDistrictMaintenanceInterval = 300f; // Reduced interval (5 minutes)
        private float _districtMaintenanceInterval = 30f; // Will be set in Initialize()
        private float _lastDistrictMaintenanceTime = 0f;
        private float _lastReconciliationTime = 0f;  // NEW: For tracking when we last reconciled officer counts
        private float _reconciliationInterval = 15f; // NEW: Reconcile every 15 seconds

        // District report tracking
        private bool _hasLoggedDistrictNeeds = false;
        private DateTime _lastFullDistrictCheckTime = DateTime.MinValue;

        // Intervals
        private float _playerCheckInterval = 5f; // Check player position every 5 seconds
        private float _lastPlayerCheckTime = 0f;
        private float _districtTransitionCooldown = 15f; // Minimum time between district processing
        private float _lastDistrictTransitionTime = 0f;

        // Player tracking
        private Vector3 _lastPlayerPosition = Vector3.zero;
        private District _currentPlayerDistrict = null;
        private District _previousPlayerDistrict = null;

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
        private float _ninePMCheckInterval = 60f; // Check less frequently
        private int _lastCheckedGameTime = -1;

        // District management
        private readonly List<District> _districts = new List<District>();

        // Time-based officer count limits (updated defaults)
        private int _morningOfficerLimit = 10;   // 6:00 AM - 12:00 PM
        private int _afternoonOfficerLimit = 15; // 12:00 PM - 6:00 PM
        private int _eveningOfficerLimit = 30;   // 6:00 PM - 9:00 PM 
        private int _nightOfficerLimit = 60;     // 9:00 PM - 6:00 AM

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
        private float _delayBeforeSpawning = 10f; // Wait 10 seconds before starting to spawn officers

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
            public GameObject GameObject { get; set; }
            public PoliceOfficer Officer { get; set; }
            public NetworkObject NetworkObject { get; set; }
            public DateTime PooledTime { get; set; }
        }

        public class SpawnJob
        {
            public Vector3 Position { get; set; }
            public District District { get; set; }
            public float Priority { get; set; } = 1f;
            public DateTime QueuedTime { get; set; } = DateTime.Now;
        }

        public class District
        {
            public string Name { get; set; }
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

        public class SpawnSystemConfig
        {
            // Added all configurable parameters here with UPDATED defaults
            public int MaxTotalOfficers { get; set; } = 60;
            public int OfficersPerDistrict { get; set; } = 10;
            public float SpawnCooldown { get; set; } = 3.5f;
            public float DespawnCooldown { get; set; } = 10f;
            public float PlayerCheckInterval { get; set; } = 5f;
            public float DistrictTransitionCooldown { get; set; } = 15f;
            public bool PreserveDistrictOfficers { get; set; } = true;
            public int MaxSpawnsPerCycle { get; set; } = 1;
            public int MaxDespawnsPerCycle { get; set; } = 1;
            public int MaxLocalOfficers { get; set; } = 30;
            public float LocalOfficerRadius { get; set; } = 100f;
            public float NormalDistrictMaintenanceInterval { get; set; } = 30f;
            public float ReducedDistrictMaintenanceInterval { get; set; } = 300f;
            public int DistrictTransitionBuffer { get; set; } = 2;
            public float MinPatrolRadius { get; set; } = 20f;
            public float MaxPatrolRadius { get; set; } = 40f;
            public int MinPatrolWaypoints { get; set; } = 5;
            public int MaxPatrolWaypoints { get; set; } = 8;
            public bool PopulateAllDistrictsOnStartup { get; set; } = true;
            public bool PreloadAllDistrictOfficers { get; set; } = false;
            public float DelayBeforeSpawning { get; set; } = 10f;

            // Time-based officer count limits
            public int MorningOfficerLimit { get; set; } = 10;
            public int AfternoonOfficerLimit { get; set; } = 15;
            public int EveningOfficerLimit { get; set; } = 30;
            public int NightOfficerLimit { get; set; } = 60;
        }

        public OfficerSpawnSystem()
        {
            _core = Core.Instance;
            _configFilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "LawEnforcementEnhancement_Config.json");
            LoadConfig();
            InitializeDistricts();

            // Register to game loaded event - simpler approach than scene events
            MelonEvents.OnSceneWasLoaded.Subscribe(OnSceneWasLoaded);
        }

        private void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _core.LoggerInstance.Msg($"Scene loaded: {sceneName}, preparing initialization");

            // Reset initialization tracking
            _gameReady = false;
            _gameReadyCheckTime = 0f;
            _initFailCount = 0;
            _playerMovementDetected = false;
            _lastCheckedPosition = Vector3.zero;
            _worldLoadedTime = Time.time; // Record when the world was loaded

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

                    _maxTotalOfficers = config.MaxTotalOfficers;
                    _officersPerDistrict = config.OfficersPerDistrict;
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

                    _core.LoggerInstance.Msg($"Loaded officer spawn system configuration from {_configFilePath}");
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
                var config = new SpawnSystemConfig
                {
                    MaxTotalOfficers = _maxTotalOfficers,
                    OfficersPerDistrict = _officersPerDistrict,
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

                    // Time-based officer limits
                    MorningOfficerLimit = _morningOfficerLimit,
                    AfternoonOfficerLimit = _afternoonOfficerLimit,
                    EveningOfficerLimit = _eveningOfficerLimit,
                    NightOfficerLimit = _nightOfficerLimit
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                _core.LoggerInstance.Msg($"Created default configuration at {_configFilePath}");
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
                TargetOfficers = _officersPerDistrict, // Simplified: same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(0.80f, 0.10f, 47.20f),
                    new Vector3(4.04f, -1.44f, 1.51f),
                    new Vector3(7.41f, 1.02f, 57.64f),
                    new Vector3(9.10f, 0.10f, 46.40f),
                    new Vector3(9.50f, 0.10f, 47.40f),
                    new Vector3(12.80f, 0.80f, 38.50f),
                    new Vector3(-12.91f, 1.01f, 33.23f),
                    new Vector3(-14.10f, 0.10f, 46.30f),
                    new Vector3(17.00f, 1.20f, 36.30f),
                    new Vector3(19.00f, 0.10f, 54.60f),
                    new Vector3(20.60f, 0.10f, 40.80f),
                    new Vector3(21.10f, 0.10f, 40.30f),
                    new Vector3(22.50f, 0.10f, 46.30f),
                    new Vector3(26.30f, 0.10f, 47.60f),
                    new Vector3(26.80f, 0.10f, 32.80f),
                    new Vector3(27.40f, 0.10f, 38.70f),
                    new Vector3(-29.90f, 1.10f, 52.10f)
                }
            };
            _districts.Add(downtown);

            // Define Eastern District
            var eastern = new District
            {
                Name = "Eastern District",
                Center = new Vector3(100, 0, 0),
                Radius = 120,
                TargetOfficers = _officersPerDistrict, // Same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(37.80f, -1.44f, -26.25f),
                    new Vector3(40.32f, -1.44f, 16.15f),
                    new Vector3(40.46f, 0.97f, 13.04f),
                    new Vector3(55.36f, 1.64f, -16.46f),
                    new Vector3(58.65f, 1.06f, 77.30f),
                    new Vector3(80.36f, 1.64f, -65.67f),
                    new Vector3(85.43f, 0.95f, -39.23f),
                    new Vector3(90.38f, 1.64f, -47.66f),
                    new Vector3(125.85f, 2.85f, -69.75f),
                    new Vector3(139.89f, 5.07f, -51.15f),
                    new Vector3(155.03f, 0.97f, 57.62f),
                    new Vector3(157.71f, 5.07f, -71.84f),
                    new Vector3(166.29f, 0.98f, -14.24f),
                    new Vector3(179.62f, 0.97f, 29.05f)
                }
            };
            _districts.Add(eastern);

            // Define Western District
            var western = new District
            {
                Name = "Western District",
                Center = new Vector3(-80, 0, 0),
                Radius = 80,
                TargetOfficers = _officersPerDistrict, // Same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(-41.11f, -1.44f, -94.58f),
                    new Vector3(-47.00f, -1.44f, -89.79f),
                    new Vector3(-63.09f, -1.44f, -46.47f),
                    new Vector3(-77.85f, -1.44f, -66.53f),
                    new Vector3(-79.00f, 0.00f, 62.70f),
                    new Vector3(-83.44f, -1.44f, -38.06f),
                    new Vector3(-88.30f, -1.44f, 37.92f),
                    new Vector3(-95.89f, -1.44f, 22.21f)
                }
            };
            _districts.Add(western);

            // Define Far Western District
            var farWestern = new District
            {
                Name = "Far Western District",
                Center = new Vector3(-150, 0, 80),
                Radius = 80,
                TargetOfficers = _officersPerDistrict, // Same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(-102.77f, -4.66f, 22.25f),
                    new Vector3(-113.82f, -4.66f, 69.82f),
                    new Vector3(-117.90f, -3.90f, 85.10f),
                    new Vector3(-122.50f, -4.15f, 102.64f),
                    new Vector3(-127.70f, -3.90f, 88.90f),
                    new Vector3(-132.11f, -3.32f, 87.61f),
                    new Vector3(-139.97f, -4.15f, 140.58f),
                    new Vector3(-140.58f, -3.16f, 73.71f),
                    new Vector3(-145.57f, -5.58f, 145.36f),
                    new Vector3(-148.15f, -4.34f, 37.22f),
                    new Vector3(-159.94f, -3.10f, 21.62f),
                    new Vector3(-183.13f, -3.10f, 103.07f),
                    new Vector3(-186.86f, -3.16f, 70.05f)
                }
            };
            _districts.Add(farWestern);

            // Define Northern District
            var northern = new District
            {
                Name = "Northern District",
                Center = new Vector3(0, 0, 120),
                Radius = 80,
                TargetOfficers = _officersPerDistrict, // Same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(100.22f, 0.97f, 111.76f),
                    new Vector3(120.36f, 0.98f, 118.38f),
                    new Vector3(-117.90f, -3.90f, 85.10f),
                    new Vector3(-122.50f, -4.15f, 102.64f),
                    new Vector3(-127.70f, -3.90f, 88.90f),
                    new Vector3(-132.11f, -3.32f, 87.61f),
                    new Vector3(-139.97f, -4.15f, 140.58f),
                    new Vector3(-145.57f, -5.58f, 145.36f),
                    new Vector3(-183.13f, -3.10f, 103.07f),
                    new Vector3(-18.71f, 0.87f, 147.80f)
                }
            };
            _districts.Add(northern);

            // Define Southern District
            var southern = new District
            {
                Name = "Southern District",
                Center = new Vector3(0, 0, -80),
                Radius = 100,
                TargetOfficers = _officersPerDistrict, // Same for all districts
                SpawnPoints = new List<Vector3>
                {
                    new Vector3(-12.74f, -1.44f, -56.73f),
                    new Vector3(-27.45f, -1.44f, -44.61f),
                    new Vector3(15.12f, 1.64f, -131.61f),
                    new Vector3(20.22f, -1.44f, -29.83f),
                    new Vector3(26.60f, 5.07f, -136.89f),
                    new Vector3(37.80f, -1.44f, -26.25f),
                    new Vector3(59.28f, 2.85f, -96.76f),
                    new Vector3(85.43f, 0.95f, -39.23f),
                    new Vector3(90.38f, 1.64f, -47.66f)
                }
            };
            _districts.Add(southern);

            // Calculate total district officers needed
            _maxDistrictOfficers = _districts.Sum(d => d.TargetOfficers);

            _core.LoggerInstance.Msg($"Initialized {_districts.Count} districts with {_districts.Sum(d => d.SpawnPoints.Count)} total spawn points");
            _core.LoggerInstance.Msg($"District population requires {_maxDistrictOfficers} officers");
        }

        public void Initialize()
        {
            _lastUpdateTime = Time.time;
            _lastPlayerCheckTime = Time.time;
            _lastNinePMCheckTime = Time.time;
            _lastDistrictTransitionTime = Time.time;
            _lastDistrictMaintenanceTime = Time.time;
            _lastReconciliationTime = Time.time; // NEW: Initialize reconciliation timer
            _lastStuckCheckTime = Time.time;     // NEW: Initialize stuck check timer
            _lastFullDistrictCheckTime = DateTime.Now;
            _lastLogDisplayTime = DateTime.Now;
            _initialized = true;
            _startupSpawnInitiated = false;
            _startupDelayTimer = 0f;
            _hasLoggedDistrictNeeds = false;
            _districtMaintenanceInterval = _normalDistrictMaintenanceInterval; // Start with normal interval
            _populationState = DistrictPopulationState.InitialCheck;
            _needsStatusUpdate = true;
            _lastPopulationStateChangeTime = DateTime.Now;

            _core.LoggerInstance.Msg("District-based officer spawn system initialized");
        }

        // New method to safely queue officers after game is loaded
        private void InitiateStartupSpawning()
        {
            if (_startupSpawnInitiated) return;

            _core.LoggerInstance.Msg("Game has stabilized - beginning district population");
            _startupSpawnInitiated = true;

            if (_populateAllDistrictsOnStartup)
            {
                if (_preloadAllDistrictOfficers)
                {
                    // If we're preloading, spawn all district officers immediately during loading
                    _core.LoggerInstance.Msg("Preloading all district officers at once");
                    PreloadDistrictOfficers();
                }
                else
                {
                    // Otherwise, queue them as usual
                    QueueInitialDistrictPopulation();
                }
            }
        }

        // MODIFIED: Improved Update method for better initialization
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
                                    _core.LoggerInstance.Msg("Waiting for NetworkManager...");
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
                                _core.LoggerInstance.Msg($"Waiting for world to stabilize: {timeElapsed:F1}/{_requiredStabilityDelay} seconds");
                            _initFailCount++;
                            return; // Wait for stability period
                        }

                        // Check if we can detect player movement
                        GameObject playerObj = GameObject.Find("Player_Local");
                        if (playerObj != null)
                        {
                            Vector3 playerPosition = playerObj.transform.position;

                            // If this is our first position check
                            if (_lastCheckedPosition == Vector3.zero)
                            {
                                _lastCheckedPosition = playerPosition;
                                if (_initFailCount % 3 == 0)
                                    _core.LoggerInstance.Msg("Found player, waiting for movement detection...");
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
                                    _core.LoggerInstance.Msg("Player movement detected, initializing officer system");
                                }
                                else
                                {
                                    _lastCheckedPosition = playerPosition; // Update for next check
                                    if (_initFailCount % 5 == 0) // Log less frequently
                                        _core.LoggerInstance.Msg("Waiting for player movement...");
                                    _initFailCount++;
                                    return; // Wait for player to move
                                }
                            }

                            // Store the player position for use by the system
                            _lastPlayerPosition = playerPosition;

                            // If we got here, we're good to go!
                            _gameReady = true;
                            _gameFullyLoaded = true;
                            _core.LoggerInstance.Msg("Game fully initialized, beginning officer spawning");
                        }
                        else
                        {
                            _initFailCount++;
                            if (_initFailCount % 3 == 0) // Log every 3rd attempt
                                _core.LoggerInstance.Msg("Waiting for player object...");

                            // Force initialization after too many failures, player might be in scene but not found
                            if (_initFailCount >= MAX_INIT_FAILURES)
                            {
                                _gameReady = true;
                                _gameFullyLoaded = true;
                                _core.LoggerInstance.Msg("Forcing initialization after multiple attempts");
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

                    // Check for 9 PM on set interval
                    if (currentTime - _lastNinePMCheckTime >= _ninePMCheckInterval)
                    {
                        _lastNinePMCheckTime = currentTime;
                        CheckForNinePM();
                    }

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

                    // Process despawn queue with even longer cooldown
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
                _core.LoggerInstance.Msg($"Reconciliation found and removed {officersRemoved} despawned officers from district counts");

                // If any were removed, it might be worth checking active officers list too
                CleanupInvalidOfficers();

                // Trigger a verification check to refill any understaffed districts
                if (_populationState == DistrictPopulationState.MaintenanceMode && officersRemoved > 5)
                {
                    _populationState = DistrictPopulationState.InitialCheck;
                    _needsStatusUpdate = true;
                    _core.LoggerInstance.Msg("Significant officer loss detected. Restarting district population process.");
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
                if (officer == null || officer.gameObject == null || !officer.gameObject.activeInHierarchy)
                {
                    officersToRemove.Add(officer);
                }
            }

            foreach (var officer in officersToRemove)
            {
                _lastOfficerPositions.Remove(officer);
                _officerStuckCounter.Remove(officer);
            }

            if (fixedCount > 0)
            {
                _core.LoggerInstance.Msg($"Fixed {fixedCount} stuck officers");
            }
        }

        // NEW: Try to fix a stuck officer
        private bool FixStuckOfficer(PoliceOfficer officer)
        {
            try
            {
                if (officer == null || officer.gameObject == null) return false;

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
                    string routeName = null;
                    if (_officerRouteNames.TryGetValue(officer, out routeName))
                    {
                        GameObject routeGO = null;
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
                District officerDistrict = null;
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
                    return (T)prop.GetValue(obj);
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

                _core.LoggerInstance.Msg($"Beginning immediate preload of {_maxDistrictOfficers} district officers");
                int spawnedCount = 0;

                foreach (var district in _districts)
                {
                    int officersNeeded = district.TargetOfficers;
                    _core.LoggerInstance.Msg($"Preloading {officersNeeded} officers for {district.Name}");

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
                    _core.LoggerInstance.Msg($"Successfully preloaded {spawnedCount} district officers");
                    _core.LoggerInstance.Msg($"All districts fully populated! Switching to maintenance mode.");
                }
                else
                {
                    _populationState = DistrictPopulationState.VerificationCheck;
                    _core.LoggerInstance.Msg($"Partially preloaded {spawnedCount}/{_maxDistrictOfficers} district officers, checking gaps...");
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
                    _core.LoggerInstance.Msg($"Queueing {officersNeeded} officers for initial population in {district.Name}");

                    for (int i = 0; i < officersNeeded; i++)
                    {
                        QueueSpawnInDistrict(district, 1.5f); // Higher priority for initial population
                    }
                }
                else
                {
                    // Don't queue any officers for this district
                    _core.LoggerInstance.Msg($"Skipping {district.Name} - already at target ({district.OfficersSpawned.Count}/{district.TargetOfficers})");
                }
            }
        }

        public int GetCurrentOfficerLimit()
        {
            // Get game time from TimeManager
            TimeManager timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
            if (timeManager == null)
                return _maxTotalOfficers; // Default to max if TimeManager is not available

            int currentTime = timeManager.CurrentTime;

            // If it's 9 PM (2100) or later, or the 9 PM max spawn has been triggered, return the max
            if (currentTime >= 2100 || _isNinePMMaxSpawnTriggered)
            {
                return _maxTotalOfficers; // Always return max after 9 PM
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

        // Method to get local officer count
        private int GetLocalOfficerCount()
        {
            if (_lastPlayerPosition == Vector3.zero) return 0;

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

        // Modified to consider district population
        private bool CanSpawnMoreOfficers(Vector3 position, bool isForDistrictPopulation)
        {
            // Always allow district population officers if under district capacity
            if (isForDistrictPopulation && _districtOfficerCount < _maxDistrictOfficers)
            {
                return true;
            }

            // Check local density if spawning near player
            if (Vector3.Distance(position, _lastPlayerPosition) <= _localOfficerRadius)
            {
                int localOfficers = GetLocalOfficerCount();
                return localOfficers < _maxLocalOfficers;
            }

            // Not near player, so allow spawn
            return true;
        }

        private float _lastUpdateTime;

        private void CheckForNinePM()
        {
            try
            {
                TimeManager timeManager = UnityEngine.Object.FindObjectOfType<TimeManager>();
                if (timeManager == null) return;

                int currentTime = timeManager.CurrentTime;

                // If it's 9 PM (2100) or later and we haven't triggered max spawning yet
                if (currentTime >= 2100 && !_isNinePMMaxSpawnTriggered)
                {
                    _core.LoggerInstance.Msg("9 PM detected - Initiating maximum officer spawning protocol");
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
                        _core.LoggerInstance.Msg("New day detected - Resetting maximum officer spawning protocol");
                    }
                }

                _lastCheckedGameTime = currentTime;
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
                District newDistrict = null;
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
                    _core.LoggerInstance.Msg($"Player entered {newDistrict.Name}");

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

        private void HandleDistrictTransition(District oldDistrict, District newDistrict)
        {
            // When transitioning to a new district, ensure it has target officers
            // No longer add a buffer - just fill up to the target
            int officersToSpawn = Math.Max(0, newDistrict.TargetOfficers - newDistrict.OfficersSpawned.Count);

            if (officersToSpawn > 0 && !_districtsFullyPopulated)
            {
                // Only log in development mode
                if (_enableDistrictDebugLogs)
                {
                    _core.LoggerInstance.Msg($"Adding {officersToSpawn} officers to {newDistrict.Name} (transition)");
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
                                _core.LoggerInstance.Msg($"Adding {adjacentOfficersNeeded} officers to adjacent {district.Name}");
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
                        _core.LoggerInstance.Msg($"Removing {toDespawn} excess officers from {oldDistrict.Name}");
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
                if (district.OverlapsWith(_currentPlayerDistrict)) continue;

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
                            _core.LoggerInstance.Msg($"Despawning {toDespawn} excess officers from distant {district.Name}");
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
        private IEnumerator SpawnOfficerCoroutine(Vector3 position, District district, bool isForDistrictPopulation)
        {
            // Wait a frame before spawning to allow the current frame to complete
            yield return null;

            bool success = SpawnOfficer(position, district, isForDistrictPopulation);

            if (!success)
            {
                _consecutiveSpawnFailures++;
                if (_consecutiveSpawnFailures >= MAX_SPAWN_FAILURES)
                {
                    _core.LoggerInstance.Msg($"Warning: {_consecutiveSpawnFailures} consecutive spawn failures detected. Possible game state issue.");
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
        private bool SpawnOfficer(Vector3 position, District district, bool isForDistrictPopulation)
        {
            try
            {
                // Double-check district limits before spawning
                if (district.OfficersSpawned.Count >= district.TargetOfficers)
                {
                    return false; // Skip if district is already at or above target 
                }

                // Check if we have a pooled officer we can reuse
                PooledOfficer pooled = null;
                if (_officerPool.Count > 0)
                {
                    pooled = _officerPool.Dequeue();
                }

                if (pooled != null)
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

                    // NEW: Enhance NavMeshAgent properties for better movement
                    EnhanceOfficerNavigation(pooledOfficer);

                    // Reactivate officer and start patrol
                    pooledOfficer.Activate();
                    pooledOfficer.StartFootPatrol(patrolGroup, true);

                    // Add to active officers
                    _activeOfficers.Add(pooledOfficer);

                    // Add to district
                    district.OfficersSpawned.Add(pooledOfficer);

                    // Update district officer count if applicable
                    if (isForDistrictPopulation)
                    {
                        _districtOfficerCount++;

                        // Only log during initial population or if debug is enabled
                        if (_populationState == DistrictPopulationState.InitialPopulation || _enableDistrictDebugLogs)
                        {
                            _core.LoggerInstance.Msg($"District population: {_districtOfficerCount}/{_maxDistrictOfficers}");
                        }
                    }

                    // Only log in development mode
                    if (_enableDistrictDebugLogs)
                    {
                        _core.LoggerInstance.Msg($"Officer added to {district.Name}");
                    }
                    return true;
                }

                // Get a random officer type
                string officerType = OfficerTypes[UnityEngine.Random.Range(0, OfficerTypes.Length)];

                // Find the prefab
                NetworkObject prefab = null;
                var spawnablePrefabs = _networkManager.SpawnablePrefabs;

                for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++)
                {
                    var obj = spawnablePrefabs.GetObject(true, i);
                    if (obj.gameObject.name == officerType)
                    {
                        prefab = obj;
                        break;
                    }
                }

                if (prefab == null)
                {
                    _core.LoggerInstance.Error($"Could not find prefab for {officerType}");
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
                spawnedOfficer.Activate();

                // Store the route name with this officer
                _officerRouteNames[spawnedOfficer] = patrolName;

                // NEW: Enhance NavMeshAgent properties before patrolling
                EnhanceOfficerNavigation(spawnedOfficer);

                // Start patrol
                spawnedOfficer.StartFootPatrol(newPatrolGroup, true);

                // Spawn on network
                _networkManager.ServerManager.Spawn(newOfficerGO);

                // Add to active officers
                _activeOfficers.Add(spawnedOfficer);

                // Add to district
                district.OfficersSpawned.Add(spawnedOfficer);

                // Update district officer count if applicable
                if (isForDistrictPopulation)
                {
                    _districtOfficerCount++;

                    // Only log during initial population or if debug is enabled
                    if (_populationState == DistrictPopulationState.InitialPopulation || _enableDistrictDebugLogs)
                    {
                        _core.LoggerInstance.Msg($"District population: {_districtOfficerCount}/{_maxDistrictOfficers}");
                    }
                }

                // Only log in development mode
                if (_enableDistrictDebugLogs)
                {
                    _core.LoggerInstance.Msg($"Officer added to {district.Name}");
                }
                return true;
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
                    _core.LoggerInstance.Msg($"Warning: Could not find valid NavMesh position for waypoint {index}, using fallback");
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
                // Find which district this officer is in
                District officerDistrict = null;
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
                        string routeName = null;
                        if (_officerRouteNames.TryGetValue(officer, out routeName))
                        {
                            // Use our reference dictionary for cleanup
                            GameObject routeGO = null;
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
                            _core.LoggerInstance.Msg($"Officer pooled from {officerDistrict.Name}");
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
                        string routeName = null;
                        if (_officerRouteNames.TryGetValue(officer, out routeName))
                        {
                            // Use our reference dictionary for cleanup
                            GameObject routeGO = null;
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
                    _core.LoggerInstance.Msg($"Officer removed from {officerDistrict.Name}");
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
                        string routeName;
                        if (_officerRouteNames.TryGetValue(officer, out routeName))
                        {
                            // Use our reference dictionary for cleanup
                            GameObject routeGO;
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
                _core.LoggerInstance.Msg($"Cleaned up {removedCount} invalid officers");

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
                    UnityEngine.Object.Destroy(pooled.GameObject);
                }
                catch { }
                removed++;
            }

            if (removed > 0)
            {
                _core.LoggerInstance.Msg($"Cleaned {removed} old officers from the pool");
            }
        }

        // Modified to show district officer tracking with less logging
        private void LogOfficerCounts()
        {
            // Don't log at all in normal operation to avoid hitching
            if (!_enableDistrictDebugLogs && _populationState == DistrictPopulationState.MaintenanceMode)
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
                _core.LoggerInstance.Msg($"Maintenance status: {totalOfficers} officers - District: {_districtOfficerCount}/{_maxDistrictOfficers}");
            }
            else
            {
                // More verbose logging during population phases
                int localOfficers = GetLocalOfficerCount();
                _core.LoggerInstance.Msg($"Population progress: {_districtOfficerCount}/{_maxDistrictOfficers} district officers - Local: {localOfficers}/{_maxLocalOfficers}");

                // Only show district details during initial population
                foreach (var district in _districts)
                {
                    // Show population percentage
                    int percentage = district.TargetOfficers > 0 ?
                        (district.OfficersSpawned.Count * 100) / district.TargetOfficers : 0;
                    _core.LoggerInstance.Msg($"  {district.Name}: {district.OfficersSpawned.Count}/{district.TargetOfficers} officers ({percentage}%)");
                }
            }
        }

        // FIXED: Updated EnsureAllDistrictsHaveTargetOfficers to handle verification and cleanup better
        private void EnsureAllDistrictsHaveTargetOfficers()
        {
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
                    _core.LoggerInstance.Msg("Some districts need repopulation. Restarting district population process.");
                }
                else
                {
                    // FIXED: Clean up excess officers in maintenance mode
                    foreach (var district in _districts)
                    {
                        int excessOfficers = Math.Max(0, district.OfficersSpawned.Count - district.TargetOfficers);
                        if (excessOfficers > 0)
                        {
                            _core.LoggerInstance.Msg($"Maintenance: {district.Name} has {excessOfficers} excess officers, removing");

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
                        _core.LoggerInstance.Msg($"==== DISTRICT POPULATION INITIAL STATUS ====");

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
                                _core.LoggerInstance.Msg($"  {district.Name} needs {officersNeeded} more officers ({populationPercentage}% populated)");
                            }
                            else if (excessOfficers > 0)
                            {
                                int populationPercentage = (district.OfficersSpawned.Count * 100) / Math.Max(1, district.TargetOfficers);
                                _core.LoggerInstance.Msg($"  {district.Name} has {excessOfficers} excess officers ({populationPercentage}% populated)");

                                // Queue excess officers for removal
                                for (int i = 0; i < excessOfficers && i < district.OfficersSpawned.Count; i++)
                                {
                                    QueueDespawn(district.OfficersSpawned[i]);
                                }
                            }
                        }

                        _core.LoggerInstance.Msg($"Total officers to spawn: {totalNeeded}");

                        if (totalNeeded > 0)
                        {
                            _core.LoggerInstance.Msg($"Beginning initial population...");
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
                        _core.LoggerInstance.Msg($"Initial population time elapsed. Verifying district status...");
                    }
                    break;

                case DistrictPopulationState.VerificationCheck:
                    // Verify that all districts are truly populated
                    if (_needsStatusUpdate)
                    {
                        _core.LoggerInstance.Msg($"==== DISTRICT POPULATION VERIFICATION ====");

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
                                _core.LoggerInstance.Msg($"  {district.Name} has {officersToRemove} excess officers, queueing for removal");
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
                                _core.LoggerInstance.Msg($"  {district.Name} still needs {officersNeeded} more officers ({populationPercentage}% populated)");

                                // Queue the needed officers
                                for (int i = 0; i < officersNeeded; i++)
                                {
                                    QueueSpawnInDistrict(district, 2.0f);
                                }
                            }
                        }

                        if (districtsNeedingOfficers > 0)
                        {
                            _core.LoggerInstance.Msg($"Found {districtsNeedingOfficers} districts still needing {officersStillNeeded} officers.");
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
                            _core.LoggerInstance.Msg($"==== ALL DISTRICTS FULLY POPULATED ====");
                            _core.LoggerInstance.Msg($"District check interval reduced to {_districtMaintenanceInterval / 60f} minutes");

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
                _hasLoggedDistrictNeeds = false;
                _populationState = DistrictPopulationState.InitialCheck;
                _needsStatusUpdate = true;
                _lastPopulationStateChangeTime = DateTime.Now;

                // Reset initialization variables
                _gameReady = false;
                _gameReadyCheckTime = 0f;
                _initFailCount = 0;
                _playerMovementDetected = false;
                _lastCheckedPosition = Vector3.zero;

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

                // Double garbage collection to ensure complete cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                _core.LoggerInstance.Msg("Officer spawn system completely reset on scene unload");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error in OnSceneUnloaded: {ex.Message}");
            }
        }
    }
}
