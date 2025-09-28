using System;
using System.Linq;
using Jaunt.Systems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public record JauntGaitMeta : GaitMeta
    {
        public EnumHabitat Environment { get; set; }
        public double? DragFactor { get; set; } = null;
        public bool CanAscend { get; set; } = true;
        public bool CanDescend { get; set; } = true;
        public float AscendSpeed { get; set; } = 0f;
        public float DescendSpeed { get; set; } = 0f;
        public AssetLocation IconTexture { get; set; }
        public AnimationMetaData Anim { get; set; }
    }

    public class EntityBehaviorJauntGait : EntityBehaviorGait
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;

        private static string AttributeKey => $"{ModSystem.ModId}:gait";
        public override string PropertyName()
        {
            return AttributeKey;
        }

        public readonly FastSmallDictionary<string, JauntGaitMeta> JauntGaits = new(1);
        private ITreeAttribute gaitTree => entity.WatchedAttributes.GetTreeAttribute(AttributeKey);
        public JauntGaitMeta CurrentJauntGait => JauntGaits[entity.WatchedAttributes.GetString("currentgait")];

        public EnumHabitat CurrentEnv => CurrentJauntGait.Environment;

        public bool EnableDamageHandler = false;
        public JauntGaitMeta IdleJauntGait => (JauntGaitMeta)IdleGait;
        public JauntGaitMeta IdleFlyingJauntGait;
        public JauntGaitMeta IdleSwimmingJauntGait;
        public double FlyingDragFactor;
        public double SwimmingDragFactor;
        public double GroundDragFactor;
        public JauntGaitMeta FallbackJauntGait => CurrentJauntGait.FallbackGaitCode is null ? IdleJauntGait : JauntGaits[CurrentJauntGait.FallbackGaitCode];

        float timeSinceLastGaitFatigue = 0f;
        EntityAgent eagent => entity as EntityAgent;
        protected ICoreClientAPI capi;
        protected EntityBehaviorJauntStamina ebs; // Reference to stamina behavior
        protected EntityBehaviorJauntRideable ebr; // Reference to rideable behavior
        protected static bool DebugMode => ModSystem.DebugMode; // Debug mode for logging

        public EntityBehaviorJauntGait(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            if (DebugMode) ModSystem.Logger.Notification(Lang.Get($"{ModSystem.ModId}:debug-rideable-init", entity.EntityId));

            capi = api as ICoreClientAPI;

            // Order of operations matters
            // 1. Get drag factors
            FlyingDragFactor = 1 - (1 - GlobalConstants.AirDragFlying) * attributes["flyingGaitDrag"].AsFloat(1f);
            SwimmingDragFactor = 1 - (1 - GlobalConstants.WaterDrag) * attributes["swimmingGaitDrag"].AsFloat(1f);
            // 0.3f comes from vanilla ground drag code
            GroundDragFactor = 1 - 0.3f * attributes["groundGaitDrag"].AsFloat(1f);

            // 2. Build gait list
            var gaitarray = attributes["gaits"].AsArray<JauntGaitMeta>();
            foreach (var gait in gaitarray)
            {
                Gaits[gait.Code] = gait;
                JauntGaits[gait.Code] = gait;
                gait.IconTexture?.WithPathPrefixOnce("textures/");
                gait.Sound?.WithPathPrefixOnce("sounds/");

                // First check for gait specific drag factors
                // If missing apply environment drag factors
                // If those aren't set they default to 1f (max drag)
                switch (gait.Environment)
                {
                    case EnumHabitat.Air:
                        gait.DragFactor ??= FlyingDragFactor;
                        break;
                    case EnumHabitat.Sea:
                    case EnumHabitat.Underwater:
                        gait.DragFactor ??= SwimmingDragFactor;
                        break;
                    case EnumHabitat.Land:
                    default:
                        gait.DragFactor ??= GroundDragFactor;
                        break;
                }

                if (api.Side == EnumAppSide.Client) ModSystem.hudIconRenderer.RegisterTexture(gait.IconTexture);
            }

            // 3. Set idle gaits
            string idleGaitCode = attributes["idleGait"].AsString("idle");
            string idleFlyingGaitCode = attributes["idleFlyingGait"].AsString("idle");
            string idleSwimmingGaitCode = attributes["idleSwimmingGait"].AsString("swim");
            EnableDamageHandler = attributes["enableDamageHandler"].AsBool();
            IdleFlyingJauntGait = JauntGaits[idleFlyingGaitCode];
            IdleSwimmingJauntGait = JauntGaits[idleSwimmingGaitCode];

            // 4. Initialize gait tree
            var gaitTree = entity.WatchedAttributes.GetTreeAttribute(AttributeKey);

            if (gaitTree == null)
            {
                entity.WatchedAttributes.SetAttribute(AttributeKey, new TreeAttribute());

                // These only get set on new initializations, not on reloads
                CurrentGait = JauntGaits[attributes["currentgait"].AsString(idleGaitCode)];
                MarkDirty();
            }

            CurrentGait = CurrentEnv switch
            {
                EnumHabitat.Land => IdleJauntGait,
                EnumHabitat.Air => IdleFlyingJauntGait,
                EnumHabitat.Sea => IdleSwimmingJauntGait,
                EnumHabitat.Underwater => IdleSwimmingJauntGait,
                _ => IdleJauntGait
            };

        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();
        }

        public bool IsIdle => eagent.Controls.IsFlying
            ? CurrentJauntGait == IdleFlyingJauntGait
            : eagent.Swimming && IdleSwimmingJauntGait != null
                ? CurrentJauntGait == IdleSwimmingJauntGait
                : CurrentJauntGait == IdleJauntGait;

        public bool IsBackward => CurrentJauntGait.Backwards || CurrentJauntGait.MoveSpeed < 0f;
        public bool IsForward => !CurrentJauntGait.Backwards && CurrentJauntGait != IdleJauntGait;

        public void SetIdle(bool forceGround)
        {
            CurrentGait = eagent.Controls.IsFlying && !forceGround ? IdleFlyingJauntGait : IdleJauntGait;
            if (forceGround) eagent.Controls.IsFlying = false;
        }
        public void ApplyGaitFatigue(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null) return;

            timeSinceLastGaitFatigue += dt;

            if (timeSinceLastGaitFatigue >= 0.25f)
            {
                if (CurrentJauntGait.StaminaCost > 0 && !entity.Swimming)
                {
                    ebs.FatigueEntity(CurrentJauntGait.StaminaCost, new FatigueSource
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
