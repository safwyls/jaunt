using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Jaunt.Systems
{
    public enum EnumFatigueSource
    {
        Run,
        Jump,
        Swim,
        Mental,
        Attack,
        Defense,
        Weather
    }

    public class FatigueSource
    {
        /// <summary>
        /// The type of source the fatigue came from.
        /// </summary>
        public EnumFatigueSource Source;

        /// <summary>
        /// The source entity the fatigue came from, if any
        /// </summary>
        public Entity SourceEntity;

        /// <summary>
        /// The entity that caused this fatigue, e.g. the entity that threw the SourceEntity projectile, if any
        /// </summary>
        public Entity CauseEntity;

        /// <summary>
        /// The source block the fatigue came from, if any
        /// </summary>
        public Block SourceBlock;

        /// <summary>
        /// the location of the fatigue source.
        /// </summary>
        private Vec3d _sourcePos;
        public Vec3d SourcePos
        {
            get
            {
                return _sourcePos ?? GetSourcePosition();
            }
            set
            {
                _sourcePos = value;
            }

        }

        /// <summary>
        /// Fetches the location of the fatigue source from either SourcePos or SourceEntity
        /// </summary>
        /// <returns></returns>
        public Vec3d GetSourcePosition()
        {
            if (SourceEntity is not null)
                return SourceEntity.SidedPos.XYZ;
            else
                return _sourcePos ?? Vec3d.Zero;
        }

        /// <summary>
        /// Get the entity that caused the fatigue.
        /// If a projectile like a stone was thrown this will return the entity that threw the stone instead of the stone.
        /// </summary>
        /// <returns>The entity that caused the fatigue</returns>
        public Entity GetCauseEntity()
        {
            return CauseEntity ?? SourceEntity;
        }
    }
}