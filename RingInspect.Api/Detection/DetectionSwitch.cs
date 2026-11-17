namespace RingInspect.Api.Detection;

/// <summary>
/// 检测启停开关。WPF 界面上的开关和 HTTP 接口 /api/switch 操作的是同一个状态（同一进程内单例）。
/// 关闭后：界面上的批量检测会在当前这张做完后停下（剩下的继续排队），
/// 所有检测接口统一返回 HTTP 409 + <c>DETECTION_PAUSED</c>。
/// </summary>
public static class DetectionSwitch
{
    private static volatile bool _enabled = true;

    /// <summary>当前是否允许检测。</summary>
    public static bool Enabled => _enabled;

    /// <summary>状态变化通知（界面按钮和接口都会触发）。参数 = 是否允许检测。</summary>
    public static event Action<bool>? Changed;

    /// <summary>设置开关；状态没变化时不会重复触发事件。</summary>
    public static void Set(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        Changed?.Invoke(enabled);
    }

    /// <summary>取反并返回新状态。</summary>
    public static bool Toggle()
    {
        Set(!_enabled);
        return _enabled;
    }

    /// <summary>给接口用的一句话状态。</summary>
    public static string State => _enabled ? "running" : "paused";
}
