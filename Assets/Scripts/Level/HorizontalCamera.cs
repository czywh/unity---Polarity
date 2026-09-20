using UnityEngine;

/// <summary>
/// 2.5D follow camera: smoothly follows a target; hold right mouse to tilt pitch up/down.
/// Only handles "how to follow", not who is being controlled -- switching focus is done by CharacterSwitcher calling SetTarget.
/// Attach to the Main Camera.
/// </summary>
public class HorizontalCamera : MonoBehaviour
{
    [Header("Basic Follow Settings")]
    public Transform target;                            // Follow target (usually an empty focus object on the character)
    public float smoothSpeed = 5f;                      // Position / look-at follow smoothing (also used to glide over when switching)
    public Vector3 baseOffset = new Vector3(0, 0, -5f); // Base offset (x for side shift; z is overridden by distance below)

    [Header("Mouse Pitch Control")]
    public bool mouseTiltEnabled = true;    // Enable mouse pitch (hold right mouse)
    public float mouseSensitivity = 1f;     // Mouse sensitivity
    public float minPitchAngle = -15f;      // Minimum pitch angle (down)
    public float maxPitchAngle = 45f;       // Maximum pitch angle (up)
    public float rotationSmoothness = 5f;   // Pitch transition smoothing

    [Header("Distance Settings")]
    public float followDistance = 5f;       // Fixed follow distance
    public float heightOffset = 2f;         // Camera height offset

    [Header("Bounds")]
    public bool useBounds = false;
    public float minX = -10f, maxX = 10f;
    public float minY = -5f, maxY = 5f;
    public float minZ = -20f, maxZ = 20f;

    private float currentPitchAngle = 0f;
    private Vector3 currentOffset;
    private Vector3 currentLookPoint;

    void Start()
    {
        if (target == null) return;
        currentPitchAngle = 0f;
        currentOffset = CalculateOffset(currentPitchAngle);
        currentLookPoint = GetLookPoint();
    }

    void LateUpdate()
    {
        if (target == null) return;
        HandleMouseInput();
        UpdateCameraPosition();
    }

    void HandleMouseInput()
    {
        if (!mouseTiltEnabled) return;

        // Hold right mouse and move to control pitch (left mouse is reserved for robot missile aiming, no conflict)
        if (Input.GetMouseButton(1))
        {
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            currentPitchAngle = Mathf.Clamp(currentPitchAngle + mouseY, minPitchAngle, maxPitchAngle);
        }
    }

    Vector3 CalculateOffset(float pitchAngle)
    {
        float verticalOffset = Mathf.Sin(pitchAngle * Mathf.Deg2Rad) * followDistance;
        float horizontalOffset = Mathf.Cos(pitchAngle * Mathf.Deg2Rad) * followDistance;
        return new Vector3(baseOffset.x, heightOffset + verticalOffset, -horizontalOffset);
    }

    Vector3 GetLookPoint()
    {
        return target.position + Vector3.up * heightOffset * 0.5f;
    }

    void UpdateCameraPosition()
    {
        Vector3 targetOffset = CalculateOffset(currentPitchAngle);
        currentOffset = Vector3.Lerp(currentOffset, targetOffset, rotationSmoothness * Time.deltaTime);

        Vector3 targetPosition = target.position + currentOffset;

        if (useBounds)
        {
            targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
            targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);
        }

        // Smooth position follow: when the target switches, this step is what glides the camera from the old character to the new one
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);

        // Smooth the look-at point too, to avoid a hard camera snap at the moment of switching
        currentLookPoint = Vector3.Lerp(currentLookPoint, GetLookPoint(), smoothSpeed * Time.deltaTime);
        transform.LookAt(currentLookPoint);
    }

    /// <summary>
    /// Switch follow target. instant=true snaps immediately (for initialization);
    /// instant=false keeps the smooth transition (camera glides to the new target on character switch).
    /// </summary>
    public void SetTarget(Transform newTarget, bool instant = false)
    {
        target = newTarget;
        if (target == null) return;

        if (instant)
        {
            currentOffset = CalculateOffset(currentPitchAngle);
            transform.position = target.position + currentOffset;
            currentLookPoint = GetLookPoint();
            transform.LookAt(currentLookPoint);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        Vector3 size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
        Gizmos.DrawWireCube(center, size);
    }
}