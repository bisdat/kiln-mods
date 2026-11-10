-- SPDX-License-Identifier: MIT
-- Offline contract tests. Mock game data: NOT a gameplay validation.
local root = (arg[0] or ""):gsub("\\", "/"):match("^(.*)/tests/test_patch%.lua$") or "."
local entry = root .. "/src/mod.lua"

-- EML's typed-array values are userdata. Use a temporary file handle as a
-- mock userdata with index/length support, matching the original mod tests.
local backing = setmetatable({}, {__mode = "k"})
local mt = {
    __len = function(o) return #assert(backing[o]) end,
    __index = function(o, k) return assert(backing[o])[k] end,
}
local function typed_array(items)
    local o = assert(io.tmpfile())
    backing[o] = items
    debug.setmetatable(o, mt)
    return o
end

local function zone(n) return {x=n, y=n, z=n} end
local tests = 0
local function test(label, callback)
    callback()
    tests = tests + 1
    print("PASS " .. label)
end

local function run(opts)
    opts = opts or {}
    local values = {zone(0), zone(40), zone(80), zone(120), zone(160), zone(160)}
    if opts.bad_tier ~= nil then
        values[opts.bad_tier + 1].y = 999
    end
    local balance = {
        altarLevelMax = opts.bad_max and 4 or 5,
        buildzoneSizesPerAltarLevel = typed_array(values),
    }
    if opts.bad_count then
        values[6] = nil
    end
    local registry_reads = 0
    local writes = 0
    local resource = setmetatable({}, {
        __index = function(_, k)
            if k == "data" then return balance end
        end,
        __newindex = function(_, k, v)
            assert(k == "data" and v == balance)
            writes = writes + 1
        end
    })
    local env = setmetatable({
        loader = {features = {patch = not opts.disabled}},
        game = {assets = {get_resources_by_type = function(name)
            if name ~= "keen::BalancingTable" then
                registry_reads = registry_reads + 1
                error("Unexpected request for " .. name)
            end
            if opts.bad_resource_count then return {} end
            return {resource}
        end}},
        print = function() end,
    }, {__index = _G})
    local chunk = assert(loadfile(entry, "t", env))
    local ok, err = pcall(chunk)
    return {
        ok = ok, err = tostring(err),
        values = values, writes = writes, registry_reads = registry_reads,
    }
end

test("change level 4 only from 160 to 300 on all three axes", function()
    local result = run()
    assert(result.ok, result.err)
    assert(result.writes == 1)
    for level, n in ipairs({0, 40, 80, 120, 300, 160}) do
        local v = result.values[level]
        assert(v.x == n and v.y == n and v.z == n, "unexpected tier " .. tostring(level - 1))
    end
    assert(result.registry_reads == 0, "no recipe registry should be accessed")
end)

local function rejected(label, options, expected)
    test(label, function()
        local result = run(options)
        assert(not result.ok, "expected fail-closed")
        assert(result.err:find(expected, 1, true), result.err)
        assert(result.writes == 0, "resource written on invalid data")
        assert(result.values[5].x == 160 and result.values[5].y == 160 and result.values[5].z == 160,
            "invalid patch changed level 4")
    end)
end

rejected("reject disabled EML patch capability",
    {disabled = true}, "EML patch capability is required")
rejected("reject unexpected game altar maximum",
    {bad_max = true}, "unexpected altarLevelMax")
rejected("reject incomplete build-zone array",
    {bad_count = true}, "expected six level entries")
rejected("reject changed vanilla level 4",
    {bad_tier = 4}, "unexpected vanilla build-zone dimensions at level 4")
rejected("reject altered level 5 from other mods",
    {bad_tier = 5}, "unexpected vanilla build-zone dimensions at level 5")
rejected("reject altered lower tiers",
    {bad_tier = 1}, "unexpected vanilla build-zone dimensions at level 1")
rejected("reject missing balancing table",
    {bad_resource_count = true}, "expected exactly one BalancingTable")

print("Level-4 range experiment: " .. tests .. " offline tests passed; game test still required")
