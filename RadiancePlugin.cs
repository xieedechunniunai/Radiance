using System.Collections;
 
using BepInEx;
using HarmonyLib;
using Radiance.Managers;
using Radiance.Patches;
using Radiance.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Radiance;

[BepInAutoPlugin(id: "io.github.xieedechunniunai.radiance")]
public partial class RadiancePlugin : BaseUnityPlugin
{
    /// <summary>
    /// 插件实例
    /// </summary>
    public static RadiancePlugin? Instance { get; private set; }

    /// <summary>
    /// Harmony 实例
    /// </summary>
    private Harmony? _harmony;

    /// <summary>
    /// 持久化管理器
    /// </summary>
    private GameObject? _radianceManager;

    /// <summary>
    /// 资源管理器
    /// </summary>
    private AssetManager? _assetManager;


    private void Awake()
    {
        Instance = this;
        Log.Init(Logger);

        // 应用 Harmony 补丁
        _harmony = new Harmony(Id);
        _harmony.PatchAll(typeof(DebugPatches));
        _harmony.PatchAll(typeof(BlurBackgroundPatches));
        _harmony.PatchAll(typeof(CustomSceneManagerGlobalGuardPatches));
        _harmony.PatchAll(typeof(GameManagerFindSceneManagerGuardPatches));
        _harmony.PatchAll(typeof(GodfinderIconPatches));
        _harmony.PatchAll(typeof(RadiancePatches));
        Log.Info("Harmony 补丁已应用");

        // 注册场景切换事件
        SceneManager.activeSceneChanged += OnSceneChange;

        Log.Info($"Plugin {Name} ({Id}) has loaded!");
    }

    private void OnSceneChange(Scene oldScene, Scene newScene)
    {
        // 当从主菜单加载存档时创建管理器
        if (oldScene.name == "Menu_Title")
        {
            CreateManager();
        }
    }

    /// <summary>
    /// 创建持久化管理器
    /// </summary>
    private void CreateManager()
    {
        // 查找是否已存在持久化管理器
        _radianceManager = GameObject.Find("RadianceManager");
        if (_radianceManager == null)
        {
            _radianceManager = new GameObject("RadianceManager");
            UnityEngine.Object.DontDestroyOnLoad(_radianceManager);

            // 添加资源管理器组件
            _assetManager = _radianceManager.AddComponent<AssetManager>();

            // 添加自定义场景管理器组件
            _radianceManager.AddComponent<RadianceSceneManager>();
            // 添加场景基础设施缓存器
            _radianceManager.AddComponent<SceneInfrastructureCache>();

            Log.Info("创建持久化管理器");

            // 启动预加载资源的协程
            StartCoroutine(_assetManager.PreloadAllAssets());
        }
        else
        {
            // 获取已存在的资源管理器
            _assetManager = _radianceManager.GetComponent<AssetManager>();
            Log.Info("找到已存在的持久化管理器");
        }
    }

    private void Update()
    {
        // 检测 T 键按下 - 生成 Radiance Boss
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartCoroutine(SpawnRadianceRoutine());
        }

        // 检测 Y 键按下 - 进入 Radiance 场景（GG_Radiance Scene bundle）
        if (Input.GetKeyDown(KeyCode.Y))
        {
            StartCoroutine(EnterRadianceSceneRoutine());
        }

        // 检测 U 键按下 - 退出自定义场景
        if (Input.GetKeyDown(KeyCode.U) && RadianceSceneManager.IsInCustomScene)
        {
            RadianceSceneManager.Instance?.ExitCustomScene();
        }

 
    }

    private IEnumerator EnterRadianceSceneRoutine()
    {
        if (RadianceSceneManager.IsInCustomScene)
        {
            Log.Warn("[RadiancePlugin] 已在自定义场景中");
            yield break;
        }

        if (_assetManager == null)
        {
            Log.Warn("[RadiancePlugin] 资源管理器未初始化");
            yield break;
        }

        // 等待预加载（至少确保 bundle 已就绪）
        if (!_assetManager.IsPreloaded)
        {
            Log.Info("[RadiancePlugin] 等待资源预加载...");
            while (_assetManager != null && !_assetManager.IsPreloaded)
            {
                yield return null;
            }
        }

        var spawnPosition = new Vector3(62.07f, 21.6402f, 0.004f);
        // 直接加载 GG_Radiance（Single），其余基础设施通过缓存器恢复
        RadianceSceneManager.Instance?.EnterCustomScene(
            "GG_Radiance",
            spawnPosition,
            targetSceneName: "GG_Radiance"
        );
    }

    private IEnumerator SpawnRadianceRoutine()
    {
        if (_assetManager == null)
        {
            Log.Warn("[RadiancePlugin] 资源管理器未初始化");
            yield break;
        }

        // 确保 boss 已缓存（如果预加载还没完成，这里会补一次）
        if (!_assetManager.IsAssetLoaded("Absolute Radiance"))
        {
            yield return _assetManager.LoadAbsoluteRadiancePrefabAsync();
        }

        var radiancePrefab = _assetManager.Get<GameObject>("Absolute Radiance");
        if (radiancePrefab == null)
        {
            Log.Error("[RadiancePlugin] 无法获取 Absolute Radiance（可能提取失败）");
            yield break;
        }

        var hero = HeroController.instance;
        if (hero == null)
        {
            Log.Warn("[RadiancePlugin] 未找到英雄");
            yield break;
        }

        Vector3 heroPosition = hero.transform.position;
        var radianceInstance = UnityEngine.Object.Instantiate(
            radiancePrefab,
            heroPosition,
            Quaternion.identity
        );
        radianceInstance.name = "Absolute Radiance (Spawned)";
        radianceInstance.SetActive(true);

        Log.Info($"[RadiancePlugin] 在位置 {heroPosition} 生成了 Radiance");
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChange;
    }

}
