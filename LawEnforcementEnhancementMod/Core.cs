using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using Newtonsoft.Json;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(LawEnforcementEnhancementMod.Core), "Law Enforcement Enhancement Mod", "1.0.0", "YourName")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace LawEnforcementEnhancementMod
{
    public class Core : MelonMod
    {
        private static Core _instance;
        private GameObject _networkManagerObj;
        private ModSettings _settings;
        private PoliceManager _policeManager;
        private OfficerSpawnSystem _officerSpawnSystem;
        public readonly string ModVersion = "1.0.0";

        public static Core Instance => _instance;
        public GameObject NetworkManagerObject => _networkManagerObj;
        public ModSettings Settings => _settings;
        public PoliceManager PoliceManager => _policeManager;
        public OfficerSpawnSystem OfficerSpawnSystem => _officerSpawnSystem;

        public override void OnInitializeMelon()
        {
            _instance = this;
            InitializeSettings();
            _policeManager = new PoliceManager();
            _officerSpawnSystem = new OfficerSpawnSystem();
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
                _policeManager?.UpdateOfficers();
                _officerSpawnSystem?.Update();

                if (Time.frameCount % 1800 == 0) // Every ~30 seconds at 60fps
                {
                    _policeManager?.CleanupOfficers();
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error in OnUpdate: {ex.Message}");
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