using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.DevUtilities;
using Il2CppFishNet.Object;
using MelonLoader;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Object;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.AI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System.IO;
using Il2CppScheduleOne.GameTime;
using Newtonsoft.Json;
using MelonLoader.Utils;

namespace LawEnforcementEnhancementMod
{
    public class OfficerSpawnSystem
    {
        private readonly string[] OfficerTypes = new string[]
        {
            "OfficerBailey", "OfficerCooper", "OfficerDavis",
            "OfficerGreen", "OfficerHoward", "OfficerJackson",
            "OfficerLee", "OfficerLeo", "OfficerLopez",
            "OfficerMurphy", "OfficerOakley", "OfficerSanchez"
        };

        private readonly Core _core;
        private readonly Dictionary<Vector3, SpawnPoint> _spawnPoints = new();
        private float _lastUpdateTime;
        private float _checkInterval = 15f; // Can be modified from config
        private int _maxTotalOfficers = 30; // Changed from 50 to 30 as requested
        private float _officerRemovalRadius = 100f; // Can be modified from config
        private float _officerDespawnRadius = 200f; // Can be modified from config
        private NetworkManager _networkManager;
        private bool _initialized;
        private readonly Dictionary<PoliceOfficer, OfficerState> _officerStates = new();
        private Queue<Vector3> _pendingSpawnPositions = new Queue<Vector3>();
        private HashSet<Vector3> _usedSpawnPositions = new HashSet<Vector3>();
        private bool _initialSpawnComplete = false;
        private float _lastSingleSpawnTime = 0f;
        private float _singleSpawnDelay = 2f; // Can be modified from config
        private float _lastDespawnCheckTime = 0f;
        private float _despawnCheckInterval = 10f; // Can be modified from config
        private float _lastProximitySpawnCheckTime = 0f;
        private float _proximitySpawnCheckInterval = 20f; // Can be modified from config
        private float _playerProximityRadius = 150f; // Can be modified from config
        private int _minOfficersAroundPlayer = 20; // Changed from 3 to 20 as requested
        private bool _isNinePMMaxSpawnTriggered = false;
        private float _lastNinePMCheckTime = 0f;
        private float _ninePMCheckInterval = 30f; // Check every 30 seconds

        // Time-based officer count limits
        private int _morningOfficerLimit; // 6:00 AM - 12:00 PM
        private int _afternoonOfficerLimit; // 12:00 PM - 6:00 PM
        private int _eveningOfficerLimit; // 6:00 PM - 12:00 AM
        private int _nightOfficerLimit; // 12:00 AM - 6:00 AM

        // Config file path
        private readonly string _configFilePath;

        private class SpawnPoint
        {
            public Vector3 Position { get; set; }
            public PoliceOfficer OriginalOfficer { get; set; }
            public bool HasSpawnedPatrol { get; set; }
            public PatrolGroup PatrolGroup { get; set; }
            public List<PoliceOfficer> SpawnedOfficers { get; set; } = new List<PoliceOfficer>();
        }

        private class OfficerState
        {
            public float SpawnTime { get; set; }
            public bool IsAlerted { get; set; }
            public Vector3 LastKnownPosition { get; set; }
            public bool HasRoute { get; set; } = false;
            public float LastPlayerProximityCheck { get; set; } = 0f;
        }

        public class SpawnSystemConfig
        {
            public int MaxTotalOfficers { get; set; } = 30; // Changed from 50 to 30
            public float OfficerRemovalRadius { get; set; } = 100f;
            public float OfficerDespawnRadius { get; set; } = 200f;
            public float CheckInterval { get; set; } = 15f;
            public float DespawnCheckInterval { get; set; } = 10f;
            public float SingleSpawnDelay { get; set; } = 2f;
            public float PlayerProximityRadius { get; set; } = 150f;
            public int MinOfficersAroundPlayer { get; set; } = 20; // Changed from 3 to 20
            public float ProximitySpawnCheckInterval { get; set; } = 20f;

            // Time-based officer count limits
            public int MorningOfficerLimit { get; set; } = 10;  // 6:00 AM - 12:00 PM (33% of max)
            public int AfternoonOfficerLimit { get; set; } = 15; // 12:00 PM - 6:00 PM (50% of max)
            public int EveningOfficerLimit { get; set; } = 20;  // 6:00 PM - 12:00 AM (66% of max)
            public int NightOfficerLimit { get; set; } = 30;    // 12:00 AM - 6:00 AM (100% of max)
        }

        public OfficerSpawnSystem()
        {
            _core = Core.Instance;
            _configFilePath = Path.Combine(MelonEnvironment.UserDataDirectory, "LawEnforcementEnhancement_Config.json");
            LoadConfig();
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
                    _officerRemovalRadius = config.OfficerRemovalRadius;
                    _officerDespawnRadius = config.OfficerDespawnRadius;
                    _checkInterval = config.CheckInterval;
                    _despawnCheckInterval = config.DespawnCheckInterval;
                    _singleSpawnDelay = config.SingleSpawnDelay;
                    _playerProximityRadius = config.PlayerProximityRadius;
                    _minOfficersAroundPlayer = config.MinOfficersAroundPlayer;
                    _proximitySpawnCheckInterval = config.ProximitySpawnCheckInterval;

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
                    OfficerRemovalRadius = _officerRemovalRadius,
                    OfficerDespawnRadius = _officerDespawnRadius,
                    CheckInterval = _checkInterval,
                    DespawnCheckInterval = _despawnCheckInterval,
                    SingleSpawnDelay = _singleSpawnDelay,
                    PlayerProximityRadius = _playerProximityRadius,
                    MinOfficersAroundPlayer = _minOfficersAroundPlayer,
                    ProximitySpawnCheckInterval = _proximitySpawnCheckInterval,

                    // Time-based officer limits
                    MorningOfficerLimit = 10,   // 6:00 AM - 12:00 PM (33% of max)
                    AfternoonOfficerLimit = 15, // 12:00 PM - 6:00 PM (50% of max)
                    EveningOfficerLimit = 20,   // 6:00 PM - 12:00 AM (66% of max)
                    NightOfficerLimit = 30,     // 12:00 AM - 6:00 AM (100% of max)
                };

                // Update instance variables with default values
                _morningOfficerLimit = config.MorningOfficerLimit;
                _afternoonOfficerLimit = config.AfternoonOfficerLimit;
                _eveningOfficerLimit = config.EveningOfficerLimit;
                _nightOfficerLimit = config.NightOfficerLimit;

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                _core.LoggerInstance.Msg($"Created default configuration at {_configFilePath}");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Failed to save default config: {ex.Message}");
            }
        }

        private int GetCurrentOfficerLimit()
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

        private int GetTotalOfficerCount()
        {
            int total = 0;
            foreach (var spawnPoint in _spawnPoints.Values)
            {
                total += spawnPoint.SpawnedOfficers.Count;
            }
            return total;
        }

        public void Update()
        {
            if (!_initialized)
            {
                if (_networkManager == null)
                {
                    _networkManager = UnityEngine.Object.FindObjectOfType<NetworkManager>();
                    if (_networkManager != null)
                    {
                        _initialized = true;
                        _lastUpdateTime = Time.time;
                        _lastDespawnCheckTime = Time.time;
                        _lastProximitySpawnCheckTime = Time.time;
                        _lastNinePMCheckTime = Time.time;
                    }
                    return;
                }
                return;
            }

            float currentTime = Time.time;

            // Check for 9 PM to trigger max officer spawning
            if (currentTime - _lastNinePMCheckTime >= _ninePMCheckInterval)
            {
                _lastNinePMCheckTime = currentTime;
                CheckForNinePM();
            }

            // Run the despawn check on its own interval
            if (currentTime - _lastDespawnCheckTime >= _despawnCheckInterval)
            {
                _lastDespawnCheckTime = currentTime;
                DespawnDistantOfficers();
            }

            // Check player proximity for spawn needs
            if (currentTime - _lastProximitySpawnCheckTime >= _proximitySpawnCheckInterval)
            {
                _lastProximitySpawnCheckTime = currentTime;
                CheckPlayerProximityForSpawns();
            }

            if (currentTime - _lastUpdateTime < _checkInterval) return;

            _lastUpdateTime = currentTime;

            try
            {
                // Clean up invalid officers
                CleanupInvalidOfficers();

                // Update officer states
                foreach (var officer in _officerStates.Keys.ToList())
                {
                    if (officer == null || !officer.gameObject.activeInHierarchy)
                    {
                        _officerStates.Remove(officer);
                        continue;
                    }

                    var state = _officerStates[officer];
                    state.IsAlerted = officer.isUnsettled || officer.IsPanicked;
                    state.LastKnownPosition = officer.transform.position;

                    // Despawn officers without routes after 10 seconds
                    if (!state.HasRoute && currentTime - state.SpawnTime > 10f)
                    {
                        _core.LoggerInstance.Warning($"Despawning officer at {state.LastKnownPosition} due to missing route.");
                        DespawnOfficer(officer);
                        continue;
                    }
                }

                // Get the current time-based officer limit
                int currentOfficerLimit = GetCurrentOfficerLimit();
                var totalOfficers = GetTotalOfficerCount();
                _core.LoggerInstance.Msg($"Current total officers: {totalOfficers}/{currentOfficerLimit} (Time-based limit, max: {_maxTotalOfficers})");

                // If we're in 9 PM max mode and under the max, spawn more
                if (_isNinePMMaxSpawnTriggered && totalOfficers < _maxTotalOfficers)
                {
                    EnsureMaxOfficerCount();
                }

                // Don't spawn new officers if we've reached the current time-based limit
                if (totalOfficers >= currentOfficerLimit)
                {
                    _core.LoggerInstance.Msg("Current time-based limit reached. Not spawning new officers.");
                    return;
                }

                // Collect new spawn positions
                CollectSpawnPositions();

                // Handle spawning based on time
                if (currentTime - _lastSingleSpawnTime >= _singleSpawnDelay)
                {
                    // Don't spawn if we're at or near the cap
                    if (totalOfficers >= currentOfficerLimit - 1)
                    {
                        _core.LoggerInstance.Msg("Near current time-based officer limit. Not spawning new officers.");
                        return;
                    }

                    if (_pendingSpawnPositions.Count > 0)
                    {
                        // Handle initial population
                        Vector3 firstSpawnPos = _pendingSpawnPositions.Dequeue();
                        if (!_spawnPoints.ContainsKey(firstSpawnPos))
                        {
                            var firstSpawnPoint = new SpawnPoint
                            {
                                Position = firstSpawnPos,
                                HasSpawnedPatrol = false
                            };
                            _spawnPoints[firstSpawnPos] = firstSpawnPoint;
                            CreatePatrolAndSpawnSingleOfficer(firstSpawnPoint);
                            _usedSpawnPositions.Add(firstSpawnPos);

                            // Find nearest pending position for second officer
                            if (_pendingSpawnPositions.Count > 0 && GetTotalOfficerCount() < currentOfficerLimit)
                            {
                                var secondSpawnPos = _pendingSpawnPositions
                                    .OrderBy(pos => Vector3.Distance(pos, firstSpawnPos))
                                    .First();
                                _pendingSpawnPositions = new Queue<Vector3>(
                                    _pendingSpawnPositions.Where(pos => pos != secondSpawnPos)
                                );

                                var secondSpawnPoint = new SpawnPoint
                                {
                                    Position = secondSpawnPos,
                                    HasSpawnedPatrol = false
                                };
                                _spawnPoints[secondSpawnPos] = secondSpawnPoint;
                                CreatePatrolAndSpawnSingleOfficer(secondSpawnPoint);
                                _usedSpawnPositions.Add(secondSpawnPos);
                            }
                        }
                        _lastSingleSpawnTime = currentTime;
                    }
                    else if (_initialSpawnComplete && GetTotalOfficerCount() < currentOfficerLimit - 1)
                    {
                        // Handle replacement spawning
                        HandleReplacementSpawning();
                    }

                    if (_pendingSpawnPositions.Count == 0 && !_initialSpawnComplete)
                    {
                        _initialSpawnComplete = true;
                        _core.LoggerInstance.Msg("Initial officer population complete, switching to replacement mode");
                    }
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error in OfficerSpawnSystem update: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        // Check if it's 9 PM and trigger maximum officer spawning
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

                    // Force spawn officers to maximum
                    EnsureMaxOfficerCount();
                }
                // Reset the flag if it's before 9 PM (for the next day)
                else if (currentTime < 2100 && _isNinePMMaxSpawnTriggered)
                {
                    _isNinePMMaxSpawnTriggered = false;
                    _core.LoggerInstance.Msg("Time is before 9 PM - Resetting maximum officer spawning protocol");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error checking for 9 PM: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Ensure we have the maximum number of officers
        private void EnsureMaxOfficerCount()
        {
            try
            {
                int totalOfficers = GetTotalOfficerCount();
                int officersNeeded = _maxTotalOfficers - totalOfficers;

                if (officersNeeded <= 0)
                {
                    return; // Already at or above max
                }

                _core.LoggerInstance.Msg($"9 PM Protocol: Spawning {officersNeeded} additional officers to reach maximum of {_maxTotalOfficers}");

                // First try to use existing spawn points
                if (_usedSpawnPositions.Count > 0)
                {
                    var validSpawnPoints = _usedSpawnPositions
                        .Where(pos => _spawnPoints.ContainsKey(pos))
                        .OrderBy(pos => UnityEngine.Random.value)
                        .ToList();

                    foreach (var spawnPos in validSpawnPoints)
                    {
                        if (GetTotalOfficerCount() >= _maxTotalOfficers)
                            break;

                        var spawnPoint = _spawnPoints[spawnPos];
                        CreatePatrolAndSpawnSingleOfficer(spawnPoint);
                    }
                }

                // If we still need more officers, generate new positions around player
                if (GetTotalOfficerCount() < _maxTotalOfficers)
                {
                    var playerObj = GameObject.Find("Player_Local");
                    if (playerObj == null) return;

                    Vector3 playerPosition = playerObj.transform.position;
                    int remainingNeeded = _maxTotalOfficers - GetTotalOfficerCount();

                    for (int i = 0; i < remainingNeeded; i++)
                    {
                        // Generate positions at random distances between 50-200m from player
                        float distance = UnityEngine.Random.Range(50f, 200f);
                        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                        Vector3 offset = new Vector3(
                            Mathf.Cos(angle) * distance,
                            0f,
                            Mathf.Sin(angle) * distance
                        );

                        Vector3 spawnPos = playerPosition + offset;

                        // Validate with NavMesh
                        NavMeshHit hit;
                        if (NavMeshUtility.SamplePosition(spawnPos, out hit, 5f, NavMesh.AllAreas))
                        {
                            spawnPos = hit.position;

                            // Create a new spawn point if this position is valid
                            if (!_spawnPoints.ContainsKey(spawnPos) && !_usedSpawnPositions.Contains(spawnPos))
                            {
                                var spawnPoint = new SpawnPoint
                                {
                                    Position = spawnPos,
                                    HasSpawnedPatrol = false
                                };

                                _spawnPoints[spawnPos] = spawnPoint;
                                CreatePatrolAndSpawnSingleOfficer(spawnPoint);
                                _usedSpawnPositions.Add(spawnPos);

                                if (GetTotalOfficerCount() >= _maxTotalOfficers)
                                    break;
                            }
                        }
                    }
                }

                _core.LoggerInstance.Msg($"9 PM Protocol: Current officer count after spawning: {GetTotalOfficerCount()}/{_maxTotalOfficers}");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error ensuring max officer count: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CheckPlayerProximityForSpawns()
        {
            try
            {
                var playerObj = GameObject.Find("Player_Local");
                if (playerObj == null) return;

                Vector3 playerPosition = playerObj.transform.position;
                int currentOfficerLimit = GetCurrentOfficerLimit();

                // Count officers near player
                int officersNearPlayer = 0;
                foreach (var spawnPoint in _spawnPoints.Values)
                {
                    foreach (var officer in spawnPoint.SpawnedOfficers)
                    {
                        if (officer != null && officer.gameObject.activeInHierarchy)
                        {
                            float distance = Vector3.Distance(officer.transform.position, playerPosition);
                            if (distance <= _playerProximityRadius)
                            {
                                officersNearPlayer++;
                            }
                        }
                    }
                }

                _core.LoggerInstance.Msg($"Officers near player: {officersNearPlayer}/{_minOfficersAroundPlayer}");

                // If not enough officers near player, spawn some
                if (officersNearPlayer < _minOfficersAroundPlayer)
                {
                    int officersToSpawn = Math.Min(_minOfficersAroundPlayer - officersNearPlayer, currentOfficerLimit - GetTotalOfficerCount());
                    if (officersToSpawn <= 0) return;

                    _core.LoggerInstance.Msg($"Not enough officers near player. Spawning {officersToSpawn} more.");

                    // Generate spawn positions around the player
                    for (int i = 0; i < officersToSpawn; i++)
                    {
                        // Generate a position at least 20 meters away, but within proximity radius
                        float distanceFromPlayer = UnityEngine.Random.Range(20f, _playerProximityRadius * 0.8f);
                        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                        Vector3 offset = new Vector3(
                            Mathf.Cos(angle) * distanceFromPlayer,
                            0f,
                            Mathf.Sin(angle) * distanceFromPlayer
                        );

                        Vector3 spawnPos = playerPosition + offset;

                        // Validate position with NavMesh
                        NavMeshHit hit;
                        if (NavMeshUtility.SamplePosition(spawnPos, out hit, 5f, NavMesh.AllAreas))
                        {
                            spawnPos = hit.position;

                            // Create spawn point and officer
                            if (!_spawnPoints.ContainsKey(spawnPos) && !_usedSpawnPositions.Contains(spawnPos))
                            {
                                var spawnPoint = new SpawnPoint
                                {
                                    Position = spawnPos,
                                    HasSpawnedPatrol = false
                                };

                                _spawnPoints[spawnPos] = spawnPoint;
                                CreatePatrolAndSpawnSingleOfficer(spawnPoint);
                                _usedSpawnPositions.Add(spawnPos);

                                _core.LoggerInstance.Msg($"Spawned officer near player at {spawnPos}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error in CheckPlayerProximityForSpawns: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CollectSpawnPositions()
        {
            // Collect new spawn positions
            var activeOfficers = new List<PoliceOfficer>();
            if (PoliceOfficer.Officers != null)
            {
                foreach (var officer in PoliceOfficer.Officers)
                {
                    var policeOfficer = officer.TryCast<PoliceOfficer>();
                    if (policeOfficer != null && policeOfficer.gameObject.activeInHierarchy)
                    {
                        activeOfficers.Add(policeOfficer);
                    }
                }
            }

            // Queue new spawn positions
            foreach (var officer in activeOfficers)
            {
                if (officer == null || !officer.gameObject.activeInHierarchy) continue;

                Vector3 position = officer.transform.position;
                if (!_spawnPoints.ContainsKey(position) && !_usedSpawnPositions.Contains(position))
                {
                    _core.LoggerInstance.Msg($"Found new officer spawn point at {position}");
                    _pendingSpawnPositions.Enqueue(position);
                }
            }
        }

        private void CleanupInvalidOfficers()
        {
            // Clean up officers that are null or inactive
            foreach (var spawnPoint in _spawnPoints.Values.ToList())
            {
                for (int i = spawnPoint.SpawnedOfficers.Count - 1; i >= 0; i--)
                {
                    var officer = spawnPoint.SpawnedOfficers[i];
                    if (officer == null || !officer.gameObject.activeInHierarchy)
                    {
                        spawnPoint.SpawnedOfficers.RemoveAt(i);
                    }
                }
            }

            // Remove officers from states that are not in any spawn point's list
            var allSpawnedOfficers = new HashSet<PoliceOfficer>();
            foreach (var spawnPoint in _spawnPoints.Values)
            {
                foreach (var officer in spawnPoint.SpawnedOfficers)
                {
                    if (officer != null && officer.gameObject.activeInHierarchy)
                    {
                        allSpawnedOfficers.Add(officer);
                    }
                }
            }

            foreach (var officer in _officerStates.Keys.ToList())
            {
                if (!allSpawnedOfficers.Contains(officer))
                {
                    _officerStates.Remove(officer);
                }
            }
        }

        private void DespawnDistantOfficers()
        {
            var playerPosition = GameObject.Find("Player_Local")?.transform.position ?? Vector3.zero;
            int despawnCount = 0;
            int currentOfficerLimit = GetCurrentOfficerLimit();

            // First check if we're over the limit
            var totalOfficers = GetTotalOfficerCount();
            if (totalOfficers > currentOfficerLimit)
            {
                _core.LoggerInstance.Warning($"Officer count ({totalOfficers}) exceeds current limit ({currentOfficerLimit}). Despawning excess officers.");

                // Get all officers sorted by distance from player (farthest first)
                var allOfficers = new List<KeyValuePair<PoliceOfficer, float>>();

                foreach (var spawnPoint in _spawnPoints.Values)
                {
                    foreach (var officer in spawnPoint.SpawnedOfficers.ToList())
                    {
                        if (officer != null && officer.gameObject.activeInHierarchy)
                        {
                            float distance = Vector3.Distance(officer.transform.position, playerPosition);
                            allOfficers.Add(new KeyValuePair<PoliceOfficer, float>(officer, distance));
                        }
                    }
                }

                // Sort by distance (descending)
                allOfficers.Sort((a, b) => b.Value.CompareTo(a.Value));

                // Despawn excess officers (farthest first)
                int excessCount = totalOfficers - currentOfficerLimit;
                for (int i = 0; i < excessCount && i < allOfficers.Count; i++)
                {
                    DespawnOfficer(allOfficers[i].Key);
                    despawnCount++;
                }
            }

            // Now despawn distant officers
            foreach (var spawnPoint in _spawnPoints.Values)
            {
                foreach (var officer in spawnPoint.SpawnedOfficers.ToList())
                {
                    if (officer != null && officer.gameObject.activeInHierarchy)
                    {
                        float distance = Vector3.Distance(officer.transform.position, playerPosition);
                        if (distance > _officerDespawnRadius)
                        {
                            DespawnOfficer(officer);
                            despawnCount++;
                        }
                    }
                }
            }

            if (despawnCount > 0)
            {
                _core.LoggerInstance.Msg($"Despawned {despawnCount} officers. Current count: {GetTotalOfficerCount()}/{currentOfficerLimit}");

                // If we're in 9 PM mode, respawn to max after despawning
                if (_isNinePMMaxSpawnTriggered && GetTotalOfficerCount() < _maxTotalOfficers)
                {
                    _core.LoggerInstance.Msg("9 PM Protocol: Replenishing officers after despawn");
                    EnsureMaxOfficerCount();
                }
            }
        }

        private void DespawnOfficer(PoliceOfficer officer)
        {
            if (officer == null) return;

            // Find the spawn point containing this officer
            foreach (var spawnPoint in _spawnPoints.Values)
            {
                if (spawnPoint.SpawnedOfficers.Contains(officer))
                {
                    spawnPoint.SpawnedOfficers.Remove(officer);
                    break;
                }
            }

            // Remove from states
            _officerStates.Remove(officer);

            // Deactivate and destroy
            officer.Deactivate();
            UnityEngine.Object.Destroy(officer.gameObject);
        }

        private void HandleReplacementSpawning()
        {
            var playerPosition = GameObject.Find("Player_Local")?.transform.position ?? Vector3.zero;
            var validSpawnPoints = _usedSpawnPositions
                .Where(pos => Vector3.Distance(pos, playerPosition) > _officerRemovalRadius)
                .OrderBy(pos => UnityEngine.Random.value)
                .ToList();

            int currentOfficerLimit = GetCurrentOfficerLimit();

            // Double check we're not at the cap
            if (GetTotalOfficerCount() >= currentOfficerLimit - 1)
            {
                _core.LoggerInstance.Msg("Maximum officer count reached in replacement phase. Not spawning new officers.");
                return;
            }

            if (validSpawnPoints.Count > 1)
            {
                // Spawn first officer
                Vector3 firstSpawnPos = validSpawnPoints[0];
                if (_spawnPoints.TryGetValue(firstSpawnPos, out var firstSpawnPoint))
                {
                    CreatePatrolAndSpawnSingleOfficer(firstSpawnPoint);

                    // Check if we can spawn a second officer
                    if (GetTotalOfficerCount() < currentOfficerLimit)
                    {
                        // Find nearest valid spawn point for second officer
                        var secondSpawnPos = validSpawnPoints
                            .Skip(1)
                            .OrderBy(pos => Vector3.Distance(pos, firstSpawnPos))
                            .FirstOrDefault();

                        if (_spawnPoints.TryGetValue(secondSpawnPos, out var secondSpawnPoint))
                        {
                            CreatePatrolAndSpawnSingleOfficer(secondSpawnPoint);
                        }
                    }
                }
            }
        }

        private void CreatePatrolAndSpawnSingleOfficer(SpawnPoint spawnPoint)
        {
            try
            {
                // Check if we've hit the cap before proceeding
                int currentOfficerLimit = GetCurrentOfficerLimit();
                if (GetTotalOfficerCount() >= currentOfficerLimit)
                {
                    _core.LoggerInstance.Msg("Current time-based officer limit reached. Skipping officer spawn.");
                    return;
                }

                var waypoints = new List<Vector3>();
                float patrolRadius = 10f;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * (2 * Mathf.PI / 4);
                    Vector3 waypointPos = spawnPoint.Position + new Vector3(
                        Mathf.Cos(angle) * patrolRadius,
                        0f,
                        Mathf.Sin(angle) * patrolRadius
                    );

                    NavMeshHit hit;
                    if (NavMeshUtility.SamplePosition(waypointPos, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        waypoints.Add(hit.position);
                    }
                }

                if (waypoints.Count < 4)
                {
                    _core.LoggerInstance.Warning($"Could not create valid patrol route at {spawnPoint.Position}");
                    return;
                }

                var routeName = $"PatrolRoute_{spawnPoint.Position.ToString()}";
                var routeGO = new GameObject(routeName);
                routeGO.transform.position = waypoints[0];

                var route = routeGO.AddComponent<FootPatrolRoute>();
                route.RouteName = routeName;
                route.StartWaypointIndex = 0;

                foreach (var pos in waypoints)
                {
                    var waypointGO = new GameObject("Waypoint");
                    waypointGO.transform.position = pos;
                    waypointGO.transform.SetParent(routeGO.transform);
                }

                route.Waypoints = new Il2CppReferenceArray<Transform>(routeGO.GetComponentsInChildren<Transform>());
                route.UpdateWaypoints();

                spawnPoint.PatrolGroup = new PatrolGroup(route);
                SpawnSingleOfficer(spawnPoint);
                spawnPoint.HasSpawnedPatrol = true;
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error creating patrol: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        private void SpawnSingleOfficer(SpawnPoint spawnPoint)
        {
            try
            {
                // Final check to ensure we don't exceed the cap
                int currentOfficerLimit = GetCurrentOfficerLimit();
                if (GetTotalOfficerCount() >= currentOfficerLimit)
                {
                    _core.LoggerInstance.Msg("Current time-based officer limit reached. Aborting officer spawn.");
                    return;
                }

                var availableOfficers = new List<NetworkObject>();
                var spawnablePrefabs = _networkManager.SpawnablePrefabs;

                for (int i = 0; i < spawnablePrefabs.GetObjectCount(); i++)
                {
                    var prefab = spawnablePrefabs.GetObject(true, i);
                    if (prefab.gameObject != null && OfficerTypes.Contains(prefab.gameObject.name))
                    {
                        availableOfficers.Add(prefab);
                    }
                }

                if (availableOfficers.Count == 0)
                {
                    _core.LoggerInstance.Error("Could not find any police officer prefabs");
                    return;
                }

                var selectedPrefab = availableOfficers[UnityEngine.Random.Range(0, availableOfficers.Count)];
                var officerObject = UnityEngine.Object.Instantiate(selectedPrefab);
                officerObject.gameObject.name = $"{selectedPrefab.gameObject.name}_Patrol_{spawnPoint.Position}";

                var officer = officerObject.gameObject.GetComponent<PoliceOfficer>();
                if (officer != null)
                {
                    Vector3 offset = new Vector3(
                        UnityEngine.Random.Range(-1f, 1f),
                        0,
                        UnityEngine.Random.Range(-1f, 1f)
                    );
                    officerObject.transform.position = spawnPoint.Position + offset;

                    officerObject.gameObject.SetActive(true);
                    officer.Activate();
                    officer.StartFootPatrol(spawnPoint.PatrolGroup, true);

                    _networkManager.ServerManager.Spawn(officerObject);

                    _officerStates[officer] = new OfficerState
                    {
                        SpawnTime = Time.time,
                        IsAlerted = false,
                        LastKnownPosition = officerObject.transform.position,
                        HasRoute = true,
                        LastPlayerProximityCheck = Time.time
                    };

                    spawnPoint.SpawnedOfficers.Add(officer);
                    _core.LoggerInstance.Msg($"Spawned {officerObject.gameObject.name} at {spawnPoint.Position} " +
                                           $"(Total officers: {GetTotalOfficerCount()}/{currentOfficerLimit})");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error spawning officer: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        public void OnSceneUnloaded()
        {
            foreach (var spawnPoint in _spawnPoints.Values)
            {
                foreach (var officer in spawnPoint.SpawnedOfficers)
                {
                    if (officer?.gameObject != null)
                    {
                        officer.Deactivate();
                        UnityEngine.Object.Destroy(officer.gameObject);
                    }
                }
                spawnPoint.SpawnedOfficers.Clear();
            }
            _spawnPoints.Clear();
            _officerStates.Clear();
            _pendingSpawnPositions.Clear();
            _usedSpawnPositions.Clear();
            _initialSpawnComplete = false;
            _initialized = false;
            _networkManager = null;
            _isNinePMMaxSpawnTriggered = false;
        }
    }
}
