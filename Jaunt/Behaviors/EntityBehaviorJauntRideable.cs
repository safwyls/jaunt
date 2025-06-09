using Cairo;
using Jaunt.Config;
using Jaunt.Systems;
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
using Vintagestory.GameContent;

namespace Jaunt.Behaviors
{
    public class JauntRideableConfig
    {
        public Dictionary<string, ControlMeta> Controls { get; set; } = new Dictionary<string, ControlMeta>();
        public int MinGeneration { get; set; } = 0; // Minimum generation for the animal to be rideable
        public string LowStaminaState { get; set; } = "walk"; // Control code for low stamina state
        public string ModerateStaminaState { get; set; } = "walk"; // Control code for moderate stamina state
        public string HighStaminaState { get; set; } = "gallop"; // Control code for high stamina state
    }

    public class EntityBehaviorJauntRideable : EntityBehaviorRideable
    {
        #region Properties
        
        #region Public
        
        public static JauntModSystem ModSystem => JauntModSystem.Instance;
        public List<GaitState> AvailableGaits = new();
        public GaitState CurrentGait
        {
            get => (GaitState)entity.WatchedAttributes.GetInt("currentgait", (int)GaitState.Walk);
            set
            {
                entity.WatchedAttributes.SetInt("currentgait", (int)value);
                entity.WatchedAttributes.MarkPathDirty(AttributeKey);
            }
        }

        #endregion Public

        #region Internal

        internal int minGeneration = 0; // Minimum generation for the animal to be rideable
        internal bool prevForwardKey, prevBackwardKey, prevSprintKey;
        internal bool forward, backward;
        internal float notOnGroundAccum;
        internal string prevSoundCode;
        internal bool shouldMove = false;
        internal string curTurnAnim = null;
        internal string curSoundCode = null;
        internal ControlMeta curControlMeta = null;
        internal EnumControlScheme scheme;
        
        #endregion Internal

        #region Protected
        
        protected long lastGaitChangeMs = 0;
        protected bool lastSprintPressed = false;
        protected float timeSinceLastLog = 0;
        protected float timeSinceLastGaitCheck = 0;
        protected float timeSinceLastGaitFatigue = 0;
        protected new JauntRideableConfig rideableconfig;
        protected GaitState lowStaminaState;
        protected GaitState moderateStaminaState;
        protected GaitState highStaminaState;
        protected ILoadedSound gaitSound;
        protected EntityBehaviorJauntStamina ebs;
        protected static bool DebugMode => ModSystem.DebugMode; // Debug mode for logging
        protected static string AttributeKey => $"{ModSystem.ModId}:rideable";
        protected static readonly List<GaitState> DefaultGaitOrder = new()
        {
            GaitState.Walkback,
            GaitState.Idle,
            GaitState.Walk,
            GaitState.Trot,
            GaitState.Canter,
            GaitState.Gallop
        };

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

            if (DebugMode) ModSystem.Logger.Notification(Lang.Get("jaunt:debug-rideable-init", entity.EntityId));

            base.Initialize(properties, attributes);

            rideableconfig = attributes.AsObject<JauntRideableConfig>();
            minGeneration = rideableconfig.MinGeneration;

            lowStaminaState = Enum.TryParse<GaitState>(rideableconfig.LowStaminaState, out var lowStamina) ? lowStamina : GaitState.Walk;
            moderateStaminaState = Enum.TryParse<GaitState>(rideableconfig.ModerateStaminaState, out var moderateStamina) ? moderateStamina : GaitState.Walk;
            highStaminaState = Enum.TryParse<GaitState>(rideableconfig.HighStaminaState, out var highStamina) ? highStamina : GaitState.Gallop;

            AvailableGaits.Clear();
            foreach (var gait in DefaultGaitOrder)
            {
                if (rideableconfig.Controls.ContainsKey(gait.ToString().ToLowerInvariant()))
                {
                    AvailableGaits.Add(gait);
                }
            }

            foreach (var val in rideableconfig.Controls.Values) { val.RiderAnim?.Init(); }

            curAnim = rideableconfig.Controls["idle"].RiderAnim;

            capi?.Event.RegisterRenderer(this, EnumRenderStage.Before, "rideablesim");

            
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            ebs = eagent.GetBehavior<EntityBehaviorJauntStamina>();
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

        private bool TaskManager_OnShouldExecuteTask(IAiTask task)
        {
            if (task is AiTaskWander && api.World.Calendar.TotalHours - lastDismountTotalHours < 24) return false;

            return !Seats.Any(seat => seat.Passenger != null);
        }
        
        #endregion AI Task Management

        #region Inventory and Control Scheme Management
        
        private void Inventory_SlotModified(int obj)
        {
            UpdateControlScheme();
            CurrentGait = GaitState.Idle;
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
                bool canride = true;
                bool canturn = true;

                var canRideField = typeof(EntityBehaviorRideable).GetField("CanRide", BindingFlags.NonPublic | BindingFlags.Instance);
                CanRideDelegate canRideEvent = (CanRideDelegate)canRideField.GetValue(this);
                if (canRideEvent != null && (controls.Jump || controls.TriesToMove))
                {
                    foreach (CanRideDelegate dele in canRideEvent.GetInvocationList().Cast<CanRideDelegate>())
                    {
                        if (!dele(seat, out string errMsg))
                        {
                            if (capi != null && seat.Passenger == capi.World.Player.Entity)
                            {
                                capi.TriggerIngameError(this, "cantride", Lang.Get("cantride-" + errMsg));
                            }
                            canride = false;
                            break;
                        }
                    }
                }

                var canTurnField = typeof(EntityBehaviorRideable).GetField("CanTurn", BindingFlags.NonPublic | BindingFlags.Instance);
                CanRideDelegate canTurnEvent = (CanRideDelegate)canTurnField.GetValue(this);
                if (canTurnEvent != null && (controls.Left || controls.Right))
                {
                    foreach (CanRideDelegate dele in canTurnEvent.GetInvocationList())
                    {
                        if (!dele(seat, out string errMsg))
                        {
                            if (capi != null && seat.Passenger == capi.World.Player.Entity)
                            {
                                capi.TriggerIngameError(this, "cantride", Lang.Get("cantride-" + errMsg));
                            }
                            canturn = false;
                            break;
                        }
                    }
                }

                if (!canride) continue;
                #endregion

                // Only able to jump every 1500ms. Only works while on the ground.
                if (controls.Jump && entity.World.ElapsedMilliseconds - lastJumpMs > 1500 && entity.Alive && (entity.OnGround || coyoteTimer > 0))
                {
                    lastJumpMs = entity.World.ElapsedMilliseconds;
                    jumpNow = true;
                }

                if (scheme == EnumControlScheme.Hold && !controls.TriesToMove)
                {
                    continue;
                }

                float str = ++seatsRowing == 1 ? 1 : 0.5f;

                bool nowForwards = controls.Forward;
                bool nowBackwards = controls.Backward;

                // Handle gait switching via sprint button
                bool nowSprint = controls.CtrlKey;

                // Detect fresh button presses
                bool forwardPressed = nowForwards && !prevForwardKey;
                bool backwardPressed = nowBackwards && !prevBackwardKey;
                bool sprintPressed = nowSprint && !prevSprintKey;

                long nowMs = entity.World.ElapsedMilliseconds;

                #region Common controls across both schemes
                // This ensures we start moving without having to press sprint
                if (forwardPressed && CurrentGait == GaitState.Idle) CurrentGait = GaitState.Walk;

                if (forward && sprintPressed && nowMs - lastGaitChangeMs > 300)
                {
                    CurrentGait = GetNextGait(CurrentGait, true);

                    lastGaitChangeMs = nowMs;
                }

                if (backwardPressed && nowMs - lastGaitChangeMs > 300)
                {
                    CurrentGait = GetNextGait(CurrentGait, false);

                    lastGaitChangeMs = nowMs;
                }

                prevSprintKey = nowSprint;
                #endregion

                if (scheme == EnumControlScheme.Hold)
                {
                    #region Snaffle bit controls (Hold scheme)
                    forward = controls.Forward;
                    backward = controls.Backward;
                    #endregion
                }
                else
                {
                    #region Curb bit controls (Press scheme)                    
                    // Handle backward to idle change without sprint key
                    if (forwardPressed && CurrentGait == GaitState.Walkback) CurrentGait = GaitState.Idle;

                    switch (CurrentGait)
                    {
                        case GaitState.Walkback:
                            backward = true;
                            forward = false;
                            break;
                        case GaitState.Idle:
                            backward = false;
                            forward = false;
                            break;
                        default:
                            backward = false;
                            forward = true;
                            break;
                    }

                    prevForwardKey = nowForwards;
                    prevBackwardKey = nowBackwards;
                    #endregion
                }

                #region Motion update
                if (canturn && (controls.Left || controls.Right))
                {
                    float dir = controls.Left ? 1 : -1;
                    angularMotion += str * dir * dt;
                }
                if (forward || backward)
                {
                    float dir = forward ? 1 : -1;
                    linearMotion += str * dir * dt * 2f;
                }
                #endregion
            }

            return new Vec2d(linearMotion, angularMotion);
        }

        protected GaitState GetNextGait(GaitState currentGait, bool forward)
        {
            if (AvailableGaits == null || AvailableGaits.Count == 0)
                return GaitState.Idle;

            int currentIndex = AvailableGaits.IndexOf(currentGait);
            if (currentIndex < 0) return GaitState.Idle;

            int nextIndex = forward ? currentIndex + 1 : currentIndex - 1;

            // Boundary behavior
            if (nextIndex < 0) nextIndex = 0;
            if (nextIndex >= AvailableGaits.Count) nextIndex = currentIndex - 1;

            return AvailableGaits[nextIndex];
        }

        protected virtual void UpdateAngleAndMotion(float dt)
        {
            // Ignore lag spikes
            dt = Math.Min(0.5f, dt);

            float step = GlobalConstants.PhysicsFrameTime;
            var motion = SeatsToMotion(step);

            if (jumpNow) UpdateRidingState();

            ForwardSpeed = Math.Sign(motion.X);

            float yawMultiplier = CurrentGait switch
            {
                GaitState.Walkback => rideableconfig.Controls["walkback"].TurnRadius,
                GaitState.Idle => rideableconfig.Controls["idle"].TurnRadius,
                GaitState.Walk => rideableconfig.Controls["walk"].TurnRadius,
                GaitState.Trot => rideableconfig.Controls["trot"].TurnRadius,
                GaitState.Canter => rideableconfig.Controls["canter"].TurnRadius,
                GaitState.Gallop => rideableconfig.Controls["gallop"].TurnRadius,
                _ => 3.5f
            };

            AngularVelocity = motion.Y * yawMultiplier;

            entity.SidedPos.Yaw += (float)motion.Y * dt * 30f;
            entity.SidedPos.Yaw = entity.SidedPos.Yaw % GameMath.TWOPI;

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
                var meta = rideableconfig.Controls["jump"];
                foreach (var seat in Seats) seat.Passenger?.AnimManager?.StopAnimation(meta.RiderAnim.Animation);
                eagent.AnimManager.StopAnimation(meta.Animation);
            }

            eagent.Controls.Backward = ForwardSpeed < 0;
            eagent.Controls.Forward = ForwardSpeed >= 0;
            eagent.Controls.Sprint = CurrentGait == highStaminaState && ForwardSpeed > 0;

            string nowTurnAnim = null;
            if (ForwardSpeed >= 0)
            {
                if (AngularVelocity > 0.001) nowTurnAnim = "turn-left";
                else if (AngularVelocity < -0.001) nowTurnAnim = "turn-right";
            }
            if (nowTurnAnim != curTurnAnim)
            {
                if (curTurnAnim != null) eagent.StopAnimation(curTurnAnim);
                eagent.StartAnimation((ForwardSpeed == 0 ? "idle-" : "") + (curTurnAnim = nowTurnAnim));
            }

            ControlMeta nowControlMeta;

            shouldMove = ForwardSpeed != 0;
            if (!shouldMove && !jumpNow)
            {
                if (curControlMeta != null) Stop();
                curAnim = rideableconfig.Controls[eagent.Swimming ? "swim" : "idle"].RiderAnim;
                nowControlMeta = eagent.Swimming ? rideableconfig.Controls["swim"] : null;
            }
            else
            {
                string controlCode = eagent.Controls.Backward ? "walkback" : "walk";

                switch (CurrentGait)
                {
                    case GaitState.Idle:
                        controlCode = "idle";
                        break;
                    case GaitState.Walk:
                        controlCode = eagent.Controls.Backward ? "walkback" : "walk";
                        break;
                    case GaitState.Trot:
                        controlCode = "trot";
                        break;
                    case GaitState.Canter:
                        controlCode = "canter";
                        break;
                    case GaitState.Gallop:
                        controlCode = "gallop";
                        break;
                }

                if (eagent.Swimming) controlCode = "swim";

                nowControlMeta = rideableconfig.Controls[controlCode];
                eagent.Controls.Jump = jumpNow;

                if (jumpNow)
                {
                    IsInMidJump = true;
                    jumpNow = false;
                    if (eagent.Properties.Client.Renderer is EntityShapeRenderer esr)
                        esr.LastJumpMs = capi.InWorldEllapsedMilliseconds;

                    nowControlMeta = rideableconfig.Controls["jump"];
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
                if (nowControlMeta != null) eagent.AnimManager.StartAnimation(nowControlMeta);
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

            if (rideableconfig.Controls.ContainsKey(CurrentGait.ToString().ToLowerInvariant()))
            {
                var controlMeta = rideableconfig.Controls[CurrentGait.ToString().ToLowerInvariant()];

                if (entity.Swimming)
                {
                    controlMeta = rideableconfig.Controls["swim"];
                }
                else if (notOnGroundAccum > 0.2f)
                {
                    controlMeta = rideableconfig.Controls["jump"];
                }

                curSoundCode = controlMeta.Sound;

                bool nowChange = curSoundCode != prevSoundCode;

                if (nowChange)
                {
                    gaitSound?.Stop();
                    prevSoundCode = curSoundCode;

                    if (curSoundCode is null) return;

                    gaitSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = controlMeta.Sound.Clone().WithPathPrefix("sounds/"),
                        DisposeOnFinish = false,
                        Position = entity.Pos.XYZ.ToVec3f(),
                        ShouldLoop = true
                    });

                    gaitSound?.Start();
                    if (DebugMode) ModSystem.Logger.Notification($"Now playing sound: {controlMeta.Sound}");
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

                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = 0.05f;
                }

                eagent.Pos.Motion.Y += (swimlineSubmergedness - 0.1) / 300.0;
            }
        }

        public new void Stop()
        {
            CurrentGait = GaitState.Idle;
            eagent.Controls.StopAllMovement();
            eagent.Controls.WalkVector.Set(0, 0, 0);
            eagent.Controls.FlyVector.Set(0, 0, 0);
            shouldMove = false;
            if (curControlMeta != null && curControlMeta.Animation != "jump")
            {
                eagent.StopAnimation(curControlMeta.Animation);
            }
            curControlMeta = null;
            eagent.StartAnimation("idle");
        }
        
        #endregion Motion Systems

        #region Utility Methods

        public new void DidUnnmount(EntityAgent entityAgent)
        {
            Stop();

            lastDismountTotalHours = entity.World.Calendar.TotalHours;
            foreach (var meta in rideableconfig.Controls.Values)
            {
                if (meta.RiderAnim?.Animation != null)
                {
                    entityAgent.StopAnimation(meta.RiderAnim.Animation);
                }
            }

            if (eagent.Swimming)
            {
                eagent.StartAnimation("swim");
            }
        }

        public new void DidMount(EntityAgent entityAgent)
        {
            UpdateControlScheme();
            CurrentGait = GaitState.Idle;
        }

        public void StaminaGaitCheck(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null) return;

            timeSinceLastGaitCheck += dt;

            // Check once a second
            if (timeSinceLastGaitCheck >= 1f)
            {
                if (CurrentGait == highStaminaState && !eagent.Swimming)
                {
                    bool isTired = api.World.Rand.NextDouble() < GetStaminaDeficitMultiplier(ebs.Stamina, ebs.MaxStamina);

                    if (isTired)
                    {
                        CurrentGait = ebs.Stamina < 10 ? lowStaminaState : moderateStaminaState;
                    }
                    else
                    {
                        CurrentGait = highStaminaState;
                    }
                }

                timeSinceLastGaitCheck = 0;
            }
        }

        public void ApplyGaitFatigue(float dt)
        {
            if (api.Side != EnumAppSide.Server || ebs == null) return;

            timeSinceLastGaitFatigue += dt;

            if (timeSinceLastGaitFatigue >= 0.25f)
            {
                JauntControlMeta nowControlmeta = rideableconfig.Controls[CurrentGait.ToString().ToLowerInvariant()];                
                if (nowControlmeta.StaminaCost > 0 && !eagent.Swimming)
                {
                    ebs.FatigueEntity(nowControlmeta.StaminaCost, new()
                    {
                        Source = EnumFatigueSource.Mounted,
                        SourceEntity = eagent.MountedOn?.Passenger ?? eagent
                    });
                }
            }
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
        
        public override void OnGameTick(float dt)
        {
            timeSinceLastLog += dt;

            if (api.Side == EnumAppSide.Server)
            {
                UpdateAngleAndMotion(dt);
            }

            ApplyGaitFatigue(dt);

            StaminaGaitCheck(dt);

            UpdateRidingState();

            if (!AnyMounted() && eagent.Controls.TriesToMove && eagent?.MountedOn != null)
            {
                eagent.TryUnmount();
            }

            if (shouldMove)
            {
                Move(dt, eagent.Controls, curControlMeta.MoveSpeed);
            }
            else
            {
                if (entity.Swimming) eagent.Controls.FlyVector.Y = 0.2;
            }

            UpdateSoundState(dt);
        }

        #endregion Listeners
    }
}
