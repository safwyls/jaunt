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
        private static readonly FastSmallDictionary<string, LoadedTexture> texturesDict = new FastSmallDictionary<string, LoadedTexture>(6);
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
            if (!texturesDict.ContainsKey("empty"))
            {
                // Generate empty texture.
                LoadedTexture empty = new(capi);
                ImageSurface surface = new(Format.Argb32, (int)ModSystem.Config.IconSize, (int)ModSystem.Config.IconSize);

                capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
                surface.Dispose();

                texturesDict.Add("empty", empty);
            }

            listenerId = capi.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        public void RegisterTexture(AssetLocation assetLocation)
        {
            if (assetLocation is null || assetLocation.Path == null || assetLocation.Path.Length == 0) return;

            var loc = assetLocation.Clone().WithPathPrefixOnce("textures/");
            if (texturesDict.ContainsKey(loc.ToNonNullString())) return;

            var size = (int)Math.Ceiling((int)ModSystem.Config.IconSize * RuntimeEnv.GUIScale);
            LoadedTexture texture = capi.Gui.LoadSvg(loc, size, size, size, size, ColorUtil.WhiteArgb);
            if (texture is null) return;
            texturesDict.Add(loc.ToNonNullString(), texture);
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

            if (player.MountedOn?.MountSupplier?.OnEntity?.GetBehavior<EntityBehaviorGait>() is EntityBehaviorGait ebg && ebg.CurrentGait.IconTexture is not null)
            {
                activeTexture = texturesDict.TryGetValue(ebg.CurrentGait.IconTexture, out LoadedTexture value) ? value : texturesDict["empty"];
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
