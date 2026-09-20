using UnityEngine;

// Robot ability interfaces: implementations pending later modules (electric field / missiles).
// RobotController / RobotAimController call through these interfaces; skipped automatically when the implementation is null.

/// <summary>ROBOT-04  Toggle instant electric field</summary>
public interface IFieldEmitter
{
    void ToggleField();
}

/// <summary>ROBOT-05  Fire missile</summary>
public interface IMissileLauncher
{
    void TryFire();                    // straight ahead
    void TryFire(Vector3 direction);   // in a given direction (crosshair direction)
}

/// <summary>ROBOT-07  G key recalls missile fields near the mouse</summary>
public interface IMissileRecaller
{
    void TryRecall();
}