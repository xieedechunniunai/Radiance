using System;
using HarmonyLib;
using Radiance.Managers;

namespace Radiance.Patches;

/// <summary>
/// 回忆场景补丁
/// 当处于自定义场景时，强制 GameManager.IsMemoryScene() 返回 true
/// 使引擎原生处理：死亡非致命化、无尸体/金币丢失、PreMemoryState 自动保存/恢复
/// </summary>
[HarmonyPatch]
internal static class MemoryScenePatches
{
    /// <summary>
    /// Patch GameManager.IsMemoryScene() 无参实例方法
    /// 当 RadianceSceneManager.IsInCustomScene 为 true 时强制返回 true
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), "IsMemoryScene", new Type[0])]
    private static void ForceMemoryInCustomScene(ref bool __result)
    {
        if (RadianceSceneManager.IsInCustomScene)
            __result = true;
    }
}
