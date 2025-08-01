using Jaunt.Behaviors;
using Jaunt.Config;
using Jaunt.Hud;
using Jaunt.Systems;
using System;
using Jaunt.Entities;
using Jaunt.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

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

            api.RegisterItemClass(ModId + ":iteminstrument", typeof(ItemJauntInstrument));
            api.RegisterEntity(ModId + ":entityflyingagent", typeof(EntityFlyingAgent));
            api.RegisterEntityBehaviorClass(ModId + ":gait", typeof(EntityBehaviorGait));
            api.RegisterEntityBehaviorClass(ModId + ":rideable", typeof(EntityBehaviorJauntRideable));
            api.RegisterEntityBehaviorClass(ModId + ":stamina", typeof(EntityBehaviorJauntStamina));

            ReloadConfig();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.OnEntityLoaded += AddFatigueHandlers;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

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

        public void ReloadConfig()
        {
            try
            {
                // Load user config
                var config = api.LoadModConfig<JauntConfig>($"{ModId}.json");

                // If no user config, create one
                if (config == null)
                {
                    Logger.Warning("Missing config! Using default.");
                    config = new JauntConfig();
                    api.StoreModConfig(config, $"{ModId}.json");
                }

                if (config.ConfigVersion == null)
                {
                    Logger.Warning("Missing config version! Using default.");
                    config.ConfigVersion = "0.0.0";
                    api.StoreModConfig(config, $"{ModId}.json");
                }

                // If config version is outdated run migration
                if (GameVersion.IsNewerVersionThan(Mod.Info.Version, config.ConfigVersion))
                {
                    Logger.Warning($"Config outdated, migrating from version {config.ConfigVersion} to version {Mod.Info.Version}");
                    config = MigrateConfig(config, config.ConfigVersion, Mod.Info.Version);
                    api.StoreModConfig(config, $"{ModId}.json");
                }

                Config = config;
            }
            catch (Exception ex)
            {
                Logger.Error($"Could not load {ModId} config!");
                Logger.Error(ex);
            }
        }

        public static JauntConfig MigrateConfig(JauntConfig config, string oldVersion, string newVersion)
        {
            // Only run this check for mod configs created before our config versioning system (1.1.7)
            if (GameVersion.IsNewerVersionThan("1.1.7", oldVersion))
            {
                // Make sure we disable debug mode for everyone since it wasnt supposed to be on
                config.GlobalDebugMode = false;
            }

            // Migrate icon placement for new icon sizes in 2.0.0
            if (GameVersion.IsNewerVersionThan("2.0.0-rc.8", oldVersion))
            {
                config.IconOffsetX = -385f;
                config.IconOffsetY = -105f;
                config.IconSize = 75f;
            }

            config.ConfigVersion = newVersion;
            return config;
        }
    }
}
