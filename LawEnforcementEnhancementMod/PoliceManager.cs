using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PoliceManager
    {
        private readonly Dictionary<string, PoliceOfficer> _activeOfficers = new();
        private readonly Dictionary<string, string> _officerDistricts = new();
        private readonly Core _core;
        private readonly PatrolSystem _patrolSystem;

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

        public PoliceOfficer GetOfficer(string officerId) =>
            _activeOfficers.TryGetValue(officerId, out PoliceOfficer officer) ? officer : null;

        public string GetOfficerDistrict(string officerId) =>
            _officerDistricts.TryGetValue(officerId, out string district) ? district : null;

        public List<PoliceOfficer> GetAllOfficers() => new(_activeOfficers.Values);

        public List<PoliceOfficer> GetDistrictOfficers(string districtName)
        {
            var officers = new List<PoliceOfficer>();
            foreach (var kvp in _activeOfficers)
            {
                if (_officerDistricts.TryGetValue(kvp.Key, out string district) && district == districtName)
                {
                    officers.Add(kvp.Value);
                }
            }
            return officers;
        }

        public void UpdateOfficers()
        {
            try
            {
                foreach (var kvp in _activeOfficers)
                {
                    string officerId = kvp.Key;
                    PoliceOfficer officer = kvp.Value;

                    if (officer == null || officer.gameObject == null)
                    {
                        continue;
                    }

                    UpdateOfficerPatrol(officerId, officer);
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error updating officers: {ex.Message}");
            }
        }

        private void UpdateOfficerPatrol(string officerId, PoliceOfficer officer)
        {
            Vector3? nextPoint = _patrolSystem.GetNextPatrolPoint(officerId);
            if (!nextPoint.HasValue) return;

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
                if (!_activeOfficers.TryGetValue(officerId, out PoliceOfficer officer))
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
            List<string> officersToRemove = new();

            foreach (var kvp in _activeOfficers)
            {
                if (kvp.Value == null || kvp.Value.gameObject == null)
                {
                    officersToRemove.Add(kvp.Key);
                }
            }

            foreach (string officerId in officersToRemove)
            {
                UnregisterOfficer(officerId);
            }
        }
    }
}