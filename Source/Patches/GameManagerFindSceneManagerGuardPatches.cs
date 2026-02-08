using HarmonyLib;
using Radiance.Managers;
using UnityEngine;

namespace Radiance.Patches;

[HarmonyPatch(typeof(GameManager), "FindSceneManager")]
internal static class GameManagerFindSceneManagerGuardPatches
{
    [HarmonyPrefix]
    private static bool BeforeFindSceneManager(GameManager __instance)
    {
        if (!RadianceSceneManager.IsInCustomScene)
        {
            return true;
        }

        var obj = GameObject.FindGameObjectWithTag("SceneManager");
        if (obj == null)
        {
            return false;
        }

        var sm = obj.GetComponent<CustomSceneManager>();
        if (sm == null)
        {
            return false;
        }

        return true;
    }
}
