namespace BunnyHopper.Models;

public class PlayerJumpState
{
    public bool WasGroundedBeforeMove { get; set; }
    public bool ModInitiatedCurrentJump { get; set; }
    public bool AwaitingLiftoffAfterAutoJump { get; set; }
}
