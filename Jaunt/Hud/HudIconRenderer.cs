using Cairo;
using Jaunt.Behaviors;
using Jaunt.Config;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Jaunt.Hud
{
    public class HudIconRenderer : IRenderer, IDisposable
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        private ICoreClientAPI capi;
        private static readonly Dictionary<string, LoadedTexture> texturesDict = new();
        private LoadedTexture activeTexture;
        private long listenerId;
        public double RenderOrder => 1;
        public int RenderRange => 0;
        public HudIconRenderer(ICoreClientAPI api) 
        {
            capi = api;
            api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
        }

        public void Initialize()
        {
            List<AssetLocation> assetLocations = capi.Assets.GetLocations("textures/hud/", ModSystem.ModId);

            RegisterTextures(assetLocations);

            // Generate empty texture.
            LoadedTexture empty = new(capi);
            ImageSurface surface = new(Format.Argb32, (int)ModSystem.Config.IconSize, (int)ModSystem.Config.IconSize);

            capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
            surface.Dispose();

            if (!texturesDict.ContainsKey("empty")) texturesDict.Add("empty", empty);

            listenerId = capi.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        public void RegisterTextures(List<AssetLocation> textures)
        {
            foreach (var asset in textures)
            {
                LoadedTexture texture = new(capi);

                var size = (int)Math.Ceiling((int)ModSystem.Config.IconSize * RuntimeEnv.GUIScale);
                var loc = asset.Clone().WithPathPrefixOnce("textures/");
                texture = capi.Gui.LoadSvg(loc, size, size, size, size, ColorUtil.WhiteArgb);
                if (texture is null) continue;
                if (!texturesDict.ContainsKey(loc.ToNonNullString())) texturesDict.Add(loc.ToNonNullString(), texture);
            }
        }

        private bool CanRender()
        {
            return ModSystem.Config.ShowGaitIcon == true && activeTexture != null;
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (!CanRender()) return;

            float width = (float)GuiElement.scaled(ModSystem.Config.IconSize);
            float height = (float)GuiElement.scaled(ModSystem.Config.IconSize);

            float x = (capi.Render.FrameWidth / 2) - (width / 2) + (float)GuiElement.scaled(ModSystem.Config.IconOffsetX);
            float y = (capi.Render.FrameHeight - height) + (float)GuiElement.scaled(ModSystem.Config.IconOffsetY);

            capi.Render.RenderTexture(activeTexture.TextureId, x, y, width, height);
        }

        protected void OnGameTick(float dt)
        {
            EntityPlayer player = capi.World.Player.Entity;

            if (player.MountedOn?.MountSupplier?.OnEntity?.GetBehavior<EntityBehaviorGait>() is EntityBehaviorGait ebg)
            {
                string key;
                if (ebg.CurrentGait.IconTexture is null)
                {
                    // Try to build the matching jaunt texture path from the gait code
                    var code = ebg.CurrentGait.Code.ToLowerInvariant();
                    key = $"jaunt:textures/hud/{code}.svg";
                }
                else
                {
                    key = ebg.CurrentGait.IconTexture.Clone().WithPathPrefixOnce("textures/").ToNonNullString();
                }

                activeTexture = texturesDict.TryGetValue(key, out LoadedTexture value) ? value : texturesDict["empty"];
            }
            else
            {
                activeTexture = texturesDict["empty"];
            }
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            capi.Event.UnregisterGameTickListener(listenerId);
            foreach (var texture in texturesDict.Values)
            {
                texture.Dispose();
            }
        }
    }
}
