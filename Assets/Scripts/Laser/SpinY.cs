using UnityEngine;

/// <summary>
/// 装饰用自转：绕指定轴（默认 Y）缓慢自转。挂在转向器或它的装饰网格上均可，
/// 因为 LaserRedirector 用"初始朝向"算激光方向，所以本体自转不影响激光方向。
/// </summary>
public class SpinY : MonoBehaviour
{
    [Tooltip("每秒自转角度（度）")]
    public float degreesPerSecond = 30f;
    [Tooltip("自转轴")]
    public Vector3 axis = Vector3.up;
    [Tooltip("本地轴 / 世界轴")]
    public Space space = Space.Self;

    private void Update()
    {
        transform.Rotate(axis.normalized, degreesPerSecond * Time.deltaTime, space);
    }
}