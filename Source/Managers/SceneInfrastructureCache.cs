using System;
using System.Collections.Generic;
using Radiance.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Radiance.Managers;

// 使用别名区分 Unity 的 SceneManager 和游戏的 SceneManager
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

/// <summary>
/// 场景基础设施缓存器
/// 在场景切换前保存关键管理器对象，在新场景加载后恢复引用
/// </summary>
public class SceneInfrastructureCache : MonoBehaviour
{
    public static SceneInfrastructureCache? Instance { get; private set; }

    /// <summary>
    /// 缓存的 SceneManager 组件（gm.sm 指向的对象）
    /// </summary>
    private CustomSceneManager? _cachedSceneManager;
    private GameObject? _cachedSceneManagerObj;

    /// <summary>
    /// 缓存的相机锁定区域容器
    /// </summary>
    private GameObject? _cachedCameraLockZones;

    /// <summary>
    /// 是否已缓存
    /// </summary>
    public bool HasCache => _cachedSceneManager != null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        ClearCache();
    }

    /// <summary>
    /// 在离开当前场景前，缓存关键的基础设施对象
    /// </summary>
    public void CacheCurrentSceneInfrastructure()
    {
        ClearCache();

        var gm = GameManager._instance;
        if (gm == null)
        {
            Log.Warn("[SceneInfrastructureCache] GameManager 不存在，无法缓存");
            return;
        }

        // 1. 缓存 SceneManager (gm.sm)
        if (gm.sm != null)
        {
            _cachedSceneManagerObj = gm.sm.gameObject;
            _cachedSceneManager = gm.sm;

            // 标记为不销毁，以便在场景切换时保留
            DontDestroyOnLoad(_cachedSceneManagerObj);
            Log.Info(
                $"[SceneInfrastructureCache] 已缓存 SceneManager: {_cachedSceneManagerObj.name}"
            );
        }
        else
        {
            Log.Warn("[SceneInfrastructureCache] gm.sm 为空，无法缓存 SceneManager");
        }

        // 2. 缓存相机锁定区域（可选）
        var cameraLockZones = GameObject.Find("_Camera Lock Zones");
        if (cameraLockZones != null)
        {
            _cachedCameraLockZones = cameraLockZones;
            DontDestroyOnLoad(_cachedCameraLockZones);
            Log.Info("[SceneInfrastructureCache] 已缓存 Camera Lock Zones");
        }

        Log.Info("[SceneInfrastructureCache] 场景基础设施缓存完成");
    }

    /// <summary>
    /// 在新场景加载后，恢复基础设施引用
    /// </summary>
    public void RestoreInfrastructureToScene()
    {
        var gm = GameManager._instance;
        if (gm == null)
        {
            Log.Warn("[SceneInfrastructureCache] GameManager 不存在，无法恢复");
            return;
        }

        // 1. 恢复 SceneManager 引用
        if (_cachedSceneManager != null && _cachedSceneManagerObj != null)
        {
            // 将缓存的对象移动到当前场景
            var currentScene = UnitySceneManager.GetActiveScene();
            UnitySceneManager.MoveGameObjectToScene(_cachedSceneManagerObj, currentScene);

            // 重新赋值 gm.sm
            gm.sm = _cachedSceneManager;

            Log.Info($"[SceneInfrastructureCache] 已恢复 SceneManager 到场景: {currentScene.name}");
        }
        else
        {
            // 如果没有缓存，尝试从新场景中查找或创建一个最小化的 SceneManager
            var existingSm = FindAnyObjectByType<CustomSceneManager>();
            if (existingSm != null)
            {
                gm.sm = existingSm;
                Log.Info("[SceneInfrastructureCache] 使用场景内已有的 SceneManager");
            }
            else
            {
                // 创建一个空的 SceneManager 对象，避免 NRE
                CreateMinimalSceneManager(gm);
            }
        }

        // 2. 恢复相机锁定区域
        if (_cachedCameraLockZones != null)
        {
            var currentScene = UnitySceneManager.GetActiveScene();
            UnitySceneManager.MoveGameObjectToScene(_cachedCameraLockZones, currentScene);
            Log.Info("[SceneInfrastructureCache] 已恢复 Camera Lock Zones");
        }

        // 清理缓存（已恢复，不再需要）
        _cachedSceneManager = null;
        _cachedSceneManagerObj = null;
        _cachedCameraLockZones = null;

        Log.Info("[SceneInfrastructureCache] 场景基础设施恢复完成");
    }

    /// <summary>
    /// 创建最小化的 SceneManager，避免空引用
    /// </summary>
    private void CreateMinimalSceneManager(GameManager gm)
    {
        Log.Info("[SceneInfrastructureCache] 创建最小化 SceneManager");

        var smObj = new GameObject("_SceneManager (Radiance)");
        var sm = smObj.AddComponent<CustomSceneManager>();

        // 设置一些基本属性（避免 NRE）
        // 注意：SceneManager 的具体属性取决于游戏版本，这里只设置最基本的

        gm.sm = sm;
        Log.Info("[SceneInfrastructureCache] 最小化 SceneManager 已创建并赋值给 gm.sm");
    }

    /// <summary>
    /// 清理缓存
    /// </summary>
    public void ClearCache()
    {
        // 如果缓存的对象还存在且处于 DontDestroyOnLoad 状态，销毁它们
        if (_cachedSceneManagerObj != null)
        {
            // 检查是否还在 DontDestroyOnLoad 场景中
            if (_cachedSceneManagerObj.scene.name == "DontDestroyOnLoad")
            {
                Destroy(_cachedSceneManagerObj);
            }
            _cachedSceneManagerObj = null;
            _cachedSceneManager = null;
        }

        if (_cachedCameraLockZones != null)
        {
            if (_cachedCameraLockZones.scene.name == "DontDestroyOnLoad")
            {
                Destroy(_cachedCameraLockZones);
            }
            _cachedCameraLockZones = null;
        }
    }
}
