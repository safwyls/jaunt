using Cairo;
using Jaunt.Hud;
using Jaunt.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public record GaitMeta
    {
        public string Code { get; set; } // Unique identifier for the gait, ideally matched with rideable controls
        public int Order { get; set; } // The sequencing order for gaits, starting from backward to fastest forward
        public float TurnRadius { get; set; } = 3.5f;
        public float MoveSpeed { get; set; } = 0f;
        public bool Backwards { get; set; } = false;
        public float StaminaCost { get; set; } = 0f;
        public string FallbackGait { get; set; } // Gait to slow down to such as when fatiguing
        public AssetLocation Sound { get; set; }
        public AssetLocation IconTexture { get; set; }
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        public override string PropertyName()
        {
            return $"{ModSystem.ModId}:gait";
        }
        public List<GaitMeta> SortedGaits { get; private set; }
        public GaitMeta CurrentGait
        {
            get => SortedGaits.FirstOrDefault(g => g.Code == entity.WatchedAttributes.GetString("currentgait"));
            set => entity.WatchedAttributes.SetString("currentgait", value.Code);
        }

        // Quick access to special gaits
        public GaitMeta IdleGait;
        public GaitMeta FallbackGait => SortedGaits.FirstOrDefault(g => g.Code == CurrentGait.FallbackGait) ?? IdleGait; // Default to Idle if no fallback defined

        float timeSinceLastGaitFatigue = 0f;
        protected ICoreAPI api;
        protected ICoreClientAPI capi;
        protected EntityBehaviorJauntStamina ebs; // Reference to stamina behavior
        protected static bool DebugMode => ModSystem.DebugMode; // Debug mode for logging

        public EntityBehaviorGait(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            if (DebugMode) ModSystem.Logger.Notification(Lang.Get($"{ModSystem.ModId}:debug-rideable-init", entity.EntityId));

            api = entity.Api;
            capi = api as ICoreClientAPI;

            GaitMeta[] gaitarray = attributes["gaits"].AsArray<GaitMeta>();
            SortedGaits = gaitarray
                .OrderBy(g => g.Order)
                .ToList();
            
            string idleGaitCode = attributes["idleGait"].AsString("idle");
            IdleGait = SortedGaits.FirstOrDefault(g => g.Code == idleGaitCode, SortedGaits[0]);
            CurrentGait = IdleGait; // Set initial gait to Idle

            List<AssetLocation> iconTextures = new();
            foreach (var gait in SortedGaits)
            {
                // sounds
                gait.Sound ??= new AssetLocation("game:creature/hooved/" + gait.Code); // Default sound path if not defined

                // textures
                if (gait.IconTexture is null) continue; // Skip if no icon texture defined
                ModSystem.hudIconRenderer?.RegisterTexture(gait.IconTexture);
            }
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();
        }

        public float GetTurnRadius() => CurrentGait?.TurnRadius ?? 3.5f; // Default turn radius if not set
        
        public void SetIdle() => CurrentGait = IdleGait;
        public bool IsIdle => CurrentGait == IdleGait;
        public bool IsBackward => CurrentGait.Backwards;
        public bool IsForward => !CurrentGait.Backwards && CurrentGait != IdleGait;

        public void ApplyGaitFatigue(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null) return;

            timeSinceLastGaitFatigue += dt;

            if (timeSinceLastGaitFatigue >= 0.25f)
            {
                if (CurrentGait.StaminaCost > 0 && !entity.Swimming)
                {
                    ebs.FatigueEntity(CurrentGait.StaminaCost, new()
                    {
                        Source = EnumFatigueSource.Mounted,
                        SourceEntity = (entity as EntityAgent).MountedOn?.Passenger ?? entity
                    });
                }
            }
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            ApplyGaitFatigue(dt);
        }
    }
}
