﻿using Vintagestory.API.Common;

namespace Jaunt.Entities;

public class EntityFlyingAgent : EntityAgent
{
    public override bool CanStepPitch => false;
    public override bool CanSwivel => true;
    public override bool CanSwivelNow => true;
}