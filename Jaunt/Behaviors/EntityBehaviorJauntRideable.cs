using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public enum TransitionEnum
    {
        landing,
        takeoff,
        none
    }
    
    public class JauntControlMeta : ControlMeta
    {
        public float MoveSpeedMultiplier { get; set; } = 0.7f; // Multiplier for the movement speed of the control
    }

    public class EntityBehaviorJauntRideable : EntityBehaviorRideable
    {
        #region Properties

        #region Public
        
        public List<GaitMeta> FlyableGaitOrder = new(); // List of gaits in order of increasing speed for the flyable entity
        public List<GaitMeta> RideableGaitOrder = new(); // List of gaits in order of increasing speed for the rideable entity
        public bool CanFly => FlyableGaitOrder?.Count > 0;
        
        #endregion Public

        #region Internal

        internal int minGeneration = 0; // Minimum generation for the animal to be rideable
        internal bool prevForwardKey, prevBackwardKey, prevSprintKey, prevJumpKey;
        internal float notOnGroundAccum;
        internal string prevSoundCode;
        internal bool shouldMove = false;
        internal bool wasFlying = false;
        internal string curTurnAnim = null;
        internal string curClimbAnim = null;
        internal string curSoundCode = null;
        internal JauntControlMeta curControlMeta = null;
        internal EnumControlScheme scheme;
        internal bool wasSwimming = false;

        #endregion Internal

        #region Protected

        protected static JauntModSystem ModSystem => JauntModSystem.Instance;
        protected long lastGaitChangeMs = 0;
        protected float timeSinceLastLog = 0;
        protected float timeSinceLastGaitCheck = 0;
        protected float timeSinceLastGaitFatigue = 0;
        protected ILoadedSound gaitSound;

        protected FastSmallDictionary<string, JauntControlMeta> Controls;
        protected string[] FlyableGaitOrderCodes; // List of gait codes in order of increasing speed for the flyable entity
        protected string[] GaitOrderCodes; // List of gait codes in order of increasing speed for the rideable entity

        protected EntityBehaviorJauntStamina ebs;
        protected EntityBehaviorGait ebg;
        protected EntityBehaviorHealth ebh;
        protected static bool DebugMode => ModSystem.DebugMode; // Debug mode for logging
        protected static string AttributeKey => $"{ModSystem.ModId}:rideable";

        #endregion Protected

        #endregion Properties

        #region Initialization

        public EntityBehaviorJauntRideable(Entity entity) : base(entity)
        {
            eagent = entity as EntityAgent;
        }

        protected override IMountableSeat CreateSeat(string seatId, SeatConfig config)
        {
            return new EntityJauntRideableSeat(this, seatId, config);
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            api = entity.Api;
            capi = api as ICoreClientAPI;

            if (DebugMode) ModSystem.Logger.Notification(Lang.Get($"{ModSystem.ModId}:debug-rideable-init", entity.EntityId));

            base.Initialize(properties, attributes);

            Controls = attributes["controls"].AsObject<FastSmallDictionary<string, JauntControlMeta>>();
            minGeneration = attributes["minGeneration"].AsInt(0);
            GaitOrderCodes = attributes["rideableGaitOrder"].AsArray<string>();
            FlyableGaitOrderCodes = attributes["flyableGaitOrder"].AsArray<string>();

            foreach (var val in Controls.Values) val.RiderAnim?.Init();

            curAnim = Controls["idle"].RiderAnim;

            capi?.Event.RegisterRenderer(this, EnumRenderStage.Before, "rideablesim");
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            ebs = eagent.GetBehavior<EntityBehaviorJauntStamina>();
            ebg = eagent.GetBehavior<EntityBehaviorGait>();
            ebh = eagent.GetBehavior<EntityBehaviorHealth>();

            // Gaits are required for rideable entities
            if (ebg is null) 
            {
                throw new Exception("EntityBehaviorGait not found on rideable entity. Ensure it is properly registered in the entity's properties.");
            }

            foreach (var str in GaitOrderCodes)
            {
                GaitMeta gait = ebg?.Gaits[str];
                if (gait != null) RideableGaitOrder.Add(gait);
            }

            if (FlyableGaitOrderCodes is not null && FlyableGaitOrderCodes.Length > 0)
            {
                foreach (var str in FlyableGaitOrderCodes)
                {
                    GaitMeta gait = ebg?.Gaits[str];
                    if (gait != null) FlyableGaitOrder.Add(gait);
                }   
            }

            if (ebh is not null)
            {
                ebh.onDamaged += (dmg, dmgSource) => HandleDamaged(eagent, dmg, dmgSource);
            }
        }
        
        #endregion Initialization

        #region AI Task Management
        
        public override void OnEntityLoaded()
        {
            SetupTaskBlocker();
        }

        public override void OnEntitySpawn()
        {
            SetupTaskBlocker();
        }

        internal void SetupTaskBlocker()
        {
            var ebc = entity.GetBehavior<EntityBehaviorAttachable>();

            if (api.Side == EnumAppSide.Server)
            {
                EntityBehaviorTaskAI taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
                taskAi.TaskManager.OnShouldExecuteTask += TaskManager_OnShouldExecuteTask;
                if (ebc != null)
                {
                    ebc.Inventory.SlotModified += Inventory_SlotModified;
                }
            }
            else
            {
                if (ebc != null)
                {
                    entity.WatchedAttributes.RegisterModifiedListener(ebc.InventoryClassName, UpdateControlScheme);
                }
            }
        }

        // Stop mounts from wandering off if mounted in last 24 hours (in game time)
        private bool TaskManager_OnShouldExecuteTask(IAiTask task)
        {
            if (task is AiTaskWander && api.World.Calendar.TotalHours - lastDismountTotalHours < 24) return false;

            return Seats.All(seat => seat.Passenger == null);
        }

        #endregion AI Task Management

        #region Inventory and Control Scheme Management

        private void Inventory_SlotModified(int obj)
        {
            UpdateControlScheme();
            ebg?.SetIdle();
        }

        private void UpdateControlScheme()
        {
            var ebc = entity.GetBehavior<EntityBehaviorAttachable>();
            if (ebc != null)
            {
                scheme = EnumControlScheme.Hold;
                foreach (var slot in ebc.Inventory)
                {
                    if (slot.Empty) continue;
                    var sch = slot.Itemstack.ItemAttributes?["controlScheme"].AsString(null);
                    if (sch != null)
                    {
                        if (!Enum.TryParse<EnumControlScheme>(sch, out scheme)) scheme = EnumControlScheme.Hold;
                        else break;
                    }
                }
            }
        }
        
        #endregion Inventory and Control Scheme Management

        #region Motion Systems

        public void SpeedUp() => SetNextGait(true);
        public void SlowDown() => SetNextGait(false);

        public GaitMeta TranslateGait(GaitMeta gait, TransitionEnum transition)
        {
            switch (transition)
            {
                case TransitionEnum.landing:
                    if (ebg.IsIdle) return ebg.IdleGait;
                    return RideableGaitOrder.FirstOrDefault(g => 
                        g.MoveSpeed >= gait.MoveSpeed 
                        && g.Backwards == gait.Backwards) 
                           ?? (gait.Backwards 
                               ? RideableGaitOrder.FirstOrDefault()
                               : RideableGaitOrder.LastOrDefault());
                case TransitionEnum.takeoff:
                    if (ebg.IsIdle) return ebg.IdleFlyingGait;
                    return FlyableGaitOrder.FirstOrDefault(g => 
                        g.MoveSpeed >= gait.MoveSpeed 
                        && g.Backwards == gait.Backwards) 
                           ?? (gait.Backwards 
                               ? FlyableGaitOrder.FirstOrDefault() 
                               : FlyableGaitOrder.LastOrDefault());
                case TransitionEnum.none:
                default:
                    return gait;
            }
        }
       
        public GaitMeta GetNextGait(bool forward = true, TransitionEnum transition = TransitionEnum.none, GaitMeta currentGait = null)
        {
            currentGait ??= ebg.CurrentGait;
            
            // Transition gaits when landing or taking off
            if (transition != TransitionEnum.none) return TranslateGait(currentGait, transition);
            
            // Eventually this should be changed to allow for more advanced swimming gaits
            if (eagent.Swimming) return forward ? ebg.Gaits["swim"] : ebg.Gaits["swimback"];
            
            if (eagent.Controls.IsFlying)
            {
                if (FlyableGaitOrder is not null && FlyableGaitOrder.Count > 0 && this.IsBeingControlled())
                {
                    int currentIndex = FlyableGaitOrder.IndexOf(currentGait);
                    int nextIndex = forward ? currentIndex + 1 : currentIndex - 1;

                    // Boundary behavior
                    if (nextIndex < 0) nextIndex = 0;
                    if (nextIndex >= FlyableGaitOrder.Count) nextIndex = currentIndex - 1;

                    return FlyableGaitOrder[nextIndex];
                }
                else
                {
                    return ebg.IdleFlyingGait;
                }
            }

            if (RideableGaitOrder is not null && RideableGaitOrder.Count > 0 && this.IsBeingControlled())
            {
                int currentIndex = RideableGaitOrder.IndexOf(currentGait);
                int nextIndex = forward ? currentIndex + 1 : currentIndex - 1;

                // Boundary behavior
                if (nextIndex < 0) nextIndex = 0;
                if (nextIndex >= RideableGaitOrder.Count) nextIndex = currentIndex - 1;

                return RideableGaitOrder[nextIndex];
            }
            else
            {
                return ebg.IdleGait;
            }
        }

        public void SetNextGait(bool forward, TransitionEnum transition = TransitionEnum.none, GaitMeta nextGait = null)
        {
            if (api.Side != EnumAppSide.Server) return;

            nextGait ??= GetNextGait(forward, transition);

            ebg.CurrentGait = nextGait;
        }

        public void AirToGround()
        {
            entity.Pos.Roll = 0;
            eagent.Controls.IsFlying = false;
            eagent.Controls.Down = eagent.Controls.Up = false;
            SetNextGait(true, transition: TransitionEnum.landing);   
        }

        public void GroundToAir()
        {
            if (!CanFly) return;
            eagent.Controls.IsFlying = true;
            SetNextGait(true, transition: TransitionEnum.takeoff);
        }
        
        public override Vec2d SeatsToMotion(float dt)
        {
            int seatsRowing = 0;

            double linearMotion = 0;
            double angularMotion = 0;

            jumpNow = false;
            coyoteTimer -= dt;

            Controller = null;
            foreach (var seat in Seats)
            {
                if (entity.OnGround) coyoteTimer = 0.15f;

                if (seat.Passenger == null || !seat.Config.Controllable) continue;

                if (seat.Passenger is EntityPlayer eplr)
                {
                    eplr.Controls.LeftMouseDown = seat.Controls.LeftMouseDown;
                    eplr.HeadYawLimits = new AngleConstraint(entity.Pos.Yaw + seat.Config.MountRotation.Y * GameMath.DEG2RAD, GameMath.PIHALF);
                    eplr.BodyYawLimits = new AngleConstraint(entity.Pos.Yaw + seat.Config.MountRotation.Y * GameMath.DEG2RAD, GameMath.PIHALF);
                }

                if (Controller != null) continue;
                Controller = seat.Passenger;

                #region Can Ride/Turn Checks
                var controls = seat.Controls;
                bool canRide = true;
                bool canTurn = true;

                var canRideField = typeof(EntityBehaviorRideable).GetField("CanRide", BindingFlags.NonPublic | BindingFlags.Instance);
                CanRideDelegate canRideEvent = (CanRideDelegate)canRideField.GetValue(this);
                if (canRideEvent != null && (controls.Jump || controls.TriesToMove))
                {
                    foreach (var dele in canRideEvent.GetInvocationList().Cast<CanRideDelegate>())
                    {
                        if (dele(seat, out string errMsg)) continue;
                        if (capi != null && seat.Passenger == capi.World.Player.Entity)
                        {
                            capi.TriggerIngameError(this, "cantride", Lang.Get("cantride-" + errMsg));
                        }
                        canRide = false;
                        break;
                    }
                }

                var canTurnField = typeof(EntityBehaviorRideable).GetField("CanTurn", BindingFlags.NonPublic | BindingFlags.Instance);
                CanRideDelegate canTurnEvent = (CanRideDelegate)canTurnField.GetValue(this);
                if (canTurnEvent != null && (controls.Left || controls.Right))
                {
                    foreach (var dele in canTurnEvent.GetInvocationList().Cast<CanRideDelegate>())
                    {
                        if (dele(seat, out string errMsg)) continue;
                        if (capi != null && seat.Passenger == capi.World.Player.Entity)
                        {
                            capi.TriggerIngameError(this, "cantride", Lang.Get("cantride-" + errMsg));
                        }
                        canTurn = false;
                        break;
                    }
                }

                if (!canRide) continue;
                #endregion

                #region Jump Control
                
                bool jumpPressed = controls.Jump && !prevJumpKey;

                // Only able to jump every 1500ms. Only works while on the ground.
                if (jumpPressed && entity.World.ElapsedMilliseconds - lastJumpMs > 1500 && entity.Alive && (entity.OnGround || coyoteTimer > 0))
                {
                    lastJumpMs = entity.World.ElapsedMilliseconds;
                    jumpNow = true;
                }
                else if (jumpPressed && !entity.OnGround)
                {
                    GroundToAir();
                }
                
                prevJumpKey = controls.Jump;

                #endregion Jump Control

                #region Flight Ascension/Descension Control

                if (eagent.Controls.IsFlying)
                {
                    eagent.Controls.Down = controls.Sneak && entity.Pos.Y > 0;
                    eagent.Controls.Up = controls.Jump && entity.Pos.Y < api.World.BlockAccessor.MapSizeY - 1;
                    bool pitchDown = controls.Sneak && !controls.Jump;
                    bool pitchUp = controls.Jump && !controls.Sneak;
                    
                    // This gives a linear tilt angled from 6 to 30 degrees at yaw multiplier of 4 down to 0
                    float tiltAngle = GameMath.DEG2RAD * (ebg.CurrentGait.YawMultiplier / 5 * 30f); // Tilt angle for the flight control
                    
                    var normalizedMoveSpeed = ebg.CurrentGait.MoveSpeed / GetFirstForwardFlyingGait().MoveSpeed;
                    var climbSpeed = 1f + MathF.Sin(tiltAngle) * normalizedMoveSpeed * ebg.CurrentGait.YawMultiplier;
                    eagent.Controls.MovespeedMultiplier = climbSpeed;
                    
                    if (eagent.Controls.MovespeedMultiplier == 0) eagent.Controls.MovespeedMultiplier = 1f;
                    
                    // Commenting out this pitch stuff for now as it seems janky
                    if (pitchUp) entity.Pos.Roll = -tiltAngle;
                    else if (pitchDown) entity.Pos.Roll = tiltAngle;
                    else entity.Pos.Roll = 0;
                    
                    if (eagent.Controls.Down)
                    {
                        bool airBelow = api.World.BlockAccessor.GetBlockBelow(entity.Pos.AsBlockPos).Code == "air";
                        if (!airBelow) AirToGround();
                    }
                }

                #endregion Flight Control

                if (scheme == EnumControlScheme.Hold && !controls.TriesToMove) continue;
                
                float str = ++seatsRowing == 1 ? 1 : 0.5f;

                // Detect if button currently being pressed
                bool nowForwards = controls.Forward;
                bool nowBackwards = controls.Backward;
                bool nowSprint = controls.Sprint;

                // Detect if current press is a fresh press
                bool forwardPressed = nowForwards && !prevForwardKey;
                bool backwardPressed = nowBackwards && !prevBackwardKey;
                bool sprintPressed = nowSprint && !prevSprintKey;

                long nowMs = entity.World.ElapsedMilliseconds;

                // This ensures we start moving without sprint key
                if (forwardPressed && ebg.IsIdle) SpeedUp();

                // Handle backward to idle change without sprint key
                if (forwardPressed && ebg.IsBackward) ebg.SetIdle();

                // Cycle up with sprint
                if (ebg.IsForward && sprintPressed && nowMs - lastGaitChangeMs > 300)
                {
                    SpeedUp();

                    lastGaitChangeMs = nowMs;
                }

                // Cycle down with back
                if (backwardPressed && nowMs - lastGaitChangeMs > 300)
                {
                    SlowDown();

                    lastGaitChangeMs = nowMs;
                }

                prevSprintKey = nowSprint;
                prevForwardKey = scheme == EnumControlScheme.Press && nowForwards;
                prevBackwardKey = scheme == EnumControlScheme.Press && nowBackwards;

                #region Motion update
                if (canTurn && (controls.Left || controls.Right))
                {
                    float dir = controls.Left ? 1 : -1;
                    angularMotion += str * dir * dt;
                }
                if (ebg.IsForward || ebg.IsBackward)
                {   
                    float dir = ebg.IsForward ? 1 : -1;
                    linearMotion += str * dir * dt * 2f;
                }
                #endregion
            }


            return new Vec2d(linearMotion, angularMotion);
        }

        protected virtual void UpdateAngleAndMotion(float dt)
        {
            // Ignore lag spikes
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);
            
            if (wasFlying) if (!eagent.Controls.IsFlying) AirToGround();
            wasFlying = eagent.Controls.IsFlying;
            
            if (jumpNow) UpdateRidingState();

            ForwardSpeed = Math.Sign(motion.X);

            float yawMultiplier = ebg?.CurrentGait.YawMultiplier ?? 3.5f;

            AngularVelocity = motion.Y * yawMultiplier;

            entity.SidedPos.Yaw += (float)motion.Y * dt * 30f;
            entity.SidedPos.Yaw %= GameMath.TWOPI;

            if (entity.World.ElapsedMilliseconds - lastJumpMs < 2000 && entity.World.ElapsedMilliseconds - lastJumpMs > 200 && entity.OnGround)
            {
                eagent.StopAnimation("jump");
            }
        }

        protected void UpdateRidingState()
        {
            if (!AnyMounted()) return;

            bool wasMidJump = IsInMidJump;
            IsInMidJump &= (entity.World.ElapsedMilliseconds - lastJumpMs < 500 || !entity.OnGround) && !entity.Swimming;
            
            // This is called when jump ends
            if (wasMidJump && !IsInMidJump)
            {
                var meta = Controls["jump"];
                foreach (var seat in Seats) seat.Passenger?.AnimManager?.StopAnimation(meta.RiderAnim.Animation);
                eagent.AnimManager.StopAnimation(meta.Animation);
            }
            
            // Handle transition from swimming to walking
            if (eagent.Swimming)
            {
                ebg.CurrentGait = ForwardSpeed > 0 ? ebg.Gaits["swim"] : ebg.Gaits["swimback"];
            }
            else if (!eagent.Swimming && wasSwimming)
            {
                ebg.CurrentGait = ForwardSpeed > 0 ? ebg.Gaits["walk"] : ebg.Gaits["walkback"];
            }

            wasSwimming = eagent.Swimming;

            eagent.Controls.Backward = ForwardSpeed < 0;
            eagent.Controls.Forward = ForwardSpeed >= 0;
            eagent.Controls.Sprint = ebg.CurrentGait.StaminaCost > 0 && ForwardSpeed > 0;

            string nowTurnAnim = null;
            if (ForwardSpeed >= 0)
            {
                if (AngularVelocity > 0.001)
                    nowTurnAnim = "turn-left";
                else if (AngularVelocity < -0.001)
                    nowTurnAnim = "turn-right";
            }
            
            if (nowTurnAnim != curTurnAnim)
            {
                if (curTurnAnim != null) eagent.StopAnimation(curTurnAnim);
                var anim = (ForwardSpeed == 0 ? "idle-" : "") + nowTurnAnim;
                curTurnAnim = anim;
                eagent.StartAnimation(anim);
            }

            JauntControlMeta nowControlMeta;

            shouldMove = ForwardSpeed != 0 ;
            if (!shouldMove && !jumpNow)
            {
                if (curControlMeta != null) Stop();
                // Idle states for each environment
                if (eagent.Swimming)
                {
                    curAnim = Controls[ebg.IdleSwimmingGait.Code].RiderAnim;
                    nowControlMeta = Controls[ebg.IdleSwimmingGait.Code];
                }
                else if (eagent.Controls.IsFlying)
                {
                    curAnim = Controls[ebg.IdleFlyingGait.Code].RiderAnim;
                    nowControlMeta = Controls[ebg.IdleFlyingGait.Code];
                }
                else
                {
                    curAnim = Controls[ebg.IdleGait.Code].RiderAnim;
                    nowControlMeta = null;
                }
            }
            else
            {
                nowControlMeta = Controls.FirstOrDefault(c => c.Key == ebg.CurrentGait.Code).Value;

                eagent.Controls.Jump = jumpNow;

                if (jumpNow)
                {
                    IsInMidJump = true;
                    jumpNow = false;
                    if (eagent.Properties.Client.Renderer is EntityShapeRenderer esr)
                        esr.LastJumpMs = capi.InWorldEllapsedMilliseconds;

                    nowControlMeta = Controls["jump"];
                    if (ForwardSpeed != 0) nowControlMeta.EaseOutSpeed = 30;

                    foreach (var seat in Seats) seat.Passenger?.AnimManager?.StartAnimation(nowControlMeta.RiderAnim);
                    EntityPlayer entityPlayer = entity as EntityPlayer;
                    IPlayer player = entityPlayer?.World.PlayerByUid(entityPlayer.PlayerUID);
                    entity.PlayEntitySound("jump", player, false);
                }
                else
                {
                    curAnim = nowControlMeta.RiderAnim;
                }
            }

            if (nowControlMeta != curControlMeta)
            {
                if (curControlMeta != null && curControlMeta.Animation != "jump")
                {
                    eagent.StopAnimation(curControlMeta.Animation);
                }

                curControlMeta = nowControlMeta;
                if (DebugMode) ModSystem.Logger.Notification($"Side: {api.Side}, Meta: {nowControlMeta?.Code}");
                if (nowControlMeta != null)
                {
                    eagent.AnimManager.StartAnimation(nowControlMeta);
                }
            }

            if (api.Side == EnumAppSide.Server)
            {
                eagent.Controls.Sprint = false; // Uh, why does the elk speed up 2x with this on?
            }
        }

        private void UpdateSoundState(float dt)
        {
            if (capi == null) return;

            if (eagent.OnGround) notOnGroundAccum = 0;
            else notOnGroundAccum += dt;

            gaitSound?.SetPosition((float)entity.Pos.X, (float)entity.Pos.Y, (float)entity.Pos.Z);

            if (Controls.ContainsKey(ebg.CurrentGait.Code))
            {
                var gaitMeta = ebg.CurrentGait;

                curSoundCode = eagent.Swimming || notOnGroundAccum > 0.2 ? null : gaitMeta.Sound;

                bool nowChange = curSoundCode != prevSoundCode;

                if (nowChange)
                {
                    gaitSound?.Stop();
                    prevSoundCode = curSoundCode;

                    if (curSoundCode is null) return;

                    gaitSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = gaitMeta.Sound,
                        DisposeOnFinish = false,
                        Position = entity.Pos.XYZ.ToVec3f(),
                        ShouldLoop = true
                    });

                    gaitSound?.Start();
                    if (DebugMode) ModSystem.Logger.Notification($"Now playing sound: {gaitMeta.Sound}");
                }
            }
        }

        private void Move(float dt, EntityControls controls, float nowMoveSpeed)
        {
            double cosYaw = Math.Cos(entity.Pos.Yaw);
            double sinYaw = Math.Sin(entity.Pos.Yaw);
            controls.WalkVector.Set(sinYaw, 0, cosYaw);
            controls.WalkVector.Mul(nowMoveSpeed * GlobalConstants.OverallSpeedMultiplier * ForwardSpeed);

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && controls.IsClimbing && entity.ClimbingOnFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingOnFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
                {
                    controls.WalkVector.Z = 0;
                }
            }

            if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Vec3d posAbove = new(pos.X, pos.Y + 1.0, pos.Z);
                Block inblock = entity.World.BlockAccessor.GetBlock(pos.AsBlockPos, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlock(posAbove.AsBlockPos, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.002f, 0.004f) * swimlineSubmergedness * 3;

                if (entity.CollidedHorizontally) controls.FlyVector.Y = 0.05f;

                eagent.Pos.Motion.Y += (swimlineSubmergedness - 0.1) / 300.0;
            }

            if (controls.IsFlying) controls.FlyVector.Set(controls.WalkVector);
            
        }

        public new void Stop()
        {
            ebg.SetIdle();
            eagent.Controls.StopAllMovement();
            eagent.Controls.WalkVector.Set(0, 0, 0);
            eagent.Controls.FlyVector.Set(0, 0, 0);
            shouldMove = false;
            if (curControlMeta != null && curControlMeta.Animation != "jump")
            {
                eagent.StopAnimation(curControlMeta.Animation);
            }
            if (eagent.Controls.IsFlying)
            {
                eagent.StartAnimation(Controls[ebg.IdleFlyingGait.Code].Animation);
                curControlMeta = Controls[ebg.IdleFlyingGait.Code];
            }
            else
            {
                eagent.StartAnimation(Controls[ebg.IdleGait.Code].Animation);
                curControlMeta = null;
            }
        }
        
        #endregion Motion Systems

        #region Utility Methods

        public new void DidUnnmount(EntityAgent entityAgent)
        {
            Stop();

            lastDismountTotalHours = entity.World.Calendar.TotalHours;
            foreach (var meta in Controls.Values)
            {
                if (meta.RiderAnim?.Animation != null)
                {
                    entityAgent.StopAnimation(meta.RiderAnim.Animation);
                }
            }

            if (eagent.Swimming)
            {
                eagent.StartAnimation(Controls[ebg.IdleSwimmingGait.Code].Animation);
            }
        }

        public new void DidMount(EntityAgent entityAgent)
        {
            UpdateControlScheme();
            ebg?.SetIdle();
        }
        
        public GaitMeta GetFirstForwardGait()
        {
            if (RideableGaitOrder == null || RideableGaitOrder.Count == 0)
                return ebg.IdleGait;

            // Find the first forward gait (Order > 1)
            return RideableGaitOrder.FirstOrDefault(g => !g.Backwards && g.MoveSpeed > 0) ?? ebg.IdleGait;
        }
        
        public GaitMeta GetFirstForwardFlyingGait()
        {
            if (FlyableGaitOrder == null || FlyableGaitOrder.Count == 0)
                return ebg.IdleFlyingGait;

            // Find the first forward gait (Order > 1)
            return FlyableGaitOrder.FirstOrDefault(g => !g.Backwards && g.MoveSpeed > 0) ?? ebg.IdleFlyingGait;
        }

        public void StaminaGaitCheck(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null || ebg == null) return;

            timeSinceLastGaitCheck += dt;

            // Check once a second
            if (timeSinceLastGaitCheck <= 1f) return;
            if (ebg.CurrentGait.StaminaCost > 0 && !eagent.Swimming)
            {
                bool isTired = api.World.Rand.NextDouble() < GetStaminaDeficitMultiplier(ebs.Stamina, ebs.MaxStamina);

                if (isTired)
                {                        
                    ebg.CurrentGait = ebs.Stamina < 10 ? ebg.CascadingFallbackGait(2) : ebg.FallbackGait;
                }
            }

            timeSinceLastGaitCheck = 0;
        }

        public static float GetStaminaDeficitMultiplier(float currentStamina, float maxStamina)
        {
            float midpoint = maxStamina * 0.5f;

            if (currentStamina >= midpoint)
                return 0f;

            float deficit = 1f - (currentStamina / midpoint);  // 0 at midpoint, 1 at 0 stamina
            return deficit * deficit;  // Quadratic curve for gradual increase
        }

        #endregion Utility Methods

        #region Listeners
        
        // This method is meant to mitigate fall damage if fall damage multiplier is not zero and the user has explicitly enabled the damage handler
        public float HandleDamaged(EntityAgent eagent, float damage, DamageSource damageSource)
        {
            if (ebh == null || entity.Properties.FallDamageMultiplier == 0) return damage;

            if (FlyableGaitOrder.Contains(ebg.CurrentGait) && ebg.EnableDamageHandler) return 0f;
            
            return damage;
        }
        
        public override void OnGameTick(float dt)
        {
            if (api.Side == EnumAppSide.Server) UpdateAngleAndMotion(dt);
            
            StaminaGaitCheck(dt);

            UpdateRidingState();

            if (!AnyMounted() && eagent.Controls.TriesToMove && eagent?.MountedOn != null)
            {
                eagent.TryUnmount();
            }

            if (shouldMove)
            {
                // Adjust move speed based on gait and control meta
                var curMoveSpeed = curControlMeta.MoveSpeed > 0 
                    ? curControlMeta.MoveSpeed 
                    : ebg.CurrentGait.MoveSpeed * curControlMeta.MoveSpeedMultiplier;

                Move(dt, eagent?.Controls, curMoveSpeed);
            }
            else
            {
                if (entity.Swimming && eagent is not null) eagent.Controls.FlyVector.Y = 0.2;
            }

            UpdateSoundState(dt);
        }

        #endregion Listeners

    }
}
