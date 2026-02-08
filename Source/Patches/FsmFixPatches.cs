using HarmonyLib;
using HutongGames.PlayMaker;
using Radiance.Tools;

namespace Radiance.Patches;

/// <summary>
/// FSM 修复补丁
/// 修复从 AssetBundle 加载的场景中 FSM 事件处理组件缺失的问题
/// </summary>
[HarmonyPatch]
internal static class FsmFixPatches
{
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

            foreach (var state in fsm.States)
            {
                if (state?.Actions == null)
                    continue;

                foreach (var action in state.Actions)
                {
                    if (action == null)
                        continue;

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

            __instance.AddEventHandlerComponents();
        }
        catch (System.Exception ex)
        {
            Log.Debug(
                $"[FsmFixPatch] 修复 FSM 事件处理组件失败: {__instance.gameObject.name}/{__instance.FsmName} - {ex.Message}"
            );
        }
    }
}
