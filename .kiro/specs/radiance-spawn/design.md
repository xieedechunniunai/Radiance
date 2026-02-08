# Design Document: Radiance Spawn

## Overview

本设计文档描述了 Radiance mod 的技术架构，该 mod 从嵌入的 AssetBundle 中加载 Absolute Radiance Boss，并允许玩家通过按键在游戏中生成该 Boss。

设计参考了 AnySilkBoss mod 的架构，但进行了简化以满足当前需求。

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      RadiancePlugin                          │
│  - 插件入口点                                                │
│  - 初始化 Log、AssetManager                                  │
│  - 处理按键输入                                              │
│  - 管理生命周期                                              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    RadianceManager (GameObject)              │
│  - 持久化管理器 (DontDestroyOnLoad)                          │
│  - 挂载 AssetManager 组件                                    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      AssetManager                            │
│  - 从嵌入资源加载 Bundle                                     │
│  - 加载场景并提取目标对象                                    │
│  - 缓存和持久化资源                                          │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                        AssetPool                             │
│  - 存放预加载的 Prefab                                       │
│  - 保持禁用状态作为模板                                      │
└─────────────────────────────────────────────────────────────┘
```

## Components and Interfaces

### 1. Log (静态工具类)

日志工具类，提供统一的日志输出接口。

```csharp
namespace Radiance.Tools;

internal static class Log
{
    // 初始化日志源
    internal static void Init(ManualLogSource logSource);
    
    // 日志方法
    internal static void Debug(object debug);
    internal static void Info(object info);
    internal static void Warn(object warning);
    internal static void Error(object error);
}
```

### 2. AssetManager (MonoBehaviour)

资源管理器，负责从嵌入的 Bundle 加载和管理资源。

```csharp
namespace Radiance.Managers;

internal sealed class AssetManager : MonoBehaviour
{
    // 资源配置
    private static readonly Dictionary<string, AssetConfig> _assetConfig;
    
    // 缓存
    private readonly Dictionary<string, UnityEngine.Object> _assetCache;
    private GameObject? _assetPool;
    
    // 公开属性
    public bool IsPreloaded { get; }
    
    // 公开方法
    public T? Get<T>(string assetName) where T : UnityEngine.Object;
    public bool IsAssetLoaded(string assetName);
    public IEnumerator PreloadAllAssets();
    
    // 内部方法
    private IEnumerator LoadFromEmbeddedBundle(string assetName, string objectPath);
    private byte[]? GetEmbeddedBundleBytes();
    private GameObject? FindObjectInScene(Scene scene, string objectPath);
}
```

### 3. RadiancePlugin (BaseUnityPlugin)

插件主类，负责初始化和按键处理。

```csharp
namespace Radiance;

[BepInAutoPlugin]
public partial class RadiancePlugin : BaseUnityPlugin
{
    public static RadiancePlugin? Instance { get; private set; }
    
    private GameObject? _radianceManager;
    private AssetManager? _assetManager;
    
    private void Awake();
    private void Update();  // 处理按键输入
    private void OnSceneChange(Scene oldScene, Scene newScene);
    private void CreateManager();
    private void SpawnRadiance();
}
```

## Data Models

### AssetConfig

资源配置类，定义如何加载特定资源。

```csharp
private class AssetConfig
{
    /// <summary>
    /// 场景内对象路径
    /// 格式如 "Boss Control/Absolute Radiance"
    /// </summary>
    public string ObjectPath { get; set; }
}
```

### 资源配置表

```csharp
private static readonly Dictionary<string, AssetConfig> _assetConfig = new()
{
    { 
        "Absolute Radiance", 
        new AssetConfig { ObjectPath = "Boss Control/Absolute Radiance" } 
    }
};
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Manager Persistence

*For any* scene transition in the game, the RadianceManager GameObject should continue to exist and not be destroyed.

**Validates: Requirements 1.2**

### Property 2: Asset Caching Consistency

*For any* asset that has been loaded, subsequent calls to Get<T> with the same asset name should return the same cached instance (reference equality).

**Validates: Requirements 2.3**

### Property 3: Spawn Position Accuracy

*For any* hero position when spawning Radiance, the spawned instance's transform.position should equal the hero's transform.position at the time of spawning.

**Validates: Requirements 4.2, 4.4**

## Error Handling

### Bundle 加载失败

- 如果嵌入资源不存在，记录错误日志并返回 null
- 如果 Bundle 无法解析，记录错误日志并返回 null

### 场景加载失败

- 如果场景无法加载，记录错误日志，卸载 Bundle，返回 null
- 创建临时相机避免渲染组件报错

### 对象查找失败

- 如果指定路径的对象不存在，记录错误日志并返回 null

### 资源未加载时生成

- 如果尝试生成未加载的资源，记录警告日志并跳过生成

## Testing Strategy

### 单元测试

由于这是一个 Unity mod，传统单元测试较难实现。主要通过以下方式验证：

1. **日志验证** - 检查关键操作的日志输出
2. **运行时检查** - 在游戏中验证功能是否正常

### 集成测试

1. **资源加载测试** - 验证 Bundle 能正确加载
2. **生成测试** - 验证按 T 键能正确生成 Boss
3. **持久化测试** - 验证场景切换后资源仍然可用

### 手动测试清单

- [ ] 插件加载时无报错
- [ ] 进入游戏后资源预加载完成
- [ ] 按 T 键在英雄位置生成 Radiance
- [ ] 场景切换后仍能生成 Radiance
- [ ] 多次生成不会导致崩溃
