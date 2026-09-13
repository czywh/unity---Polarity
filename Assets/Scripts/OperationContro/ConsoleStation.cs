using UnityEngine;

/// <summary>
/// PROP　操作台（带调试日志版）：玩家靠近并聚焦后按 activateKey（默认 F）进入「操作模式」。
/// 继承 InteractableBase（默认 PlayerOnly）。每个操作台自带相机机位参数。
///
/// 调试：勾 verboseLog 后，聚焦变化 / 按 F 会打印，方便确认"为什么按 F 没进操作模式"。
/// </summary>
public class ConsoleStation : InteractableBase
{
    [Header("操作台激活")]
    [Tooltip("靠近并聚焦后，按此键进入操作模式")]
    public KeyCode activateKey = KeyCode.F;

    [Header("操作模式机位")]
    [Tooltip("机位锚点：拖一个空物体摆到想要的相机位置/角度；留空则用下面的坐标")]
    public Transform cameraAnchor;
    public Vector3 cameraPosition = new Vector3(-107.53f, 75.81f, -93.7f);
    public Vector3 cameraEuler = new Vector3(55.952f, 90f, -0.2f);
    public float orthographicSize = 33f;

    [Header("引用（留空自动查找）")]
    [SerializeField] private OperationModeController operationMode;

    [Header("调试")]
    public bool verboseLog = true;
    [Header("调试（运行时只读）")]
    [SerializeField] private bool focusedReadout;

    private void Reset()
    {
        access = InteractAccess.PlayerOnly;
        interactVerb = "Operate";
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (operationMode == null) operationMode = FindFirstObjectByType<OperationModeController>();
        if (operationMode == null)
            Debug.LogWarning("[操作台] 场景里没找到 OperationModeController！按 F 也没法进操作模式。", this);
    }

    private void Update()
    {
        // 聚焦状态变化时打印一次
        if (IsFocused != focusedReadout)
        {
            focusedReadout = IsFocused;
            if (verboseLog)
                Debug.Log($"[操作台] {name} 聚焦 = {IsFocused}" +
                          (IsFocused && CurrentInteractor != null ? $"，交互者={CurrentInteractor.name}({CurrentInteractor.type})" : ""), this);
        }

        if (!IsFocused) return;

        if (CurrentInteractor == null)
        {
            if (verboseLog) Debug.Log("[操作台] 已聚焦但 CurrentInteractor 为空", this);
            return;
        }
        if (CurrentInteractor.type != InteractorType.Player)
        {
            if (verboseLog) Debug.Log($"[操作台] 聚焦者不是玩家（是 {CurrentInteractor.type}），不响应 F", this);
            return;
        }

        if (Input.GetKeyDown(activateKey))
        {
            if (verboseLog) Debug.Log($"[操作台] 按下 {activateKey} → 请求进入操作模式", this);
            if (operationMode != null) operationMode.Enter(this);
            else Debug.LogWarning("[操作台] operationMode 为空，无法进入", this);
        }
    }

    /// 提供本操作台的相机机位（有锚点用锚点，否则用坐标字段）
    public void GetCameraPose(out Vector3 pos, out Quaternion rot, out float size)
    {
        if (cameraAnchor != null)
        {
            pos = cameraAnchor.position;
            rot = cameraAnchor.rotation;
        }
        else
        {
            pos = cameraPosition;
            rot = Quaternion.Euler(cameraEuler);
        }
        size = orthographicSize;
    }
}