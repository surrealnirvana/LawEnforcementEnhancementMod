using UnityEngine;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PoliceEquipmentBehavior : MonoBehaviour
    {
        // Fix warning by making _flashlight nullable or using null! to suppress
        private Light? _flashlight;
        private bool _isNight;
        private float _lastCheckTime;
        private const float CHECK_INTERVAL = 5f;
        private const float NIGHT_START_HOUR = 20f; // 8 PM
        private const float NIGHT_END_HOUR = 6f;    // 6 AM

        // Frame-based optimization
        private int _frameCounter = 0;
        private const int FRAMES_BETWEEN_CHECKS = 300; // Check every ~5 seconds at 60fps

        public void Initialize()
        {
            // Create and set up the flashlight
            GameObject lightObj = new GameObject("OfficerFlashlight");
            lightObj.transform.SetParent(this.transform);
            lightObj.transform.localPosition = new Vector3(0, 1.5f, 0.5f);
            lightObj.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);

            _flashlight = lightObj.AddComponent<Light>();
            _flashlight.type = LightType.Spot;
            _flashlight.range = 10f;
            _flashlight.spotAngle = 45f;
            _flashlight.intensity = 2f;
            _flashlight.color = Color.white;
            _flashlight.enabled = false;

            // Initialize time check
            _lastCheckTime = Time.time;

            // Initialize flashlight state immediately
            UpdateFlashlightState();
        }

        private void Update()
        {
            // Skip if flashlight isn't initialized yet
            if (_flashlight == null) return;

            // Frame-based check instead of time-based for better performance
            _frameCounter++;
            if (_frameCounter >= FRAMES_BETWEEN_CHECKS)
            {
                _frameCounter = 0;
                UpdateFlashlightState();
            }
        }

        private void UpdateFlashlightState()
        {
            if (_flashlight == null) return;

            // Cache DateTime.Now to avoid multiple property calls
            var now = System.DateTime.Now;
            float currentHour = now.Hour + now.Minute / 60f;
            bool shouldBeNight = (currentHour >= NIGHT_START_HOUR || currentHour < NIGHT_END_HOUR);

            // Only update if state changed
            if (shouldBeNight != _isNight)
            {
                _isNight = shouldBeNight;
                _flashlight.enabled = _isNight;
            }
        }

        private void OnDestroy()
        {
            if (_flashlight != null)
            {
                Destroy(_flashlight.gameObject);
            }
        }
    }
}