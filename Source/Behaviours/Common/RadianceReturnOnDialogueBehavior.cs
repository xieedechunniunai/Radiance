using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Radiance.Managers;
using Radiance.Tools;
using UnityEngine;

namespace Radiance.Behaviours.Common;

/// <summary>
/// Radiance 返回行为组件
/// 挂载到 Absolute Radiance 上，当 FSM 进入 Godseeker Dialogue 状态时触发返回原场景
/// </summary>
public class RadianceReturnOnDialogueBehavior : MonoBehaviour
{
    private const string TargetStateName = "Godseeker Dialogue";
    private const string ReturnMethodName = "TriggerReturnToOriginalScene";

    /// <summary>
    /// 是否已触发返回（防止重复调用）
    /// </summary>
    private bool _hasTriggeredReturn = false;

    /// <summary>
    /// 是否已完成初始化
    /// </summary>
    private bool _isInitialized = false;

    /// <summary>
    /// 初始化组件，注入 FSM 逻辑
    /// </summary>
    /// <param name="fsm">目标 PlayMakerFSM</param>
    public void Initialize(PlayMakerFSM fsm)
    {
        if (_isInitialized)
        {
            Log.Debug("[RadianceReturnBehavior] 已初始化，跳过");
            return;
        }

        InjectCallMethodToState(fsm);
        _isInitialized = true;
    }

    /// <summary>
    /// 向目标状态注入 CallMethod Action，并清除原有 Action 防止空引用
    /// </summary>
    private void InjectCallMethodToState(PlayMakerFSM fsm)
    {
        var targetState = fsm.FsmStates?.FirstOrDefault(s => s.Name == TargetStateName);
        if (targetState == null)
        {
            Log.Warn($"[RadianceReturnBehavior] 未找到状态: {TargetStateName}");
            return;
        }

        // 防重：检查是否已经只有我们的 CallMethod
        if (targetState.Actions != null && targetState.Actions.Length == 1)
        {
            var firstAction = targetState.Actions[0];
            if (firstAction is CallMethod existingCall &&
                existingCall.methodName?.Value == ReturnMethodName)
            {
                Log.Debug("[RadianceReturnBehavior] CallMethod 已存在，跳过注入");
                return;
            }
        }

        // 记录被移除的 Action 数量
        var removedCount = targetState.Actions?.Length ?? 0;

        // 创建 CallMethod Action
        var callMethodAction = new CallMethod
        {
            behaviour = new FsmObject { Value = this },
            methodName = new FsmString(ReturnMethodName) { Value = ReturnMethodName },
            parameters = new FsmVar[0],
            everyFrame = false
        };

        // 完全替换 Actions 数组，移除原有的所有 Action（防止场景切换时空引用）
        targetState.Actions = new FsmStateAction[] { callMethodAction };

        // 同时清除该状态的转换，因为我们会直接切换场景
        targetState.Transitions = new FsmTransition[0];

        // 刷新 FSM 数据
        fsm.Fsm.InitData();

        Log.Info($"[RadianceReturnBehavior] 已替换 [{TargetStateName}] 状态（移除 {removedCount} 个原有 Action）");
    }

    /// <summary>
    /// PlayMaker CallMethod 调用入口
    /// 在 Godseeker Dialogue 状态的第一个 Action 被调用
    /// </summary>
    public void TriggerReturnToOriginalScene()
    {
        // 防止重复触发
        if (_hasTriggeredReturn)
        {
            Log.Debug("[RadianceReturnBehavior] 已触发过返回，跳过重复调用");
            return;
        }

        // 只在自定义场景中才执行返回
        if (!RadianceSceneManager.IsInCustomScene)
        {
            Log.Debug("[RadianceReturnBehavior] 不在自定义场景中，跳过返回");
            return;
        }

        _hasTriggeredReturn = true;
        Log.Info("[RadianceReturnBehavior] 触发返回原场景");

        // 调用场景管理器的退出方法
        RadianceSceneManager.Instance?.ExitCustomScene();
    }

    /// <summary>
    /// 重置触发状态（在场景重新进入时可能需要）
    /// </summary>
    public void ResetTriggerState()
    {
        _hasTriggeredReturn = false;
        _isInitialized = false;
    }

    private void OnDestroy()
    {
        _hasTriggeredReturn = false;
        _isInitialized = false;
    }
}
