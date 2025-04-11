using UnityEngine;
using Il2CppScheduleOne.Police;

namespace LawEnforcementEnhancementMod
{
    public class PoliceEquipmentBehavior : MonoBehaviour
    {
        private Light _flashlight;
        private bool _isNight;
        private float _lastCheckTime;
        private const float CHECK_INTERVAL = 5f;
        private const float NIGHT_START_HOUR = 20f; // 8 PM
        private const float NIGHT_END_HOUR = 6f;    // 6 AM

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
        }

        private void Update()
        {
            if (Time.time - _lastCheckTime < CHECK_INTERVAL) return;
            _lastCheckTime = Time.time;

            UpdateFlashlightState();
        }

        private void UpdateFlashlightState()
        {
            if (_flashlight == null) return;

            float currentHour = System.DateTime.Now.Hour + System.DateTime.Now.Minute / 60f;
            bool shouldBeNight = (currentHour >= NIGHT_START_HOUR || currentHour < NIGHT_END_HOUR);

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