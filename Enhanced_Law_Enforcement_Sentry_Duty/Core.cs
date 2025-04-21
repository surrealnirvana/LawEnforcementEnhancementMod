using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using Newtonsoft.Json;
using MelonLoader.Utils;

namespace LawEnforcementEnhancementMod
{
    public class Core : MelonMod
    {
        // Use null! to indicate to the compiler that these will be initialized before use
        private static Core _instance = null!;
        private GameObject? _networkManagerObj;
        private ModSettings _settings = null!;
        private PoliceManager? _policeManager;
        private OfficerSpawnSystem? _officerSpawnSystem;
        private SentrySpawnSystem? _sentrySpawnSystem;
        public readonly string ModVersion = "1.0.0";

        public static Core Instance => _instance;
        public GameObject? NetworkManagerObject => _networkManagerObj;
        public ModSettings Settings => _settings;
        public PoliceManager PoliceManager => _policeManager ?? throw new InvalidOperationException("PoliceManager not initialized");
        public OfficerSpawnSystem OfficerSpawnSystem => _officerSpawnSystem ?? throw new InvalidOperationException("OfficerSpawnSystem not initialized");
        public SentrySpawnSystem SentrySpawnSystem => _sentrySpawnSystem ?? throw new InvalidOperationException("SentrySpawnSystem not initialized");

        // Frame counter for optimized updates
        private int _framesSinceLastCheck = 0;

        public override void OnInitializeMelon()
        {
            _instance = this;
            InitializeSettings();
            _policeManager = new PoliceManager();
            _officerSpawnSystem = new OfficerSpawnSystem();
            _sentrySpawnSystem = new SentrySpawnSystem(); // Temporarily disabled
            LoggerInstance.Msg($"Law Enforcement Enhancement Mod v{ModVersion} Initialized.");
        }

        private void InitializeSettings()
        {
            try
            {
                string settingsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "law_enforcement_settings.json");
                _settings = new ModSettings();

                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    var loadedSettings = JsonConvert.DeserializeObject<ModSettings>(json);
                    if (loadedSettings != null)
                    {
                        _settings = loadedSettings;
                        LoggerInstance.Msg("Settings loaded successfully.");
                    }
                }
                else
                {
                    string json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                    File.WriteAllText(settingsPath, json);
                    LoggerInstance.Msg("Created default settings file.");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to initialize settings: {ex.Message}");
                _settings = new ModSettings();
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (!sceneName.Contains("Main")) return;

            try
            {
                _networkManagerObj = GameObject.Find("NetworkManager");

                if (_networkManagerObj == null)
                {
                    LoggerInstance.Error("NetworkManager not found in scene!");
                    return;
                }

                InitializeModSystems();
                LoggerInstance.Msg("Law Enforcement Enhancement Mod: Scene setup complete.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error in OnSceneWasLoaded: {ex.Message}");
            }
        }

        private void InitializeModSystems()
        {
            try
            {
                _policeManager = new PoliceManager();
                _officerSpawnSystem = new OfficerSpawnSystem();
                _sentrySpawnSystem = new SentrySpawnSystem();
                LoggerInstance.Msg("Mod systems initialized successfully.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to initialize mod systems: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            if (_networkManagerObj == null) return;

            try
            {
                // FIX: Pass Time.frameCount to UpdateOfficers method
                _policeManager?.UpdateOfficers(Time.frameCount);
                _officerSpawnSystem?.Update();
                _sentrySpawnSystem?.Update();

                // Use the frame counter for periodic tasks
                _framesSinceLastCheck++;

                // Every ~30 seconds at 60fps
                if (_framesSinceLastCheck >= 1800)
                {
                    _policeManager?.CleanupOfficers();
                    _framesSinceLastCheck = 0;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error in OnUpdate: {ex.Message}");
            }
        }

        // MelonLoader 0.5.x and later uses OnSceneWasUnloaded instead of OnSceneUnloaded
        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            try
            {
                _officerSpawnSystem?.OnSceneUnloaded();
                _sentrySpawnSystem?.OnSceneUnloaded();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error in OnSceneWasUnloaded: {ex.Message}`");
            }
        }

        public override void OnApplicationQuit()
        {
            try
            {
                _policeManager?.CleanupOfficers();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error during application quit cleanup: {ex.Message}");
            }
        }
    }
}