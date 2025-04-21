using System;
using System.IO;
using MelonLoader;
using Newtonsoft.Json;

namespace LawEnforcementEnhancementMod
{
    public class ModSettings
    {
        public bool EnableMod { get; set; } = true;
        public bool DebugMode { get; set; } = false;

        // Patrol Settings
        public PatrolSettings Patrol { get; set; }

        // AI Settings
        public AISettings AI { get; set; }

        public ModSettings()
        {
            // Initialize sub-settings with default values
            Patrol = new PatrolSettings();
            AI = new AISettings();
        }
    }

    public class PatrolSettings
    {
        public float PatrolSpeed { get; set; } = 5f;
        public float PatrolWaitTime { get; set; } = 30f;
        public float PatrolRadius { get; set; } = 50f;
        public int MaxPatrolPoints { get; set; } = 5;
        public bool RandomizePatrolPoints { get; set; } = true;
    }

    public class AISettings
    {
        public float DetectionRange { get; set; } = 20f;
        public float SuspicionThreshold { get; set; } = 0.7f;
        public float InvestigationTime { get; set; } = 60f;
        public bool EnableNightPatrols { get; set; } = true;
        public float NightPatrolSpeedMultiplier { get; set; } = 1.2f;
    }
}