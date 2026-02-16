namespace StreetRiders
{
    public partial class StreetRidersScript
    {
        private enum Phase { Idle, Attempting, Sustained }
        private enum FollowTrigger { Proximity, Dynamic }
        private enum DismountBehavior { Wait, Continue, Freestyle }
        private enum GroupFollowMode { Autonomous, FollowingPlayer, WaitingForPlayer }
        private enum CruiseMode { Territory, GoAway, Mixed }
    }
}
