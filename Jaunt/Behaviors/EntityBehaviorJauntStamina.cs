using Jaunt.Config;
using Jaunt.Systems;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    /// <summary>
    /// Entity behavior for stamina management. This is a server side behavior that syncs to the client.
    /// </summary>
    /// <param name="fatigue"></param>
    /// <param name="ftgSource"></param>
    /// <returns></returns>
    public delegate float OnFatiguedDelegate(float fatigue, FatigueSource ftgSource);
    public class EntityBehaviorJauntStamina : EntityBehavior
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;

        public event OnFatiguedDelegate OnFatigued = (ftg, ftgSource) => ftg;

        private float timeSinceLastUpdate;
        private float timeSinceLastLog;

        #region Config props

        private static bool DebugMode => JauntConfig.Config.DebugMode; // Debug mode for logging
        #endregion

        ITreeAttribute StaminaTree => entity.WatchedAttributes.GetTreeAttribute(AttributeKey);

        private static string AttributeKey => $"{ModSystem.ModId}:stamina";

        public bool Exhausted
        {
            get => StaminaTree?.GetBool("exhausted") ?? false;
            set
            {
                StaminaTree.SetBool("exhausted", value);
                MarkDirty();
            }
        }

        public float Stamina
        {
            get => StaminaTree?.GetFloat("currentstamina") ?? 100f;
            set
            {
                StaminaTree.SetFloat("currentstamina", value);
                MarkDirty();
            }
        }

        public float MaxStamina
        {
            get => StaminaTree?.GetFloat("maxstamina") ?? 100f;
            set
            {
                StaminaTree.SetFloat("maxstamina", value);
                MarkDirty();
            }
        }

        public float AdjustedMaxStamina
        {
            get => MaxStamina * JauntConfig.Config.GlobalMaxStaminaMultiplier;
        }

        public float SprintFatigue
        {
            get => StaminaTree?.GetFloat("sprintfatigue") ?? 1f;
            set
            {
                StaminaTree.SetFloat("sprintfatigue", value);
                MarkDirty();
            }
        }
        public float SwimFatigue
        {
            get => StaminaTree?.GetFloat("swimfatigue") ?? 1f;
            set
            {
                StaminaTree.SetFloat("swimfatigue", value);
                MarkDirty();
            }
        }

        public float BaseFatigueRate
        {
            get => StaminaTree?.GetFloat("basefatiguerate") ?? 1f;
            set
            {
                StaminaTree.SetFloat("basefatiguerate", value);
                MarkDirty();
            }
        }

        public float StaminaRegenRate
        {
            get => StaminaTree?.GetFloat("staminaregenrate") ?? 1f;
            set
            {
                StaminaTree.SetFloat("staminaregenrate", value);
                MarkDirty();
            }
        }

        public float RegenPenaltyWounded
        {
            get => StaminaTree?.GetFloat("regenpenaltywounded") ?? 0f;
            set
            {
                StaminaTree.SetFloat("regenpenaltywounded", value);
                MarkDirty();
            }
        }

        public float RegenPenaltyMounted
        {
            get => StaminaTree?.GetFloat("regenpenaltymounted") ?? 0f;
            set
            {
                StaminaTree.SetFloat("regenpenaltymounted", value);
                MarkDirty();
            }
        }

        public bool Sprinting
        {
            get => StaminaTree?.GetBool("sprinting") ?? false;
            set
            {
                StaminaTree.SetBool("sprinting", value);
                MarkDirty();
            }
        }

        public FatigueSource SprintFatigueSource;
        public FatigueSource SwimFatigueSource;

        public EntityBehaviorJauntStamina(Entity entity) : base(entity) 
        { 
        }

        public void MapAttributes(JsonObject typeAttributes, JsonObject staminaAttributes)
        {
            Exhausted = typeAttributes["exhausted"].AsBool(false);
            MaxStamina = typeAttributes["maxstamina"].AsFloat(staminaAttributes["maxStamina"].AsFloat(100f));
            Stamina = typeAttributes["currentstamina"].AsFloat(staminaAttributes["maxStamina"].AsFloat(100f));
            SprintFatigue = typeAttributes["sprintfatigue"].AsFloat(staminaAttributes["sprintfatigue"].AsFloat(0.2f));
            SwimFatigue = typeAttributes["swimfatigue"].AsFloat(staminaAttributes["swimfatigue"].AsFloat(0.2f));
            StaminaRegenRate = typeAttributes["staminaregenrate"].AsFloat(staminaAttributes["staminaregenrate"].AsFloat(1f));
            BaseFatigueRate = typeAttributes["basefatiguerate"].AsFloat(staminaAttributes["basefatiguerate"].AsFloat(1f));
            RegenPenaltyWounded = typeAttributes["regenpenaltywounded"].AsFloat(staminaAttributes["regenpenaltywounded"].AsFloat(0f));
            RegenPenaltyMounted = typeAttributes["regenpenaltymounted"].AsFloat(staminaAttributes["regenpenaltymounted"].AsFloat(0f));
            Sprinting = typeAttributes["sprinting"].AsBool(false);
            MarkDirty();
        }


        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            if (DebugMode) ModSystem.Logger.Notification(Lang.Get("equus:debug-stamina-init", entity.EntityId));

            // Initialize common fatigue sources
            SprintFatigueSource = new()
            {
                Source = EnumFatigueSource.Run,
                SourceEntity = entity
            };

            SwimFatigueSource = new()
            {
                Source = EnumFatigueSource.Swim,
                SourceEntity = entity
            };

            // Fetch the stamina tree attribute
            var staminaTree = entity.WatchedAttributes.GetTreeAttribute(AttributeKey);

            // Fetch the stamina attributes from the entity properties
            var staminaAttributes = entity.Properties.Attributes[AttributeKey];

            // Initialize stamina tree
            if (staminaTree == null) entity.WatchedAttributes.SetAttribute(AttributeKey, new TreeAttribute());

            // Map attributes from entity properties to attribute tree
            MapAttributes(typeAttributes, staminaAttributes);

            timeSinceLastUpdate = (float)entity.World.Rand.NextDouble();   // Randomise which game tick these update, a starting server would otherwise start all loaded entities with the same zero timer
        }

        public override void OnGameTick(float deltaTime)
        {
            if (entity.World.Side == EnumAppSide.Client) return;

            var stamina = Stamina;  // better performance to read this TreeAttribute only once

            var ebr = entity.GetBehavior<EntityBehaviorRideable>();

            bool anySprint = ebr.Seats.Any(s => s.Controls.Sprint);

            bool sprinting = (ebr is EntityBehaviorJauntRideable) ? Sprinting : anySprint;

            timeSinceLastUpdate += deltaTime;
            timeSinceLastLog += deltaTime;

            if (timeSinceLastLog > 1f)
            {
                // Do some logging every second

                timeSinceLastLog = 0f;
            }

            // Check stamina 4 times a second
            if (timeSinceLastUpdate >= 0.25f)
            {
                if (entity.Alive)
                {
                    bool activelyFatiguing = false;

                    // --- Fatiguing actions ---
                    // Entity swimming
                    if (entity.Swimming)
                    {
                        activelyFatiguing = ApplyFatigue(SwimFatigue * CalculateElapsedMultiplier(timeSinceLastUpdate), EnumFatigueSource.Swim);
                    }
                    
                    // Entity sprinting
                    if (sprinting)
                    {
                        activelyFatiguing = ApplyFatigue(SprintFatigue * CalculateElapsedMultiplier(timeSinceLastUpdate), EnumFatigueSource.Run);
                    }

                    // --- Stamina regeneration ---
                    if (!activelyFatiguing)
                    {
                        RegenerateStamina(timeSinceLastUpdate);
                    }
                }

                Exhausted = stamina <= 0; // Entity is exhausted when stamina reaches 0

                timeSinceLastUpdate = 0; 
            }
        }

        private float CalculateElapsedMultiplier(float elapsedTime)
        {
            return elapsedTime * entity.Api.World.Calendar.SpeedOfTime * entity.Api.World.Calendar.CalendarSpeedMul;
        }

        public void RegenerateStamina(float elapsedTime)
        {
            var stamina = Stamina;  // better performance to read this TreeAttribute only once
            var maxStamina = AdjustedMaxStamina;

            var ebh = entity.GetBehavior<EntityBehaviorHealth>();

            // Add up penalties for various actions
            var currentWoundedPenalty = ebh.Health < ebh.MaxHealth * 0.7f ? RegenPenaltyWounded : 0f;
            var currentMountedPenalty = entity.GetBehavior<EntityBehaviorRideable>()?.AnyMounted() ?? false ? RegenPenaltyMounted : 0f;

            var totalPenalty = currentMountedPenalty + currentWoundedPenalty;

            var staminaRegenRate = (StaminaRegenRate - totalPenalty) * JauntConfig.Config.GlobalStaminaRegenMultiplier;

            if (stamina < maxStamina)
            {
                // 25% multiplier to convert per second regen to per quarter second regen
                var staminaRegenPerQuarterSecond = 0.25f * staminaRegenRate;
                var multiplierPerGameSec = elapsedTime * ModSystem.Api.World.Calendar.SpeedOfTime * ModSystem.Api.World.Calendar.CalendarSpeedMul;

                Stamina = Math.Min(stamina + (multiplierPerGameSec * staminaRegenPerQuarterSecond), maxStamina);
            }
        }

        private bool ApplyFatigue(float fatigueAmount, EnumFatigueSource source)
        {
            if (fatigueAmount <= 0) return false;

            FatigueSource fs = new()
            {
                Source = source,
                SourceEntity = entity,
                CauseEntity = entity,
                SourceBlock = null,
                SourcePos = entity.Pos.XYZ
            };

            FatigueEntity(fatigueAmount, fs);

            return true;
        }

        public void OnEntityFatigued(FatigueSource fatigueSource, ref float fatigue)
        {
            // Only fatigue server side and sync to client
            if (entity.World.Side == EnumAppSide.Client) return;         

            if (OnFatigued != null)
            {
                foreach (OnFatiguedDelegate dele in OnFatigued.GetInvocationList().Cast<OnFatiguedDelegate>())
                {
                    fatigue = dele.Invoke(fatigue, fatigueSource);
                }
            }

            FatigueEntity(fatigue, fatigueSource);
        }

        public void FatigueEntity(float fatigue, FatigueSource ftgSource)
        {
            if (entity.World.Side == EnumAppSide.Client) return;

            if (!entity.Alive) return;
            if (fatigue <= 0) return;

            var fatigueRate = BaseFatigueRate * fatigue;
            Stamina = GameMath.Clamp(Stamina - fatigueRate, 0, AdjustedMaxStamina);
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            if (entity.Api is not ICoreClientAPI capi) return;

            if (capi.World.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative || capi.Settings.Bool["extendedDebugInfo"])
            {
                infotext.AppendLine(Lang.Get("equus:infotext-stamina-state", Stamina, AdjustedMaxStamina));
                infotext.AppendLine(Lang.Get("equus:infotext-stamina-sprint-fatigue", SprintFatigue));
                infotext.AppendLine(Lang.Get("equus:infotext-stamina-swim-fatigue", SwimFatigue));
            }
        }

        public override string PropertyName()
        {
            return AttributeKey;
        }

        public void MarkDirty()
        {
            entity.WatchedAttributes.MarkPathDirty(AttributeKey);
        }
    }
}
