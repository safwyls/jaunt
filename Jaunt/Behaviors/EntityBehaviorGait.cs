using System;
using System.Linq;
using Jaunt.Systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Jaunt.Behaviors
{
    public record GaitMeta
    {
        public string Code { get; set; } // Unique identifier for the gait, ideally matched with rideable controls
        public EnumHabitat Environment { get; set; }
        public float YawMultiplier { get; set; } = 1f;
        public float MoveSpeed { get; set; } = 0f;
        public bool Backwards { get; set; } = false;
        public bool CanAscend { get; set; } = true;
        public bool CanDescend { get; set; } = true;
        public float AscendSpeed { get; set; } = 0f;
        public float DescendSpeed { get; set; } = 0f;
        public float StaminaCost { get; set; } = 0f;
        public string FallbackGaitCode { get; set; } // Gait to slow down to such as when fatiguing
        public AssetLocation Sound { get; set; }
        public AssetLocation IconTexture { get; set; }
        public AnimationMetaData Anim { get; set; }
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;

        private static string AttributeKey => $"{ModSystem.ModId}:gait";
        public override string PropertyName()
        {
            return AttributeKey;
        }

        public readonly FastSmallDictionary<string, GaitMeta> Gaits = new(1);
        private ITreeAttribute gaitTree => entity.WatchedAttributes.GetTreeAttribute(AttributeKey);
        public GaitMeta CurrentGait
        {
            get => Gaits[entity.WatchedAttributes.GetString("currentgait")];
            set
            {
                entity.WatchedAttributes.SetString("currentgait", value.Code);
                MarkDirty();
            }
        }

        public EnumHabitat CurrentEnv => CurrentGait.Environment;

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
        protected EntityBehaviorJauntRideable ebr; // Reference to rideable behavior
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

            var gaitTree = entity.WatchedAttributes.GetTreeAttribute(AttributeKey);

            if (gaitTree == null)
            {
                entity.WatchedAttributes.SetAttribute(AttributeKey, new TreeAttribute());

                // These only get set on new initializations, not on reloads
                CurrentGait = Gaits[attributes["currentgait"].AsString(idleGaitCode)];
                MarkDirty();
            }

            ModSystem.Logger.Debug($"API: {api.Side.ToString()} Current Env: {CurrentEnv.ToString()}");

            CurrentGait = CurrentEnv switch
            {
                EnumHabitat.Land => IdleGait,
                EnumHabitat.Air => IdleFlyingGait,
                EnumHabitat.Sea => IdleSwimmingGait,
                EnumHabitat.Underwater => IdleSwimmingGait,
                _ => IdleGait
            };
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();
        }

        public bool IsIdle => eagent.Controls.IsFlying ? CurrentGait == IdleFlyingGait : CurrentGait == IdleGait;
        public bool IsBackward => CurrentGait.Backwards || CurrentGait.MoveSpeed < 0f;
        public bool IsForward => !CurrentGait.Backwards && CurrentGait != IdleGait;

        public void SetIdle(bool forceGround)
        {
            CurrentGait = eagent.Controls.IsFlying && !forceGround ? IdleFlyingGait : IdleGait;
            if (forceGround) eagent.Controls.IsFlying = false;
        }
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

        public void MarkDirty()
        {
            entity.WatchedAttributes.MarkPathDirty(AttributeKey);
        }
    }
}
