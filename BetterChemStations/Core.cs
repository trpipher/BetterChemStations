using MelonLoader;
using FishNet.Object;
using ScheduleOne.ItemFramework;
using ScheduleOne.Management;
using ScheduleOne.ObjectScripts;
using ScheduleOne.StationFramework;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using System;
using UnityEngine;
using System.Collections;

[assembly: MelonInfo(typeof(BetterChemStations.Core), "BetterChemStations", "1.1.0", "trpipher", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterChemStations
{
	public class Core : MelonMod
    {
        // Category for our preferences
        private static readonly string CATEGORY = "BetterChemStations";
        
        // Debug mode preference entry
        private static MelonPreferences_Entry<bool> _debugModeEntry;
        
        // Public property to access debug mode setting
        public static bool DebugMode => _debugModeEntry?.Value ?? false;

        public override void OnInitializeMelon()
        {
            // Register preferences
            var category = MelonPreferences.CreateCategory(CATEGORY);
            _debugModeEntry = category.CreateEntry("DebugMode", false, "Debug Logging", 
                "When enabled, outputs detailed logs to help with troubleshooting.");
            
            // Apply Harmony patches
            HarmonyLib.Harmony.CreateAndPatchAll(typeof(RecipeChangedPatch));
            HarmonyLib.Harmony.CreateAndPatchAll(typeof(ChemistryStationAwakePatch));
            
            LoggerInstance.Msg("Chemistry Station Filter Mod loaded!");
            DebugLog("Debug mode is enabled");
        }

        // Helper method for debug logging
        public static void DebugLog(string message)
        {
            if (DebugMode)
            {
                MelonLogger.Msg("[DEBUG] " + message);
            }
        }
    }

    // Helper class to apply filters
    public static class FilterHelper
    {
        public static void ApplyRecipeFilters(ChemistryStation station, StationRecipe recipe)
        {
            if (station == null || recipe?.Ingredients == null)
            {
                MelonLogger.Warning("Cannot apply filters: station or recipe is null");
                return;
            }

            Core.DebugLog($"Applying filters for recipe: {recipe.RecipeTitle} with {recipe.Ingredients.Count} ingredients");

            try
            {
                // Clear existing filters first
                for (int i = 0; i < station.IngredientSlots.Length; i++)
                {
                    var slot = station.IngredientSlots[i];
                    
                    // First, remove any existing filters
                    // We use reflection to access the protected Filters collection if needed
                    var filters = slot.GetType().GetField("Filters", 
                        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(slot) as List<ItemFilter>;
                    
                    if (filters != null)
                    {
                        filters.Clear();
                    }
                }
                // Apply new filters based on recipe ingredients
                for (int i = 0; i < recipe.Ingredients.Count && i < station.IngredientSlots.Length; i++)
                {
                    var ingredient = recipe.Ingredients[i];
                    var slot = station.IngredientSlots[i];
                    var filterItems = new List<string>();
                    foreach(var item in ingredient.Items)
                    {
                        filterItems.Add(item.ID);
                    }
                    // Create a filter that only allows this specific ingredient
                    slot.AddFilter(new ItemFilter_ID(filterItems)
                    {
                        IsWhitelist = true
                    });
                    
                    Core.DebugLog($"Added filter for ingredient {ingredient.Item.Name} to slot {i}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying filters: {ex.Message}");
            }
        }
        
        // Helper method to get a station configuration using reflection
        public static ChemistryStationConfiguration GetStationConfiguration(ChemistryStation station)
        {
            try
            {
                // Try to access it as a field
                var configField = AccessTools.Property(typeof(ChemistryStation), "stationConfiguration");
                if (configField != null)
                {
                    return configField.GetValue(station) as ChemistryStationConfiguration;
                }
                
                MelonLogger.Warning("Could not find stationConfiguration field");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error accessing station configuration: {ex.Message}");
                return null;
            }
        }
        
        // Helper method to get recipe from a station configuration
        public static StationRecipe GetRecipeFromConfig(ChemistryStationConfiguration config)
        {
            if (config == null) return null;
            
            try
            {
                // Since Recipe is public, we can access it directly
                var recipe = config.Recipe?.SelectedRecipe;
                return recipe;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error getting recipe: {ex.Message}");
                return null;
            }
        }
        
        // Helper method to check if object is a ghost/preview
        public static bool IsGhost(ChemistryStation station)
        {
            try
            {
                // Check if this is a ghost using reflection
                var isGhostField = AccessTools.Field(typeof(ChemistryStation), "isGhost");
                if (isGhostField != null)
                {
                    return (bool)isGhostField.GetValue(station);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error checking isGhost: {ex.Message}");
            }
            return false;
        }
        
        // Helper method to create and start a delayed coroutine
        public static void StartDelayedFilterApplication(ChemistryStation station, float delay)
        {
            MelonCoroutines.Start(DelayedFilterApplication(station, delay));
        }
        
        // Coroutine to apply filters after a delay
        private static IEnumerator DelayedFilterApplication(ChemistryStation station, float delay)
        {
            Core.DebugLog($"Starting delayed filter application for {station.name} with delay {delay}s");
            
            // Wait for the specified delay
            yield return new WaitForSeconds(delay);
            
            Core.DebugLog($"Applying delayed filters for {station.name} after {delay}s");
            
            try
            {
                // Get the configuration
                var config = GetStationConfiguration(station);
                if (config == null)
                {
                    Core.DebugLog("Config is still null after delay - recipe may not be set");
                    yield break;
                }
                
                // Check if recipe is now available
                var recipe = config.Recipe?.SelectedRecipe;
                if (recipe != null && !IsGhost(station))
                {
                    Core.DebugLog($"Found recipe after delay: {recipe.RecipeTitle}");
                    ApplyRecipeFilters(station, recipe);
                }
                else
                {
                    Core.DebugLog($"No recipe found after delay: recipe is {(recipe == null ? "null" : "not null")}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in delayed filter application: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ChemistryStationConfiguration), MethodType.Constructor, 
        new[] { typeof(ConfigurationReplicator), typeof(IConfigurable), typeof(ChemistryStation) })]
    public class RecipeChangedPatch
    {
        private static void Postfix(ChemistryStationConfiguration __instance)
        {
            try
            {
                // Add our listener to the recipe changed event
                __instance.Recipe.onRecipeChanged.AddListener(recipe => 
                {
                    Core.DebugLog($"Recipe changed to: {recipe?.RecipeTitle ?? "null"}");
                    FilterHelper.ApplyRecipeFilters(__instance.Station, recipe);
                });
                
                // Also apply filters right away if there's an existing recipe
                var currentRecipe = __instance.Recipe?.SelectedRecipe;
                Core.DebugLog($"Current recipe: {currentRecipe?.RecipeTitle ?? "null"}");
                if (currentRecipe != null && !FilterHelper.IsGhost(__instance.Station))
                {
                    Core.DebugLog($"Applying filters for initial recipe: {currentRecipe.RecipeTitle}");
                    FilterHelper.ApplyRecipeFilters(__instance.Station, currentRecipe);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in RecipeChangedPatch: {ex.Message}");
            }
        }
    }
    
    [HarmonyPatch(typeof(ChemistryStation), "Awake")]
    public class ChemistryStationAwakePatch
    {
        private static void Postfix(ChemistryStation __instance)
        {
            try
            {
                // Check if this is a ghost/preview
                if (FilterHelper.IsGhost(__instance))
                {
                    Core.DebugLog($"Skipping Awake hook for ghost ChemistryStation {__instance.name}");
                    return;
                }
                
                Core.DebugLog($"Awake called for ChemistryStation {__instance.name}");
                
                // Get the station configuration and recipe
                var config = FilterHelper.GetStationConfiguration(__instance);
                
                if (config != null)
                {
                    Core.DebugLog($"Found config in Awake, checking for recipe");
                    var recipe = config.Recipe?.SelectedRecipe;
                    
                    if (recipe != null)
                    {
                        Core.DebugLog($"Found recipe in Awake: {recipe.RecipeTitle}");
                        FilterHelper.ApplyRecipeFilters(__instance, recipe);
                    }
                    else
                    {
                        Core.DebugLog("No recipe found in Awake, setting up delayed attempt");
                        // Recipe might be loaded later via network, try again after a delay
                        FilterHelper.StartDelayedFilterApplication(__instance, 2.0f);
                    }
                }
                else
                {
                    Core.DebugLog("Station configuration is null in Awake");
                    // Configuration might be loaded later, try again after a delay
                    FilterHelper.StartDelayedFilterApplication(__instance, 3.0f);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in ChemistryStationAwakePatch: {ex.Message}");
            }
        }
    }
}