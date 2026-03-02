using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public class EntityBehaviorJauntRideable : EntityBehaviorRideable
    {
        #region Properties
        public int MinGeneration => minGeneration;
        public bool CanFlyRidden => RideableGaitOrder.ContainsKey(EnumHabitat.Air);

        protected long onGroundSinceMs;

        protected static JauntModSystem ModSystem => JauntModSystem.Instance;
        protected static string AttributeKey => $"{ModSystem.ModId}:rideable";

        #endregion Properties

        #region Initialization

        public EntityBehaviorJauntRideable(Entity entity) : base(entity)
        {
            onGroundSinceMs = entity.World.ElapsedMilliseconds;
        }

        protected override IMountableSeat CreateSeat(string seatId, SeatConfig config)
        {
            var seat = new EntityRideableSeat(this, seatId, config);
            seat.controls.OnAction = (EnumEntityAction action, bool on, ref EnumHandling handled) => OnJauntControls(seat, action, on, ref handled);
            return seat;
        }

        public void OnJauntControls(EntityRideableSeat seat, EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            // This is only called server side, don't try and map sneak/jump controls here

            if (action == EnumEntityAction.Sneak && on && !eagent.Controls.IsFlying)
            {
                if (entity.World.ElapsedMilliseconds - onGroundSinceMs > 500)
                {
                    (seat.Passenger as EntityAgent)?.TryUnmount();
                    seat.controls.StopAllMovement();
                }
            }

            return;
        }

        #endregion Initialization

        #region Motion Systems

        public void AirToGround()
        {
            entity.Pos.Roll = 0;
            eagent.Controls.IsFlying = false;
            eagent.Controls.Down = eagent.Controls.Up = false;
            onGroundSinceMs = entity.World.ElapsedMilliseconds;
        }

        public override double SeatsToMotion(float dt)
        {
            double angularMotion = base.SeatsToMotion(dt);

            foreach (var seat in Seats)
            {
                if (Controller != seat.Passenger) continue;

                #region Check if passenger controls mount
                var controls = seat.Controls;

                if (RemainingSaddleBreaks > 0)
                {
                    return angularMotion; // Handled by superclass
                }

                // Use reflection because we can't invoke a superclass's event from a subclass otehrwise
                var canRideField = typeof(EntityBehaviorRideable).GetField("CanRide", BindingFlags.NonPublic | BindingFlags.Instance);
                CanRideDelegate canRideEvent = (CanRideDelegate)canRideField.GetValue(this);
                if (canRideEvent != null && (controls.Jump || controls.TriesToMove))
                {
                    foreach (var dele in canRideEvent.GetInvocationList().Cast<CanRideDelegate>())
                    {
                        if (!dele(seat, out string? errMsg))
                        {
                            return angularMotion; // Handled by superclass
                        }
                    }
                }

                #endregion

                #region Jump Control

                jumpNow = false;
                if (controls.Jump)
                {
                    if (entity.OnGround || coyoteTimer > 0)
                    {
                        lastJumpMs = entity.World.ElapsedMilliseconds;
                        jumpNow = true;
                    }
                    else if (CanFlyRidden && coyoteTimer < -0.15) // Re-use coyoteTimer as a free jump timer
                    {
                        eagent.Controls.IsFlying = true;
                    }
                }

                #endregion Jump Control

                #region Flight Ascension/Descension Control

                if (eagent.Controls.IsFlying && ebg is EntityBehaviorJauntGait ebjg)
                {
                    eagent.Controls.Down = controls.Sneak && !controls.Jump && entity.Pos.Y > 0;
                    eagent.Controls.Up = controls.Jump && !controls.Sneak && entity.Pos.Y < entity.World.BlockAccessor.MapSizeY - 1;

                    JauntGaitMeta currentJauntGait = ebg.CurrentGait as JauntGaitMeta;
                    float verticalMoveSpeed = Math.Min(0.2f, dt) * GlobalConstants.BaseMoveSpeed * controls.MovespeedMultiplier / 2;
                    ebjg.VerticalSpeed = verticalMoveSpeed * (eagent.Controls.Up ? currentJauntGait?.AscendSpeed ?? 1 : eagent.Controls.Down ? -(currentJauntGait?.DescendSpeed ?? 5) : 0);

                    if (eagent.Controls.Down)
                    {
                        bool airBelow = entity.World.BlockAccessor.GetBlockBelow(entity.Pos.AsBlockPos).Code == "air";
                        if (!airBelow) AirToGround();
                    }
                }

                #endregion Flight Control
            }

            return angularMotion;
        }

        public void OnGaitChangedForEnvironment()
        {
            BehaviorGait_OnGaitChangedForEnvironment();
        }

        #endregion Motion Systems

        #region Utility Methods

        public override void GetInfoText(StringBuilder infotext)
        {
            if (RemainingSaddleBreaks > 0)
            {
                infotext.AppendLine(Lang.Get("jaunt:infotext-saddlebreaks-remaining", RemainingSaddleBreaks));
                if (entity.World.Calendar.TotalDays - LastSaddleBreakTotalDays > saddleBreakDayInterval)
                { 
                    infotext.AppendLine(Lang.Get("jaunt:infotext-saddlebreak-ready")); 
                }
                else
                {
                    if ((saddleBreakDayInterval - (entity.World.Calendar.TotalDays - LastSaddleBreakTotalDays)) >= 1)
                    {
                        infotext.AppendLine(Lang.Get("jaunt:infotext-saddlebreak-cooldown-days", Math.Round(saddleBreakDayInterval - (entity.World.Calendar.TotalDays - LastSaddleBreakTotalDays), 1)));
                    }
                    else
                    {
                        infotext.AppendLine(Lang.Get("jaunt:infotext-saddlebreak-cooldown-hours", Math.Round(24 * (saddleBreakDayInterval - (entity.World.Calendar.TotalDays - LastSaddleBreakTotalDays)), 1)));
                    }
                }
            }
        }

        #endregion Utility Methods

    }
}
