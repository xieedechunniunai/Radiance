using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Radiance.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Radiance.Managers;

/// <summary>
/// 资源管理器：
/// - 从嵌入式 AssetBundle 加载 GG_Radiance 场景（Scene bundle）
/// - 可从场景中提取指定对象并缓存为“可复用 Prefab”
///
/// 说明：场景 bundle 与 Prefab bundle 不同，必须先确保 AssetBundle 已加载，再 LoadScene。
/// </summary>
internal sealed class AssetManager : MonoBehaviour
{
    private const string EmbeddedBundleName = "Radiance.Asset.radiance.bundle";
    private const string EmbeddedBundleSuffix = ".radiance.bundle";

    private const string RadianceSceneName = "GG_Radiance";

    /// <summary>
    /// 最近一次 Additive 加载的 Radiance 场景（仅在调用 LoadRadianceSceneAdditiveAsync 后有效）
    /// </summary>
    public Scene LoadedRadianceScene { get; private set; }

    /// <summary>
    /// 获取 bundle 内的 Radiance 场景路径（用于 GameManager / SceneManager 加载）
    /// </summary>
    public string? RadianceScenePath => _scenePathInBundle;

    public bool IsBundleLoaded => _bundle != null;

    public void UnloadBundleKeepLoadedAssets()
    {
        if (_bundle == null)
            return;

        _bundle.Unload(unloadAllLoadedObjects: true);
        Resources.UnloadUnusedAssets();
        _bundle = null;
    }

    // 场景内对象路径（来自你的 Hierarchy 截图）
    private const string AbsoluteRadiancePath = "Boss Control/Absolute Radiance";

    private byte[]? _bundleBytes;
    private AssetBundle? _bundle;
    private string? _scenePathInBundle;

    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();
    private readonly HashSet<string> _loadingAssets = new();

    private GameObject? _assetPool;

    private bool _isPreloaded;
    private bool _isPreloading;

    public bool IsPreloaded => _isPreloaded;

    private void Awake()
    {
        CreateAssetPool();
        Log.Info("[AssetManager] 已初始化（Scene bundle 模式）");
    }

    private void CreateAssetPool()
    {
        if (_assetPool != null)
            return;

        _assetPool = new GameObject("AssetPool");
        _assetPool.transform.SetParent(transform);
        _assetPool.SetActive(false);
    }

    public T? Get<T>(string assetName)
        where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(assetName))
        {
            Log.Error("[AssetManager] 资源名称不能为空");
            return null;
        }

        if (_assetCache.TryGetValue(assetName, out var cached) && cached != null)
        {
            if (cached is T typed)
                return typed;
            Log.Warn(
                $"[AssetManager] 资源 '{assetName}' 类型不匹配：期望 {typeof(T).Name}，实际 {cached.GetType().Name}"
            );
            return null;
        }

        return null;
    }

    public bool IsAssetLoaded(string assetName)
    {
        return _assetCache.TryGetValue(assetName, out var cached) && cached != null;
    }

    /// <summary>
    /// 预加载：目前只预加载 Absolute Radiance（用于按 T 直接召唤）。
    /// </summary>
    public IEnumerator PreloadAllAssets()
    {
        if (_isPreloaded || _isPreloading)
        {
            while (_isPreloading)
                yield return null;
            yield break;
        }

        _isPreloading = true;

        yield return EnsureBundleLoadedAsync();
        _isPreloaded = true;
        _isPreloading = false;

        Log.Info("[AssetManager] 预加载完成");
    }

    /// <summary>
    /// 加载（或确保已加载）GG_Radiance 场景（Single）。
    /// 注意：场景不在 Build Settings 中时，必须通过 Scene bundle 的 scenePath 加载。
    /// </summary>
    public IEnumerator LoadRadianceSceneSingleAsync()
    {
        yield return EnsureBundleLoadedAsync();

        if (string.IsNullOrEmpty(_scenePathInBundle))
        {
            Log.Error("[AssetManager] scenePath 为空，无法加载 GG_Radiance 场景");
            yield break;
        }

        Log.Info($"[AssetManager] 加载场景（Single）：{_scenePathInBundle}");
        var op = SceneManager.LoadSceneAsync(_scenePathInBundle, LoadSceneMode.Single);
        if (op == null)
        {
            Log.Error("[AssetManager] SceneManager.LoadSceneAsync 返回 null");
            yield break;
        }

        yield return op;
    }

    /// <summary>
    /// Additive 加载 GG_Radiance 场景，并记录到 LoadedRadianceScene。
    /// 用途：把场景内容“搬运/注入”到当前游戏场景，以复用二代场景的管理器/相机/SceneManager。
    /// </summary>
    public IEnumerator LoadRadianceSceneAdditiveAsync()
    {
        LoadedRadianceScene = default;

        yield return EnsureBundleLoadedAsync();

        if (string.IsNullOrEmpty(_scenePathInBundle))
        {
            Log.Error("[AssetManager] scenePath 为空，无法 Additive 加载 GG_Radiance 场景");
            yield break;
        }

        // 如果已加载，直接复用
        var existingByName = SceneManager.GetSceneByName(RadianceSceneName);
        if (existingByName.IsValid() && existingByName.isLoaded)
        {
            LoadedRadianceScene = existingByName;
            yield break;
        }

        Log.Info($"[AssetManager] 加载场景（Additive）：{_scenePathInBundle}");
        var op = SceneManager.LoadSceneAsync(_scenePathInBundle, LoadSceneMode.Additive);
        if (op == null)
        {
            Log.Error("[AssetManager] SceneManager.LoadSceneAsync(Additive) 返回 null");
            yield break;
        }

        yield return op;

        var scene = SceneManager.GetSceneByPath(_scenePathInBundle);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = SceneManager.GetSceneByName(RadianceSceneName);
        }

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Log.Error("[AssetManager] Additive 场景加载完成但无法获取 Scene 句柄");
            yield break;
        }

        LoadedRadianceScene = scene;
    }

    /// <summary>
    /// 从 GG_Radiance 场景中提取 Absolute Radiance，缓存为可复用对象（禁用状态，放入 AssetPool）。
    /// </summary>
    public IEnumerator LoadAbsoluteRadiancePrefabAsync()
    {
        const string cacheKey = "Absolute Radiance";
        if (IsAssetLoaded(cacheKey))
            yield break;

        if (_loadingAssets.Contains(cacheKey))
        {
            while (_loadingAssets.Contains(cacheKey))
                yield return null;
            yield break;
        }

        _loadingAssets.Add(cacheKey);

        yield return EnsureBundleLoadedAsync();
        if (string.IsNullOrEmpty(_scenePathInBundle))
        {
            Log.Error("[AssetManager] scenePath 为空，无法提取 Absolute Radiance");
            _loadingAssets.Remove(cacheKey);
            yield break;
        }

        // 如果玩家已经在 GG_Radiance 场景中，不需要 Additive 临时加载
        var existingScene = SceneManager.GetSceneByName(RadianceSceneName);
        bool sceneAlreadyLoaded = existingScene.IsValid() && existingScene.isLoaded;

        Scene scene;
        if (sceneAlreadyLoaded)
        {
            scene = existingScene;
        }
        else
        {
            Log.Info($"[AssetManager] 临时 Additive 加载场景以提取对象：{_scenePathInBundle}");
            var loadOp = SceneManager.LoadSceneAsync(_scenePathInBundle, LoadSceneMode.Additive);
            yield return loadOp;

            scene = SceneManager.GetSceneByPath(_scenePathInBundle);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // 兼容：有的 Unity 版本需要按 name 获取
                scene = SceneManager.GetSceneByName(RadianceSceneName);
            }
        }

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Log.Error("[AssetManager] 无法加载/获取 GG_Radiance 场景用于提取对象");
            _loadingAssets.Remove(cacheKey);
            yield break;
        }

        var source = FindObjectInSceneByPath(scene, AbsoluteRadiancePath);
        if (source == null)
        {
            Log.Error(
                $"[AssetManager] 在场景 '{scene.name}' 中未找到对象路径：{AbsoluteRadiancePath}"
            );
        }
        else
        {
            var copy = Instantiate(source);
            copy.name = cacheKey + " (Prefab)";
            copy.SetActive(false);

            DisableAutoDestructComponents(copy);

            if (_assetPool != null)
            {
                copy.transform.SetParent(_assetPool.transform, worldPositionStays: true);
            }

            _assetCache[cacheKey] = copy;
            Log.Info("[AssetManager] 已缓存 Absolute Radiance");
        }

        if (!sceneAlreadyLoaded)
        {
            var unloadOp = SceneManager.UnloadSceneAsync(scene);
            if (unloadOp != null)
                yield return unloadOp;
        }

        _loadingAssets.Remove(cacheKey);
    }

    private IEnumerator EnsureBundleLoadedAsync()
    {
        if (_bundle != null)
        {
            if (string.IsNullOrEmpty(_scenePathInBundle))
            {
                ResolveScenePath();
            }
            yield break;
        }

        _bundleBytes ??= GetEmbeddedBundleBytes();
        if (_bundleBytes == null)
        {
            Log.Error("[AssetManager] 无法获取嵌入的 radiance.bundle 字节数据");
            yield break;
        }

        var req = AssetBundle.LoadFromMemoryAsync(_bundleBytes);
        yield return req;

        _bundle = req.assetBundle;
        if (_bundle == null)
        {
            Log.Error("[AssetManager] AssetBundle.LoadFromMemoryAsync 失败");
            yield break;
        }

        ResolveScenePath();
    }

    private void ResolveScenePath()
    {
        if (_bundle == null)
            return;

        var scenePaths = _bundle.GetAllScenePaths();
        if (scenePaths == null || scenePaths.Length == 0)
        {
            Log.Error(
                "[AssetManager] radiance.bundle 内没有 Scene。请确认打包的是 Scene bundle（包含 GG_Radiance.unity）"
            );
            return;
        }

        // 优先匹配包含 gg_radiance 的场景路径，否则用第一个
        _scenePathInBundle =
            scenePaths.FirstOrDefault(p =>
                p.IndexOf("gg_radiance", StringComparison.OrdinalIgnoreCase) >= 0
            ) ?? scenePaths[0];

        Log.Info($"[AssetManager] ScenePaths: {string.Join(", ", scenePaths)}");
        Log.Info($"[AssetManager] 选用 scenePath: {_scenePathInBundle}");
    }

    private byte[]? GetEmbeddedBundleBytes()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();

            // 1) 先按固定名获取（最快）
            Stream? stream = asm.GetManifestResourceStream(EmbeddedBundleName);

            // 2) 如果固定名失效，按后缀兜底（防止命名空间/项目名变化）
            if (stream == null)
            {
                var names = asm.GetManifestResourceNames();
                var candidate = names.FirstOrDefault(n =>
                    n.EndsWith(EmbeddedBundleSuffix, StringComparison.OrdinalIgnoreCase)
                );
                if (!string.IsNullOrEmpty(candidate))
                {
                    stream = asm.GetManifestResourceStream(candidate);
                    Log.Warn(
                        $"[AssetManager] EmbeddedBundleName 不匹配，使用兜底资源名：{candidate}"
                    );
                }
                else
                {
                    Log.Error(
                        $"[AssetManager] 未找到嵌入资源：{EmbeddedBundleName}（也没有匹配 {EmbeddedBundleSuffix} 的资源）"
                    );
                    Log.Debug($"[AssetManager] 可用嵌入资源：{string.Join(", ", names)}");
                    return null;
                }
            }

            using (stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch (Exception e)
        {
            Log.Error($"[AssetManager] 读取嵌入 bundle 失败：{e.Message}");
            return null;
        }
    }

    private GameObject? FindObjectInSceneByPath(Scene scene, string objectPath)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return null;

        var parts = objectPath.Split('/');
        if (parts.Length == 0)
            return null;

        // root
        var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0]);
        if (root == null)
            return null;

        Transform current = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            Transform? child = current.Find(parts[i]);
            if (child == null)
            {
                // 兜底：遍历 direct children（包括 inactive）
                foreach (Transform c in current)
                {
                    if (c.name == parts[i])
                    {
                        child = c;
                        break;
                    }
                }
            }

            if (child == null)
                return null;
            current = child;
        }

        return current.gameObject;
    }

    private void DisableAutoDestructComponents(GameObject obj)
    {
        if (obj == null)
            return;

        var autoDestructTypes = new HashSet<string>
        {
            "AutoRecycleSelf",
            "ActiveRecycler",
            "ObjectBounce",
            "DropRecycle",
            "RecycleResetHandler",
            "EventRegister",
        };

        var allComponents = obj.GetComponentsInChildren<Component>(true);
        foreach (var comp in allComponents)
        {
            if (comp == null)
                continue;
            if (!autoDestructTypes.Contains(comp.GetType().Name))
                continue;

            if (comp is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }
            else
            {
                Destroy(comp);
            }
        }
    }


    private void OnDestroy()
    {
        foreach (var kvp in _assetCache)
        {
            if (kvp.Value is GameObject go && go != null)
            {
                Destroy(go);
            }
        }
        _assetCache.Clear();

        if (_assetPool != null)
        {
            Destroy(_assetPool);
            _assetPool = null;
        }

        if (_bundle != null)
        {
            _bundle.Unload(unloadAllLoadedObjects: false);
            _bundle = null;
        }

        _bundleBytes = null;
    }
}
