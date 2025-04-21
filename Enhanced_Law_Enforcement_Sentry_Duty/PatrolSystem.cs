using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PatrolSystem
    {
        private readonly Core _core;
        private readonly Dictionary<string, PatrolRoute> _activeRoutes = new(StringComparer.Ordinal); // Use Ordinal for best performance
        private readonly System.Random _random;

        // Pre-calculated constants
        private const float DEG_TO_RAD = Mathf.PI / 180f; // Precalculate instead of using Mathf.Deg2Rad each time

        // Cache frequently used settings
        private bool _randomizePatrolPoints;
        private float _defaultPatrolSpeed;
        private float _defaultWaitTime;
        private int _maxPatrolPoints;

        // Object pooling for routes
        private readonly Queue<PatrolRoute> _routePool = new Queue<PatrolRoute>();
        private const int MAX_POOLED_ROUTES = 20;

        // Debug toggle
        private bool _enableVerboseLogging = false;

        // Frame counter for operations that don't need to run every frame
        private int _frameCounter = 0;

        // Avoid GC pressure with reusable lists for temporary operations
        private readonly List<string> _routesToRemove = new List<string>();

        public PatrolSystem()
        {
            _core = Core.Instance;
            _random = new System.Random();

            // Cache settings on startup
            CacheSettings();
        }

        public void CacheSettings()
        {
            var settings = _core.Settings.Patrol;
            _randomizePatrolPoints = settings.RandomizePatrolPoints;
            _defaultPatrolSpeed = settings.PatrolSpeed;
            _defaultWaitTime = settings.PatrolWaitTime;
            _maxPatrolPoints = settings.MaxPatrolPoints;

            // Initialize the route pool with a few routes
            PrewarmRoutePool(5);
        }

        private void PrewarmRoutePool(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _routePool.Enqueue(new PatrolRoute
                {
                    Points = new Vector3[_maxPatrolPoints],
                    CurrentPointIndex = 0
                });
            }
        }

        private PatrolRoute GetRouteFromPool()
        {
            if (_routePool.Count > 0)
                return _routePool.Dequeue();

            return new PatrolRoute
            {
                Points = new Vector3[_maxPatrolPoints],
                CurrentPointIndex = 0
            };
        }

        private void ReturnRouteToPool(PatrolRoute route)
        {
            if (_routePool.Count < MAX_POOLED_ROUTES)
            {
                route.CurrentPointIndex = 0;
                _routePool.Enqueue(route);
            }
        }

        public void CreatePatrolRoute(string officerId, Vector3 startPoint, float radius)
        {
            try
            {
                // Check if a route already exists for this officer
                if (_activeRoutes.TryGetValue(officerId, out var existingRoute))
                {
                    // If we're regenerating an existing route, return the old one to the pool
                    ReturnRouteToPool(existingRoute);
                }

                // Get a route from the pool or create a new one
                var route = GetRouteFromPool();

                // Generate patrol points directly into the route's Points array
                GeneratePatrolPoints(startPoint, radius, _maxPatrolPoints, route.Points);

                route.CurrentPointIndex = 0;
                route.WaitTime = _defaultWaitTime;
                route.Speed = _defaultPatrolSpeed;

                // Store in active routes
                _activeRoutes[officerId] = route;

                // Only log if verbose logging is enabled
                if (_enableVerboseLogging)
                {
                    _core.LoggerInstance.Msg($"Created patrol route for officer: {officerId}");
                }
            }
            catch (Exception ex)
            {
                _core.LoggerInstance.Error($"Error creating patrol route: {ex.Message}");
            }
        }

        // Optimized to write directly to an existing array instead of creating a new one
        private void GeneratePatrolPoints(Vector3 center, float radius, int count, Vector3[] points)
        {
            // Using radial distribution for consistent coverage
            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                float randomRadius = radius * ((float)_random.NextDouble() * 0.4f + 0.6f); // 60-100% of max radius

                // Use precalculated DEG_TO_RAD constant
                float x = center.x + randomRadius * Mathf.Cos(angle * DEG_TO_RAD);
                float z = center.z + randomRadius * Mathf.Sin(angle * DEG_TO_RAD);

                points[i] = new Vector3(x, center.y, z);
            }

            if (_randomizePatrolPoints)
            {
                ShufflePoints(points);
            }
        }

        private void ShufflePoints(Vector3[] points)
        {
            int n = points.Length;
            Vector3 temp;

            // Manual swap instead of tuple to avoid implicit allocations
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                temp = points[k];
                points[k] = points[n];
                points[n] = temp;
            }
        }

        public Vector3? GetNextPatrolPoint(string officerId)
        {
            // Single dictionary lookup with out parameter
            if (_activeRoutes.TryGetValue(officerId, out var route))
            {
                return route.Points[route.CurrentPointIndex];
            }
            return null;
        }

        public void UpdatePatrolProgress(string officerId)
        {
            // Single dictionary lookup with out parameter
            if (_activeRoutes.TryGetValue(officerId, out var route))
            {
                route.CurrentPointIndex = (route.CurrentPointIndex + 1) % route.Points.Length;
            }
        }

        public void RemovePatrolRoute(string officerId)
        {
            // Single dictionary lookup with TryGetValue+Remove
            if (_activeRoutes.TryGetValue(officerId, out var route))
            {
                _activeRoutes.Remove(officerId);

                // Return the route to the pool
                ReturnRouteToPool(route);

                // Only log if verbose logging is enabled
                if (_enableVerboseLogging)
                {
                    _core.LoggerInstance.Msg($"Removed patrol route for officer: {officerId}");
                }
            }
        }

        // Batch remove patrol routes to reduce individual dictionary operations
        public void BatchRemovePatrolRoutes(IEnumerable<string> officerIds)
        {
            foreach (var id in officerIds)
            {
                if (_activeRoutes.TryGetValue(id, out var route))
                {
                    _routesToRemove.Add(id);
                    ReturnRouteToPool(route);
                }
            }

            // Remove all marked routes at once
            foreach (var id in _routesToRemove)
            {
                _activeRoutes.Remove(id);
            }

            if (_enableVerboseLogging && _routesToRemove.Count > 0)
            {
                _core.LoggerInstance.Msg($"Batch removed {_routesToRemove.Count} patrol routes");
            }

            _routesToRemove.Clear();
        }

        // Use cached values, avoiding dictionary lookups when possible
        public float GetPatrolSpeed(string officerId)
        {
            // Single dictionary lookup with out parameter, using cached default if not found
            return _activeRoutes.TryGetValue(officerId, out var route) ? route.Speed : _defaultPatrolSpeed;
        }

        public float GetWaitTime(string officerId)
        {
            // Single dictionary lookup with out parameter, using cached default if not found
            return _activeRoutes.TryGetValue(officerId, out var route) ? route.WaitTime : _defaultWaitTime;
        }

        // Periodically clean up abandoned routes
        public void Update(int frameCount)
        {
            _frameCounter = frameCount;

            // Only run cleanup every 600 frames (assuming 60fps, that's every 10 seconds)
            if (_frameCounter % 600 == 0)
            {
                CleanupAbandonedRoutes();
            }

            // Recache settings every 3000 frames (every 50 seconds at 60fps)
            if (_frameCounter % 3000 == 0)
            {
                CacheSettings();
            }
        }

        private void CleanupAbandonedRoutes()
        {
            // In a real implementation, you would track last access time for routes
            // and remove those that haven't been accessed for a long time
            int oldCount = _activeRoutes.Count;

            // Logic to identify and remove abandoned routes would go here
            // For now, this is a placeholder

            if (_enableVerboseLogging && _activeRoutes.Count < oldCount)
            {
                _core.LoggerInstance.Msg($"Cleaned up {oldCount - _activeRoutes.Count} abandoned patrol routes");
            }
        }

        // Allow toggling verbose logging
        public void SetVerboseLogging(bool enabled)
        {
            _enableVerboseLogging = enabled;
        }

        // Memory-efficient route class
        private class PatrolRoute
        {
            // Fix: Initialize Points with an empty array to address the warning
            public Vector3[] Points { get; set; } = Array.Empty<Vector3>();
            public int CurrentPointIndex { get; set; }
            public float WaitTime { get; set; }
            public float Speed { get; set; }
        }
    }
}