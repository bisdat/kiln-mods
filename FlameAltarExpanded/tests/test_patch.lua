-- SPDX-License-Identifier: MIT
-- Offline Kiln EML Lua 5.4 tests: mock game resources, not a gameplay test.
local base = (arg[0] or ""):gsub("\\", "/"):match("^(.*)/tests/test_patch%.lua$") or "."
local script = base .. "/src/mod.lua"
local passed = 0

-- The real EML build-zone array is userdata, not a native Lua table.
local arrays = setmetatable({}, {__mode = "k"})
local mt = {
    __len = function(o) return #assert(arrays[o]) end,
    __index = function(o, k) return assert(arrays[o])[k] end,
    __newindex = function(o, k, v) assert(arrays[o])[k] = v end,
}
local function mapped_array(items)
    local o = assert(io.tmpfile())
    arrays[o] = items
    debug.setmetatable(o, mt)
    return o
end

local function zone(n) return {x=n, y=n, z=n} end
local function xyz(z) return {x=z.x, y=z.y, z=z.z} end
local function same(a,b) return a.x == b.x and a.y == b.y and a.z == b.z end

local function setup(opts)
    opts = opts or {}
    -- Keep level zero nonzero deliberately: the mod must preserve it and
    -- must not rely on guesses about its game default.
    local values = {zone(7), zone(40), zone(80), zone(120), zone(160), zone(160)}
    if opts.bad_tier ~= nil then values[opts.bad_tier + 1].y = 777 end
    if opts.short_array then values[6] = nil end
    local before = {}
    for i,v in ipairs(values) do before[i] = xyz(v) end

    local balance = {
        altarLevelMax = opts.bad_max and 4 or 5,
        buildzoneSizesPerAltarLevel = mapped_array(values),
    }
    local writes, reads, prints = 0, {}, {}
    local resource = setmetatable({}, {
        __index = function(_, k)
            if k == "data" then return balance end
        end,
        __newindex = function(_, k, v)
            assert(k == "data" and v == balance)
            writes = writes + 1
        end,
    })
    local env = setmetatable({
        loader = {features = {patch = not opts.no_capability}},
        game = {assets = {get_resources_by_type = function(name)
            reads[#reads + 1] = name
            assert(name == "keen::BalancingTable", "recipe registry must never be accessed")
            if opts.missing_resource then return {} end
            if opts.duplicate_resource then return {resource, resource} end
            return {resource}
        end}},
        print = function(v) prints[#prints+1] = v end,
    }, {__index = _G})
    local chunk = assert(loadfile(script, "t", env))
    local ok, err = pcall(chunk)
    return {
        ok=ok, err=tostring(err), values=values, before=before,
        writes=writes, reads=reads, prints=prints,
    }
end

local function test(name, fn)
    fn()
    passed = passed+1
    print("PASS " .. name)
end

test("level 4 only: 160m -> 300m on all axes, exactly one resource written", function()
    local result = setup()
    assert(result.ok, result.err)
    assert(result.writes == 1, "expected exactly one resource update")
    assert(#result.reads == 1 and result.reads[1] == "keen::BalancingTable")
    assert(#result.prints == 1 and result.prints[1]:find("level-4 build-zone", 1, true))
    for level, expected in ipairs({7, 40, 80, 120, 300, 160}) do
        local v = result.values[level]
        assert(v.x == expected and v.y == expected and v.z == expected,
            "unexpected dimensions at level " .. tostring(level - 1))
    end
end)

local function rejected(name, opts, message)
    test(name, function()
        local result = setup(opts)
        assert(not result.ok, "unsafe patch should have failed")
        assert(result.err:find(message, 1, true), result.err)
        assert(result.writes == 0, "a failed patch published data")
        assert(#result.prints == 0, "failed patch reported success")
        for i,before in ipairs(result.before) do
            assert(same(result.values[i], before),
                "failed patch changed level " .. tostring(i-1))
        end
    end)
end

rejected("refuse missing EML patch capability",
    {no_capability=true}, "EML patch capability")
rejected("refuse missing BalancingTable",
    {missing_resource=true}, "exactly one BalancingTable")
rejected("refuse duplicate BalancingTable",
    {duplicate_resource=true}, "exactly one BalancingTable")
rejected("refuse incompatible altar level max",
    {bad_max=true}, "expected vanilla altarLevelMax=5")
rejected("refuse an incomplete size array",
    {short_array=true}, "expected six build-zone entries")
rejected("refuse altered level 1",
    {bad_tier=1}, "unexpected build-zone dimensions at level 1")
rejected("refuse altered level 4",
    {bad_tier=4}, "unexpected build-zone dimensions at level 4")
rejected("refuse changed level 5 (e.g. old 0.1.1 still active)",
    {bad_tier=5}, "unexpected build-zone dimensions at level 5")

print("Flame Altar Expanded 0.1.2: " .. passed .. " offline tests passed; in-game test pending")
