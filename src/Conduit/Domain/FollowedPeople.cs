namespace Conduit.Domain;

/// <summary>
/// Follow edge: Observer follows Target.
/// </summary>
public class FollowedPeople
{
    /// <summary>The follower.</summary>
    public int ObserverId { get; init; }

    /// <summary>The follower.</summary>
    public Person? Observer { get; init; }

    /// <summary>The person being followed.</summary>
    public int TargetId { get; init; }

    /// <summary>The person being followed.</summary>
    public Person? Target { get; init; }
}
