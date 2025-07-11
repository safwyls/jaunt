using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Jaunt.Behaviors;

public class EntityBehaviorJauntPhysics : EntityBehaviorControlledPhysics, IRenderer
{
    public EntityAgent eagent;
    protected static JauntModSystem ModSystem => JauntModSystem.Instance;
    private float accum = 0;
    private int currentTick;
    private const float interval = 1 / 60f;
    private WireframeCube entityWf;
    private Cuboidd entitySensorBox;
    private List<Cuboidd> steppableBoxes;
    private double? targetStepY = null;
    
    protected internal static Jaunt.Systems.CachingCollisionTester collisionTester;

    public double RenderOrder => 1;
    public int RenderRange => 9999;
    
    public EntityBehaviorJauntPhysics(Entity entity) : base(entity)
    {
    }
    
    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        
        collisionTester = new Jaunt.Systems.CachingCollisionTester();

        if (entity.Api is ICoreClientAPI capi)
        {
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "jaunt:physicswf");
            entityWf = WireframeCube.CreateCenterOriginCube(capi, ColorUtil.WhiteArgb);
        }
    }
    
    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        Dispose();
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (entitySensorBox is not null)
        {
            Cuboidf sensorBoxFloat = entitySensorBox.ToFloat();
        
            float colScaleX = sensorBoxFloat.XSize / 2;
            float colScaleY = sensorBoxFloat.YSize / 2;
            float colScaleZ = sensorBoxFloat.ZSize / 2;

            var x = sensorBoxFloat.X1 + colScaleX;
            var y = sensorBoxFloat.Y1 + colScaleY;
            var z = sensorBoxFloat.Z1 + colScaleZ;
        
            entityWf.Render(capi, x, y, z, colScaleX, colScaleY, colScaleZ, 1, new Vec4f(1, 1, 0, 1));
        }

        if (steppableBoxes is not null && steppableBoxes.Count > 0)
        {
            foreach (var box in steppableBoxes)
            {
                Cuboidf boxF = box.ToFloat();
        
                float colScaleX = boxF.XSize / 2;
                float colScaleY = boxF.YSize / 2;
                float colScaleZ = boxF.ZSize / 2;

                var x = boxF.X1 + colScaleX;
                var y = boxF.Y1 + colScaleY;
                var z = boxF.Z1 + colScaleZ;
        
                entityWf.Render(capi, x, y, z, colScaleX, colScaleY, colScaleZ, 1, new Vec4f(0, 1, 1, 1));
            }   
        }
    }

    protected override bool HandleSteppingOnBlocks(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
    {
        if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming) || entity.Properties.Habitat == EnumHabitat.Underwater) return false;

        Cuboidd entityCollisionBox = entity.CollisionBox.ToDouble();

        double forwardExtent = entity.CollisionBox.ZSize + (controls.Sprint ? 0.5 : controls.Sneak ? 0.05 : 0.2);

        Vec2d center = new((entityCollisionBox.X1 + entityCollisionBox.X2) / 2, (entityCollisionBox.Z1 + entityCollisionBox.Z2) / 2);
        double searchHeight = Math.Max(entityCollisionBox.Y1 + StepHeight, entityCollisionBox.Y2);
        entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);

        Vec3d walkVec = controls.WalkVector.Clone();
        Vec3d walkVecNormalized = walkVec.Clone().Normalize();
        double outerX = walkVecNormalized.X * forwardExtent;
        double outerZ = walkVecNormalized.Z * forwardExtent;
        
        //ModSystem.Logger.Debug($"outerX: {outerX}, outerZ: {outerZ}");
        entitySensorBox = new Cuboidd
        {
            X1 = Math.Min(0.1, outerX),
            X2 = Math.Max(0.1, outerX),

            Z1 = Math.Min(0.1, outerZ),
            Z2 = Math.Max(0.1, outerZ),

            Y1 = entity.CollisionBox.Y1 + 0.01 - (!entity.CollidedVertically && !controls.Jump ? 0.05 : 0),

            Y2 = searchHeight
        };

        entitySensorBox.Translate(center.X, 0, center.Y);
        entitySensorBox.Translate(pos.X, pos.Y, pos.Z);
        
        Vec3d testVec = new();
        Vec2d testMotion = new();

        steppableBoxes = FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBox, moveDelta.Y, walkVec);
        if (steppableBoxes != null && steppableBoxes.Count > 0)
        {
            // Try stepping up in the current vector direction
            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, walkVec.Z), dtFac, steppableBoxes, entityCollisionBox)) return true;

            // If previous step failed, try stepping in the X direction
            Cuboidd entitySensorBoxXAligned = entitySensorBox.Clone();
            if (entitySensorBoxXAligned.Z1 == pos.Z + center.Y)
            {
                entitySensorBoxXAligned.Z2 = entitySensorBoxXAligned.Z1;
            }
            else
            {
                entitySensorBoxXAligned.Z1 = entitySensorBoxXAligned.Z2;
            }
            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, 0), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxXAligned, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox)) return true;
            
            // If previous step failed, try stepping in the Z direction
            Cuboidd entitySensorBoxZAligned = entitySensorBox.Clone();
            if (entitySensorBoxZAligned.X1 == pos.X + center.X)
            {
                entitySensorBoxZAligned.X2 = entitySensorBoxZAligned.X1;
            }
            else
            {
                entitySensorBoxZAligned.X1 = entitySensorBoxZAligned.X2;
            }
            
            if (TryStepSmooth(controls, pos, testMotion.Set(0, walkVec.Z), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxZAligned, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)) return true;
        }

        return false;
    }

    public bool TryStepSmooth(EntityControls controls, EntityPos pos, Vec2d walkVec, float dtFac, List<Cuboidd> steppableBoxes, Cuboidd entityCollisionBox)
    {
        if (steppableBoxes == null || steppableBoxes.Count == 0) return false;
        double gravityOffset = 0.3;

        Vec2d walkVecOrtho = new Vec2d(walkVec.Y, -walkVec.X).Normalize();
        double halfWidth = entity.CollisionBox.XSize * 0.5 + 0.01;
        double halfDepth = entity.CollisionBox.ZSize * 0.5 + 0.01;
        
        double maxX = Math.Abs(walkVecOrtho.X * halfWidth) + 0.001;
        double maxZ = Math.Abs(walkVecOrtho.Y * halfDepth) + 0.001;
        double minX = -maxX;
        double minZ = -maxZ;
        Cuboidf col = new((float)minX, entity.CollisionBox.Y1, (float)minZ, (float)maxX, entity.CollisionBox.Y2, (float)maxZ);

        double newYPos = pos.Y;
        bool foundStep = false;
        double heightDiff = 0;
        foreach (Cuboidd steppableBox in steppableBoxes)
        {
            heightDiff = steppableBox.Y2 - entityCollisionBox.Y1 + gravityOffset;
            Vec3d stepPos = new(GameMath.Clamp(newPos.X, steppableBox.MinX, steppableBox.MaxX), newPos.Y + heightDiff + pos.DimensionYAdjustment, GameMath.Clamp(newPos.Z, steppableBox.MinZ, steppableBox.MaxZ));

            bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, col, stepPos, false);

            if (canStep)
            {
                double elevateFactor = controls.Sprint ? 0.10 : controls.Sneak ? 0.025 : 0.05;
                if (!steppableBox.IntersectsOrTouches(entityCollisionBox))
                {
                    newYPos = Math.Max(newYPos, Math.Min(pos.Y + (elevateFactor * dtFac), steppableBox.Y2 - entity.CollisionBox.Y1 + gravityOffset));
                }
                else
                {
                    newYPos = pos.Y + (elevateFactor * dtFac);
                }
                foundStep = true;
            }
        }
        if (foundStep)
        {
            pos.Y = newYPos;
            collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref newPos, StepHeight);
            
        }
        return foundStep;
    }

   
    public void Dispose()
    {
        entityWf?.Dispose();
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}