using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jaunt.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace Jaunt.Items
{
    public class ItemJauntInstrument : Item
    {
        protected static JauntModSystem ModSystem => JauntModSystem.Instance;
        SkillItem[] modes;
        private ICoreClientAPI capi;
        private bool isLocked;
        private string lockedGroupCode;
        private AssetLocation callSound;
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            lockedGroupCode = Attributes["groupCode"].AsString();
            isLocked = !string.IsNullOrEmpty(lockedGroupCode);

            callSound = Attributes["callSound"].AsObject(new AssetLocation("sounds/instrument/elkcall"));

            capi = api as ICoreClientAPI;

            List<SkillItem> toolModes = new List<SkillItem>()
            {
                new()
                {
                    Code = new AssetLocation("play"),
                    Name = Lang.Get("jaunt:instrument-skill-play")
                }
            };

            // If not locked to an entity group in item json add the bind toolmode
            if (!isLocked)
            {
                toolModes.Add(new SkillItem
                {
                    Code = new AssetLocation("bind"),
                    Name = Lang.Get("jaunt:instrument-skill-bind")
                });
            }

            modes = toolModes.ToArray();

            if (capi != null)
            {
                modes[0].WithIcon(capi, capi.Gui.LoadSvgWithPadding(
                    new AssetLocation("jaunt:textures/icons/instrument-play.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                modes[0].TexturePremultipliedAlpha = false;

                // Bind toolmode
                if (!isLocked)
                {
                    modes[1].WithIcon(capi, capi.Gui.LoadSvgWithPadding(
                        new AssetLocation("jaunt:textures/icons/instrument-bind.svg"), 48, 48, 5, ColorUtil.WhiteArgb));
                    modes[1].TexturePremultipliedAlpha = false;
                }
            }
        }

        /// <summary>
        /// Called by the inventory system when you hover over an item stack. This is the item stack name that is getting displayed.
        /// </summary>
        /// <param name="itemStack"></param>
        /// <returns></returns>
        public override string GetHeldItemName(ItemStack itemStack)
        {
            if (Code == null) return "Invalid block, id " + this.Id;

            // If the item is locked to an entity group in the item json, set the group code to the entity group
            if (isLocked && string.IsNullOrEmpty(itemStack.Attributes.GetString("groupCode"))) SetBoundEntityType(lockedGroupCode, itemStack);

            var groupCode = itemStack.Attributes.GetString("groupCode");

            string type = ItemClass.Name();
            StringBuilder sb = new StringBuilder();
            sb.Append(Lang.GetMatching(Code?.Domain + AssetLocation.LocationSeparator + type + "-" + Code?.Path));
            if (string.IsNullOrEmpty(groupCode))
                sb.Append(" (" + Lang.Get($"jaunt:groupcode-unbound").ToLower() + ")");
            else
                sb.Append(" (" + Lang.Get($"jaunt:groupcode-{groupCode}").ToLower() + ")");

            foreach (var bh in CollectibleBehaviors)
            {
                bh.GetHeldItemName(sb, itemStack);
            }

            return sb.ToString();
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            var groupCode = inSlot.Itemstack.Attributes.GetString("groupCode");
            if (string.IsNullOrEmpty(groupCode)) groupCode = "unbound";
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("\n" + Lang.Get($"jaunt:instrument-descprepend-{groupCode}"));
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            bool playmode = slot.Itemstack.Attributes.GetInt("toolMode", 0) == 0;

            if (playmode)
            {
                var ela = api.World.ElapsedMilliseconds;
                var prevela = slot.Itemstack.Attributes.GetLong("lastPlayerMs", -99999);

                // User must have restarted his game world, allow call
                if (prevela > ela)
                {
                    prevela = ela-4001;
                }
                if (ela - prevela <= 4000) return;


                slot.Itemstack.Attributes.SetLong("lastPlayerMs", ela);
                api.World.PlaySoundAt(callSound, byEntity, (byEntity as EntityPlayer)?.Player, 0.75f, 32, 0.5f);

                if (api.Side == EnumAppSide.Server)
                {
                    callEntity(slot, byEntity);
                }
            }
            else
            {
                if (entitySel is null || entitySel.Entity is null)
                {
                    capi.TriggerIngameError(this, "no-entity-selected", Lang.Get("jaunt:ingame-error-no-entitysel"));
                    return;
                }
                SetBoundEntityType(slot, entitySel.Entity);

                //Automatically toggle to play mode after binding to prevent accidental rebinding
                slot.Itemstack.Attributes.SetInt("toolMode", 0);
            }

            handling = EnumHandHandling.PreventDefault;
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            return modes;
        }

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
        {
            return slot.Itemstack.Attributes.GetInt("toolMode", 0);
        }

        public void SetBoundEntityType(string groupCode, ItemStack itemStack)
        {
            if (string.IsNullOrEmpty(groupCode)) return;
            itemStack.Attributes.SetString("groupCode", groupCode);
        }

        public void SetBoundEntityType(ItemSlot slot, Entity entity)
        {
            var groupCode = entity.GetBehavior<EntityBehaviorOwnable>()?.Group;

            if (string.IsNullOrEmpty(groupCode)) return;

            slot.Itemstack.Attributes.SetString("groupCode", groupCode);

            capi?.TriggerIngameDiscovery(this, "bound-groupcode", Lang.Get("jaunt:discovery-bound-entity", Lang.Get($"jaunt:groupcode-{groupCode}")));
        }

        private void callEntity(ItemSlot slot, EntityAgent byEntity)
        {
            var groupCode = slot.Itemstack.Attributes.GetString("groupCode", "mountableanimal");
            var plr = (byEntity as EntityPlayer).Player;
            var mseo = api.ModLoader.GetModSystem<ModSystemEntityOwnership>();
            if (!mseo.OwnerShipsByPlayerUid.TryGetValue(plr.PlayerUID, out var ownerships) || ownerships == null || !ownerships.TryGetValue(groupCode, out var ownership))
            {
                return;
            }

            var entity = api.World.GetEntityById(ownership.EntityId);
            if (entity == null)
            {
                return;
            }

            var mw = entity.GetBehavior<EntityBehaviorMortallyWoundable>();
            if (mw?.HealthState == EnumEntityHealthState.MortallyWounded || mw?.HealthState == EnumEntityHealthState.Dead)
            {
                return;
            }

            var tm = entity.GetBehavior<EntityBehaviorTaskAI>().TaskManager;
            var aitcto = tm.AllTasks.FirstOrDefault(t => t is AiTaskComeToOwner) as AiTaskComeToOwner;
            if (entity.ServerPos.DistanceTo(byEntity.ServerPos) > aitcto.TeleportMaxRange) // Do nothing outside max teleport range
            {
                return;
            }

            var mount = entity?.GetInterface<IMountable>();
            if (mount != null)
            {
                if (mount.IsMountedBy(plr.Entity)) return;
                if (mount.AnyMounted())
                {
                    entity.GetBehavior<EntityBehaviorRideable>()?.UnmnountPassengers(); // You are not my owner, get lost!
                }
            }

            entity.AlwaysActive = true;
            entity.State = EnumEntityState.Active;
            aitcto.allowTeleportCount=1;
            tm.StopTasks();
            tm.ExecuteTask(aitcto, 0);
        }
    }
}
