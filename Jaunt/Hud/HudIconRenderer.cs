using Cairo;
using Jaunt.Behaviors;
using Jaunt.Config;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Hud
{
    public class HudIconRenderer : IRenderer, IDisposable
    {
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        private ICoreClientAPI capi;
        private FastSmallDictionary<string, LoadedTexture> texturesDict = new(5);
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
            var icons = capi.Assets.GetMany("textures/hud/", ModSystem.ModId, false);
            List<AssetLocation> assetLocations = capi.Assets.GetLocations("textures/hud/", ModSystem.ModId);

            foreach (var icon in icons)
            {
                string name = icon.Name.Substring(0, icon.Name.IndexOf('.'));

                name = Regex.Replace(name, @"\d+\-", "");

                var size = (int)Math.Ceiling((int)ModSystem.Config.IconSize * RuntimeEnv.GUIScale);
                LoadedTexture texture = capi.Gui.LoadSvg(icon.Location, size, size, size, size, ColorUtil.WhiteArgb);
                texturesDict.Add(name, texture);
            }

            // Generate empty texture.
            LoadedTexture empty = new(this.capi);
            ImageSurface surface = new ImageSurface(Format.Argb32, (int)ModSystem.Config.IconSize, (int)ModSystem.Config.IconSize);

            this.capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
            surface.Dispose();

            texturesDict.Add("empty", empty);

            listenerId = this.capi.Event.RegisterGameTickListener(OnGameTick, 100);
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

            if (player.MountedOn?.MountSupplier?.OnEntity?.GetBehavior<EntityBehaviorRideable>() is EntityBehaviorJauntRideable ebr)
            {
                activeTexture = ebr.CurrentGait switch
                {
                    GaitState.Walkback => ebr.texturesDict.TryGetValue("walkback", out LoadedTexture t) ? t : texturesDict["walkback"],
                    GaitState.Idle => ebr.texturesDict.TryGetValue("idle", out LoadedTexture t) ? t : texturesDict["idle"],
                    GaitState.Walk => ebr.texturesDict.TryGetValue("walk", out LoadedTexture t) ? t : texturesDict["walk"],
                    GaitState.Trot => ebr.texturesDict.TryGetValue("trot", out LoadedTexture t) ? t : texturesDict["trot"],
                    GaitState.Canter => ebr.texturesDict.TryGetValue("canter", out LoadedTexture t) ? t : texturesDict["canter"],
                    GaitState.Gallop => ebr.texturesDict.TryGetValue("gallop", out LoadedTexture t) ? t : texturesDict["gallop"],
                    _ => texturesDict["empty"]  
                };
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
