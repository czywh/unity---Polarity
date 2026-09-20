/// <summary>
/// An object that can be clicked and operated in Operation Mode. When the player left-clicks it from the console view, OperationModeController calls Operate().
/// </summary>
public interface IConsoleOperable
{
    void Operate();
}