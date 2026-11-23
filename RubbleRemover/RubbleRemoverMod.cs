// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using Kiln;

namespace RubbleRemover;

/// <summary>
/// A craftable, terrain-only remover using Kiln's qualified building-tool action pipeline.
/// There is no arbitrary Build.Dig call, native patch, managed mouse hook or EML dependency.
/// </summary>
public sealed class RubbleRemoverMod : Mod
{
    // Explicitly verified in the supplied EML-R reference as terrain cells, not item IDs.
    // Nine placeable rubble types, seven world-authored rubble variants, four mineable
    // mycelium types. Deliberately excludes bone (48, unverified), glow dirt (32),
    // underwater mud (91), building voxels (128+) and any surface-only growth.
    private static readonly byte[] RemovableTerrain =
    [
        8, 64, 66, 69, 74, 81, 87, 102, 107,
        23, 36, 43, 44, 61, 62, 92,
        14, 33, 65, 72,
    ];

    // These are CLIENT-SIDE presentation gates, independent of crafting and
    // boot table registration. Do not remove/rewrite terrain on a failed gate.
    private static readonly string[] BrushCapabilities =
    [
        "tools.brush_families", "tools.identity", "tools.actions.remove",
        "tools.material_picker", "tools.build_area",
    ];

    private static void ReportCapabilities(string stage)
    {
        foreach (var name in BrushCapabilities)
            Log.Info($"Rubble Remover {stage}: {Capabilities.Why(name)}");
    }

    // The brush picker may be installed after the mod's first OnLoad. Capture
    // later transitions, including failures, without polling every frame.
    [Subscribe]
    private void OnCapabilityChanged(CapabilityChangedEvent e)
    {
        foreach (var name in BrushCapabilities)
            if (e.Name == name)
            {
                Log.Info($"Rubble Remover capability changed: {Capabilities.Why(name)}");
                break;
            }
    }

    // Run on the game client as well as the host. The actual brush selection
    // UI is client-only; a server-only /rbrushstatus can misreport its status.
    private static void ReportPublishedItems()
    {
        foreach (var name in new[]
        {
            "rubble-remover.rubble-remover",
            "rubble-remover.small",
            "rubble-remover.medium",
            "rubble-remover.large",
        })
        {
            var present = Content.Items.Get(name) is not null;
            string tag = "unavailable";
            if (Capabilities.Is("kiln.gamedata"))
            {
                try
                {
                    tag = Content.ItemData.Get(name)?.GetJson("tags[0].tag")?.ToJsonString() ?? "missing";
                }
                catch (Exception e)
                {
                    tag = "unreadable: " + e.Message;
                }
            }
            Log.Info($"Rubble Remover content ready: {name}: item={(present ? "present" : "MISSING")}, brush-tag={tag}");
        }
        ReportCapabilities("content ready");
    }

    [Command("rbrushstatus", Description = "inspect Rubble Remover brush item and native capability status")]
    private void BrushStatus(CommandContext c)
    {
        c.Reply("Rubble Remover 0.1.4 — inspect the CLIENT's kiln/state/capabilities.json for tools.brush_families");
        c.Reply("The three brush sizes are defined as separate item types: small (0.5m), medium (1.5m), large (2.5m).");
        foreach (var name in BrushCapabilities)
            c.Reply(Capabilities.Why(name));
        if (Blocks.Find("rubble-remover") is TableBlockHandle<BuildingToolBlock> h)
            c.Reply($"BuildingToolBlock: {h.Status}, identity {h.Guid}");
        if (Blocks.Report().TryGetValue(Context.Info.Id, out var report))
            foreach (var error in report.Errors)
                c.Reply("Block error: " + error);
    }

    public override void OnLoad()
    {
        var small = Blocks.Define("small", new TerrainBrushBlock
        {
            Name = "Rubble Remover - Small",
            Description = "A precise 0.5 m removal brush.",
            Radius = 0.5,
        });
        var medium = Blocks.Define("medium", new TerrainBrushBlock
        {
            Name = "Rubble Remover - Medium",
            Description = "A 1.5 m removal brush.",
            Radius = 1.5,
        });
        var large = Blocks.Define("large", new TerrainBrushBlock
        {
            Name = "Rubble Remover - Large",
            Description = "A 2.5 m removal brush. Aim clear of terrain you want to keep.",
            Radius = 2.5,
        });

        // A removal-only tool needs no placement pool. Both normal building-mode
        // controls remove only listed terrain; neither places material or consumes it.
        var tool = Blocks.Define("rubble-remover", new BuildingToolBlock
        {
            Name = "Rubble Remover",
            Description = "Removes rubble and thick, mineable mycelium inside a Flame Altar build area. Does not clear buildings, props or ordinary ground.",
            Brushes = [small, medium, large],
            Primary = new TerrainToolAction
            {
                Mode = TerrainToolMode.Remove,
                RemoveMaterials = RemovableTerrain,
                TerrainOnly = true,
            },
            Secondary = new TerrainToolAction
            {
                Mode = TerrainToolMode.Remove,
                RemoveMaterials = RemovableTerrain,
                TerrainOnly = true,
            },
        });

        var recipe = Blocks.Define("craft-rubble-remover", new RecipeBlock
        {
            Output = tool,
            Station = StationKind.Workbench.Workshop,
            // Deliberately no MenuGroup: Kiln selects a category in the Workbench's menu.
            // A tab from another workshop makes RecipeBlock throw during OnLoad.
            TrackCrafted = true,
            Needs = new Dictionary<ItemRef, int>
            {
                ["Build_TerrainMaterial_T1_Rubble"] = 20,
                ["Material_T1_FogTerrain_Mycelium"] = 20,
                ["Material_T1_Wood_Logs"] = 1,
            },
        });

        Blocks.Define("rubble-remover-found", new KnowledgeBlock
        {
            Trigger = KnowledgeTrigger.Pickup,
            Of = tool.Name,
        });
        Blocks.Define("rubble-remover-crafted", new KnowledgeBlock
        {
            Trigger = KnowledgeTrigger.Craft,
            Of = recipe.Name,
        });

        Log.Info($"Rubble Remover 0.1.4: Workbench recipe declared; brush table status={tool.Status}, root={tool.Guid}.");
        ReportCapabilities("OnLoad");
        if (Content.Available)
            Content.OnReady(_ => ReportPublishedItems());
    }
}
