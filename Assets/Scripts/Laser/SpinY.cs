using UnityEngine;

/// <summary>
/// Decorative spin: slowly rotates around a given axis (Y by default). Can go on the redirector or its decorative mesh,
/// since LaserRedirector computes the laser direction from its "initial facing", so spinning does not affect the laser direction.
/// </summary>
public class SpinY : MonoBehaviour
{
    [Tooltip("Spin angle per second (degrees)")]
    public float degreesPerSecond = 30f;
    [Tooltip("Spin axis")]
    public Vector3 axis = Vector3.up;
    [Tooltip("Local axis / world axis")]
    public Space space = Space.Self;

    private void Update()
    {
        transform.Rotate(axis.normalized, degreesPerSecond * Time.deltaTime, space);
    }
}