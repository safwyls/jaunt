using Jaunt.Behaviors;
using Jaunt.Config;
using Jaunt.Hud;
using Jaunt.Systems;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

namespace Jaunt
{
    public class JauntModSystem : ModSystem
    {
        private HudElementStaminaBar _staminaHud;
        private long customHudListenerId;

        internal HudIconRenderer hudIconRenderer;

        public string ModId => Mod.Info.ModID;
        public ILogger Logger => Mod.Logger;
        public ICoreAPI api { get; private set; }
        public ICoreClientAPI capi { get; private set; }
        public JauntConfig Config { get; private set; }
        public static JauntModSystem Instance { get; private set; }

        // Debug mode for logging
        internal bool DebugMode => Config.GlobalDebugMode;

        public override void Start(ICoreAPI api)
        {
            Instance = this;
            this.api = api;

            api.RegisterEntityBehaviorClass(ModId + ":gait", typeof(EntityBehaviorGait));
            api.RegisterEntityBehaviorClass(ModId + ":rideable", typeof(EntityBehaviorJauntRideable));
            api.RegisterEntityBehaviorClass(ModId + ":stamina", typeof(EntityBehaviorJauntStamina));

            ReloadConfig(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.OnEntityLoaded += AddFatigueHandlers;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            RegisterHotkeys();

            if (Config.EnableStamina)
            {
                customHudListenerId = api.Event.RegisterGameTickListener(CheckAndInitializeCustomHud, 20);
            }

            if (Config.ShowGaitIcon)
            {
                hudIconRenderer = new HudIconRenderer(api);
                hudIconRenderer.Initialize();
            }
        }

        private void CheckAndInitializeCustomHud(float dt)
        {
            var vanillaHudStatbar = GetVanillaStatbarHud();

            if (vanillaHudStatbar != null && vanillaHudStatbar.IsOpened())
            {
                _staminaHud = new HudElementStaminaBar(capi);
                capi.Event.RegisterGameTickListener(_staminaHud.OnGameTick, 1000);
                capi.Gui.RegisterDialog(_staminaHud);

                capi.Event.UnregisterGameTickListener(customHudListenerId);
            }
        }

        private HudStatbar GetVanillaStatbarHud()
        {
            foreach (var hud in capi.Gui.OpenedGuis)
            {
                if (hud is HudStatbar statbar)
                {
                    return statbar;
                }
            }

            return null;
        }

        private void RegisterHotkeys()
        {
            capi.Input.RegisterHotKey("ascendInFlight", "Ascended While Flying", GlKeys.R, HotkeyType.GUIOrOtherControls);
            capi.Input.RegisterHotKey("descendInFlight", "Descend While Flying", GlKeys.F, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("ascendInFlight", OnAscendInFlight);
            capi.Input.SetHotKeyHandler("decreaseStepHeight", OnDescendInFlight);
        }

        private bool OnAscendInFlight(KeyCombination comb)
        {
            // Track button state
            return true;
        }

        private bool OnDescendInFlight(KeyCombination comb)
        {
            // Track button state
            return true;
        }

        private void AddFatigueHandlers(Entity entity)
        {
            var ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();
            if (ebs == null) return;
            ebs.OnFatigued += (ftg, ftgSource) => HandleFatigued(entity as EntityAgent, ftg, ftgSource);
        }

        public float HandleFatigued(EntityAgent eagent, float fatigue, FatigueSource ftgSource)
        {
            fatigue = ApplyFatigueProtection(eagent, fatigue, ftgSource);

            return fatigue;
        }

        public float ApplyFatigueProtection(EntityAgent eagent, float fatigue, FatigueSource ftgSource)
        {
            return fatigue;
        }

        public void ReloadConfig(ICoreAPI api)
        {
            try
            {
                // Load user config
                var _config = api.LoadModConfig<JauntConfig>($"{ModId}.json");

                // If no user config, create one
                if (_config == null)
                {
                    Logger.Warning("Missing config! Using default.");
                    Config = new JauntConfig();
                    api.StoreModConfig(Config, $"{ModId}.json");
                }
                else
                {
                    Config = _config;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Could not load {ModId} config!");
                Logger.Error(ex);
            }
        }
    }
}
