-- SPDX-License-Identifier: MIT
-- Offline contract tests: these use mock typed resource values, not Enshrouded.
local base = (arg[0] or ""):gsub("\\", "/"):match("^(.*)/tests/test_patch%.lua$") or "."
if base == "" then base = "." end
local script = base .. "/src/mod.lua"
local GUID = "bce60e59-c9f2-40ea-9386-6c2d119ec705"
local OLD = "FlameAltar_HomeBase_Upgrade_3"
local NEW = "FlameAltar_HomeBase_Upgrade_4"
local SOURCE_ID = 0x12A34567
local TARGET_ID = 0x43B21CAB
local passed = 0

local function zone(n) return {x=n,y=n,z=n} end
local function setup(options)
    options = options or {}
    local balance = {
        altarLevelMax=5,
        buildzoneSizesPerAltarLevel={
            zone(0),zone(40),zone(80),zone(120),zone(160),zone(160)
        }
    }
    local old = {
        recipeGuid="41414141-4141-4141-4141-414141414141",
        recipeId={value=SOURCE_ID},
        debugName=OLD,
        workshopId={value=100},
        input={{
            itemStack={itemRef="ShroudNexus",item={value=99},count=5},
            inputItemCategory={category={value=0}, count=0}
        }},
        output={},
        completionRequirementQuery={knowledgeOrQueryId={value=SOURCE_ID}}
    }
    local recipes = {old}
    if options.bad_dimension then balance.buildzoneSizesPerAltarLevel[5].x=111 end
    if options.bad_cost then old.input[1].itemStack.count=3 end
    if options.unexpected_output then old.output[1]={item={value=1},count=1} end
    if options.duplicate then
        recipes[2]={recipeId={value=999},debugName=NEW}
    end
    if options.collision then
        recipes[2]={recipeId={value=TARGET_ID},debugName="Other_Recipe"}
    end
    if options.missing_source then old.debugName="Different" end
    local br={data=balance}
    local rr={data={recipes=recipes}}
    local env=setmetatable({
        loader={features={patch=not options.disabled}},
        game={
            assets={
                get_resources_by_type=function(kind)
                    if kind=="keen::BalancingTable" then return {br} end
                    if kind=="keen::RecipeRegistryResource" then return {rr} end
                    error("Unexpected game resource type: " .. kind)
                end
            },
            guid={hash=function(g)
                assert(g==GUID, "changed recipe GUID unexpectedly")
                return TARGET_ID
            end}
        },
        print=function() end
    }, {__index=_G})
    local chunk,err=loadfile(script,"t",env)
    assert(chunk,err)
    local ok,reason=pcall(chunk)
    return {ok=ok,reason=tostring(reason),balance=balance,old=old,recipes=recipes}
end

local function test(name,fn)
    fn()
    passed=passed+1
    print("PASS " .. name)
end

test("exactly one zero-output level-5 recipe, with distinct stable identity",function()
    local result=setup()
    assert(result.ok,result.reason)
    assert(#result.recipes==2)
    local src,added=result.recipes[1],result.recipes[2]
    assert(src.debugName==OLD and src.recipeId.value==SOURCE_ID)
    assert(src.input[1].itemStack.count==5,"vanilla tier unexpectedly modified")
    assert(added.debugName==NEW and added.recipeGuid==GUID)
    assert(added.recipeId.value==TARGET_ID)
    assert(added.input[1].itemStack.count==10)
    assert(added.input[1].itemStack.itemRef=="ShroudNexus")
    assert(#added.output==0)
    assert(added.workshopId.value==100)
    assert(added.completionRequirementQuery.knowledgeOrQueryId.value==TARGET_ID)
    assert(src.completionRequirementQuery.knowledgeOrQueryId.value==SOURCE_ID)
    for i,n in ipairs({40,80,120,160,300}) do
        local z=result.balance.buildzoneSizesPerAltarLevel[i+1]
        assert(z.x==n and z.y==n and z.z==n,"changed unexpected tier "..i)
    end
end)

local function failure(name,option,needle)
    test(name,function()
        local result=setup({[option]=true})
        assert(not result.ok,"unsafe patch should have failed")
        assert(result.reason:find(needle,1,true),result.reason)
        assert(result.balance.buildzoneSizesPerAltarLevel[6].x==160,"patch modified balance before checking data")
        assert(#result.recipes<3,"patch registered a recipe before validating")
    end)
end

failure("reject unknown preexisting zone", "bad_dimension", "unexpected build-zone dimensions")
failure("reject changed vanilla upgrade cost", "bad_cost", "expected 5 direct items")
failure("reject output-bearing non-altar recipe", "unexpected_output", "one input and zero outputs")
failure("reject existing level-5 recipe", "duplicate", "already present")
failure("reject recipe id collision", "collision", "collides")
failure("reject missing upgrade recipe", "missing_source", "not found")
failure("reject no EML patch capability", "disabled", "requires Kiln kiln-eml")

print("Flame Altar Expanded: "..passed.." offline contract tests passed (not an in-game test)")
