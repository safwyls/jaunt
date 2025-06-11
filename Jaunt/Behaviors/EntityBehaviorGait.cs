using Cairo;
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
    public class GaitConfig
    {
        public List<GaitMeta> Gaits { get; set; } = new();
    }

    public record GaitMeta
    {
        public string Code { get; set; } // Unique identifier for the gait, ideally matched with rideable controls
        public int Order { get; set; } // The sequencing order for gaits, starting from backward to fastest forward
        public float TurnRadius { get; set; } = 3.5f; // Turn radius for this control
        public float StaminaCost { get; set; } = 0f; // Stamina cost for this control
        public AssetLocation Sound { get; set; } // Sound to play when this control is active
        public AssetLocation IconTexture { get; set; } // Icon to display for this control
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        public override string PropertyName()
        {
            return $"{ModSystem.ModId}:gait";
        }
        public List<GaitMeta> SortedGaits { get; private set; }
        public Dictionary<string, LoadedTexture> texturesDict = new();
        public GaitMeta CurrentGait
        {
            get => SortedGaits.FirstOrDefault(g => g.Code == entity.WatchedAttributes.GetString("currentgait"));
            set => entity.WatchedAttributes.SetString("currentgait", value.Code);
        }

        // Quick access to special gaits
        public GaitMeta WalkbackGait => SortedGaits.FirstOrDefault(g => g.Order == 0);
        public GaitMeta IdleGait => SortedGaits.FirstOrDefault(g => g.Order == 1);

        float timeSinceLastGaitFatigue = 0f;
        protected GaitConfig gaitconfig;
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

            gaitconfig = attributes.AsObject<GaitConfig>();
            SortedGaits = gaitconfig.Gaits
                .OrderBy(g => g.Order)
                .ToList();
            
            CurrentGait = IdleGait; // Set initial gait to Idle

            // Fetch custom gait icons
            if (api is ICoreClientAPI)
            {
                foreach (var gait in SortedGaits)
                {
                    if (gait.IconTexture is null) continue; // Skip if no icon texture defined

                    LoadedTexture texture = new(capi);

                    var size = (int)Math.Ceiling((int)ModSystem.Config.IconSize * RuntimeEnv.GUIScale);
                    var loc = gait.IconTexture.Clone().WithPathPrefix("textures/");
                    texture = capi.Gui.LoadSvg(loc, size, size, size, size, ColorUtil.WhiteArgb);
                    texturesDict.Add(gait.Code, texture);
                }

                // Generate empty texture.
                LoadedTexture empty = new(capi);
                ImageSurface surface = new(Format.Argb32, (int)ModSystem.Config.IconSize, (int)ModSystem.Config.IconSize);

                capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
                surface.Dispose();

                texturesDict.Add("empty", empty);
            }
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            ebs = entity.GetBehavior<EntityBehaviorJauntStamina>();            
        }

        public GaitMeta GetNextGait(bool forward, GaitMeta currentGait = null)
        {
            currentGait ??= CurrentGait;

            if (SortedGaits == null || SortedGaits.Count == 0)
                return IdleGait;

            int currentIndex = SortedGaits.IndexOf(currentGait);
            int nextIndex = forward ? currentIndex + 1 : currentIndex - 1;

            // Boundary behavior
            if (nextIndex < 0) nextIndex = 0;
            if (nextIndex >= SortedGaits.Count) nextIndex = currentIndex - 1;

            return SortedGaits[nextIndex];
        }

        public void SetNextGait(bool forward, GaitMeta nextGait = null)
        {
            if (api.Side != EnumAppSide.Server) return;

            nextGait ??= GetNextGait(forward);

            CurrentGait = nextGait;
        }

        public float GetTurnRadius() => CurrentGait?.TurnRadius ?? 3.5f; // Default turn radius if not set
        
        public void SpeedUp() => SetNextGait(true);                
        public void SlowDown() => SetNextGait(false);
        public void SetIdle() => CurrentGait = IdleGait;
        public bool IsIdle => CurrentGait == IdleGait;
        public bool IsBackward => CurrentGait == WalkbackGait;
        public bool IsForward => CurrentGait != WalkbackGait && CurrentGait != IdleGait;

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

        public GaitMeta GetFirstForwardGait()
        {
            if (SortedGaits == null || SortedGaits.Count == 0)
                return IdleGait;

            // Find the first forward gait (Order > 1)
            return SortedGaits.FirstOrDefault(g => g.Order > 1) ?? IdleGait;
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            ApplyGaitFatigue(dt);
        }

        #region Disposal

        public void Dispose()
        {
            foreach (var texture in texturesDict.Values)
            {
                texture.Dispose();
            }
        }

        #endregion
    }
}
