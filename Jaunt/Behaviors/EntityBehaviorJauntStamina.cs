using Jaunt.Config;
using Jaunt.Systems;
using System;
using System.Collections.Generic;
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

        protected float timeSinceLastUpdate;
        protected float timeSinceLastLog;
        protected static bool DebugMode => ModSystem.DebugMode; // Debug mode for logging
        
        ITreeAttribute StaminaTree => entity.WatchedAttributes.GetTreeAttribute(AttributeKey);

        private static string AttributeKey => $"{ModSystem.ModId}:stamina";
        public bool ActivelyFatiguing { get; set; } = false;

        public bool Exhausted
        {
            get => StaminaTree.GetBool("exhausted");
            set => StaminaTree.SetBool("exhausted", value);
        }

        public float Stamina
        {
            get => StaminaTree.GetFloat("currentstamina");
            set => StaminaTree.SetFloat("currentstamina", value);
        }

        public float MaxStamina
        {
            get => StaminaTree.GetFloat("maxstamina");
            set => StaminaTree.SetFloat("maxstamina", value);
        }

        public float AdjustedMaxStamina
        {
            get => MaxStamina * ModSystem.Config.GlobalMaxStaminaMultiplier;
        }

        public float SprintFatigue
        {
            get => StaminaTree.GetFloat("sprintfatigue");
            set => StaminaTree.SetFloat("sprintfatigue", value);
        }
        public float SwimFatigue
        {
            get => StaminaTree.GetFloat("swimfatigue");
            set => StaminaTree.SetFloat("swimfatigue", value);
        }

        public float BaseFatigueRate
        {
            get => StaminaTree.GetFloat("basefatiguerate");
            set => StaminaTree.SetFloat("basefatiguerate", value);
        }

        public float StaminaRegenRate
        {
            get => StaminaTree.GetFloat("staminaregenrate");
            set => StaminaTree.SetFloat("staminaregenrate", value);
        }

        public float RegenPenaltyWounded
        {
            get => StaminaTree.GetFloat("regenpenaltywounded");
            set => StaminaTree.SetFloat("regenpenaltywounded", value);
        }

        public float RegenPenaltyMounted
        {
            get => StaminaTree.GetFloat("regenpenaltymounted");
            set => StaminaTree.SetFloat("regenpenaltymounted", value);
        }

        public bool Sprinting
        {
            get => StaminaTree.GetBool("sprinting");
            set => StaminaTree.SetBool("sprinting", value);
        }

        public bool Fleeing
        {
            get => StaminaTree.GetBool("fleeing");
            set => StaminaTree.SetBool("fleeing", value);
        }

        public bool DontFleeWhenExhausted
        {
            get => StaminaTree.GetBool("dontfleewhenexhausted");
            set => StaminaTree.SetBool("dontfleewhenexhausted", value);
        }
        public float ExhaustionThreshold
        {
            get => StaminaTree.GetFloat("exhaustionthreshold");
            set => StaminaTree.GetFloat("exhaustionthreshold", value);
        }

        public FatigueSource SprintFatigueSource;
        public FatigueSource SwimFatigueSource;

        public EntityBehaviorJauntStamina(Entity entity) : base(entity) 
        { 
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            if (DebugMode) ModSystem.Logger.Notification(Lang.Get("jaunt:debug-stamina-init", entity.EntityId));

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

            // Initialize stamina tree
            if (staminaTree == null)
            {
                entity.WatchedAttributes.SetAttribute(AttributeKey, new TreeAttribute());

                MaxStamina = typeAttributes["maxstamina"].AsFloat(ModSystem.Config.DefaultMaxStamina);
                // These only get set on new initializations, not on reloads
                Stamina = typeAttributes["currentstamina"].AsFloat(MaxStamina); // Start with full stamina
                Sprinting = false; // Not sprinting by default
            }

            // Refresh stamina config values from json
            MaxStamina = typeAttributes["maxstamina"].AsFloat(ModSystem.Config.DefaultMaxStamina);
            Stamina = Math.Clamp(Stamina, 0, MaxStamina); // Ensure stamina is in bounds on reload in case max stamina changed
            SprintFatigue = typeAttributes["sprintfatigue"].AsFloat(ModSystem.Config.DefaultSprintFatigue);
            SwimFatigue = typeAttributes["swimfatigue"].AsFloat(ModSystem.Config.DefaultSwimFatigue);
            BaseFatigueRate = typeAttributes["basefatiguerate"].AsFloat(ModSystem.Config.DefaultBaseFatigueRate);
            StaminaRegenRate = typeAttributes["staminaregenrate"].AsFloat(ModSystem.Config.DefaultStaminaRegenRate);
            RegenPenaltyWounded = typeAttributes["regenpenaltywounded"].AsFloat(ModSystem.Config.DefaultRegenPenaltyWounded);
            RegenPenaltyMounted = typeAttributes["regenpenaltymounted"].AsFloat(ModSystem.Config.DefaultRegenPenaltyMounted);
            ExhaustionThreshold = typeAttributes["exhaustionthreshold"].AsFloat(ModSystem.Config.DefaultExhaustionThreshold);
            DontFleeWhenExhausted = typeAttributes["dontfleewhenexhausted"].AsBool(ModSystem.Config.DefaultDontFleeWhenExhausted);
            MarkDirty();

            if (MaxStamina <= 0)
            {
                // If max stamina is not set, use the type attribute value
                MaxStamina = typeAttributes["maxstamina"].AsFloat(ModSystem.Config.DefaultMaxStamina);
                MarkDirty();
            }

            timeSinceLastUpdate = (float)entity.World.Rand.NextDouble();   // Randomise which game tick these update, a starting server would otherwise start all loaded entities with the same zero timer
        }

        public override void OnGameTick(float dt)
        {
            if (entity.World.Side == EnumAppSide.Client) return;

            var ebtai = entity.GetBehavior<EntityBehaviorTaskAI>();

            Fleeing = ebtai.TaskManager.ActiveTasksBySlot.Any(task => task is AiTaskFleeEntity);

            timeSinceLastUpdate += dt;
            timeSinceLastLog += dt;

            if (timeSinceLastLog > 1f && DebugMode)
            {                
                // Do some logging every second

                timeSinceLastLog = 0f;
            }

            // Check stamina 4 times a second
            if (timeSinceLastUpdate >= 0.25f)
            {
                if (entity.Alive)
                {
                    if (Fleeing && Exhausted && DontFleeWhenExhausted)
                    {
                        ebtai.TaskManager.StopTask(typeof(AiTaskFleeEntity));
                    }

                    // --- Globally Fatiguing Actions Here ---
                    // Entity swimming
                    if (entity.Swimming)
                    {
                        ActivelyFatiguing = ApplyFatigue(SwimFatigue * ModSystem.Config.GlobalSwimStaminaCostMultiplier * CalculateElapsedMultiplier(timeSinceLastUpdate), EnumFatigueSource.Swim);
                    }

                    // Fleeing fatigue
                    if (Fleeing)
                    {
                        ActivelyFatiguing = ApplyFatigue(SprintFatigue * ModSystem.Config.GlobalSprintStaminaCostMultiplier * CalculateElapsedMultiplier(timeSinceLastUpdate), EnumFatigueSource.Run);
                    }

                    // --- Stamina regeneration ---
                    if (!ActivelyFatiguing)
                    {
                        RegenerateStamina(timeSinceLastUpdate);
                    }
                }

                Exhausted = Stamina / MaxStamina <= ExhaustionThreshold; // Entity is exhausted when stamina reaches 0
                MarkDirty();

                timeSinceLastUpdate = 0;
                ActivelyFatiguing = false;
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

            var staminaRegenRate = (StaminaRegenRate - totalPenalty) * ModSystem.Config.GlobalStaminaRegenMultiplier;

            if (stamina < maxStamina)
            {
                // 25% multiplier to convert per second regen to per quarter second regen
                var staminaRegenPerQuarterSecond = 0.25f * staminaRegenRate;
                if (ModSystem.Api.World.Calendar is not null)
                {
                    var multiplierPerGameSec = elapsedTime * ModSystem.Api.World.Calendar.SpeedOfTime * ModSystem.Api.World.Calendar.CalendarSpeedMul;

                    Stamina = Math.Min(stamina + (multiplierPerGameSec * staminaRegenPerQuarterSecond), maxStamina);
                    MarkDirty();
                }
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

        // Used to apply fatigue immediately
        public void FatigueEntity(float fatigue, FatigueSource fs)
        {
            ActivelyFatiguing = true;

            if (entity.World.Side == EnumAppSide.Client) return;

            if (!entity.Alive) return;
            if (fatigue <= 0) return;

            var fatigueRate = BaseFatigueRate * fatigue;
            Stamina = GameMath.Clamp(Stamina - fatigueRate, 0, AdjustedMaxStamina);
            MarkDirty();
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            if (entity.Api is not ICoreClientAPI capi) return;

            if (capi.World.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative || capi.Settings.Bool["extendedDebugInfo"])
            {
                infotext.AppendLine(Lang.Get("jaunt:infotext-stamina-state", Stamina, AdjustedMaxStamina));
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
