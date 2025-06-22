using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public class EntityJauntRideableSeat : EntityRideableSeat
    {
        public EntityJauntRideableSeat(IMountable mountablesupplier, string seatId, SeatConfig config) : base(mountablesupplier, seatId, config)
        {
            controls.OnAction = OnJauntControls;
        }

        public override bool CanMount(EntityAgent entityAgent)
        {
            if (entityAgent is not EntityPlayer player) return false;

            var ebr = Entity.GetBehavior<EntityBehaviorJauntRideable>();
            if (Entity.WatchedAttributes.GetInt("generation") < ebr.minGeneration && player.Player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                var capi = entityAgent.World.Api as ICoreClientAPI;
                capi?.TriggerIngameError(this, "toowild", Lang.Get("jaunt:ingame-error-too-wild"));
                return false;
            }

            return base.CanMount(entityAgent);
        }

        public override void DidMount(EntityAgent entityAgent)
        {
            base.DidMount(entityAgent);

            if (Entity != null)
            {
                Entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                Entity.StartAnimation("idle");

                var capi = entityAgent.Api as ICoreClientAPI;
                if (capi != null && capi.World.Player.Entity.EntityId == entityAgent.EntityId) // Isself
                {
                    capi.Input.MouseYaw = Entity.Pos.Yaw;
                }
            }

            var ebr = mountedEntity as IMountableListener;
            (ebr as EntityBehaviorJauntRideable)?.DidMount(entityAgent);

            ebr = Entity as IMountableListener;
            (ebr as EntityBehaviorJauntRideable)?.DidMount(entityAgent);
        }

        public override void DidUnmount(EntityAgent entityAgent)
        {
            if (entityAgent.World.Side == EnumAppSide.Server && DoTeleportOnUnmount)
            {
                tryTeleportToFreeLocation();
            }
            if (entityAgent is EntityPlayer eplr)
            {
                eplr.BodyYawLimits = null;
                eplr.HeadYawLimits = null;
            }

            base.DidUnmount(entityAgent);

            var ebr = mountedEntity as IMountableListener;
            (ebr as EntityBehaviorJauntRideable)?.DidUnnmount(entityAgent);

            ebr = Entity as IMountableListener;
            (ebr as EntityBehaviorJauntRideable)?.DidUnnmount(entityAgent);
        }

        public void OnJauntControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.Sneak && on)
            {
                EntityAgent entityAgent = mountedEntity as EntityAgent;
                if (entityAgent.Controls.IsFlying)
                {
                    // Descend
                    return; // Don't unmount while flying
                }

                (Passenger as EntityAgent)?.TryUnmount();
                controls.StopAllMovement();
            }
        }
    }
}