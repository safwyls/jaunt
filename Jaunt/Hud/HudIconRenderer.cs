using Cairo;
using Jaunt.Behaviors;
using Jaunt.Config;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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
            List<AssetLocation> assetLocations = capi.Assets.GetLocations("textures/hud/", ModSystem.ModId);

            foreach (var asset in assetLocations)
            {
                LoadedTexture texture = new(capi);

                string name = asset.GetName().Split('.')[0]; // Get the name without extension

                capi.Render.GetOrLoadTexture(asset, ref texture);
                texturesDict.Add(name, texture);
            }

            // Generate empty texture.
            LoadedTexture empty = new(this.capi);
            ImageSurface surface = new ImageSurface(Format.Argb32, (int)JauntConfig.ChildConfig.IconSize, (int)JauntConfig.ChildConfig.IconSize);

            this.capi.Gui.LoadOrUpdateCairoTexture(surface, true, ref empty);
            surface.Dispose();

            texturesDict.Add("empty", empty);

            listenerId = this.capi.Event.RegisterGameTickListener(OnGameTick, 100);
        }

        private bool CanRender()
        {
            return JauntConfig.ChildConfig.ShowGaitIcon == true && activeTexture != null;
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (!CanRender()) return;

            float width = (float)GuiElement.scaled(JauntConfig.ChildConfig.IconSize);
            float height = (float)GuiElement.scaled(JauntConfig.ChildConfig.IconSize);

            float x = (capi.Render.FrameWidth / 2) - (width / 2) + (float)GuiElement.scaled(JauntConfig.ChildConfig.IconOffsetX);
            float y = (capi.Render.FrameHeight - height) + (float)GuiElement.scaled(JauntConfig.ChildConfig.IconOffsetY);

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
