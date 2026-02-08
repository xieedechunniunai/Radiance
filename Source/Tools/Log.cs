using BepInEx.Logging;
using System.Reflection;

namespace Radiance.Tools;

/// <summary>
/// 插件日志工具类
/// </summary>
internal static class Log
{
    /// <summary>
    /// 日志前缀，包含版本号
    /// </summary>
    private static string LogPrefix => $"[{Assembly.GetExecutingAssembly().GetName().Version}] ";

    /// <summary>
    /// BepInEx 日志源
    /// </summary>
    private static ManualLogSource? _logSource;

    /// <summary>
    /// 初始化日志源
    /// </summary>
    /// <param name="logSource">BepInEx 日志源</param>
    internal static void Init(ManualLogSource logSource)
    {
        _logSource = logSource;
    }

    /// <summary>
    /// 输出调试日志
    /// </summary>
    /// <param name="debug">调试信息</param>
    internal static void Debug(object debug) => _logSource?.LogDebug(LogPrefix + debug);

    /// <summary>
    /// 输出信息日志
    /// </summary>
    /// <param name="info">信息内容</param>
    internal static void Info(object info) => _logSource?.LogInfo(LogPrefix + info);

    /// <summary>
    /// 输出警告日志
    /// </summary>
    /// <param name="warning">警告内容</param>
    internal static void Warn(object warning) => _logSource?.LogWarning(LogPrefix + warning);

    /// <summary>
    /// 输出错误日志
    /// </summary>
    /// <param name="error">错误内容</param>
    internal static void Error(object error) => _logSource?.LogError(LogPrefix + error);
}
