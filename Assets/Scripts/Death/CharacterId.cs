using UnityEngine;

/// <summary>
/// Character type. player and robot each track their own respawn point, distinguished by this enum.
/// </summary>
public enum CharacterType
{
    Player,
    Robot
}

/// <summary>
/// Identity marker component. Attach to the root of player / robot.
/// Checkpoints and death zones use it to identify which character entered.
/// </summary>
public class CharacterId : MonoBehaviour
{
    public CharacterType characterType;
}