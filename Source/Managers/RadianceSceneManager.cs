using System;
using System.Collections;
using System.Collections.Generic;
using GlobalEnums;
using Radiance.Tools;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace Radiance.Managers;

/// <summary>
/// Radiance 场景管理器
/// 负责自定义场景的进入、退出、清理，以及与引擎原生 Memory 场景机制的协作
/// </summary>
public class RadianceSceneManager : MonoBehaviour
{
    public static RadianceSceneManager? Instance { get; private set; }

    // === 公开状态 ===

    /// <summary>
    /// 是否在自定义场景中（被 Patch 读取以判断是否拦截）
    /// </summary>
    public static bool IsInCustomScene { get; private set; }

    /// <summary>
    /// 当前自定义场景名称
    /// </summary>
    public static string? CurrentCustomSceneName { get; private set; }

    /// <summary>
    /// 返回场景名称（供 SceneTransitionPatches 读取以重定向）
    /// </summary>
    public string ReturnSceneName => _returnSceneName;

    /// <summary>
    /// 是否有有效的返回信息
    /// </summary>
    public bool HasReturnInfo => _hasSavedReturnInfo;

    // === 内部状态 ===

    // 返回位置信息
    private string _returnSceneName = "";
    private Vector3 _returnPosition;
    private bool _hasSavedReturnInfo;



    // 目标场景名称
    private string _targetSceneName = "";

    // 等待进入的自定义场景名称（在 OnSceneChanged 中检测到达后激活）
    private string? _pendingCustomSceneName;

    // 缓存被禁用的 TransitionPoint
    private readonly List<TransitionPoint> _disabledTransitionPoints = new();

    /// <summary>
    /// 需要在 DontDestroyOnLoad 场景中禁用渲染的对象路径
    /// </summary>
    private static readonly string[] GlobalRenderersToDisable =
    {
        "_GameCameras/CameraParent/tk2dCamera/Masker Blackout",
    };

    /// <summary>
    /// 一代未解析本地化文本 → (中文, 英文/默认) 文本映射
    /// Language.Get() 找不到键时返回 "!!Sheet/Key!!" 格式，以此作为匹配依据
    /// </summary>
    private static readonly Dictionary<string, (string zh, string fallback)> HK1TextMap = new()
    {
        { "!!Titles/ABSOLUTE_RADIANCE_MAIN!!", ("无上辐光", "Absolute Radiance") },
        { "!!Titles/ABSOLUTE_RADIANCE_SUPER!!", ("", "") },
    };

    private static readonly System.Reflection.BindingFlags PrivateInstanceFlags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    private static readonly System.Reflection.FieldInfo? MusicCueField =
        typeof(CustomSceneManager).GetField("musicCue", PrivateInstanceFlags);

    private static readonly System.Reflection.FieldInfo? AtmosCueField =
        typeof(CustomSceneManager).GetField("atmosCue", PrivateInstanceFlags);

    private static readonly System.Reflection.FieldInfo? MusicSnapshotField =
        typeof(CustomSceneManager).GetField("musicSnapshot", PrivateInstanceFlags);

    private static readonly System.Reflection.FieldInfo? OverrideColorSettingsField =
        typeof(CustomSceneManager).GetField("overrideColorSettings", PrivateInstanceFlags);

    #region 生命周期

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
            return;
        }

        SceneManager.activeSceneChanged += OnSceneChanged;
        Log.Info("[RadianceSceneManager] 初始化完成");
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        Instance = null;
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        // 返回主菜单时重置所有状态
        if (newScene.name == "Menu_Title" || newScene.name == "Quit_To_Menu")
        {
            ResetState();
            return;
        }

        // 检测是否已到达待进入的自定义场景
        if (
            !string.IsNullOrEmpty(_pendingCustomSceneName)
            && !string.IsNullOrEmpty(_targetSceneName)
            && newScene.name == _targetSceneName
        )
        {
            IsInCustomScene = true;
            CurrentCustomSceneName = _pendingCustomSceneName;
            _pendingCustomSceneName = null;

            Log.Info($"[RadianceSceneManager] 已进入目标场景: {newScene.name}");
            StartCoroutine(OnEnterTargetScene());
            return;
        }

        // 检测是否已返回原场景
        // 不禁用 TransitionPoint —— 引擎的 EnterScene 入场动画会正确处理碰撞
        if (_hasSavedReturnInfo && newScene.name == _returnSceneName && !IsInCustomScene)
        {
            Log.Info($"[RadianceSceneManager] 返回原场景: {newScene.name}");
            StartCoroutine(OnReturnToOriginalScene());
        }

        // 在 Belltown 场景中创建传送点（首次进入或从自定义场景返回均会触发）
        if (newScene.name == "Belltown")
        {
            CreateBelltownTransitionPoint();
        }
    }

    private void ResetState()
    {
        IsInCustomScene = false;
        CurrentCustomSceneName = null;
        _pendingCustomSceneName = null;
        _hasSavedReturnInfo = false;
        _disabledTransitionPoints.Clear();
        Log.Info("[RadianceSceneManager] 状态已重置");
    }

    #endregion

    #region 场景进入

    /// <summary>
    /// 在 Belltown 场景中动态创建传送点（isADoor=true 的 TransitionPoint）
    /// 玩家走入触发区后出现交互提示，按键确认后通过 EnterDoorSequence 进入 GG_Radiance
    /// </summary>
    private void CreateBelltownTransitionPoint()
    {
        // 先关闭 GameObject，防止 AddComponent<TransitionPoint> 时 Awake 立即执行
        // （Awake 中检查 isADoor 决定是否激活交互，必须在字段设置后再触发）
        var go = new GameObject("door_radiance");
        go.SetActive(false);
        go.transform.position = new Vector3(50f, 40.7f, 0f);

        // BoxCollider2D 作为触发区
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(2.13f, 0.2258f);
        collider.isTrigger = true;

        // 添加 TransitionPoint 组件（Awake 尚未执行）
        var tp = go.AddComponent<TransitionPoint>();

        // 设置关键字段
        tp.isADoor = true;                  // 启用交互提示，玩家需按键确认
        tp.targetScene = "GG_Radiance";     // 目标场景名
        tp.entryPoint = "door_radiance";    // 非空即可，保证 DoSceneTransition 不跳过
        tp.nonHazardGate = true;            // 不需要 HazardRespawnMarker
        tp.OnDoorEnter = new UnityEngine.Events.UnityEvent(); // 防止 EnterDoorSequence 中 NRE
        tp.interactLabel = InteractableBase.PromptLabels.Challenge;
        // skipSceneMapCheck 是 private 字段，必须通过反射设置
        // 如果不跳过，DoSceneTransition 会因 GG_Radiance 不在 SceneTeleportMap 中而回退到当前场景
        var skipField = typeof(TransitionPoint).GetField(
            "skipSceneMapCheck",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        skipField?.SetValue(tp, true);

        // 移动到当前活跃场景（确保随场景卸载而销毁）
        SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());

        // 激活：此时 Awake 运行，isADoor=true → EnableInteractableFields()=true → 交互系统激活
        go.SetActive(true);

        Log.Info("[RadianceSceneManager] 已在 Belltown 创建传送点 door_radiance");
        tp.interactLabel = InteractableBase.PromptLabels.Challenge;
        // 在传送点旁放置一个房屋背景作为视觉标识
        SetupBelltownTransitionDecor();
    }

    /// <summary>
    /// 克隆场景中的房屋背景对象放到传送点附近作为视觉标识，
    /// 并禁用原始父对象的 AmbientSway 组件防止晃动
    /// </summary>
    private void SetupBelltownTransitionDecor()
    {
        // 找到源对象：Hornet House States / Full / bg_bell_hang (1) / background_bell_generic_upright_after
        var sourceObj = GameObject.Find(
            "Hornet House States/Full/bg_bell_hang (1)/background_bell_generic_upright_after"
        );
        if (sourceObj == null)
        {
            Log.Warn("[RadianceSceneManager] 未找到房屋背景源对象，跳过装饰创建");
            return;
        }

        // 克隆并放置到传送点附近
        var decor = Instantiate(sourceObj);
        decor.name = "radiance_transition_decor";
        decor.transform.SetParent(null);
        decor.transform.position = new Vector3(50.0918f, 42.4555f, 0.1f);
        decor.transform.localScale = new Vector3(1f, 1f, 1f);

        // 设置金黄色调
        var sr = decor.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(1f, 0.7f, 0.2f, 1f);
        }

        // 移动到活跃场景（随场景卸载而销毁）
        SceneManager.MoveGameObjectToScene(decor, SceneManager.GetActiveScene());

        Log.Info("[RadianceSceneManager] 已创建传送点装饰: radiance_transition_decor");
    }

    /// <summary>
    /// 进入自定义场景
    /// </summary>
    public void EnterCustomScene(
        string customSceneName,
        Vector3 spawnPosition,
        string targetSceneName
    )
    {
        if (IsInCustomScene || !string.IsNullOrEmpty(_pendingCustomSceneName))
        {
            Log.Warn("[RadianceSceneManager] 已在自定义场景中或正在进入");
            return;
        }

        StartCoroutine(EnterCustomSceneRoutine(customSceneName, spawnPosition, targetSceneName));
    }

    private IEnumerator EnterCustomSceneRoutine(
        string customSceneName,
        Vector3 spawnPosition,
        string targetSceneName
    )
    {
        Log.Info($"[RadianceSceneManager] 开始进入自定义场景: {customSceneName}");

        var hero = HeroController.instance;
        if (hero == null || GameManager._instance == null)
        {
            Log.Error("[RadianceSceneManager] HeroController 或 GameManager 为空");
            yield break;
        }

        _targetSceneName = string.IsNullOrEmpty(targetSceneName) ? "GG_Radiance" : targetSceneName;

        // 保存返回信息（仅场景名+坐标）
        SaveReturnInfo();

        // 禁用玩家操作 + 播放进入动画
        DisablePlayerControl();
        yield return PlayEnterAnimation(hero);

        // 标记为"等待进入"（OnSceneChanged 检测到达后才设置 IsInCustomScene）
        _pendingCustomSceneName = customSceneName;

        // 根据目标场景选择加载方式
        if (_targetSceneName == "GG_Radiance")
        {
            // GG_Radiance 通过 AssetBundle 直接 Single 加载
            var assetManager = GetComponent<AssetManager>();
            if (assetManager == null)
            {
                Log.Error("[RadianceSceneManager] AssetManager 未找到");
                _pendingCustomSceneName = null;
                EnablePlayerControl();
                yield break;
            }

            // 淡出画面
            try
            {
                GameManager._instance?.screenFader_fsm?.SendEvent("SCENE FADE OUT");
            }
            catch
            {
                // 忽略淡出失败
            }

            // 等待淡出动画完成后再加载场景，避免可见的场景切换
            yield return new WaitForSeconds(0.5f);

            yield return assetManager.LoadRadianceSceneSingleAsync();
            Log.Info("[RadianceSceneManager] 加载 GG_Radiance 成功");
        }
        else
        {
            // 其他场景通过引擎标准转换
            try
            {
                GameManager._instance.BeginSceneTransition(
                    new GameManager.SceneLoadInfo
                    {
                        SceneName = _targetSceneName,
                        EntryGateName = "",
                        HeroLeaveDirection = GatePosition.unknown,
                        EntryDelay = 0f,
                        WaitForSceneTransitionCameraFade = true,
                        Visualization = GameManager.SceneLoadVisualizations.Default,
                        AlwaysUnloadUnusedAssets = false,
                    }
                );

                Log.Info($"[RadianceSceneManager] 传送到: {_targetSceneName}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RadianceSceneManager] 传送失败: {ex.Message}");
                _pendingCustomSceneName = null;
                _targetSceneName = "";
                EnablePlayerControl();
            }
        }
    }

    /// <summary>
    /// 由 SceneTransitionPatches prefix 调用：TransitionPoint 的 EnterDoorSequence 已完成
    /// 控制禁用、门动画和屏幕淡出，此方法仅需保存返回信息并启动 AssetBundle 加载
    /// </summary>
    public void EnterViaTransitionPoint()
    {
        if (IsInCustomScene || !string.IsNullOrEmpty(_pendingCustomSceneName))
        {
            Log.Warn("[RadianceSceneManager] 已在自定义场景中或正在进入");
            return;
        }

        // 保存返回信息
        SaveReturnInfo();

        // 设置目标和待进入标记
        _targetSceneName = "GG_Radiance";
        _pendingCustomSceneName = "GG_Radiance";

        // 启动 AssetBundle 加载（TransitionPoint 已处理淡出，无需重复）
        StartCoroutine(LoadFromTransitionPointRoutine());
    }

    /// <summary>
    /// TransitionPoint 触发的 AssetBundle 加载协程
    /// 不含控制禁用和动画播放（TransitionPoint.EnterDoorSequence 已处理）
    /// </summary>
    private IEnumerator LoadFromTransitionPointRoutine()
    {
        var assetManager = GetComponent<AssetManager>();
        if (assetManager == null)
        {
            Log.Error("[RadianceSceneManager] AssetManager 未找到");
            _pendingCustomSceneName = null;
            _targetSceneName = "";
            EnablePlayerControl();
            yield break;
        }

        // 等待资源预加载完成（通常已在启动时完成）
        if (!assetManager.IsPreloaded)
        {
            Log.Info("[RadianceSceneManager] 等待资源预加载...");
            while (assetManager != null && !assetManager.IsPreloaded)
            {
                yield return null;
            }

            if (assetManager == null)
            {
                Log.Error("[RadianceSceneManager] AssetManager 在等待预加载期间被销毁");
                _pendingCustomSceneName = null;
                _targetSceneName = "";
                EnablePlayerControl();
                yield break;
            }
        }

        // TransitionPoint.EnterDoorSequence 已执行淡出并等待了 0.5s，直接加载
        yield return assetManager.LoadRadianceSceneSingleAsync();
        Log.Info("[RadianceSceneManager] TransitionPoint 触发: GG_Radiance 加载成功");
    }

    #endregion

    #region 场景设置

    /// <summary>
    /// 进入目标场景后的初始化
    /// 手动设置英雄位置和状态，不调用引擎的 SetupSceneRefs/BeginScene/OnNextLevelReady
    /// （调用这些方法会触发 CSM.Start 劫持音频系统等严重副作用）
    /// </summary>
    private IEnumerator OnEnterTargetScene()
    {
        // 1. 处理不兼容的组件
        HandleProblematicComponents();

        // 2. 将一代 AudioSource 的 MixerGroup 替换为二代同名 Group
        RemapAudioMixerGroups();

        // 3. 创建最小化 CustomSceneManager（mapZone=MEMORY）
        CreateMinimalSceneManager();
        RefreshCustomSceneVisualState();

        // 3.5 保存 PreMemoryState（进入 MEMORY 区域时的物品/状态快照）
        // 退出时引擎的 EnteredNewMapZone 检测到 previousMapZone=MEMORY → 自动恢复
        SavePreMemoryState();

        // 4. 禁用 door_dreamEnter 的交互功能（防止玩家误触触发传送）
        //    isInactive=true 仅影响 EnableInteractableFields 的返回值，
        //    但 Awake 中已经根据旧值决定了激活状态，所以必须手动调用 Deactivate
        var existingTP = FindAnyObjectByType<TransitionPoint>();
        if (existingTP != null)
        {
            existingTP.isInactive = true;
            existingTP.Deactivate(false); // 设置 IsDisabled=true 并从 InteractManager 移除
            Log.Info($"[RadianceSceneManager] 已禁用 TransitionPoint 交互: {existingTP.gameObject.name}");
        }

        // 5. 禁用干扰渲染的全局对象
        DisableInterferingRenderers();

        // 6. 设置英雄出生点并初始化相机
        InitializeHeroAndCameraForCustomEntry();

        // 7. 等待一帧让场景渲染就绪
        yield return null;

        // 7.1 修复一代本地化文本（Start() 已运行，覆盖 TMPro 上的未解析文本）
        FixHK1LocalizationTexts();

        RefreshCustomSceneVisualState();
        yield return EnsureCustomSceneAudioState();

        // 7.2 强制应用一次开场相机锁区，避免镜头先锁在玩家身上
        ForceApplyInitialCameraLocks();

        // 8. 淡入场景
        FadeSceneIn();

        yield return new WaitForSeconds(0.5f);

        // 9. 恢复玩家控制
        EnablePlayerControl();

        // 10. 通知引擎场景入场完成
        // 设置 GameState=PLAYING + hasFinishedEnteringScene=true，
        // 确保引擎状态机正确运转（暂停菜单、死亡重生等依赖此标志）
        try
        {
            GameManager._instance?.FinishedEnteringScene();
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] FinishedEnteringScene 失败: {ex.Message}");
        }

        // 11. 重置 TransitionPoint.EnterDoorSequence 设置的 PlayerData 状态
        // disablePause=true 会阻止暂停菜单，isInvincible=true 会阻止玩家受伤
        // Y 键调试入口不会设置这些值，但 TransitionPoint 入口会，统一在此重置
        try
        {
            var pd = PlayerData.instance;
            if (pd != null)
            {
                pd.disablePause = false;
                pd.isInvincible = false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] 重置 PlayerData 状态失败: {ex.Message}");
        }

        Log.Info("[RadianceSceneManager] 自定义场景设置完成");
    }

    /// <summary>
    /// 处理一代场景中与二代引擎不兼容的组件
    /// 必须在引擎初始化流程之前调用
    /// </summary>
    private void HandleProblematicComponents()
    {
        // 只禁用组件 + 通过反射设 restoreBindingsOnDestroy=false 防止场景卸载时 NRE
        var bossSceneCtrl = FindAnyObjectByType<BossSceneController>();
        if (bossSceneCtrl != null)
        {
            var field = typeof(BossSceneController).GetField(
                "restoreBindingsOnDestroy",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            field?.SetValue(bossSceneCtrl, false);

            Log.Info("[RadianceSceneManager] 已禁用 BossSceneController（保留 door_dreamEnter）");
        }
    }

    /// <summary>
    /// 修复一代场景中未解析的本地化文本。
    /// 必须在 yield return null 之后调用（此时 SetTextMeshProGameText.Start() 已运行，
    /// TMPro 上已被设置了 "!!Sheet/Key!!" 格式的未解析文本）。
    /// 遍历场景所有组件，按类型名 TextMeshPro 筛选后通过反射读写 text 属性。
    /// </summary>
    private void FixHK1LocalizationTexts()
    {
        bool isChinese = Language.CurrentLanguage() == LanguageCode.ZH;
        int fixedCount = 0;

        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp.GetType().Name != "TextMeshPro")
                    continue;

                var textProp = comp.GetType().GetProperty("text",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (textProp == null)
                    continue;

                if (textProp.GetValue(comp) is not string currentText || string.IsNullOrEmpty(currentText))
                    continue;

                if (!HK1TextMap.TryGetValue(currentText, out var texts))
                    continue;

                string replacement = isChinese ? texts.zh : texts.fallback;
                textProp.SetValue(comp, replacement);
                fixedCount++;

                Log.Info(
                    $"[RadianceSceneManager] 已修复 HK1 本地化文本: \"{currentText}\" → \"{replacement}\""
                    + $" (on {comp.gameObject.name})"
                );
            }
        }

        if (fixedCount == 0)
        {
            Log.Warn("[RadianceSceneManager] 未找到需要修复的 HK1 本地化文本");
        }
    }

    /// <summary>
    /// 将一代 AssetBundle 带入的 AudioMixer 的输出嫁接到二代 Mixer 链上，
    /// 使所有使用一代 Mixer 的 AudioSource（包括运行时动态生成的）都受游戏音量设置控制。
    /// 原理：找到一代 Mixer，将其 outputAudioMixerGroup 设为同名二代 Mixer 的 outputAudioMixerGroup，
    /// 这样一代 Mixer 的输出直接接入二代链条，无需逐个替换 AudioSource。
    /// </summary>
    private void RemapAudioMixerGroups()
    {
        var gm = GameManager._instance;
        if (gm == null)
        {
            Log.Warn("[RadianceSceneManager] GameManager 为空，跳过 AudioMixer 重映射");
            return;
        }

        // 1. 从 GameManager 的 Snapshot 字段收集所有二代 AudioMixer（按名称索引）
        var hk2MixersByName = new Dictionary<string, AudioMixer>();

        AudioMixerSnapshot?[] snapshots =
        {
            gm.actorSnapshotUnpaused,
            gm.actorSnapshotPaused,
            gm.silentSnapshot,
            gm.noMusicSnapshot,
            gm.noAtmosSnapshot,
            gm.soundOptionsDefaultSnaphot,
        };

        foreach (var snapshot in snapshots)
        {
            if (snapshot == null || snapshot.audioMixer == null)
                continue;

            var mixer = snapshot.audioMixer;
            hk2MixersByName.TryAdd(mixer.name, mixer);
        }

        if (hk2MixersByName.Count == 0)
        {
            Log.Warn("[RadianceSceneManager] 未发现任何二代 AudioMixer，跳过重映射");
            return;
        }

        // 2. 从场景 AudioSource 收集一代 Mixer 实例（不在二代集合中的）
        var hk2MixerSet = new HashSet<AudioMixer>(hk2MixersByName.Values);
        var hk1Mixers = new HashSet<AudioMixer>();

        var activeScene = SceneManager.GetActiveScene();
        foreach (var root in activeScene.GetRootGameObjects())
        {
            foreach (var src in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (src.outputAudioMixerGroup == null)
                    continue;

                var mixer = src.outputAudioMixerGroup.audioMixer;
                if (mixer != null && !hk2MixerSet.Contains(mixer))
                {
                    hk1Mixers.Add(mixer);
                }
            }
        }

        if (hk1Mixers.Count == 0)
        {
            Log.Info("[RadianceSceneManager] 未发现一代 AudioMixer，无需重映射");
            return;
        }

        // 3. 对每个一代 Mixer，将其输出嫁接到同名二代 Mixer 的输出目标
        int graftedCount = 0;

        foreach (var hk1Mixer in hk1Mixers)
        {
            if (!hk2MixersByName.TryGetValue(hk1Mixer.name, out var hk2Mixer))
            {
                Log.Warn(
                    $"[RadianceSceneManager] 一代 Mixer '{hk1Mixer.name}' 在二代中找不到同名 Mixer"
                );
                continue;
            }

            var hk2Output = hk2Mixer.outputAudioMixerGroup;
            hk1Mixer.outputAudioMixerGroup = hk2Output;
            graftedCount++;

            var outputDesc = hk2Output != null
                ? $"'{hk2Output.name}' (Mixer: '{hk2Output.audioMixer?.name}')"
                : "null (root)";
            Log.Info(
                $"[RadianceSceneManager] Mixer 嫁接: '{hk1Mixer.name}' → {outputDesc}"
            );
        }

        Log.Info($"[RadianceSceneManager] AudioMixer 重映射完成: 嫁接={graftedCount} 个 Mixer");
    }

    /// <summary>
    /// 在目标场景中创建最小化的 CustomSceneManager，设置 mapZone=MEMORY
    /// 使引擎原生的 Memory 场景机制生效（死亡非致命、PreMemoryState 自动保存/恢复）
    /// </summary>
    private void CreateMinimalSceneManager()
    {
        var gm = GameManager._instance;
        if (gm == null)
        {
            Log.Warn("[RadianceSceneManager] GameManager 不存在，无法创建 SceneManager");
            return;
        }

        // 检查场景内是否已有 CustomSceneManager
        var existingSm = FindAnyObjectByType<CustomSceneManager>();
        if (existingSm != null)
        {
            // 已有则直接修改 mapZone
            existingSm.mapZone = MapZone.MEMORY;
            existingSm.sceneType = SceneType.GAMEPLAY;
            gm.sm = existingSm;
            Log.Info("[RadianceSceneManager] 使用场景内已有的 CustomSceneManager，已设置为 MEMORY");
            return;
        }

        // 创建新的最小化 SceneManager
        // 先禁用 GameObject，防止 AddComponent 时立即触发 Awake（序列化字段未初始化会 NRE）
        var smObj = new GameObject("_SceneManager (Radiance)");
        smObj.tag = "SceneManager";
        smObj.SetActive(false);

        var sm = smObj.AddComponent<CustomSceneManager>();

        // 初始化序列化字段（避免 Awake/Start 中的 NRE）
        sm.scenePools = new SceneObjectPool[0];
        sm.sceneBordersMask = 0; // 不绘制边界
        // borderPrefab 在 Start->DrawBlackBorders 的第一行被访问，必须非空
        var dummyBorder = new GameObject("_DummyBorder");
        dummyBorder.SetActive(false);
        dummyBorder.transform.SetParent(smObj.transform);
        sm.borderPrefab = dummyBorder;

        // 视觉默认值：贴近原场景 _SceneManager 参数，避免出现灰白偏色
        // Default Color: #C1A67E (193,166,126)
        // Hero Light Color: #FFE3B9
        // Saturation: 0.9, Darkness: -1, EnvironmentType: Dust(0)
        sm.defaultColor = new Color32(193, 166, 126, byte.MaxValue);
        sm.defaultIntensity = 1f;
        sm.heroLightColor = new Color32(byte.MaxValue, 227, 185, 28);
        sm.saturation = 0.9f;
        sm.redChannel = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        sm.greenChannel = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        sm.blueChannel = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        sm.darknessLevel = -1;
        sm.environmentType = EnvironmentTypes.Dust;
        sm.isWindy = false;
        sm.blurPlaneVibranceOffset = 0f;
        OverrideColorSettingsField?.SetValue(sm, true);

        sm.mapZone = MapZone.MEMORY;
        sm.sceneType = SceneType.GAMEPLAY;

        // 赋值给 GameManager
        gm.sm = sm;

        // 移动到当前活跃场景
        SceneManager.MoveGameObjectToScene(smObj, SceneManager.GetActiveScene());

        // 激活：此时 Awake 才会运行，序列化字段已就绪
        smObj.SetActive(true);

        Log.Info("[RadianceSceneManager] 已创建最小化 CustomSceneManager (mapZone=MEMORY)");
    }

    private void RefreshCustomSceneVisualState()
    {
        var sm = GameManager._instance?.sm;
        if (sm == null)
            return;

        try
        {
            sm.UpdateScene();
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] 刷新场景视觉状态失败: {ex.Message}");
        }
    }

    private static bool HasCustomSceneAudioConfig(CustomSceneManager sm)
    {
        if (sm.atmosSnapshot != null || sm.enviroSnapshot != null || sm.actorSnapshot != null || sm.shadeSnapshot != null)
        {
            return true;
        }

        return MusicCueField?.GetValue(sm) != null
            || AtmosCueField?.GetValue(sm) != null
            || MusicSnapshotField?.GetValue(sm) != null;
    }

    private IEnumerator EnsureCustomSceneAudioState()
    {
        var gm = GameManager._instance;
        var sm = gm?.sm;
        var cameraController = GameCameras.instance?.cameraController;

        if (cameraController != null)
        {
            // 关键点：CSM.Start 订阅 PositionedAtHero 后，再触发一次保证音频回调执行
            cameraController.PositionToHero(forceDirect: true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        if (gm == null || sm == null || gm.AudioManager == null)
            yield break;

        if (sm.IsAudioSnapshotsApplied)
            yield break;

        if (!HasCustomSceneAudioConfig(sm))
        {
            // 自定义场景没有自己的音频配置时，主动清理上一场景残留的音乐/氛围
            gm.AudioManager.StopAndClearMusic();
            gm.AudioManager.StopAndClearAtmos();
            AudioManager.CustomSceneManagerReady();
            Log.Info("[RadianceSceneManager] 未检测到场景音频配置，已清理旧场景音乐/氛围");
            yield break;
        }

        if (cameraController != null)
        {
            cameraController.PositionToHero(forceDirect: true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        if (!sm.IsAudioSnapshotsApplied)
        {
            AudioManager.CustomSceneManagerReady();
            Log.Warn("[RadianceSceneManager] 场景音频回调未触发，已执行 Ready 兜底");
        }
    }

    #endregion

    #region 场景清理

    /// <summary>
    /// 清理自定义场景状态（被 SceneTransitionPatches 调用）
    /// 仅执行 MOD 状态重置，不修改引擎场景转换目标
    /// </summary>
    public void CleanupCustomScene()
    {
        if (!IsInCustomScene)
            return;

        Log.Info("[RadianceSceneManager] 执行自定义场景清理");

        // 标记离开自定义场景
        IsInCustomScene = false;
        CurrentCustomSceneName = null;

        // 恢复干扰渲染器
        EnableInterferingRenderers();

        // 清理 Boss 残留在 DontDestroyOnLoad 场景中的克隆对象
        DestroyBossLeftovers();

        // 不在此处卸载 AssetBundle：场景转换期间资源仍在使用，bundle 保持加载可供复用
    }

    #endregion

    #region 场景退出

    /// <summary>
    /// 主动退出自定义场景（Boss 击败/手动退出时调用）
    /// 直接触发 BeginSceneTransition（SceneName 已设为 returnScene），
    /// SceneTransitionPatches 的 prefix 会自动执行 CleanupCustomScene 清理 MOD 状态
    /// </summary>
    public void ExitCustomScene()
    {
        if (!IsInCustomScene)
        {
            Log.Warn("[RadianceSceneManager] 不在自定义场景中");
            return;
        }

        if (!_hasSavedReturnInfo || GameManager._instance == null)
        {
            Log.Error("[RadianceSceneManager] 无返回信息或 GameManager 为空");
            return;
        }

        Log.Info($"[RadianceSceneManager] 退出自定义场景，目标: {_returnSceneName}");

        // 直接触发场景转换
        // SceneTransitionPatches prefix 会执行 CleanupCustomScene()（仅清理，不改目标场景）
        try
        {
            GameManager._instance.BeginSceneTransition(
                new GameManager.SceneLoadInfo
                {
                    SceneName = _returnSceneName,
                    // 使用 door_radiance 让引擎精确匹配 Belltown 中我们创建的 TransitionPoint
                    // OnSceneChanged 先创建 door_radiance TP → FindTransitionPoint 匹配 → hero.EnterScene 门入场动画
                    EntryGateName = "door_radiance",
                    HeroLeaveDirection = GatePosition.unknown,
                    EntryDelay = 0f,
                    WaitForSceneTransitionCameraFade = true,
                    Visualization = GameManager.SceneLoadVisualizations.Default,
                    AlwaysUnloadUnusedAssets = false,
                }
            );
        }
        catch (Exception ex)
        {
            Log.Error($"[RadianceSceneManager] 退出失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 返回原场景后的处理
    /// 引擎的入场流程会完整处理英雄状态、相机、控制等：
    /// - 手动退出：使用非空 EntryGateName → FindTransitionPoint fallback → hero.EnterScene 完整入场动画
    /// - 死亡退出：RespawningHero=true → hero.Respawn 完整重生流程
    /// 我们只需清理 MOD 内部标志
    /// </summary>
    private IEnumerator OnReturnToOriginalScene()
    {
        // 等待引擎完成入场流程
        var gm = GameManager._instance;
        if (gm != null)
        {
            float timeout = 5f;
            while (!gm.hasFinishedEnteringScene && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        // 恢复 HUD（血条、资源条等）
        try
        {
            GameCameras.instance?.HUDIn();
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] HUDIn 失败: {ex.Message}");
        }

        // 清除返回信息
        _hasSavedReturnInfo = false;

        Log.Info("[RadianceSceneManager] 已返回原场景");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// Boss 残留对象名称（这些对象可能被对象池生成到 DontDestroyOnLoad 场景根节点）
    /// </summary>
    private static readonly HashSet<string> BossLeftoverNames = new()
    {
        "Radiant Nail Comb(Clone)",
        "Radiant Beam R(Clone)",
        "Radiant Beam L(Clone)",
        "Radiant Nail(Clone)",
    };

    /// <summary>
    /// 清理 Boss 战残留在 DontDestroyOnLoad 场景根节点的克隆对象。
    /// 通过 DDOL 场景的 GetRootGameObjects 一次性遍历，O(n) 完成。
    /// </summary>
    private void DestroyBossLeftovers()
    {
        // this（RadianceSceneManager）本身就在 DontDestroyOnLoad 场景中
        var ddolScene = gameObject.scene;
        var rootObjects = ddolScene.GetRootGameObjects();

        int destroyedCount = 0;
        foreach (var obj in rootObjects)
        {
            if (BossLeftoverNames.Contains(obj.name))
            {
                Destroy(obj);
                destroyedCount++;
            }
        }

        if (destroyedCount > 0)
        {
            Log.Info($"[RadianceSceneManager] 已清理 {destroyedCount} 个 Boss 残留对象");
        }
    }

    private void SaveReturnInfo()
    {
        var currentScene = SceneManager.GetActiveScene();
        _returnSceneName = currentScene.name;

        var hero = HeroController.instance;
        if (hero != null)
        {
            _returnPosition = hero.transform.position;
        }

        _hasSavedReturnInfo = true;
        Log.Info($"[RadianceSceneManager] 保存返回信息: {_returnSceneName} at {_returnPosition}");
    }


    private void SavePreMemoryState()
    {
        var hero = HeroController.instance;
        var pd = PlayerData.instance;
        if (hero == null || pd == null)
            return;

        if (!pd.HasStoredMemoryState)
        {
            pd.PreMemoryState = HeroItemsState.Record(hero);
            pd.HasStoredMemoryState = true;
            pd.CaptureToolAmountsOverride();
            EventRegister.SendEvent("END FOLLOWERS INSTANT");
            hero.MaxHealthKeepBlue();
            Log.Info("[RadianceSceneManager] 已保存 PreMemoryState");
        }
    }

    private void DisablePlayerControl()
    {
        var hero = HeroController.instance;
        if (hero == null)
            return;

        GameManager._instance?.inputHandler?.StopAcceptingInput();
        hero.RelinquishControl();
        hero.StopAnimationControl();
    }

    private void EnablePlayerControl()
    {
        var hero = HeroController.instance;
        if (hero == null)
            return;

        GameManager._instance?.inputHandler?.StartAcceptingInput();
        hero.RegainControl();
        hero.StartAnimationControl();
    }

    private IEnumerator PlayEnterAnimation(HeroController hero)
    {
        var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();
        if (tk2dAnimator != null)
        {
            tk2dAnimator.Play("Abyss Kneel");
            yield return new WaitForSeconds(0.8f);
            tk2dAnimator.Play("Kneel To Prostrate");
            yield return new WaitForSeconds(1f);
        }
        else
        {
            yield return new WaitForSeconds(1.8f);
        }
    }

    private void FadeSceneIn()
    {
        try
        {
            GameManager._instance?.screenFader_fsm?.SendEvent("SCENE FADE IN");
        }
        catch (Exception ex)
        {
            Log.Error($"[RadianceSceneManager] 屏幕淡入失败: {ex.Message}");
        }
    }

    private void InitializeHeroAndCameraForCustomEntry()
    {
        var hero = HeroController.instance;
        if (hero == null)
        {
            Log.Warn("[RadianceSceneManager] HeroController 不存在，跳过开场镜头初始化");
            return;
        }
        hero.SceneInit();

        var gameCameras = GameCameras.instance;
        if (gameCameras == null)
        {
            Log.Warn("[RadianceSceneManager] GameCameras 不存在，无法同步开场镜头");
            return;
        }

        gameCameras.SceneInit();

        var cameraController = gameCameras.cameraController;
        if (cameraController == null)
        {
            Log.Warn("[RadianceSceneManager] CameraController 不存在，无法同步开场镜头");
            return;
        }

        cameraController.ResetStartTimer();
        cameraController.PositionToHero(forceDirect: true);
    }

    private void ForceApplyInitialCameraLocks()
    {
        var hero = HeroController.instance;
        var cameraController = GameCameras.instance?.cameraController;
        if (hero == null || cameraController == null)
        {
            return;
        }

        var heroPosition = (Vector2)hero.transform.position;
        var lockAreas = FindObjectsByType<CameraLockArea>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        int appliedCount = 0;
        foreach (var lockArea in lockAreas)
        {
            if (lockArea == null || !lockArea.isActiveAndEnabled)
            {
                continue;
            }

            var collider = lockArea.GetComponent<Collider2D>();
            if (collider == null || !collider.enabled || !collider.OverlapPoint(heroPosition))
            {
                continue;
            }

            cameraController.LockToArea(lockArea);
            appliedCount++;
        }

        if (appliedCount > 0)
        {
            Log.Info($"[RadianceSceneManager] 开场强制应用 CameraLockArea: {appliedCount}");
        }
    }

    private void SetRendererEnabled(string path, bool enabled)
    {
        var obj = GameObject.Find(path);
        if (obj == null)
            return;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = enabled;
        }
    }

    private void DisableInterferingRenderers()
    {
        foreach (var path in GlobalRenderersToDisable)
        {
            SetRendererEnabled(path, false);
        }
    }

    private void EnableInterferingRenderers()
    {
        foreach (var path in GlobalRenderersToDisable)
        {
            SetRendererEnabled(path, true);
        }
    }

    #endregion
}
