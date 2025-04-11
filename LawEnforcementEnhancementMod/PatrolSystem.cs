using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PatrolSystem
    {
        private readonly Core _core;
        private readonly Dictionary<string, PatrolRoute> _activeRoutes = new();
        private readonly System.Random _random;

        public PatrolSystem()
        {
            _core = Core.Instance;
            _random = new System.Random();
        }

        public void CreatePatrolRoute(string officerId, Vector3 startPoint, float radius)
        {
            try
            {
                var settings = _core.Settings.Patrol;
                var points = GeneratePatrolPoints(startPoint, radius, settings.MaxPatrolPoints);

                var route = new PatrolRoute
                {
                    Points = points,
                    CurrentPointIndex = 0,
                    WaitTime = settings.PatrolWaitTime,
                    Speed = settings.PatrolSpeed
                };

                _activeRoutes[officerId] = route;
                _core.LoggerInstance.Msg($"Created patrol route for officer: {officerId}");
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error creating patrol route: {ex.Message}");
            }
        }

        private Vector3[] GeneratePatrolPoints(Vector3 center, float radius, int count)
        {
            var points = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                float randomRadius = radius * (float)(_random.NextDouble() * 0.4 + 0.6); // 60-100% of max radius

                float x = center.x + randomRadius * Mathf.Cos(angle * Mathf.Deg2Rad);
                float z = center.z + randomRadius * Mathf.Sin(angle * Mathf.Deg2Rad);

                points[i] = new Vector3(x, center.y, z);
            }

            if (_core.Settings.Patrol.RandomizePatrolPoints)
            {
                ShufflePoints(points);
            }

            return points;
        }

        private void ShufflePoints(Vector3[] points)
        {
            int n = points.Length;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                (points[k], points[n]) = (points[n], points[k]); // Using tuple for swap
            }
        }

        public Vector3? GetNextPatrolPoint(string officerId) =>
            _activeRoutes.TryGetValue(officerId, out var route) ? route.Points[route.CurrentPointIndex] : null;

        public void UpdatePatrolProgress(string officerId)
        {
            if (_activeRoutes.TryGetValue(officerId, out var route))
            {
                route.CurrentPointIndex = (route.CurrentPointIndex + 1) % route.Points.Length;
            }
        }

        public void RemovePatrolRoute(string officerId)
        {
            if (_activeRoutes.ContainsKey(officerId))
            {
                _activeRoutes.Remove(officerId);
                _core.LoggerInstance.Msg($"Removed patrol route for officer: {officerId}");
            }
        }

        public float GetPatrolSpeed(string officerId) =>
            _activeRoutes.TryGetValue(officerId, out var route) ? route.Speed : _core.Settings.Patrol.PatrolSpeed;

        public float GetWaitTime(string officerId) =>
            _activeRoutes.TryGetValue(officerId, out var route) ? route.WaitTime : _core.Settings.Patrol.PatrolWaitTime;

        private class PatrolRoute
        {
            public Vector3[] Points { get; set; }
            public int CurrentPointIndex { get; set; }
            public float WaitTime { get; set; }
            public float Speed { get; set; }
        }
    }
}