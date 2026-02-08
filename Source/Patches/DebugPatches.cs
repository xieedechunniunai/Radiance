using HarmonyLib;
using HutongGames.PlayMaker;
using Radiance.Tools;
using UnityEngine;

namespace Radiance.Patches;

/// <summary>
/// 调试用 Harmony 补丁
/// 用于定位并处理问题 FSM 和 Action
/// </summary>
[HarmonyPatch]
internal static class DebugPatches
{
    /// <summary>
    /// 在 PlayMakerFSM 启动前检查
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayMakerFSM), "Start")]
    private static bool CheckForProblematicActions(PlayMakerFSM __instance)
    {
        if (__instance == null)
            return true;

        var goName = __instance.gameObject.name;

        try
        {
            var fsm = __instance.Fsm;
            if (fsm == null)
                return true;

            var states = fsm.States;
            if (states == null)
                return true;

            foreach (var state in states)
            {
                if (state?.Actions == null)
                    continue;

                for (int i = 0; i < state.Actions.Length; i++)
                {
                    var action = state.Actions[i];
                    if (action == null)
                        continue;

                    var actionTypeName = action.GetType().Name;

                    // 检查问题 Action 并销毁
                    if (
                        actionTypeName == "ShowGodfinderIcon"
                        || actionTypeName == "HideGodfinderIcon"
                        || actionTypeName == "LoadBossSequence"
                    )
                    {
                        Log.Info($"[DebugPatch] 发现问题 Action: {actionTypeName}");
                        Log.Info($"  GameObject: {goName}");
                        Log.Info($"  FSM 名称: {__instance.FsmName}");
                        Log.Info($"  State 名称: {state.Name}");
                        Log.Info($"  完整路径: {GetFullPath(__instance.gameObject)}");

                        Object.DestroyImmediate(__instance.gameObject);
                        Log.Info($"  已销毁该 GameObject");
                        return false;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Debug($"[DebugPatch] 检查 FSM 时出错: {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// 修复初始禁用的 GameObject 激活后 FSM 事件处理组件缺失的问题
    /// 原因：物体禁用时预处理，某些 Action 的 OnPreprocess 无法正确初始化
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
    private static void FixFsmEventHandlersOnEnable(PlayMakerFSM __instance)
    {
        if (__instance == null)
            return;

        try
        {
            var fsm = __instance.Fsm;
            if (fsm == null || fsm.States == null)
                return;

            // 遍历所有 State 的 Actions，检查是否有需要事件处理组件的 Action
            foreach (var state in fsm.States)
            {
                if (state?.Actions == null)
                    continue;

                foreach (var action in state.Actions)
                {
                    if (action == null)
                        continue;

                    // 重新调用 Action 的 OnPreprocess，确保事件处理组件被正确添加
                    try
                    {
                        action.OnPreprocess();
                    }
                    catch
                    {
                        // 忽略预处理错误
                    }
                }
            }

            // 重新添加事件处理组件
            __instance.AddEventHandlerComponents();
        }
        catch (System.Exception ex)
        {
            Log.Debug(
                $"[DebugPatch] 修复 FSM 事件处理组件失败: {__instance.gameObject.name}/{__instance.FsmName} - {ex.Message}"
            );
        }
    }

    private static string GetFullPath(GameObject obj)
    {
        if (obj == null)
            return "(null)";

        var path = obj.name;
        var parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
