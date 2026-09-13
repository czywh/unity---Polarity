using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreeLookCamera : MonoBehaviour
{
    [Header("跟随目标")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // 目标位置偏移

    [Header("相机距离")]
    [Range(1f, 20f)] public float distance = 5.0f;
    [Range(1f, 10f)] public float minDistance = 2.0f;
    [Range(5f, 20f)] public float maxDistance = 10.0f;
    public float zoomSpeed = 2.0f;

    [Header("旋转控制")]
    [Range(50f, 500f)] public float xSpeed = 120.0f;
    [Range(50f, 500f)] public float ySpeed = 120.0f;
    [Range(-90f, 0f)] public float yMinLimit = -20f;
    [Range(0f, 90f)] public float yMaxLimit = 80f;
    [Range(0f, 0.5f)] public float rotationSmoothTime = 0.12f;

    [Header("高级设置")]
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

        // 初始化当前旋转
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
        // 鼠标滚轮缩放
        desiredDistance = Mathf.Clamp(desiredDistance - Input.GetAxis("Mouse ScrollWheel") * zoomSpeed, minDistance, maxDistance);
    }

    void CalculateDesiredRotation()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // 获取鼠标输入
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (Mathf.Abs(mouseX) > 0.01f || Mathf.Abs(mouseY) > 0.01f)
        {
            // 应用旋转方向设置
            xDeg += mouseX * xSpeed * 0.02f * (invertX ? -1 : 1);
            yDeg -= mouseY * ySpeed * 0.02f * (invertY ? -1 : 1);

            // 限制垂直角度
            yDeg = Mathf.Clamp(yDeg, yMinLimit, yMaxLimit);
        }
        else if (autoRotate)
        {
            xDeg += autoRotateSpeed * 0.02f;
        }

        // 使用四元数计算旋转，避免万向节锁
        desiredRotation = Quaternion.Euler(yDeg, xDeg, 0);
        currentRotation = Quaternion.Slerp(currentRotation, desiredRotation, rotationSmoothTime * Time.timeScale);
    }

    void UpdateCameraPosition()
    {
        // 平滑过渡距离
        currentDistance = Mathf.SmoothDamp(currentDistance, desiredDistance, ref velDistance, rotationSmoothTime);

        // 计算目标位置(考虑偏移)
        Vector3 targetPosition = target.position + targetOffset;

        // 计算相机位置
        position = targetPosition - (currentRotation * Vector3.forward * currentDistance);

        // 检查障碍物
        CheckCameraOcclusion(targetPosition, ref position);

        transform.rotation = currentRotation;
        transform.position = position;
    }

    void CheckCameraOcclusion(Vector3 from, ref Vector3 to)
    {
        RaycastHit hit;
        if (Physics.Linecast(from, to, out hit))
        {
            // 如果相机与目标之间有障碍物，调整相机位置
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
