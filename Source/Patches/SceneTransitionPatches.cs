using HarmonyLib;
using Radiance.Managers;
using Radiance.Tools;

namespace Radiance.Patches;

/// <summary>
/// 场景切换清理补丁
/// 当处于自定义场景时，在任何 BeginSceneTransition 触发前执行 MOD 状态清理，
/// 但不修改目标场景，让引擎走原版的场景转换/复活逻辑
/// </summary>
[HarmonyPatch]
internal static class SceneTransitionPatches
{
    /// <summary>
    /// Prefix 拦截 GameManager.BeginSceneTransition
    /// 仅执行 MOD 状态清理（渲染器恢复、Boss 残留对象销毁等），
    /// 不重定向 info.SceneName —— 死亡走 playerData.respawnScene 原版复活，
    /// 手动退出走 ExitCustomScene 已设置的 returnSceneName
    /// </summary>
    /// <remarks>
    /// 进入自定义场景时 IsInCustomScene 尚未设置为 true（在 OnSceneChanged 中才激活），
    /// 因此进入流程中的 BeginSceneTransition 不会被拦截。
    /// 仅当玩家已经在自定义场景中（死亡、手动退出等）触发转换时才执行清理。
    /// </remarks>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), "BeginSceneTransition")]
    private static void CleanupOnTransitionFromCustomScene(GameManager.SceneLoadInfo info)
    {
        if (!RadianceSceneManager.IsInCustomScene)
            return;

        var rsm = RadianceSceneManager.Instance;
        if (rsm == null)
            return;

        Log.Info(
            $"[SceneTransitionPatch] 自定义场景内场景转换: {info.SceneName}，执行清理"
        );

        // 仅执行状态清理，不修改 info.SceneName
        rsm.CleanupCustomScene();
    }
}
