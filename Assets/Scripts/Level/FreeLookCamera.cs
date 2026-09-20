using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreeLookCamera : MonoBehaviour
{
    [Header("Follow Target")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // Target position offset

    [Header("Camera Distance")]
    [Range(1f, 20f)] public float distance = 5.0f;
    [Range(1f, 10f)] public float minDistance = 2.0f;
    [Range(5f, 20f)] public float maxDistance = 10.0f;
    public float zoomSpeed = 2.0f;

    [Header("Rotation Control")]
    [Range(50f, 500f)] public float xSpeed = 120.0f;
    [Range(50f, 500f)] public float ySpeed = 120.0f;
    [Range(-90f, 0f)] public float yMinLimit = -20f;
    [Range(0f, 90f)] public float yMaxLimit = 80f;
    [Range(0f, 0.5f)] public float rotationSmoothTime = 0.12f;

    [Header("Advanced Settings")]
    public bool autoRotate = false;
    [Range(0f, 2f)] public float autoRotateSpeed = 0.5f;
    public bool invertY = false;
    public bool invertX = false;

    private Quaternion currentRotation;
    private Quaternion desiredRotation;
    private float xDeg;
    private float yDeg;
    private float currentDistance;
    private float desiredDistance;
    private float velDistance;
    private Vector3 position;
    private bool firstPerson;

    void Start()
    {
        if (!target)
        {
            GameObject go = new GameObject("Cam Target");
            go.transform.position = transform.position + (transform.forward * distance);
            target = go.transform;
        }

        currentDistance = distance;
        desiredDistance = distance;

        // Initialize current rotation
        xDeg = Vector3.Angle(Vector3.right, transform.right);
        yDeg = Vector3.Angle(Vector3.up, transform.up);
        currentRotation = transform.rotation;
        desiredRotation = transform.rotation;
    }

    void LateUpdate()
    {
        //target = GameObject.FindGameObjectWithTag("Target").transform;
        if (!target) return;

        HandleInput();
        CalculateDesiredRotation();
        UpdateCameraPosition();
    }

    void HandleInput()
    {
        // Mouse wheel zoom
        desiredDistance = Mathf.Clamp(desiredDistance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed, minDistance, maxDistance);
    }

    void CalculateDesiredRotation()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // Read mouse input
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (Mathf.Abs(mouseX) > 0.01f || Mathf.Abs(mouseY) > 0.01f)
        {
            // Apply rotation direction settings
            xDeg += mouseX * xSpeed * 0.02f * (invertX ? -1 : 1);
            yDeg -= mouseY * ySpeed * 0.02f * (invertY ? -1 : 1);

            // Clamp vertical angle
            yDeg = Mathf.Clamp(yDeg, yMinLimit, yMaxLimit);
        }
        else if (autoRotate)
        {
            xDeg += autoRotateSpeed * 0.02f;
        }

        // Use quaternions for rotation to avoid gimbal lock
        desiredRotation = Quaternion.Euler(yDeg, xDeg, 0);
        currentRotation = Quaternion.Slerp(currentRotation, desiredRotation, rotationSmoothTime * Time.timeScale);
    }

    void UpdateCameraPosition()
    {
        // Smoothly transition distance
        currentDistance = Mathf.SmoothDamp(currentDistance, desiredDistance, ref velDistance, rotationSmoothTime);

        // Compute target position (including offset)
        Vector3 targetPosition = target.position + targetOffset;

        // Compute camera position
        position = targetPosition - (currentRotation * Vector3.forward * currentDistance);

        // Check for obstacles
        CheckCameraOcclusion(targetPosition, ref position);

        transform.rotation = currentRotation;
        transform.position = position;
    }

    void CheckCameraOcclusion(Vector3 from, ref Vector3 to)
    {
        RaycastHit hit;
        if (Physics.Linecast(from, to, out hit))
        {
            // If there's an obstacle between camera and target, adjust camera position
            to = new Vector3(hit.point.x + hit.normal.x * 0.2f,
                            hit.point.y + hit.normal.y * 0.2f,
                            hit.point.z + hit.normal.z * 0.2f);
        }
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f)
            angle += 360f;
        if (angle > 360f)
            angle -= 360f;
        return Mathf.Clamp(angle, min, max);
    }
}
