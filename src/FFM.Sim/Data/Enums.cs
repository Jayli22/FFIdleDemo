namespace FFM.Sim.Data
{
    public enum Role : byte
    {
        Rusher = 0,
        SecondRusher = 1,
        Sniper = 2,
        Support = 3,
        Grenadier = 4
    }

    public enum Phase : byte
    {
        Drop = 0,
        Loot = 1,
        Rotate = 2,
        MidGame = 3,
        EndGame = 4,
        Finished = 5
    }

    public enum ZoneState : byte
    {
        Idle = 0,
        Waiting = 1,
        Shrinking = 2
    }

    public enum UnitState : byte
    {
        Alive = 0,
        Knocked = 1,
        Eliminated = 2
    }

    public enum UnitDropState : byte
    {
        Grounded = 0,
        InPlane = 1,
        Freefall = 2,
        Gliding = 3
    }

    public enum Behavior : byte
    {
        Follow = 0,
        TakeCover = 1,
        Fire = 2,
        Reload = 3,
        Heal = 4,
        Revive = 5,
        PlaceGloo = 6,
        Throw = 7,
        Mount = 8,
        Loot = 9,
        Rotate = 10
    }

    public enum SquadMode : byte
    {
        Contest = 0,
        Push = 1,
        Hold = 2,
        Disengage = 3,
        SecurePlacement = 4
    }

    public enum DropStyle : byte
    {
        Hot = 0,
        Cold = 1,
        Flex = 2
    }

    public enum RotationStyle : byte
    {
        Early = 0,
        Edge = 1,
        Late = 2
    }

    public enum EngageStyle : byte
    {
        Contest = 0,
        Balanced = 1,
        Avoid = 2
    }

    public enum EndgameStyle : byte
    {
        HighGround = 0,
        Building = 1,
        GlooFort = 2
    }

    public enum CommandType : byte
    {
        RotateNow = 0,
        RotateLate = 1,
        SetEngage = 2,
        TargetPriority = 3,
        SecurePlacement = 4,
        EndgameFormation = 5,
        HoldPosition = 6,
        ContestTarget = 7,
        PopSkill = 8,
        AvoidFight = 9
    }

    public enum TargetPriorityMode : byte
    {
        Weakest = 0,
        AvoidStrong = 1
    }

    public enum SkillCategory : byte
    {
        Shield = 0,
        Dash = 1,
        Heal = 2,
        Scan = 3,
        Deploy = 4,
        Passive = 5
    }

    public enum WeaponCategory : byte
    {
        AR = 0,
        SMG = 1,
        Shotgun = 2,
        Sniper = 3,
        DMR = 4
    }
}
