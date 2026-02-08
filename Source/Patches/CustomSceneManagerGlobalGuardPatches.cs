using HarmonyLib;
using Radiance.Managers;

namespace Radiance.Patches;

[HarmonyPatch(typeof(CustomSceneManager))]
internal static class CustomSceneManagerGlobalGuardPatches
{
    [HarmonyPatch(nameof(CustomSceneManager.UpdateScene))]
    [HarmonyPrefix]
    private static bool BeforeUpdateScene()
    {
        return !RadianceSceneManager.SuppressSceneManagerGlobalWrites;
    }

    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    private static bool BeforeUpdate()
    {
        return !RadianceSceneManager.SuppressSceneManagerGlobalWrites;
    }

    [HarmonyPatch(nameof(CustomSceneManager.SetLighting))]
    [HarmonyPrefix]
    private static bool BeforeSetLighting()
    {
        return !RadianceSceneManager.SuppressSceneManagerGlobalWrites;
    }
}
