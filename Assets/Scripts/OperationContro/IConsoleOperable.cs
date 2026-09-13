/// <summary>
/// 操作模式下可被点击操作的物体。玩家在操作台视角左键点中它时，OperationModeController 调 Operate()。
/// </summary>
public interface IConsoleOperable
{
    void Operate();
}