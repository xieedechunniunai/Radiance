using System;
using System.IO;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Radiance.Tools;

/// <summary>
/// FSM 分析器 - 用于输出 FSM 详细信息到日志文件
/// </summary>
public static class FsmAnalyzer
{
    /// <summary>
    /// 默认输出目录
    /// </summary>
    private static readonly string DefaultOutputDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "BepInEx", "plugins", "Radiance", "temp"
    );

    /// <summary>
    /// 分析 GameObject 上的所有 FSM 并输出到文件
    /// </summary>
    /// <param name="gameObject">目标 GameObject</param>
    /// <param name="outputDir">输出目录（可选，默认为插件 temp 目录）</param>
    public static void AnalyzeAllFsms(GameObject gameObject, string? outputDir = null)
    {
        if (gameObject == null)
        {
            Log.Error("[FsmAnalyzer] GameObject 为空");
            return;
        }

        var dir = outputDir ?? DefaultOutputDir;
        Directory.CreateDirectory(dir);

        var fsms = gameObject.GetComponents<PlayMakerFSM>();
        Log.Info($"[FsmAnalyzer] 开始分析 {gameObject.name} 的 {fsms.Length} 个 FSM");

        // 生成时间戳
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeGoName = SanitizeFileName(gameObject.name);

        foreach (var fsm in fsms)
        {
            var safeFsmName = SanitizeFileName(fsm.FsmName);
            var fileName = $"{safeGoName}_{safeFsmName}_{timestamp}.txt";
            var outputPath = Path.Combine(dir, fileName);

            WriteFsmReport(fsm, outputPath);
            Log.Info($"[FsmAnalyzer] 已输出: {fileName}");
        }

        // 同时输出一个汇总文件
        var summaryPath = Path.Combine(dir, $"{safeGoName}_Summary_{timestamp}.txt");
        WriteSummaryReport(gameObject, fsms, summaryPath);
        Log.Info($"[FsmAnalyzer] 已输出汇总: {safeGoName}_Summary_{timestamp}.txt");
    }

    /// <summary>
    /// 输出单个 FSM 的详细报告
    /// </summary>
    public static void WriteFsmReport(PlayMakerFSM fsm, string outputPath)
    {
        if (fsm == null) return;

        var sb = new StringBuilder();

        sb.AppendLine($"================================================================================");
        sb.AppendLine($"FSM 报告: {fsm.FsmName}");
        sb.AppendLine($"GameObject: {fsm.gameObject.name}");
        sb.AppendLine($"路径: {GetGameObjectPath(fsm.gameObject)}");
        sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"================================================================================");
        sb.AppendLine($"当前状态: {fsm.ActiveStateName}");
        sb.AppendLine($"起始状态: {fsm.Fsm.StartState}");

        // 状态列表
        var states = fsm.FsmStates;
        sb.AppendLine($"\n--- 所有状态 ({states.Length}个) ---");
        foreach (var state in states)
        {
            sb.AppendLine($"  - {state.Name}");
        }

        // 事件列表
        var events = fsm.FsmEvents;
        sb.AppendLine($"\n--- 所有事件 ({events.Length}个) ---");
        foreach (var evt in events)
        {
            sb.AppendLine($"  - {evt.Name} (IsGlobal: {evt.IsGlobal})");
        }

        // 全局转换
        var globalTransitions = fsm.FsmGlobalTransitions;
        sb.AppendLine($"\n--- 全局转换 ({globalTransitions.Length}个) ---");
        foreach (var gt in globalTransitions)
        {
            sb.AppendLine($"  {gt.FsmEvent?.Name} -> {gt.toState}");
        }

        // 各状态详细信息
        sb.AppendLine($"\n================================================================================");
        sb.AppendLine($"各状态详情");
        sb.AppendLine($"================================================================================");

        foreach (var state in states)
        {
            sb.AppendLine($"\n【状态: {state.Name}】");
            
            // 转换
            sb.AppendLine($"  转换:");
            if (state.Transitions != null && state.Transitions.Length > 0)
            {
                foreach (var tr in state.Transitions)
                {
                    sb.AppendLine($"    {tr.FsmEvent?.Name ?? "(null)"} -> {tr.toState}");
                }
            }
            else
            {
                sb.AppendLine($"    (无转换)");
            }

            // 动作
            sb.AppendLine($"  动作 ({state.Actions?.Length ?? 0}个):");
            if (state.Actions != null && state.Actions.Length > 0)
            {
                for (int i = 0; i < state.Actions.Length; i++)
                {
                    var action = state.Actions[i];
                    if (action == null)
                    {
                        sb.AppendLine($"    [{i}] (null)");
                        continue;
                    }
                    sb.AppendLine($"    [{i}] {action.GetType().Name}");
                    AnalyzeActionVariables(sb, action, i);
                }
            }
            else
            {
                sb.AppendLine($"    (无动作)");
            }
        }

        // 变量
        var vars = fsm.FsmVariables;
        sb.AppendLine($"\n================================================================================");
        sb.AppendLine($"FSM 变量");
        sb.AppendLine($"================================================================================");
        sb.AppendLine($"Bool: {vars.BoolVariables.Length}, Int: {vars.IntVariables.Length}, Float: {vars.FloatVariables.Length}, String: {vars.StringVariables.Length}");
        sb.AppendLine($"GameObject: {vars.GameObjectVariables.Length}, Object: {vars.ObjectVariables.Length}, Vector3: {vars.Vector3Variables.Length}");
        
        sb.AppendLine($"\n  Float 变量:");
        foreach (var v in vars.FloatVariables) sb.AppendLine($"    {v.Name} = {v.Value}");
        
        sb.AppendLine($"\n  Bool 变量:");
        foreach (var v in vars.BoolVariables) sb.AppendLine($"    {v.Name} = {v.Value}");
        
        sb.AppendLine($"\n  Int 变量:");
        foreach (var v in vars.IntVariables) sb.AppendLine($"    {v.Name} = {v.Value}");
        
        sb.AppendLine($"\n  String 变量:");
        foreach (var v in vars.StringVariables) sb.AppendLine($"    {v.Name} = \"{v.Value}\"");
        
        sb.AppendLine($"\n  GameObject 变量:");
        foreach (var v in vars.GameObjectVariables)
        {
            var goName = v.Value != null ? v.Value.name : "(null)";
            sb.AppendLine($"    {v.Name} = {goName}");
        }
        
        sb.AppendLine($"\n  Vector3 变量:");
        foreach (var v in vars.Vector3Variables) sb.AppendLine($"    {v.Name} = {v.Value}");

        // 写文件
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 输出汇总报告
    /// </summary>
    private static void WriteSummaryReport(GameObject gameObject, PlayMakerFSM[] fsms, string outputPath)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"================================================================================");
        sb.AppendLine($"FSM 汇总报告: {gameObject.name}");
        sb.AppendLine($"路径: {GetGameObjectPath(gameObject)}");
        sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"FSM 数量: {fsms.Length}");
        sb.AppendLine($"================================================================================");

        foreach (var fsm in fsms)
        {
            sb.AppendLine($"\n【{fsm.FsmName}】");
            sb.AppendLine($"  状态数: {fsm.FsmStates.Length}");
            sb.AppendLine($"  事件数: {fsm.FsmEvents.Length}");
            sb.AppendLine($"  起始状态: {fsm.Fsm.StartState}");
            
            // 列出所有状态
            sb.AppendLine($"  状态列表:");
            foreach (var state in fsm.FsmStates)
            {
                var actionCount = state.Actions?.Length ?? 0;
                var transitionCount = state.Transitions?.Length ?? 0;
                sb.AppendLine($"    - {state.Name} ({actionCount} actions, {transitionCount} transitions)");
            }
        }

        // 子对象上的 FSM
        sb.AppendLine($"\n================================================================================");
        sb.AppendLine($"子对象 FSM");
        sb.AppendLine($"================================================================================");

        var childFsms = gameObject.GetComponentsInChildren<PlayMakerFSM>(true);
        var childOnlyFsms = childFsms.Where(f => f.gameObject != gameObject).ToArray();
        
        sb.AppendLine($"子对象 FSM 数量: {childOnlyFsms.Length}");
        foreach (var fsm in childOnlyFsms)
        {
            sb.AppendLine($"  {GetGameObjectPath(fsm.gameObject)} -> {fsm.FsmName} ({fsm.FsmStates.Length} states)");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 分析 Action 的变量
    /// </summary>
    private static void AnalyzeActionVariables(StringBuilder sb, FsmStateAction action, int actionIndex)
    {
        try
        {
            var actionType = action.GetType();
            var fields = actionType.GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            );

            foreach (var field in fields)
            {
                var value = field.GetValue(action);
                if (value == null) continue;

                var valueStr = FormatValue(value, action);
                sb.AppendLine($"        {field.Name}: {valueStr}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"        (分析变量时出错: {ex.Message})");
        }
    }

    /// <summary>
    /// 格式化变量值
    /// </summary>
    private static string FormatValue(object value, FsmStateAction action)
    {
        return value switch
        {
            FsmFloat f => f.UseVariable ? $"[Float变量:{f.Name}]" : $"{f.Value}",
            FsmInt i => i.UseVariable ? $"[Int变量:{i.Name}]" : $"{i.Value}",
            FsmBool b => b.UseVariable ? $"[Bool变量:{b.Name}]" : $"{b.Value}",
            FsmString s => s.UseVariable ? $"[String变量:{s.Name}]" : $"\"{s.Value}\"",
            FsmEvent e => $"[Event:{e.Name}]",
            FsmGameObject go => go.UseVariable 
                ? $"[GO变量:{go.Name}]" 
                : (go.Value != null ? $"GO:{go.Value.name}" : "GO:(null)"),
            FsmOwnerDefault od => FormatOwnerDefault(od, action),
            FsmColor c => c.UseVariable ? $"[Color变量:{c.Name}]" : $"RGBA({c.Value.r:F2},{c.Value.g:F2},{c.Value.b:F2},{c.Value.a:F2})",
            FsmVector3 v => v.UseVariable ? $"[V3变量:{v.Name}]" : $"({v.Value.x:F2},{v.Value.y:F2},{v.Value.z:F2})",
            FsmObject o => o.UseVariable ? $"[Obj变量:{o.Name}]" : $"Obj:{o.Value?.GetType().Name ?? "(null)"}",
            GameObject go => $"GO:{go.name}",
            _ => $"{value} ({value.GetType().Name})"
        };
    }

    /// <summary>
    /// 格式化 FsmOwnerDefault
    /// </summary>
    private static string FormatOwnerDefault(FsmOwnerDefault od, FsmStateAction action)
    {
        try
        {
            var ownerOption = od.OwnerOption;
            if (ownerOption == OwnerDefaultOption.UseOwner)
            {
                return "[Owner]";
            }
            else if (od.GameObject != null && od.GameObject.Value != null)
            {
                return $"GO:{od.GameObject.Value.name}";
            }
            else if (od.GameObject != null && od.GameObject.UseVariable)
            {
                return $"[GO变量:{od.GameObject.Name}]";
            }
        }
        catch { }
        return "[OwnerDefault]";
    }

    /// <summary>
    /// 获取 GameObject 完整路径
    /// </summary>
    private static string GetGameObjectPath(GameObject obj)
    {
        if (obj == null) return "(null)";

        var path = obj.name;
        var parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    /// <summary>
    /// 清理文件名中的非法字符
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
