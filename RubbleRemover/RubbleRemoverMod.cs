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

        Log.Info("Rubble Remover 0.1.3: terrain-only removal tool and Workbench recipe declared.");
    }
}
