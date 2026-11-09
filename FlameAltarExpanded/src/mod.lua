-- SPDX-License-Identifier: MIT
-- Flame Altar Expanded: pregame Enshrouded data patch via Kiln's kiln-eml plugin.
-- Prototype: must be validated in-game on both client and server before release.
--
-- This is deliberately NOT a normal craft recipe. Flame Altar upgrades are
-- output-less recipes, which the pinned Kiln.Content new-recipe API cannot create.
-- All validations precede all writes: unknown game layouts fail closed.

assert(loader and loader.features and loader.features.patch,
    "flame-altar-expanded: requires Kiln kiln-eml with EML patch capability")

local NEW_RECIPE = "FlameAltar_HomeBase_Upgrade_4" -- upgrade from altar 4 to 5
local SOURCE_RECIPE = "FlameAltar_HomeBase_Upgrade_3" -- vanilla 3 to 4
local RECIPE_GUID = "bce60e59-c9f2-40ea-9386-6c2d119ec705" -- fixed forever
local LEVEL5_SIZE = 300
local LEVEL5_COST = 10 -- Shroud Nexus; source is vanilla level-4 cost (5)

-- EML exposes mapped game arrays/structs as userdata, not plain Lua tables.
-- Its custom table.insert handles typed arrays and clones inserted game values.
local function is_array(value)
    local kind = type(value)
    return kind == "table" or kind == "userdata"
end

local balance_resources = game.assets.get_resources_by_type("keen::BalancingTable")
assert(#balance_resources == 1,
    "flame-altar-expanded: expected exactly one BalancingTable")
local balance_resource = balance_resources[1]
local balance = balance_resource.data
assert(balance and balance.altarLevelMax == 5,
    "flame-altar-expanded: expected vanilla altarLevelMax=5; game version or another mod changed it")

-- The game's array includes index 1 for level zero, then levels 1..5.
-- Refuse to rewrite other mods' balancing values or unrecognised layouts.
local sizes = balance.buildzoneSizesPerAltarLevel
assert(is_array(sizes) and #sizes == 6,
    "flame-altar-expanded: expected six altar build-zone entries (levels 0..5)")
local expected = {40, 80, 120, 160, 160}
for level, metres in ipairs(expected) do
    local zone = sizes[level + 1]
    assert(zone and zone.x == metres and zone.y == metres and zone.z == metres,
        "flame-altar-expanded: unexpected build-zone dimensions at level " .. level)
end

local registries = game.assets.get_resources_by_type("keen::RecipeRegistryResource")
assert(#registries > 0, "flame-altar-expanded: no recipe registry")

local source, source_registry, source_data
local new_id = game.guid.hash(RECIPE_GUID)
for _, resource in ipairs(registries) do
    local data = resource.data
    assert(data and is_array(data.recipes),
        "flame-altar-expanded: malformed RecipeRegistryResource")
    for _, recipe in ipairs(data.recipes) do
        assert(recipe.debugName ~= NEW_RECIPE,
            "flame-altar-expanded: level-5 recipe already present (conflicting mod or game update)")
        assert(recipe.recipeId and recipe.recipeId.value ~= new_id,
            "flame-altar-expanded: new recipe id collides with an existing recipe")
        if recipe.debugName == SOURCE_RECIPE then
            assert(not source,
                "flame-altar-expanded: more than one vanilla level-4 upgrade recipe")
            source, source_registry, source_data = recipe, resource, data
        end
    end
end
assert(source, "flame-altar-expanded: vanilla level-4 upgrade recipe not found")
assert(source.recipeId and source.recipeId.value and
       source.input and #source.input == 1 and
       source.output and #source.output == 0,
    "flame-altar-expanded: vanilla altar upgrade no longer has one input and zero outputs")

local input = source.input[1]
assert(input.itemStack and input.itemStack.count == 5 and
       input.itemStack.item and input.itemStack.item.value ~= 0 and
       input.inputItemCategory and input.inputItemCategory.count == 0,
    "flame-altar-expanded: vanilla altar level-4 ingredient is not the expected 5 direct items")

-- EML's typed-array insertion clones the recipe value, including nested
-- typed inputs and queries. Do not run our own copy(source): source is userdata,
-- which would alias the vanilla recipe and mutate level 4 instead of adding 5.
-- All layout assertions above finish before this first registry mutation.
table.insert(source_data.recipes, source)
local recipe = source_data.recipes[#source_data.recipes]
recipe.recipeGuid = RECIPE_GUID
recipe.recipeId = {value = new_id}
recipe.debugName = NEW_RECIPE
recipe.input[1].itemStack.count = LEVEL5_COST

-- The per-recipe 'crafted once' knowledge query, if it targets its own id,
-- must not keep referring to the previous tier.
local completion = recipe.completionRequirementQuery
if completion and completion.knowledgeOrQueryId and
   completion.knowledgeOrQueryId.value == source.recipeId.value then
    completion.knowledgeOrQueryId = {value = new_id}
end

-- Complete the level-5 zone update and publish both modified resources.
sizes[6].x, sizes[6].y, sizes[6].z =
    LEVEL5_SIZE, LEVEL5_SIZE, LEVEL5_SIZE
source_registry.data = source_data
balance_resource.data = balance

print("flame-altar-expanded: registered " .. NEW_RECIPE ..
      " (10 Shroud Nexus); level-5 build zone = 300 x 300 x 300 m")
