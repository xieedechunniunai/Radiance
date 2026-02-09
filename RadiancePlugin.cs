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

            var assetManager = _radianceManager.AddComponent<AssetManager>();
            _radianceManager.AddComponent<RadianceSceneManager>();

            Log.Info("创建持久化管理器");

            StartCoroutine(assetManager.PreloadAllAssets());
        }
        else
        {
            Log.Info("找到已存在的持久化管理器");
        }
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChange;
    }
}
