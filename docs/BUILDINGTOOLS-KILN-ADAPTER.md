# Building Tools for Kiln — retired

The independently maintained BuildingToolsKiln adapter was retired on 2026-11-24. The original author's standard native Building Tools 0.34.0 package now loads directly with Kiln dev.31. In-game testing confirmed backpack/storage scrolling, building and cross-base Magic Chest functionality with the camera service enabled. The adapter and the ShroudForge variant exhibited unexpected menu closures in the same environment; the native package passed. A load-order-dependent interaction is suspected, not proven at function level.

## Migration

1. Remove or disable the BuildingToolsKiln adapter and any ShroudForge Building Tools variant in Kiln.
2. Import the author's [standard native package](https://github.com/AleksanderWojtal-CC/Enshrouded-BuildingTools/releases) into Kiln Library, not the ShroudForge variant. Retain the native package's default loading phase. Do not overwrite Kiln's game-root dinput8.dll by extracting the original archive manually.
3. Activate the native package in the profile. Keep Kiln's camera service enabled. Enable Magic chests of all bases in Building Tools (Home → Options) if desired.

Historical adapter [v1.0.2](https://github.com/bisdat/kiln-mods/releases/tag/building-tools-kiln-v1.0.2) and earlier releases remain available for reference only. No further adapter builds, updates or SDK compatibility checks are maintained. Original Building Tools is by Aleksander Wojtal, MIT licensed.
