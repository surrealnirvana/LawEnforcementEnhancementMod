#Enhanced Law Enforcement 

## Overview
Enhanced Law Enforcement is a comprehensive modification for enhancing police officer behavior, spawning, and management in the game. This mod additional a greater police presence, creates new patrol routes, and has dynamic officer spawning mechanics based on time of day and player location.

## Features

### Dynamic Officer Spawning
- **Time-based Spawning**: Different officer population limits based on the time of day:
  - Morning (6:00 AM - 12:00 PM): 33% of max capacity
  - Afternoon (12:00 PM - 6:00 PM): 50% of max capacity
  - Evening (6:00 PM - 9:00 PM): 66% of max capacity
  - Night (9:00 PM - 4:00 AM): 100% of max capacity
  - 9:00 PM Maximum Protocol: Automatically spawns officers to reach maximum capacity

- **Proximity-based Spawning**: Ensures a minimum number of officers around the player at all times
- **Intelligent Despawning**: Officers out of player view are despawned to maintain performance

### Enhanced Police AI
- Improved patrol routes with customizable waypoints
- Realistic officer behavior with better response to player actions
- Various officer types with individualized equipment loadouts

### Configuration System
- Highly customizable settings through an easy-to-use JSON configuration file
- Adjustable parameters for spawn rates, officer counts, and patrol behavior
- Debug mode for mod development and troubleshooting

## Installation
1. Ensure you have MelonLoader installed correctly
2. Download the latest version of Law Enforcement Enhancement Mod
3. Place the mod's DLL file in your game's `Mods` folder
4. Launch the game

## Configuration
After first launch, a configuration file will be created at:

```
[MelonLoader User Data Directory]/LawEnforcementEnhancement_Config.json

```

### Main Configuration Options
- `MaxTotalOfficers`: Maximum number of officers allowed in the game (default: 30)
- `MinOfficersAroundPlayer`: Minimum officers to maintain within proximity to player (default: 20)
- `OfficerRemovalRadius`: Distance from player at which officers can be replaced (default: 100m)- scale with OfficerDespawnRadius less odd things happen
- `OfficerDespawnRadius`: Distance from player at which officers are despawned (default: 200m)
- `PlayerProximityRadius`: Radius around player to check for officers (default: 150m)

### Time-based Settings
- `MorningOfficerLimit`: Officer limit during morning hours (default: 10)
- `AfternoonOfficerLimit`: Officer limit during afternoon hours (default: 15)
- `EveningOfficerLimit`: Officer limit during evening hours (default: 20)
- `NightOfficerLimit`: Officer limit during night hours (default: 30)

## Special Features
- **9 PM Maximum Protocol**: When the in-game time reaches 9:00 PM, the mod will automatically spawn officers up to the maximum allowed limit and maintain this count until the next day.

## Compatibility
- Requires MelonLoader
- Not compatible with other police behavior mods unless specifically designed for integration

## Known Issues
- Officers may occasionally spawn in invalid locations
- Rare performance impact when large numbers of officers are in view simultaneously

## Credits
- Developed by surrealnirvana
- Special thanks to contributors and early testers


---

For bug reports, feature requests, or other inquiries, please open an issue on the project repository.
https://github.com/surrealnirvana/LawEnforcementEnhancementMod
