using HarmonyLib;
using Radiance.Managers;
using Radiance.Tools;

namespace Radiance.Patches;

/// <summary>
/// 场景切换拦截补丁
/// 当处于自定义场景时，拦截所有 BeginSceneTransition 调用（死亡重生、手动退出等），
/// 执行清理逻辑并将目标场景重定向到返回场景
/// </summary>
[HarmonyPatch]
internal static class SceneTransitionPatches
{
    /// <summary>
    /// Prefix 拦截 GameManager.BeginSceneTransition
    /// 在自定义场景中触发任何场景转换时：
    /// 1. 执行清理（CleanupCustomScene）
    /// 2. 将转换目标重定向到保存的返回场景
    /// </summary>
    /// <remarks>
    /// 进入自定义场景时 IsInCustomScene 尚未设置为 true（在 OnSceneChanged 中才激活），
    /// 因此进入流程中的 BeginSceneTransition 不会被拦截。
    /// 仅当玩家已经在自定义场景中（死亡、手动退出等）触发转换时才拦截。
    /// </remarks>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), "BeginSceneTransition")]
    private static void InterceptTransitionFromCustomScene(GameManager.SceneLoadInfo info)
    {
        if (!RadianceSceneManager.IsInCustomScene)
            return;

        var rsm = RadianceSceneManager.Instance;
        if (rsm == null)
            return;

        Log.Info(
            $"[SceneTransitionPatch] 拦截场景转换: {info.SceneName} -> 重定向到 {rsm.ReturnSceneName}"
        );

        // 执行清理（不启动新的转换，仅重置状态）
        rsm.CleanupCustomScene();

        // 重定向目标场景到返回场景
        if (rsm.HasReturnInfo)
        {
            info.SceneName = rsm.ReturnSceneName;
        }
    }
}
