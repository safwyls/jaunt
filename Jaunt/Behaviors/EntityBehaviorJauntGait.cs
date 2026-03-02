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
    public class JauntGaitMeta : GaitMeta
    {
        public float AscendSpeed { get; set; } = 0f;
        public float DescendSpeed { get; set; } = 0f;
        public AssetLocation IconTexture { get; set; }
    }

    public class EntityBehaviorJauntGait : EntityBehaviorGait
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;

        private static string AttributeKey => $"{ModSystem.ModId}:gait";
        public override string PropertyName()
        {
            return AttributeKey;
        }

        public double VerticalSpeed;
        public string currentClimbAnimation = null;

        public bool EnableDamageHandler = false;

        float timeSinceLastGaitFatigue = 0f;
        protected EntityBehaviorJauntStamina staminaBehavior;

        public EntityBehaviorJauntGait(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // Rebuild gait list from superclass, this time allocating JauntGaitMeta fields too
            var gaitarray = attributes["gaits"].AsArray<JauntGaitMeta>();
            foreach (var gait in gaitarray)
            {
                Gaits[gait.Code] = gait;
                gait.IconTexture?.WithPathPrefixOnce("textures/");
                gait.Sound?.WithPathPrefixOnce("sounds/");

                if (entity.Api.Side == EnumAppSide.Client) ModSystem.hudIconRenderer.RegisterTexture(gait.IconTexture);
            }

            // Shift the idle gait pointers over to those new JauntGaitMeta instances
            foreach (EnumHabitat key in IdleGaits.Keys)
            {
                IdleGaits[key] = Gaits[IdleGaits[key].Code];
            }

            EnableDamageHandler = attributes["enableDamageHandler"].AsBool();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            staminaBehavior = entity.GetBehavior<EntityBehaviorJauntStamina>();

            EntityBehaviorHealth ebh = eagent.GetBehavior<EntityBehaviorHealth>();

            if (ebh is not null)
            {
                ebh.onDamaged += (dmg, dmgSource) => HandleDamaged(eagent, dmg, dmgSource);
            }
        }

        public void ApplyGaitFatigue(float dt)
        {
            if (entity.Api.Side != EnumAppSide.Server || staminaBehavior == null) return;

            timeSinceLastGaitFatigue += dt;

            if (timeSinceLastGaitFatigue >= 0.25f)
            {
                if (CurrentGait.StaminaCost > 0 && !entity.Swimming)
                {
                    staminaBehavior.FatigueEntity(CurrentGait.StaminaCost, new FatigueSource
                    {
                        Source = EnumFatigueSource.Mounted,
                        SourceEntity = (entity as EntityAgent)?.MountedOn?.Passenger ?? entity
                    });

                    bool isTired = entity.World.Rand.NextDouble() < GetStaminaDeficitMultiplier(staminaBehavior.Stamina, staminaBehavior.MaxStamina);

                    if (isTired)
                    {
                        CurrentGait = staminaBehavior.Stamina < 10 ? CascadingFallbackGait(2) : FallbackGait;
                    }
                }
            }
        }

        public static float GetStaminaDeficitMultiplier(float currentStamina, float maxStamina)
        {
            float midpoint = maxStamina * 0.5f;

            if (currentStamina >= midpoint)
                return 0f;

            float deficit = 1f - (currentStamina / midpoint);  // 0 at midpoint, 1 at 0 stamina
            return deficit * deficit;  // Quadratic curve for gradual increase
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            UpdateClimbAnimation();
            ApplyGaitFatigue(dt);
        }

        public override void UpdateGaitForEnvironment()
        {
            EnumHabitat targetEnvironment = entity.Swimming ? EnumHabitat.Sea : EnumHabitat.Land;
            if (eagent.Controls.IsFlying || (entity.Api.Side == EnumAppSide.Server && CurrentGait.Environment == EnumHabitat.Air)) targetEnvironment = EnumHabitat.Air;
            // No condition for underwater implemented at this time

            if (CurrentGait.Environment == targetEnvironment) return;

            GaitMeta? closest = null;
            foreach (GaitMeta gait in Gaits.Values)
            {
                if (!gait.Natural || gait.Environment != targetEnvironment || gait.Direction != CurrentGait.Direction) continue;

                if (closest == null || (Math.Abs(gait.MoveSpeed - CurrentGait.MoveSpeed) < Math.Abs(closest.MoveSpeed - CurrentGait.MoveSpeed)))
                {
                    closest = gait;
                }
            }

            CurrentGait = closest;
            entity.GetBehavior<EntityBehaviorJauntRideable>()?.OnGaitChangedForEnvironment(); // Can't invoke parent class's delegate from a subclass
        }

        protected override void Move(float dt)
        {
            base.Move(dt);

            if (eagent.Controls.IsFlying)
            {
                eagent.Controls.FlyVector.Set(eagent.Controls.WalkVector);
                eagent.Pos.Motion.Y = VerticalSpeed;
            }
        }

        protected virtual void UpdateClimbAnimation()
        {
            string? nowClimbAnim = null;
            if (eagent.Controls.IsFlying && !CurrentGait.HasBackwardMotion)
            {
                if (VerticalSpeed > 0.001)
                {
                    nowClimbAnim = "fly-up";
                }
                else if (VerticalSpeed < -0.001)
                {
                    nowClimbAnim = "fly-down";
                }
            }

            if (nowClimbAnim != currentClimbAnimation)
            {
                if (currentClimbAnimation != null)
                {
                    eagent.StopAnimation(currentClimbAnimation);
                    eagent.AnimManager.AnimationsDirty = true;
                }
                currentClimbAnimation = nowClimbAnim;
                if (nowClimbAnim != null)
                {
                    eagent.StartAnimation(nowClimbAnim);
                }
            }
        }

        // This method is meant to mitigate fall damage if fall damage multiplier is not zero and the user has explicitly enabled the damage handler
        public float HandleDamaged(EntityAgent eagent, float damage, DamageSource damageSource)
        {
            if (entity.Properties.FallDamageMultiplier == 0) return damage;

            if (CurrentGait.Environment == EnumHabitat.Air && EnableDamageHandler) return 0f;

            return damage;
        }
    }
}
