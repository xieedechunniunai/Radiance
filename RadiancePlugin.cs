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
        _harmony.PatchAll(typeof(FsmFixPatches));
        _harmony.PatchAll(typeof(GodfinderIconPatches));
        _harmony.PatchAll(typeof(RadiancePatches));
        _harmony.PatchAll(typeof(MemoryScenePatches));
        _harmony.PatchAll(typeof(SceneTransitionPatches));
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
        _radianceManager = GameObject.Find("RadianceManager");
        if (_radianceManager == null)
        {
            _radianceManager = new GameObject("RadianceManager");
            Object.DontDestroyOnLoad(_radianceManager);

            _assetManager = _radianceManager.AddComponent<AssetManager>();
            _radianceManager.AddComponent<RadianceSceneManager>();

            Log.Info("创建持久化管理器");

            StartCoroutine(_assetManager.PreloadAllAssets());
        }
        else
        {
            _assetManager = _radianceManager.GetComponent<AssetManager>();
            Log.Info("找到已存在的持久化管理器");
        }
    }

    private void Update()
    {
        // Y 键 - 进入 Radiance 场景
        if (Input.GetKeyDown(KeyCode.Y))
        {
            StartCoroutine(EnterRadianceSceneRoutine());
        }

        // U 键 - 退出自定义场景
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

        if (!_assetManager.IsPreloaded)
        {
            Log.Info("[RadiancePlugin] 等待资源预加载...");
            while (_assetManager != null && !_assetManager.IsPreloaded)
            {
                yield return null;
            }
        }

        var spawnPosition = new Vector3(62.07f, 21.6402f, 0.004f);
        RadianceSceneManager.Instance?.EnterCustomScene(
            "GG_Radiance",
            spawnPosition,
            targetSceneName: "GG_Radiance"
        );
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChange;
    }
}
