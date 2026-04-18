namespace StreetRiders
{
    public enum WheeliePhase
    {
        Idle,
        Attempting,
        Sustained
    }

    public enum FollowTrigger
    {
        Proximity,
        Dynamic
    }

    public enum DismountBehavior
    {
        Wait,
        Continue,
        Freestyle
    }

    public enum GroupFollowMode
    {
        Autonomous,
        FollowingPlayer,
        WaitingForPlayer
    }

    public enum CruiseMode
    {
        Territory,
        GoAway,
        Mixed
    }
}