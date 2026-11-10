-- SPDX-License-Identifier: MIT
-- Flame Altar Level 4 Range Test (Kiln EML)
-- EXPERIMENTAL. Changes exactly one existing BalancingTable entry:
-- level 4 (array index 5) from 160 to 300 metres on all three axes.
-- It does NOT add or alter a recipe, altar level, maximum level, or cost.
-- Run only in a disposable test world with this mod ALONE.
assert(loader and loader.features and loader.features.patch,
    "flame-altar-level4-range-test: EML patch capability is required")

local resource_type = "keen::BalancingTable"
local range = 300

local resources = game.assets.get_resources_by_type(resource_type)
assert(resources and #resources == 1,
    "flame-altar-level4-range-test: expected exactly one BalancingTable")

local resource = resources[1]
local balance = resource.data
assert(balance and balance.altarLevelMax == 5,
    "flame-altar-level4-range-test: unexpected altarLevelMax (expected 5)")

local sizes = balance.buildzoneSizesPerAltarLevel
local kind = type(sizes)
assert((kind == "userdata" or kind == "table") and #sizes == 6,
    "flame-altar-level4-range-test: expected six level entries (0 through 5)")

-- Validate the full vanilla layout BEFORE modifying anything.
-- Also refuses running alongside Flame Altar Expanded 0.1.1, which
-- changes level 5 to 300m, and refuses unknown future game updates.
local vanilla = {0, 40, 80, 120, 160, 160}
for index, metres in ipairs(vanilla) do
    local v = sizes[index]
    assert(v and v.x == metres and v.y == metres and v.z == metres,
        "flame-altar-level4-range-test: unexpected vanilla build-zone dimensions at level " ..
        tostring(index - 1) .. "; refusing to patch")
end

-- Level 4 is element 5: element 1 represents level zero.
-- Do not mutate level 5 (element 6) or any recipe registry resource.
sizes[5].x, sizes[5].y, sizes[5].z = range, range, range
resource.data = balance

print("flame-altar-level4-range-test: level 4 changed 160x160x160 -> " ..
      tostring(range) .. "x" .. tostring(range) .. "x" .. tostring(range) ..
      " m; level 5 unchanged; recipes untouched")
