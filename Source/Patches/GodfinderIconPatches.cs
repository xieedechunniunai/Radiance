using HarmonyLib;
using Radiance.Managers;

namespace Radiance.Patches;

/// <summary>
/// 自定义场景中禁用 Godfinder 图标相关 Action，避免空引用
/// </summary>
[HarmonyPatch]
internal static class GodfinderIconPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ShowGodfinderIcon), "OnEnter")]
    private static bool SkipShowGodfinderIcon()
    {
        return !RadianceSceneManager.IsInCustomScene;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GodfinderIcon), "Show")]
    private static bool SkipHideGodfinderIcon()
    {
        return !RadianceSceneManager.IsInCustomScene;
    }
}
