using Cairo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public class GaitConfig
    {
        public Dictionary<string, GaitMeta> Gaits { get; set; }
    }

    public class GaitMeta
    {
        public float TurnRadius { get; set; } = 3.5f;
        public float StaminaCost { get; set; } = 0f;
        public float MoveSpeed { get; set; } = 0;
        public AssetLocation Sound { get; set; }
        public AssetLocation IconTexture { get; set; }
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        protected GaitConfig gaitconfig;
        protected ICoreAPI api;
        protected ICoreClientAPI capi;
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        public FastSmallDictionary<string, LoadedTexture> texturesDict;
        public override string PropertyName() => "harvestable";
        public string CurrentGait
        {
            get => entity.WatchedAttributes.GetString("currentgait", "idle");
            set => entity.WatchedAttributes.SetString("currentgait", value);
        }

        public List<string> AvailableGaits { get; set; }

        public EntityBehaviorGait(Entity entity) : base(entity) 
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            api = entity.Api;
            base.Initialize(properties, typeAttributes);

            ITreeAttribute gaitTree = entity.WatchedAttributes.GetTreeAttribute($"{ModSystem.ModId}:gait");

            if (gaitTree == null)
            {
                entity.WatchedAttributes.SetAttribute($"{ModSystem.ModId}:gait", gaitTree = new TreeAttribute());
            }

            gaitconfig = typeAttributes.AsObject<GaitConfig>();

            var orderedGaits = gaitconfig.Gaits.OrderBy(g => g.Value.MoveSpeed);

            // Fetch custom gait icons
            if (api is ICoreClientAPI)
            {
                List<AssetLocation> assetLocations = gaitconfig.Gaits.Values
                    .Where(g => g.IconTexture is not null)
                    .Select(g => g.IconTexture)
                    .ToList();

                texturesDict = new(assetLocations.Count);

                foreach (var asset in assetLocations)
                {
                    LoadedTexture texture = new(capi);

                    string name = asset.GetName().Split('.')[0]; // Get the name without extension

                    capi.Render.GetOrLoadTexture(asset.Clone().WithPathPrefix("textures/"), ref texture);
                    texturesDict.Add(name, texture);
                }

                // Generate empty texture.
                LoadedTexture empty = new(capi);
                ImageSurface surface = new ImageSurface(Format.Argb32, (int)ModSystem.Config.IconSize, (int)ModSystem.Config.IconSize);

                capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
                surface.Dispose();

                texturesDict.Add("empty", empty);
            }
        }

        public string GetNextGait(string currentGait, bool forward)
        {
            if (AvailableGaits == null || AvailableGaits.Count == 0)
                return "idle";

            int currentIndex = AvailableGaits.IndexOf(currentGait);
            if (currentIndex < 0) return "idle";

            int nextIndex = forward ? currentIndex + 1 : currentIndex - 1;

            // Boundary behavior
            if (nextIndex < 0) nextIndex = 0;
            if (nextIndex >= AvailableGaits.Count) nextIndex = currentIndex - 1;

            return AvailableGaits[nextIndex];
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
