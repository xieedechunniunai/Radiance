# Radiance

## English

### Overview
`Radiance` is a Hollow Knight: Silksong mod that adds a new challenge room in the **top-left area of BellTown**.
From this new entrance, you can challenge **Radiance**.

All in-mod visual/audio/gameplay assets are sourced from **Hollow Knight**.

### Features
- Adds a new BellTown transition point/room entrance for the Radiance challenge.
- Loads the `GG_Radiance` scene from an embedded AssetBundle.
- Handles return flow back to the original scene after challenge completion/exit.
- Applies runtime compatibility fixes for PlayMaker FSM and scene transition behavior.

### Code Summary
- `RadiancePlugin.cs`
  - Entry point.
  - Registers Harmony patches and creates persistent managers on scene load.
- `Source/Managers/AssetManager.cs`
  - Preloads and manages the embedded `radiance.bundle`.
  - Loads the Radiance scene in `LoadSceneMode.Single`.
- `Source/Managers/RadianceSceneManager.cs`
  - Core flow controller.
  - Injects the BellTown entrance, tracks enter/exit state, sets up custom-scene behavior, and restores game state on return.
- `Source/Patches/*.cs`
  - Intercepts scene transition to redirect `GG_Radiance` loading.
  - Forces custom scene memory behavior where needed.
  - Fixes FSM event handlers and disables incompatible Godfinder icon actions in custom scene.
- `Source/Behaviours/Common/RadianceReturnOnDialogueBehavior.cs`
  - Hooks Radiance FSM dialogue state and triggers return to the original scene safely.

### Dependencies
- `BepInEx-BepInExPack_Silksong` (`5.4.2304`)

### Credits
- Asset sources: **Hollow Knight (HK1)**.

### License
This project is licensed under the **MIT License**. See `LICENSE` for details.

---

## 中文

### 模组简介
`Radiance` 是一个 Hollow Knight: Silksong 模组，在 **BellTown（钟心镇）左上角**新增了一个挑战入口房间。
通过这个入口可以挑战 **Radiance（辐光）**。

模组内使用到的视觉、音频与玩法资源均来自 **《空洞骑士》一代**。

### 功能说明
- 在 BellTown 新增用于挑战 Radiance 的传送点/房间入口。
- 通过内嵌 AssetBundle 加载 `GG_Radiance` 场景。
- 挑战结束或退出后，自动返回原场景并恢复状态。
- 在运行时补丁中处理 PlayMaker FSM 与场景切换兼容问题。

### 代码简述
- `RadiancePlugin.cs`
  - 插件入口。
  - 注册 Harmony 补丁，并在场景切换时创建常驻管理器。
- `Source/Managers/AssetManager.cs`
  - 负责预加载与管理内嵌 `radiance.bundle`。
  - 以 `LoadSceneMode.Single` 加载 Radiance 场景。
- `Source/Managers/RadianceSceneManager.cs`
  - 核心流程管理器。
  - 注入 BellTown 入口，维护进出自定义场景状态，处理自定义场景初始化与返回清理。
- `Source/Patches/*.cs`
  - 拦截场景切换并重定向 `GG_Radiance` 的加载流程。
  - 必要时强制启用记忆场景行为。
  - 修复 FSM 事件处理，并在自定义场景中禁用不兼容的 Godfinder 图标逻辑。
- `Source/Behaviours/Common/RadianceReturnOnDialogueBehavior.cs`
  - 挂接 Radiance FSM 对话状态，安全触发返回原场景。

### 依赖
- `BepInEx-BepInExPack_Silksong`（`5.4.2304`）

### 致谢
- 资源来源：**《空洞骑士》一代（HK1）**。

### 开源协议
本项目采用 **MIT License** 开源，详见 `LICENSE`。
