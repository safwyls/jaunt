using Jaunt.Behaviors;
using Jaunt.Config;
using Jaunt.Hud;
using Jaunt.Systems;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

namespace Jaunt
{
    public class JauntModSystem : ModSystem
    {
        private HudElementStaminaBar _staminaHud;
        private long customHudListenerId;
        private HudIconRenderer hudIconRenderer;

        public string ModId => Mod.Info.ModID;
        public ILogger Logger => Mod.Logger;
        public ICoreAPI Api { get; private set; }
        public ICoreClientAPI ClientApi { get; private set; }
        public static JauntModSystem Instance { get; private set; }

        // Called on server and client
        public override void Start(ICoreAPI api)
        {
            Instance = this;
            Api = api;

            api.RegisterEntityBehaviorClass(ModId + ":rideable", typeof(EntityBehaviorJauntRideable));
            api.RegisterEntityBehaviorClass(ModId + ":stamina", typeof(EntityBehaviorJauntStamina));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.OnEntityLoaded += AddFatigueHandlers;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;

            if (JauntConfig.Config.EnableStamina)
            {
                customHudListenerId = api.Event.RegisterGameTickListener(CheckAndInitializeCustomHud, 20);
            }

            if (JauntConfig.Config.ShowHudIcon)
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
                _staminaHud = new HudElementStaminaBar(ClientApi);
                ClientApi.Event.RegisterGameTickListener(_staminaHud.OnGameTick, 1000);
                ClientApi.Gui.RegisterDialog(_staminaHud);

                ClientApi.Event.UnregisterGameTickListener(customHudListenerId);
            }
        }

        private HudStatbar GetVanillaStatbarHud()
        {
            foreach (var hud in ClientApi.Gui.OpenedGuis)
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
    }
}
