using System;
using System.Collections;
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
///
/// 说明：场景 bundle 与 Prefab bundle 不同，必须先确保 AssetBundle 已加载，再 LoadScene。
/// </summary>
internal sealed class AssetManager : MonoBehaviour
{
    private const string EmbeddedBundleName = "Radiance.Asset.radiance.bundle";
    private const string EmbeddedBundleSuffix = ".radiance.bundle";

    public bool IsBundleLoaded => _bundle != null;

    public bool IsPreloaded => _isPreloaded;

    private byte[]? _bundleBytes;
    private AssetBundle? _bundle;
    private string? _scenePathInBundle;

    private bool _isPreloaded;
    private bool _isPreloading;

    private void Awake()
    {
        Log.Info("[AssetManager] 已初始化（Scene bundle 模式）");
    }

    /// <summary>
    /// 预加载：确保 AssetBundle 已就绪
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
    /// 加载 GG_Radiance 场景（Single 模式）
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
    /// 卸载 AssetBundle 并释放资源
    /// </summary>
    public void UnloadBundleKeepLoadedAssets()
    {
        if (_bundle == null)
            return;

        _bundle.Unload(unloadAllLoadedObjects: false);
        Resources.UnloadUnusedAssets();
        _bundle = null;
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

            Stream? stream = asm.GetManifestResourceStream(EmbeddedBundleName);

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

    private void OnDestroy()
    {
        if (_bundle != null)
        {
            _bundle.Unload(unloadAllLoadedObjects: false);
            _bundle = null;
        }

        _bundleBytes = null;
    }
}
