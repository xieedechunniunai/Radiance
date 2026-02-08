using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using Radiance.Behaviours.Common;
using Radiance.Tools;
using UnityEngine;

namespace Radiance.Patches;

/// <summary>
/// Radiance 核心补丁
/// 在 Absolute Radiance 的各个 FSM 启动时挂载自定义 MonoBehaviour
/// </summary>
[HarmonyPatch]
internal static class RadiancePatches
{
    private const string TargetGameObjectName = "Absolute Radiance";

    /// <summary>
    /// 是否启用 FSM 分析输出
    /// </summary>
    public static bool EnableFsmAnalysis = true;

    /// <summary>
    /// FSM 分析输出目录（可在运行时修改）
    /// </summary>
    public static string FsmAnalysisOutputDir = @"D:\tool\unityTool\mods\new\AnySilkBoss\bin\Debug\temp";

    /// <summary>
    /// 已分析过的 GameObject 实例 ID（防止重复分析）
    /// </summary>
    private static readonly HashSet<int> AnalyzedInstances = new();

    /// <summary>
    /// FSM 处理器委托
    /// </summary>
    /// <param name="fsm">目标 PlayMakerFSM</param>
    public delegate void FsmHandler(PlayMakerFSM fsm);

    /// <summary>
    /// FSM 名称 -> 处理器映射表
    /// </summary>
    private static readonly Dictionary<string, FsmHandler> FsmHandlers = new()
    {
        // Control FSM - 主控制逻辑
        { "Control", HandleControlFsm },
        
        // 后续可在此添加其他 FSM 的处理器
        // { "Attack Control", HandleAttackControlFsm },
        // { "Phase Control", HandlePhaseControlFsm },
    };

    /// <summary>
    /// 在 PlayMakerFSM.Start 前挂载自定义组件
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayMakerFSM), "Start")]
    private static void MountRadianceBehaviours(PlayMakerFSM __instance)
    {
        if (__instance == null)
            return;

        // 匹配目标 GameObject
        if (__instance.gameObject.name != TargetGameObjectName)
            return;

        // // 首次遇到该 Boss 实例时进行 FSM 分析
        // TryAnalyzeFsms(__instance.gameObject);

        // 查找对应 FSM 的处理器
        if (!FsmHandlers.TryGetValue(__instance.FsmName, out var handler))
            return;

        Log.Info($"[RadiancePatch] 发现目标 FSM: {TargetGameObjectName}/{__instance.FsmName}");

        try
        {
            handler(__instance);
        }
        catch (Exception ex)
        {
            Log.Error($"[RadiancePatch] 处理 FSM [{__instance.FsmName}] 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 尝试分析 FSM（每个 Boss 实例只分析一次）
    /// </summary>
    private static void TryAnalyzeFsms(GameObject bossObject)
    {
        if (!EnableFsmAnalysis)
            return;

        var instanceId = bossObject.GetInstanceID();
        if (AnalyzedInstances.Contains(instanceId))
            return;

        AnalyzedInstances.Add(instanceId);

        try
        {
            Log.Info($"[RadiancePatch] 开始分析 {bossObject.name} 的所有 FSM...");
            FsmAnalyzer.AnalyzeAllFsms(bossObject, FsmAnalysisOutputDir);
            Log.Info($"[RadiancePatch] FSM 分析完成，输出到: {FsmAnalysisOutputDir}");
        }
        catch (Exception ex)
        {
            Log.Error($"[RadiancePatch] FSM 分析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 清除已分析实例记录（场景切换时调用）
    /// </summary>
    public static void ClearAnalyzedInstances()
    {
        AnalyzedInstances.Clear();
    }

    #region FSM Handlers

    /// <summary>
    /// Control FSM 处理器
    /// </summary>
    private static void HandleControlFsm(PlayMakerFSM fsm)
    {
        // 挂载返回行为组件
        MountBehaviour<RadianceReturnOnDialogueBehavior>(fsm, b => b.Initialize(fsm));
    }

    // 后续可添加其他 FSM 处理器
    // private static void HandleAttackControlFsm(PlayMakerFSM fsm) { }
    // private static void HandlePhaseControlFsm(PlayMakerFSM fsm) { }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 通用组件挂载方法
    /// </summary>
    /// <typeparam name="T">MonoBehaviour 类型</typeparam>
    /// <param name="fsm">目标 FSM</param>
    /// <param name="initializer">初始化回调（可选）</param>
    /// <returns>挂载的组件实例</returns>
    private static T MountBehaviour<T>(PlayMakerFSM fsm, Action<T>? initializer = null) where T : UnityEngine.MonoBehaviour
    {
        var component = fsm.gameObject.GetComponent<T>();
        if (component == null)
        {
            component = fsm.gameObject.AddComponent<T>();
            Log.Info($"[RadiancePatch] 已挂载 {typeof(T).Name} 组件");
        }

        initializer?.Invoke(component);
        return component;
    }

    /// <summary>
    /// 运行时注册 FSM 处理器（供外部模块扩展）
    /// </summary>
    /// <param name="fsmName">FSM 名称</param>
    /// <param name="handler">处理器</param>
    public static void RegisterFsmHandler(string fsmName, FsmHandler handler)
    {
        if (FsmHandlers.ContainsKey(fsmName))
        {
            Log.Warn($"[RadiancePatch] FSM 处理器 [{fsmName}] 已存在，将被覆盖");
        }
        FsmHandlers[fsmName] = handler;
        Log.Info($"[RadiancePatch] 已注册 FSM 处理器: {fsmName}");
    }

    /// <summary>
    /// 运行时注销 FSM 处理器
    /// </summary>
    /// <param name="fsmName">FSM 名称</param>
    public static void UnregisterFsmHandler(string fsmName)
    {
        if (FsmHandlers.Remove(fsmName))
        {
            Log.Info($"[RadiancePatch] 已注销 FSM 处理器: {fsmName}");
        }
    }

    #endregion
}
