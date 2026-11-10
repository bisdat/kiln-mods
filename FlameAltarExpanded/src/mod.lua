-- SPDX-License-Identifier: MIT
-- Flame Altar Expanded 0.1.2: pregame data-only range experiment for Kiln EML.
-- Existing altar level 4 gets a 300m cube instead of 160m.
-- Level 5 is NOT added; recipe registry and upgrade costs are NOT touched.
-- Game/save persistence requires a separate controlled in-game test.

assert(loader and loader.features and loader.features.patch,
    "flame-altar-expanded: requires Kiln kiln-eml with EML patch capability")

local LEVEL4_SIZE = 300

local resources = game.assets.get_resources_by_type("keen::BalancingTable")
assert(resources and #resources == 1,
    "flame-altar-expanded: expected exactly one BalancingTable")

local resource = resources[1]
local balance = resource.data
assert(balance and balance.altarLevelMax == 5,
    "flame-altar-expanded: expected vanilla altarLevelMax=5")

local sizes = balance.buildzoneSizesPerAltarLevel
local kind = type(sizes)
assert((kind == "userdata" or kind == "table") and #sizes == 6,
    "flame-altar-expanded: expected six build-zone entries (levels 0..5)")

-- Assert every known vanilla tier BEFORE the first write.
-- The unused level-zero value is deliberately not guessed or modified.
-- If any other mod changes a tier (including former 0.1.1's level 5),
-- fail closed instead of overwriting or stacking patches.
local vanilla = {40, 80, 120, 160, 160} -- levels 1..5
for level, metres in ipairs(vanilla) do
    local zone = sizes[level + 1]
    assert(zone and zone.x == metres and zone.y == metres and zone.z == metres,
        "flame-altar-expanded: unexpected build-zone dimensions at level " ..
        tostring(level) .. "; refusing to patch")
end

-- EML's arrays are Lua 1-based: level 4 lives at index 5.
-- Do not touch level 5, the recipe registry or player progression.
sizes[5].x, sizes[5].y, sizes[5].z =
    LEVEL4_SIZE, LEVEL4_SIZE, LEVEL4_SIZE
resource.data = balance

print("flame-altar-expanded 0.1.2: level-4 build-zone 160x160x160 -> " ..
      tostring(LEVEL4_SIZE) .. "x" .. tostring(LEVEL4_SIZE) .. "x" ..
      tostring(LEVEL4_SIZE) .. " m; recipes and level 5 unchanged")
