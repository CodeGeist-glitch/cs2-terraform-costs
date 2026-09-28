using System;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using TerraformCost.Systems;

namespace TerraformCost
{
    /// <summary>
    /// Entry point. NOTE: the game creates IMod instances with FormatterServices.GetUninitializedObject,
    /// so instance field initializers / constructors do NOT run. Keep state static or create it in OnLoad.
    ///
    /// Full mod per plans\02_terraform_cost_and_disasters_plan.md section 3: every terrain-tool brush
    /// stroke (through the vanilla ApplyBrushesSystem -> TerrainSystem.ApplyBrush pipeline only, never a
    /// direct ApplyBrush call from e.g. DisasterDirector) costs money, in GameMode.Game only.
    /// </summary>
    public class Mod : IMod
    {
        public const string Version = "1.0.0";
        public const string HarmonyId = "com.erdgeist.terraformcost";

        public static ILog log = LogManager.GetLogger("TerraformCost").SetShowsErrorsInUI(false);
        public static Settings settings;

        private static Harmony s_Harmony;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("TerraformCost " + Version + " loading");
            try
            {
                s_Harmony = new Harmony(HarmonyId);
                s_Harmony.PatchAll();
                log.Info("TerraformCost Harmony patches applied (ApplyBrushesGatePatch, ApplyBrushPatch)");
            }
            catch (Exception e)
            {
                log.Error(e, "TerraformCost failed to apply Harmony patches; mod disabled (terrain edits stay free)");
            }

            try
            {
                settings = new Settings(this);
                settings.RegisterInOptionsUI();
                Settings.instance = settings;
                GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(settings));
                GameManager.instance.localizationManager.AddSource("de-DE", new LocaleDE(settings));
                Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(nameof(TerraformCost), settings, new Settings(this));
                log.Info("TerraformCost settings loaded (enabled=" + settings.Enabled + ", multiplier=" + settings.Multiplier + "x)");
            }
            catch (Exception e)
            {
                log.Error(e, "TerraformCost failed to load settings; using defaults");
            }

            if (Compat.HardModeContinuedLoaded)
            {
                log.Warn("TerraformCost: Hard Mode Continued is also loaded and also charges for terraforming - you may be charged by both mods. TerraformCost stays active; disable one of them in Options if you don't want double charges.");
            }

            try
            {
                // T-1 fix (0.9.1): TerraformChargeSystem used to run at SystemUpdatePhase.GameSimulation,
                // which Game.Simulation.SimulationSystem.OnUpdate skips ENTIRELY while paused (selectedSpeed
                // == 0 => num == 0, so the whole GameSimulation loop body - and everything registered in
                // it - never runs, decompiled/Game/Game.Simulation/SimulationSystem.cs:221-296). Since the
                // terrain tool itself applies via SystemUpdatePhase.ApplyTool (Game.Tools.ToolOutputSystem),
                // which - like all Tool* phases (Game.Tools.ToolSystem.OnUpdate) - runs every real frame
                // regardless of simulation pause, the old registration meant Budget.AvailableMoney was never
                // refreshed and pending charges were never drained into real PlayerMoney while paused - the
                // stale/frozen balance is what produced spurious "Not enough money" (and, symmetrically,
                // silently under-charged strokes) while paused. PreTool runs earlier the same real frame
                // than ToolUpdate/ApplyTool (SystemUpdatePhase order), so money published/drained here is
                // fresh by the time ApplyBrushPatch's StrokeVolume charge/block check runs, paused or not.
                updateSystem.UpdateAt<TerraformChargeSystem>(SystemUpdatePhase.PreTool);
                updateSystem.UpdateAt<TerraformCostTooltipSystem>(SystemUpdatePhase.UITooltip);
                updateSystem.UpdateAt<TestStrokeSystem>(SystemUpdatePhase.ToolUpdate);
                log.Info("TerraformCost systems registered");
            }
            catch (Exception e)
            {
                log.Error(e, "TerraformCost failed to register systems; mod disabled");
            }
        }

        public void OnDispose()
        {
            log.Info("TerraformCost OnDispose");
            try
            {
                settings?.UnregisterInOptionsUI();
            }
            catch (Exception e)
            {
                log.Warn("TerraformCost UnregisterInOptionsUI failed: " + e.Message);
            }
            try
            {
                s_Harmony?.UnpatchAll(HarmonyId);
            }
            catch (Exception e)
            {
                log.Warn("TerraformCost UnpatchAll failed: " + e.Message);
            }
            s_Harmony = null;
        }
    }
}
