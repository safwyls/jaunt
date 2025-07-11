using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using Jaunt.Systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Jaunt.Behaviors
{
    public record GaitMeta
    {
        public string Code { get; set; } // Unique identifier for the gait, ideally matched with rideable controls
        public float TurnRadius { get; set; } = 1f / 3.5f;
        private float? _yawMultiplier = null;
        public float YawMultiplier
        {
            get => _yawMultiplier ?? 1f / TurnRadius;
            set => _yawMultiplier = value;
        }
        public float MoveSpeed { get; set; } = 0f;
        public bool Backwards { get; set; } = false;
        public float StaminaCost { get; set; } = 0f;
        public string FallbackGaitCode { get; set; } // Gait to slow down to such as when fatiguing
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
        public readonly FastSmallDictionary<string, GaitMeta> Gaits = new(1);
        public GaitMeta CurrentGait
        {
            get => Gaits[entity.WatchedAttributes.GetString("currentgait")];
            set => entity.WatchedAttributes.SetString("currentgait", value.Code);
        }

        public bool EnableDamageHandler = false;
        public GaitMeta IdleGait;
        public GaitMeta IdleFlyingGait;
        public GaitMeta IdleSwimmingGait;
        public GaitMeta FallbackGait => CurrentGait.FallbackGaitCode is null ? IdleGait : Gaits[CurrentGait.FallbackGaitCode];
        public GaitMeta CascadingFallbackGait(int n)
        {
            var result = CurrentGait;
            
            while (n > 0)
            {
                if (result.FallbackGaitCode is null) return IdleGait;
                result = Gaits[result.FallbackGaitCode];
                n--;
            }

            return result;
        }

        float timeSinceLastGaitFatigue = 0f;
        EntityAgent eagent => entity as EntityAgent;
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

            var gaitarray = attributes["gaits"].AsArray<GaitMeta>();
            foreach (var gait in gaitarray)
            {
                Gaits[gait.Code] = gait;
                gait.IconTexture?.WithPathPrefixOnce("textures/");
                gait.Sound?.WithPathPrefixOnce("sounds/");
                
                if (api.Side == EnumAppSide.Client) ModSystem.hudIconRenderer.RegisterTexture(gait.IconTexture);
            }

            string idleGaitCode = attributes["idleGait"].AsString("idle");
            string idleFlyingGaitCode = attributes["idleFlyingGait"].AsString("idle");
            string idleSwimmingGaitCode = attributes["idleSwimmingGait"].AsString("swim");
            EnableDamageHandler = attributes["enableDamageHandler"].AsBool();
            IdleGait = Gaits[idleGaitCode];
            IdleFlyingGait = Gaits[idleFlyingGaitCode];
            IdleSwimmingGait = Gaits[idleSwimmingGaitCode];
            
            // Set initial gait
            // This is important to make sure CurrentGait can be called by other behaviors
            // Todo: Review this for a potentially more robust way of picking the initial gait
            // currently leaving as idle gait, but could be set to a more appropriate gait based on entity state
            CurrentGait = IdleGait;
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();
        }

        public float GetTurnRadius() => CurrentGait?.TurnRadius ?? 1 / 3.5f; // Default turn radius if not set

        public void SetIdle() => CurrentGait = eagent.Controls.IsFlying ? IdleFlyingGait : IdleGait;
        public bool IsIdle => eagent.Controls.IsFlying ? CurrentGait == IdleFlyingGait : CurrentGait == IdleGait;
        public bool IsBackward => CurrentGait.Backwards || CurrentGait.MoveSpeed < 0f;
        public bool IsForward => !CurrentGait.Backwards && CurrentGait != IdleGait;

        public void ApplyGaitFatigue(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null) return;

            timeSinceLastGaitFatigue += dt;

            if (timeSinceLastGaitFatigue >= 0.25f)
            {
                if (CurrentGait.StaminaCost > 0 && !entity.Swimming)
                {
                    ebs.FatigueEntity(CurrentGait.StaminaCost, new FatigueSource
                    {
                        Source = EnumFatigueSource.Mounted,
                        SourceEntity = (entity as EntityAgent)?.MountedOn?.Passenger ?? entity
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
