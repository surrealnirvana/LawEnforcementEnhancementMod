using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PoliceManager
    {
        // Use StringComparer.Ordinal for better string key performance
        private readonly Dictionary<string, PoliceOfficer> _activeOfficers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _officerDistricts = new(StringComparer.Ordinal);
        private readonly Core _core;
        private readonly PatrolSystem _patrolSystem;

        // Reusable collections to reduce allocations during updates
        private readonly List<string> _officersToRemove = new();
        private readonly List<PoliceOfficer> _tempOfficersList = new();

        // Frame-based update tracking
        private int _frameCounter = 0;
        private const int CLEANUP_INTERVAL = 300; // Every 5 seconds at 60fps

        // Performance tracking
        private float _lastUpdateTime = 0f;

        public PoliceManager()
        {
            _core = Core.Instance;
            _patrolSystem = new PatrolSystem();
        }

        public void RegisterOfficerWithDistrict(PoliceOfficer officer, string districtName)
        {
            if (officer == null)
            {
                _core.LoggerInstance.Error("Attempted to register null officer");
                return;
            }

            string officerId = officer.gameObject.name;

            try
            {
                if (!_activeOfficers.ContainsKey(officerId))
                {
                    _activeOfficers.Add(officerId, officer);
                    _officerDistricts[officerId] = districtName;
                    InitializeOfficerPatrolForDistrict(officerId, officer, districtName);
                    _core.LoggerInstance.Msg($"Officer registered in district {districtName}: {officerId}");
                }
                else
                {
                    _core.LoggerInstance.Warning($"Officer already registered: {officerId}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error registering officer: {ex.Message}");
            }
        }

        public void RegisterOfficer(PoliceOfficer officer)
        {
            if (officer == null)
            {
                _core.LoggerInstance.Error("Attempted to register null officer");
                return;
            }

            string officerId = officer.gameObject.name;

            try
            {
                if (!_activeOfficers.ContainsKey(officerId))
                {
                    _activeOfficers.Add(officerId, officer);
                    InitializeOfficerPatrol(officerId, officer);
                    _core.LoggerInstance.Msg($"Officer registered: {officerId}");
                }
                else
                {
                    _core.LoggerInstance.Warning($"Officer already registered: {officerId}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error registering officer: {ex.Message}");
            }
        }

        private void InitializeOfficerPatrol(string officerId, PoliceOfficer officer)
        {
            if (officer == null || officer.gameObject == null) return;

            try
            {
                Vector3 startPosition = officer.gameObject.transform.position;
                float patrolRadius = _core.Settings.Patrol.PatrolRadius;
                _patrolSystem.CreatePatrolRoute(officerId, startPosition, patrolRadius);
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error initializing officer patrol: {ex.Message}");
            }
        }

        private void InitializeOfficerPatrolForDistrict(string officerId, PoliceOfficer officer, string districtName)
        {
            if (officer == null || officer.gameObject == null) return;

            try
            {
                Vector3 startPosition = officer.gameObject.transform.position;
                float patrolRadius = GetPatrolRadiusForDistrict(districtName);
                _patrolSystem.CreatePatrolRoute(officerId, startPosition, patrolRadius);
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error initializing officer patrol: {ex.Message}");
            }
        }

        private float GetPatrolRadiusForDistrict(string districtName)
        {
            return districtName switch
            {
                "Downtown" => _core.Settings.Patrol.PatrolRadius * 0.75f,
                "Docks" => _core.Settings.Patrol.PatrolRadius * 1.5f,
                _ => _core.Settings.Patrol.PatrolRadius
            };
        }

        public void UnregisterOfficer(string officerId)
        {
            try
            {
                if (_activeOfficers.ContainsKey(officerId))
                {
                    _activeOfficers.Remove(officerId);
                    _officerDistricts.Remove(officerId);
                    _patrolSystem.RemovePatrolRoute(officerId);
                    _core.LoggerInstance.Msg($"Officer unregistered: {officerId}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error unregistering officer: {ex.Message}");
            }
        }

        public PoliceOfficer? GetOfficer(string officerId) =>
            _activeOfficers.TryGetValue(officerId, out PoliceOfficer? officer) ? officer : null;

        public string? GetOfficerDistrict(string officerId) =>
            _officerDistricts.TryGetValue(officerId, out string? district) ? district : null;

        public List<PoliceOfficer> GetAllOfficers()
        {
            // Reuse the temporary list to avoid allocations
            _tempOfficersList.Clear();
            _tempOfficersList.AddRange(_activeOfficers.Values);
            return _tempOfficersList;
        }

        public List<PoliceOfficer> GetDistrictOfficers(string districtName)
        {
            // Reuse the temporary list to avoid allocations
            _tempOfficersList.Clear();

            foreach (var kvp in _activeOfficers)
            {
                // FIX: Add explicit cast to string? to fix CS8600 warning
                if (_officerDistricts.TryGetValue(kvp.Key, out var district) &&
                    district is string validDistrict && validDistrict == districtName)
                {
                    _tempOfficersList.Add(kvp.Value);
                }
            }
            return _tempOfficersList;
        }

        public void UpdateOfficers(int frameCount)
        {
            _frameCounter = frameCount;

            try
            {
                // Performance tracking
                float startTime = Time.realtimeSinceStartup;

                // Process officers in batches to reduce frame impact
                int processedCount = 0;
                int maxToProcessPerFrame = 20; // Limit how many officers are processed per frame

                foreach (var kvp in _activeOfficers)
                {
                    string officerId = kvp.Key;
                    PoliceOfficer? officer = kvp.Value;

                    if (officer == null || officer.gameObject == null)
                    {
                        continue;
                    }

                    UpdateOfficerPatrol(officerId, officer);

                    // Count processed officers
                    processedCount++;
                    if (processedCount >= maxToProcessPerFrame)
                        break;
                }

                // Run cleanup on a set interval
                if (_frameCounter % CLEANUP_INTERVAL == 0)
                {
                    CleanupOfficers();
                }

                // Log performance metrics occasionally
                float updateTime = Time.realtimeSinceStartup - startTime;
                if (updateTime > 0.01f) // Only log if took more than 10ms
                {
                    _core.LoggerInstance.Msg($"Officer update took {updateTime * 1000:F2}ms for {processedCount} officers");
                }

                _lastUpdateTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error updating officers: {ex.Message}");
            }
        }

        private void UpdateOfficerPatrol(string officerId, PoliceOfficer officer)
        {
            // Early null check
            if (officer == null || officer.gameObject == null) return;

            Vector3? nextPoint = _patrolSystem.GetNextPatrolPoint(officerId);
            if (!nextPoint.HasValue) return;

            // FIX: Add null check for transform
            if (officer.gameObject.transform == null) return;

            Vector3 currentPosition = officer.gameObject.transform.position;
            float distanceToTarget = Vector3.Distance(currentPosition, nextPoint.Value);

            if (distanceToTarget < 1f)
            {
                _patrolSystem.UpdatePatrolProgress(officerId);
            }
            else
            {
                float speed = _patrolSystem.GetPatrolSpeed(officerId);
                Vector3 direction = (nextPoint.Value - currentPosition).normalized;
                officer.gameObject.transform.position += direction * speed * Time.deltaTime;
                officer.gameObject.transform.forward = direction;
            }
        }

        public void AssignPatrol(string officerId, Vector3[] patrolPoints)
        {
            try
            {
                if (!_activeOfficers.TryGetValue(officerId, out PoliceOfficer? officer) || officer == null)
                {
                    _core.LoggerInstance.Warning($"Officer not found for patrol assignment: {officerId}");
                    return;
                }

                if (patrolPoints == null || patrolPoints.Length == 0)
                {
                    _core.LoggerInstance.Error("Invalid patrol points provided");
                    return;
                }

                _patrolSystem.CreatePatrolRoute(officerId, officer.gameObject.transform.position, _core.Settings.Patrol.PatrolRadius);
                _core.LoggerInstance.Msg($"Patrol assigned to officer: {officerId}");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error assigning patrol: {ex.Message}");
            }
        }

        public void CleanupOfficers()
        {
            // Clear and reuse the list to avoid allocations
            _officersToRemove.Clear();

            // Find all invalid officers
            foreach (var kvp in _activeOfficers)
            {
                if (kvp.Value == null || kvp.Value.gameObject == null)
                {
                    _officersToRemove.Add(kvp.Key);
                }
            }

            // Batch unregister them
            if (_officersToRemove.Count > 0)
            {
                _core.LoggerInstance.Msg($"Cleaning up {_officersToRemove.Count} invalid officers");

                foreach (string officerId in _officersToRemove)
                {
                    UnregisterOfficer(officerId);
                }
            }
        }

        // Efficient bulk patrol point update
        public void UpdatePatrolPoints(Dictionary<string, Vector3[]> newPatrolPoints)
        {
            int updatedCount = 0;

            foreach (var kvp in newPatrolPoints)
            {
                if (_activeOfficers.TryGetValue(kvp.Key, out var officer) && officer != null &&
                    kvp.Value != null && kvp.Value.Length > 0)
                {
                    _patrolSystem.CreatePatrolRoute(kvp.Key, officer.gameObject.transform.position,
                        _core.Settings.Patrol.PatrolRadius);
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                _core.LoggerInstance.Msg($"Batch updated patrol routes for {updatedCount} officers");
            }
        }
    }
}