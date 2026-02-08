using HarmonyLib;
using Radiance.Managers;
using Radiance.Tools;

namespace Radiance.Patches;

/// <summary>
/// 场景切换补丁
/// 1. 入场拦截：当 TransitionPoint 触发 BeginSceneTransition 且目标为 GG_Radiance 时，
///    阻止原版加载（GG_Radiance 不在 Addressables Catalog 中），转为 AssetBundle 加载
/// 2. 出场清理：当处于自定义场景时，在任何 BeginSceneTransition 触发前执行 MOD 状态清理，
///    但不修改目标场景，让引擎走原版的场景转换/复活逻辑
/// </summary>
[HarmonyPatch]
internal static class SceneTransitionPatches
{
    /// <summary>
    /// Prefix 拦截 GameManager.BeginSceneTransition
    /// </summary>
    /// <returns>
    /// true  — 让原版 BeginSceneTransition 继续执行
    /// false — 阻止原版执行（入场拦截：由 AssetBundle 加载替代）
    /// </returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameManager), "BeginSceneTransition")]
    private static bool InterceptSceneTransition(GameManager.SceneLoadInfo info)
    {
        // === 入场拦截 ===
        // TransitionPoint.DoSceneTransition 调用 BeginSceneTransition 时，
        // IsInCustomScene 尚未设置（在 OnSceneChanged 中才激活），
        // 此时 SceneName == "GG_Radiance" 表示从 Belltown 的 TransitionPoint 触发进入
        if (!RadianceSceneManager.IsInCustomScene && info.SceneName == "GG_Radiance")
        {
            var rsm = RadianceSceneManager.Instance;
            if (rsm != null)
            {
                Log.Info(
                    "[SceneTransitionPatch] 拦截 TransitionPoint 入场: GG_Radiance，转为 AssetBundle 加载"
                );
                rsm.EnterViaTransitionPoint();
                return false; // 阻止原版 BeginSceneTransition
            }
        }

        // === 出场清理 ===
        // 玩家已在自定义场景中（死亡、手动退出等）触发的转换
        if (RadianceSceneManager.IsInCustomScene)
        {
            var rsm = RadianceSceneManager.Instance;
            if (rsm != null)
            {
                Log.Info(
                    $"[SceneTransitionPatch] 自定义场景内场景转换: {info.SceneName}，执行清理"
                );

                // 仅执行状态清理，不修改 info.SceneName
                rsm.CleanupCustomScene();
            }
        }

        return true; // 让原版 BeginSceneTransition 继续
    }
}
