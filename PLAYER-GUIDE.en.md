# Transport Fever 3 Helicopters

Passenger helicopter transport for Cities: Skylines II, with a twin-pad heliport, a separate rooftop helipad and a 60-seat Mil Mi-26TC. The three models were exported from Transport Fever 3; transport and management behavior is implemented by this mod.

## Requirements

Windows. Tested base game: 1.6.2f1; declared compatibility: 1.6.2*. Maximum cruise speed: 350 km/h, with slower approach and ground movement. CityWeaver MCP, Node.js and an AI client are not required.

## Getting started

1. Enable the mod, finish downloading its content, and restart the game.
2. Build a heliport with a depot from the Passenger Helicopters transport category. At least one depot is needed to supply aircraft.
3. Build another heliport or a rooftop helipad alongside an ordinary road. Confirm road, pedestrian access, utilities and operating status.
4. Use the helicopter line tool to connect boarding stops and close the route. The airport's two pads are independent stops.
5. Select Mil Mi-26TC in line details, set vehicle count and operating hours, and allow native citizen trips to board and alight.
6. Check both stops and vehicles before saving the city.

350 km/h is a cruise cap, not the speed used during landing or taxiing. Independent pads still obey native occupancy and collision rules.

Rotors use real blades: the mod's angular trails clear immediately during takeoff, approach, landing, vertical climb/descent, ground movement and boarding. Trails fade in after about1 second of steady level flight, with no blur disks. This effect is independent of global motion blur; enabling that graphics option is unnecessary for the rotor effect. With global motion blur enabled, clear rotor blades bypass it while other objects retain the global effect. Pausing restores clear blades. If the custom rendering pass is unavailable, the original real blades remain visible without affecting transport.

## Existing local installations

Back up saves and exit the game before migration. Remove the old local helicopter entry and compatibility runtime before enabling the subscription version. Do not load duplicate PassengerHelicopters or CityWeaver.dll copies. Resource CIDs, prefab names and the legacy CityWeaver assembly identity remain stable for existing saves.

The old monolithic CityWeaver DLL cannot coexist with the independent compatibility runtime. A compatible optional MCP adapter is separate from the transport mod.

## Limits and troubleshooting

Only Windows has been tested. Keep airport platforms, surrounding terrain and road elevation aligned. Historical terrain repairs in an existing city do not certify every new placement. Check missing stops, stopped lines, depot availability, pad occupancy and normal departure spacing before reporting stuck aircraft.

For missing models, finish downloading and restart. Include the game/mod versions, reproduction steps and relevant Logs/PassengerHelicopters.log entries in reports.

## Removal

Back up the city. In a disposable copy, stop and remove helicopter lines, vehicles and facilities; save and reload before disabling the content. Direct removal from a save using custom assets can cause missing content. A full subscription removal test remains pending.
