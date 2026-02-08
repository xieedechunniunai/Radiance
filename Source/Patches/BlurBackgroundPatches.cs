using HarmonyLib;
using Radiance.Managers;
using UnityEngine;

namespace Radiance.Patches;

/// <summary>
/// 修正自定义场景中模糊背景的裁剪面和层级
/// </summary>
[HarmonyPatch(typeof(LightBlurredBackground), "UpdateCameraClipPlanes")]
internal static class BlurBackgroundPatches
{
    private static readonly AccessTools.FieldRef<LightBlurredBackground, Camera?> BackgroundCameraRef =
        AccessTools.FieldRefAccess<LightBlurredBackground, Camera>("backgroundCamera");

    [HarmonyPostfix]
    private static void AfterUpdateCameraClipPlanes(LightBlurredBackground __instance)
    {
        if (!RadianceSceneManager.IsInCustomScene)
        {
            return;
        }

        if (!RadianceSceneManager.TryGetCustomBlurOverrides(out var nearOverride, out var maskOverride))
        {
            return;
        }

        var backgroundCamera = BackgroundCameraRef(__instance);
        if (backgroundCamera == null)
        {
            return;
        }

        if (nearOverride > 0f && backgroundCamera.nearClipPlane > nearOverride)
        {
            backgroundCamera.nearClipPlane = nearOverride;
        }

        if (maskOverride != 0)
        {
            backgroundCamera.cullingMask |= maskOverride;
        }
    }
}
