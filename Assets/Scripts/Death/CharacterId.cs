using UnityEngine;

/// <summary>
/// 角色类型。player 与 robot 各自独立记录复活点，就靠这个枚举区分。
/// </summary>
public enum CharacterType
{
    Player,
    Robot
}

/// <summary>
/// 身份标记组件。挂在 player / robot 的根物体上。
/// 检查点、死亡区域都通过它来识别是哪个角色进来了。
/// </summary>
public class CharacterId : MonoBehaviour
{
    public CharacterType characterType;
}