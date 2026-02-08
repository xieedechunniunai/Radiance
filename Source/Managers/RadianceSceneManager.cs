using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Radiance.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Radiance.Managers;

/// <summary>
/// Radiance 场景管理器
/// 负责加载一代场景内容到二代游戏中，并处理场景清理和传送
/// </summary>
public class RadianceSceneManager : MonoBehaviour
{
    public static RadianceSceneManager? Instance { get; private set; }

    /// <summary>
    /// 是否在自定义场景中
    /// </summary>
    public static bool IsInCustomScene { get; private set; } = false;

    /// <summary>
    /// 当前自定义场景名称
    /// </summary>
    public static string? CurrentCustomSceneName { get; private set; }

    // 返回位置信息
    private string _returnSceneName = "";
    private Vector3 _returnPosition;
    private bool _hasSavedReturnInfo = false;

    // 进入自定义场景时的出生点
    private Vector3 _customSpawnPosition;

    // 直接加载目标场景名称
    private string _targetSceneName = "";

    // 缓存被禁用的 TransitionPoint
    private readonly List<TransitionPoint> _disabledTransitionPoints = new();

    // 保存的玩家数据
    private PlayerDataSnapshot? _savedPlayerData;

    public static bool SuppressSceneManagerGlobalWrites { get; private set; }

    /// <summary>
    /// 需要在 DontDestroyOnLoad 场景中禁用渲染的对象路径
    /// </summary>
    private static readonly string[] GlobalRenderersToDisable = new[]
    {
        "_GameCameras/CameraParent/tk2dCamera/Masker Blackout",
    };

    private static float? _customBlurNearOverride;
    private static int? _customBlurMaskOverride;

    // 保存 EnsureBlurPlaneVisibility 修改前的相机 cullingMask，退出时恢复
    private Camera? _savedSceneCamera;
    private int _savedSceneCameraMask;
    private Camera? _savedBgCamera;
    private int _savedBgCameraMask;
    private bool _hasSavedCameraState;

    /// <summary>
    /// 需要在场景加载后立即删除的 GameObject 名称
    /// </summary>
    private static readonly string[] ObjectsToDestroy = new[] { "" };
    //Boss Scene Unlocker
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
        SceneManager.sceneLoaded += OnSceneLoaded;
        Log.Info("[RadianceSceneManager] 初始化完成");
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    /// <summary>
    /// 场景加载完成时立即删除不需要的物体（在 FSM Start 之前）
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsInCustomScene && scene.name == _targetSceneName)
        {
            DestroyUnwantedObjects();
        }
    }
    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        // 返回主菜单时重置状态
        if (newScene.name == "Menu_Title" || newScene.name == "Quit_To_Menu")
        {
            ResetState();
            return;
        }

        // 如果正在进入自定义场景：检测是否已切换到目标场景
        if (
            IsInCustomScene
            && !string.IsNullOrEmpty(CurrentCustomSceneName)
            && !string.IsNullOrEmpty(_targetSceneName)
            && newScene.name == _targetSceneName
        )
        {
            Log.Info($"[RadianceSceneManager] 已进入目标场景: {newScene.name}");
            StartCoroutine(OnEnterTargetScene());
        }

        // 如果从自定义场景返回
        if (_hasSavedReturnInfo && newScene.name == _returnSceneName && !IsInCustomScene)
        {
            Log.Info($"[RadianceSceneManager] 返回原场景: {newScene.name}");
            StartCoroutine(OnReturnToOriginalScene());
        }
    }

    /// <summary>
    /// 进入目标场景后的处理
    /// </summary>
    private IEnumerator OnEnterTargetScene()
    {
        yield return new WaitForSeconds(0.5f);

        // 禁用干扰渲染的对象
        DisableInterferingRenderers();

        // 恢复基础设施
        var cache = SceneInfrastructureCache.Instance;
        try
        {
            if (cache != null && cache.HasCache)
            {
                cache.RestoreInfrastructureToScene();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] 恢复基础设施失败: {ex.Message}");
        }

        // 禁用场景切换点
        DisableAllTransitionPoints();

        // 设置玩家位置
        var hero = HeroController.instance;
        if (hero != null)
        {
            hero.transform.position = _customSpawnPosition;
            var rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.zero;

            try
            {
                hero.SceneInit();
            }
            catch (Exception ex)
            {
                Log.Warn(
                    $"[RadianceSceneManager] 调用 HeroController.SceneInit 失败: {ex.Message}"
                );
            }
        }

        // 淡入场景
        FadeSceneIn();

        // 确保 BlurPlane 层可被主相机渲染，同时被背景相机排除
        EnsureBlurPlaneVisibility();
        yield return new WaitForSeconds(0.5f);

        // 恢复玩家控制
        EnablePlayerControl();



        Log.Info("[RadianceSceneManager] 自定义场景设置完成");
    }

    private void ResetState()
    {
        IsInCustomScene = false;
        CurrentCustomSceneName = null;
        _hasSavedReturnInfo = false;
        _savedPlayerData = null;
        _disabledTransitionPoints.Clear();
        ClearCustomBlurOverrides();
        _hasSavedCameraState = false;
        _savedSceneCamera = null;
        _savedBgCamera = null;
        Log.Info("[RadianceSceneManager] 状态已重置");
    }

    #region 场景进入
    /// <summary>
    /// 进入自定义场景
    /// </summary>
    public void EnterCustomScene(
        string customSceneName,
        Vector3 spawnPosition,
        string targetSceneName
    )
    {
        if (IsInCustomScene)
        {
            Log.Warn("[RadianceSceneManager] 已在自定义场景中");
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

        SaveReturnInfo();
        SavePlayerData();

        var activeSceneName = SceneManager.GetActiveScene().name;
        var radianceScene = SceneManager.GetSceneByName("GG_Radiance");
        var assetManagerForEnter = GetComponent<AssetManager>();
        bool bundleLoadedForEnter = assetManagerForEnter != null && assetManagerForEnter.IsBundleLoaded;
        DisablePlayerControl();

        yield return PlayEnterAnimation(hero);

        IsInCustomScene = true;
        CurrentCustomSceneName = customSceneName;
        _customSpawnPosition = spawnPosition;

        SceneInfrastructureCache.Instance?.CacheCurrentSceneInfrastructure();

        if (_targetSceneName == "GG_Radiance")
        {
            var assetManager = GetComponent<AssetManager>();
            if (assetManager == null)
            {
                Log.Error("[RadianceSceneManager] AssetManager 未找到");
                yield break;
            }

            try
            {
                GameManager._instance?.screenFader_fsm?.SendEvent("SCENE FADE OUT");
            }
            catch { }

            yield return assetManager.LoadRadianceSceneSingleAsync();

            Log.Info("[RadianceSceneManager] 加载 GG_Radiance 成功");
        }
        else
        {
            try
            {
                GameManager._instance.BeginSceneTransition(
                    new GameManager.SceneLoadInfo
                    {
                        SceneName = _targetSceneName,
                        EntryGateName = "",
                        HeroLeaveDirection = GlobalEnums.GatePosition.unknown,
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
                IsInCustomScene = false;
                CurrentCustomSceneName = null;
                _targetSceneName = "";
                EnablePlayerControl();
            }
        }
    }
    #endregion


    #region 场景退出
    /// <summary>
    /// 退出自定义场景，返回原场景
    /// </summary>
    public void ExitCustomScene()
    {
        if (!IsInCustomScene)
        {
            Log.Warn("[RadianceSceneManager] 不在自定义场景中");
            return;
        }

        StartCoroutine(ExitCustomSceneRoutine());
    }

    private IEnumerator ExitCustomSceneRoutine()
    {
        Log.Info("[RadianceSceneManager] 开始退出自定义场景");

        var hero = HeroController.instance;
        if (hero == null || GameManager._instance == null)
        {
            Log.Error("[RadianceSceneManager] HeroController 或 GameManager 为空");
            yield break;
        }

        DisablePlayerControl();

        yield return PlayExitAnimation(hero);

        var activeSceneName = SceneManager.GetActiveScene().name;
        var radianceScene = SceneManager.GetSceneByName("GG_Radiance");
        var assetManagerForExit = GetComponent<AssetManager>();
        bool bundleLoadedForExit = assetManagerForExit != null && assetManagerForExit.IsBundleLoaded;
        try
        {
            SceneInfrastructureCache.Instance?.CacheCurrentSceneInfrastructure();
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] 退出前缓存基础设施失败: {ex.Message}");
        }

        if (_hasSavedReturnInfo)
        {
            try
            {
                GameManager._instance.BeginSceneTransition(
                    new GameManager.SceneLoadInfo
                    {
                        SceneName = _returnSceneName,
                        EntryGateName = "",
                        HeroLeaveDirection = GlobalEnums.GatePosition.unknown,
                        EntryDelay = 0f,
                        WaitForSceneTransitionCameraFade = true,
                        Visualization = GameManager.SceneLoadVisualizations.Default,
                        AlwaysUnloadUnusedAssets = false,
                    }
                );

                Log.Info($"[RadianceSceneManager] 返回 {_returnSceneName}");
            }
            catch (Exception ex)
            {
                Log.Error($"[RadianceSceneManager] 返回失败: {ex.Message}");
            }
        }

        IsInCustomScene = false;
        CurrentCustomSceneName = null;
    }

    /// <summary>
    /// 返回原场景后的处理
    /// </summary>
    public IEnumerator OnReturnToOriginalScene()
    {
        yield return new WaitForSeconds(0.3f);

        var activeSceneName = SceneManager.GetActiveScene().name;
        var radianceScene = SceneManager.GetSceneByName("GG_Radiance");
        RestoreCameraState();
        EnableInterferingRenderers();

        var hero = HeroController.instance;
        if (hero == null)
            yield break;

        try
        {
            var cache = SceneInfrastructureCache.Instance;
            if (cache != null && cache.HasCache)
            {
                cache.RestoreInfrastructureToScene();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[RadianceSceneManager] 返回后恢复基础设施失败: {ex.Message}");
        }

        var assetManager = GetComponent<AssetManager>();
        if (assetManager != null)
        {
            assetManager.UnloadBundleKeepLoadedAssets();
        }

        RestorePlayerData();

        if (_hasSavedReturnInfo)
        {
            hero.transform.position = _returnPosition;
            var rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.zero;
        }

        FadeSceneIn();

        yield return new WaitForSeconds(0.3f);

        EnableAllTransitionPoints();
        EnablePlayerControl();

        _hasSavedReturnInfo = false;
        Log.Info("[RadianceSceneManager] 已返回原场景");
    }
    #endregion


    #region 玩家数据保存/恢复
    private class PlayerDataSnapshot
    {
        public bool AtBench;
        public string RespawnScene = "";
        public string RespawnMarkerName = "";
        public int RespawnType;
        public HazardRespawnMarker.FacingDirection HazardRespawnFacing;
        public string? TempRespawnScene;
        public string? TempRespawnMarker;
        public int TempRespawnType;
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

    private void SavePlayerData()
    {
        try
        {
            var pd = GameManager.instance?.playerData;
            if (pd == null)
                return;

            _savedPlayerData = new PlayerDataSnapshot
            {
                AtBench = pd.atBench,
                RespawnScene = pd.respawnScene ?? "",
                RespawnMarkerName = pd.respawnMarkerName ?? "",
                RespawnType = pd.respawnType,
                HazardRespawnFacing = pd.hazardRespawnFacing,
                TempRespawnScene = pd.tempRespawnScene,
                TempRespawnMarker = pd.tempRespawnMarker,
                TempRespawnType = pd.tempRespawnType,
            };

            Log.Info("[RadianceSceneManager] 玩家数据已保存");
        }
        catch (Exception ex)
        {
            Log.Error($"[RadianceSceneManager] 保存玩家数据失败: {ex.Message}");
        }
    }

    private void RestorePlayerData()
    {
        if (_savedPlayerData == null)
            return;

        try
        {
            var pd = GameManager.instance?.playerData;
            if (pd == null)
                return;

            pd.atBench = _savedPlayerData.AtBench;
            pd.respawnScene = _savedPlayerData.RespawnScene;
            pd.respawnMarkerName = _savedPlayerData.RespawnMarkerName;
            pd.respawnType = _savedPlayerData.RespawnType;
            pd.hazardRespawnFacing = _savedPlayerData.HazardRespawnFacing;
            pd.tempRespawnScene = _savedPlayerData.TempRespawnScene;
            pd.tempRespawnMarker = _savedPlayerData.TempRespawnMarker;
            pd.tempRespawnType = _savedPlayerData.TempRespawnType;

            _savedPlayerData = null;
            Log.Info("[RadianceSceneManager] 玩家数据已恢复");
        }
        catch (Exception ex)
        {
            Log.Error($"[RadianceSceneManager] 恢复玩家数据失败: {ex.Message}");
        }
    }
    #endregion


    #region 辅助方法


    /// <summary>
    /// 删除不需要的 GameObject
    /// </summary>
    private void DestroyUnwantedObjects()
    {
        foreach (var objName in ObjectsToDestroy)
        {
            var obj = GameObject.Find(objName);
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
                Log.Info($"[RadianceSceneManager] 已删除 GameObject: {objName}");
            }
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

    private IEnumerator PlayExitAnimation(HeroController hero)
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

    private void DisableAllTransitionPoints()
    {
        _disabledTransitionPoints.Clear();
        var transitionPoints = FindObjectsByType<TransitionPoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var tp in transitionPoints)
        {
            if (tp != null && tp.gameObject.activeSelf)
            {
                _disabledTransitionPoints.Add(tp);
                tp.gameObject.SetActive(false);
            }
        }

        Log.Info(
            $"[RadianceSceneManager] 已禁用 {_disabledTransitionPoints.Count} 个 TransitionPoint"
        );
    }

    private void EnableAllTransitionPoints()
    {
        foreach (var tp in _disabledTransitionPoints)
        {
            if (tp != null)
            {
                tp.gameObject.SetActive(true);
            }
        }

        Log.Info(
            $"[RadianceSceneManager] 已启用 {_disabledTransitionPoints.Count} 个 TransitionPoint"
        );
        _disabledTransitionPoints.Clear();
    }

    /// <summary>
    /// 禁用指定对象的渲染组件
    /// </summary>
    private void DisableRenderers(string path)
    {
        var obj = GameObject.Find(path);
        if (obj == null)
            return;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = false;
            Log.Info($"[RadianceSceneManager] 已禁用 Renderer: {path}");
        }
    }

    /// <summary>
    /// 启用指定对象的渲染组件
    /// </summary>
    private void EnableRenderers(string path)
    {
        var obj = GameObject.Find(path);
        if (obj == null)
            return;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = true;
            Log.Info($"[RadianceSceneManager] 已启用 Renderer: {path}");
        }
    }

    private void DisableInterferingRenderers()
    {
        foreach (var path in GlobalRenderersToDisable)
        {
            DisableRenderers(path);
        }
    }

    private void EnableInterferingRenderers()
    {
        foreach (var path in GlobalRenderersToDisable)
        {
            EnableRenderers(path);
        }
    }

    private static T? TryGetField<T>(object obj, string fieldName) where T : class
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(obj) as T;
    }

    private void EnsureBlurPlaneVisibility()
    {
        var blurPlanes = FindObjectsByType<BlurPlane>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (blurPlanes.Length == 0)
        {
            return;
        }

        var blurPlaneLayerMask = 0;
        foreach (var plane in blurPlanes)
        {
            if (plane == null)
            {
                continue;
            }

            blurPlaneLayerMask |= 1 << plane.gameObject.layer;
        }

        if (blurPlaneLayerMask == 0)
        {
            return;
        }

        var blurBackground = FindAnyObjectByType<LightBlurredBackground>();
        if (blurBackground == null)
        {
            return;
        }

        var sceneCamera = TryGetField<Camera>(blurBackground, "sceneCamera") ?? Camera.main;
        var backgroundCamera = TryGetField<Camera>(blurBackground, "backgroundCamera");

        // 保存修改前的相机状态，退出时恢复
        _savedSceneCamera = sceneCamera;
        _savedBgCamera = backgroundCamera;
        if (sceneCamera != null) _savedSceneCameraMask = sceneCamera.cullingMask;
        if (backgroundCamera != null) _savedBgCameraMask = backgroundCamera.cullingMask;
        _hasSavedCameraState = true;

        if (sceneCamera != null && (sceneCamera.cullingMask & blurPlaneLayerMask) == 0)
        {
            var oldMask = sceneCamera.cullingMask;
            sceneCamera.cullingMask |= blurPlaneLayerMask;
            Log.Info(
                $"[RadianceSceneManager] 补充 sceneCamera cullingMask: {oldMask} -> {sceneCamera.cullingMask}"
            );
        }

        if (backgroundCamera != null && (backgroundCamera.cullingMask & blurPlaneLayerMask) != 0)
        {
            var oldMask = backgroundCamera.cullingMask;
            backgroundCamera.cullingMask &= ~blurPlaneLayerMask;
            Log.Info(
                $"[RadianceSceneManager] 排除 BlurPlane 层(背景相机): {oldMask} -> {backgroundCamera.cullingMask}"
            );
        }
    }

    /// <summary>
    /// 恢复 EnsureBlurPlaneVisibility 修改的相机 cullingMask
    /// </summary>
    private void RestoreCameraState()
    {
        if (!_hasSavedCameraState) return;

        if (_savedSceneCamera != null)
        {
            _savedSceneCamera.cullingMask = _savedSceneCameraMask;
            Log.Info($"[RadianceSceneManager] 恢复 sceneCamera cullingMask: {_savedSceneCameraMask}");
        }
        if (_savedBgCamera != null)
        {
            _savedBgCamera.cullingMask = _savedBgCameraMask;
            Log.Info($"[RadianceSceneManager] 恢复 backgroundCamera cullingMask: {_savedBgCameraMask}");
        }

        _hasSavedCameraState = false;
        _savedSceneCamera = null;
        _savedBgCamera = null;
    }

    internal static bool TryGetCustomBlurOverrides(out float nearOverride, out int maskOverride)
    {
        if (_customBlurNearOverride.HasValue && _customBlurMaskOverride.HasValue)
        {
            nearOverride = _customBlurNearOverride.Value;
            maskOverride = _customBlurMaskOverride.Value;
            return true;
        }

        nearOverride = 0f;
        maskOverride = 0;
        return false;
    }

    private static void ClearCustomBlurOverrides()
    {
        _customBlurNearOverride = null;
        _customBlurMaskOverride = null;
    }

    #endregion
}
