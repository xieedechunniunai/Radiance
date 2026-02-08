# Requirements Document

## Introduction

本文档定义了 Radiance mod 的基础架构和核心功能需求。该 mod 需要从游戏的场景 bundle 中加载 Absolute Radiance Boss 对象，并在玩家按下 T 键时在英雄位置生成该 Boss。

## Glossary

- **Plugin**: BepInEx 插件主类，负责 mod 的初始化和生命周期管理
- **AssetManager**: 资源管理器，负责从 AssetBundle 加载和缓存游戏资源
- **Scene_Bundle**: Unity 场景打包文件，包含场景中的所有 GameObject
- **Embedded_Bundle**: 嵌入到 mod 程序集中的 AssetBundle 资源文件
- **Absolute_Radiance**: 游戏中的 Boss 对象，位于场景 bundle 的 "Boss Control" 子物品下
- **Hero**: 游戏中的玩家角色
- **Log**: 日志工具类，用于输出调试信息

## Requirements

### Requirement 1: 插件基础架构

**User Story:** As a mod developer, I want a well-structured plugin foundation, so that I can easily extend and maintain the mod.

#### Acceptance Criteria

1. THE Plugin SHALL initialize a Log utility for debugging output
2. THE Plugin SHALL create a persistent manager GameObject that survives scene changes
3. THE Plugin SHALL register scene change event handlers
4. WHEN the plugin loads, THE Plugin SHALL apply necessary Harmony patches

### Requirement 2: 资源管理器

**User Story:** As a mod developer, I want an asset manager to load resources from embedded bundles, so that I can access game assets programmatically.

#### Acceptance Criteria

1. THE AssetManager SHALL support loading assets from embedded bundle resources
2. THE AssetManager SHALL extract the embedded bundle from the mod assembly at runtime
3. THE AssetManager SHALL cache loaded assets to avoid repeated loading
4. THE AssetManager SHALL persist loaded GameObjects under an AssetPool container
5. WHEN loading a scene bundle, THE AssetManager SHALL load the scene additively and extract the target object
6. WHEN the target object is extracted, THE AssetManager SHALL instantiate a copy and disable auto-destruct components
7. IF the embedded bundle cannot be loaded, THEN THE AssetManager SHALL log an error and return null

### Requirement 3: Radiance Boss 加载

**User Story:** As a player, I want the mod to load the Absolute Radiance boss from the embedded bundle, so that I can spawn it in the game.

#### Acceptance Criteria

1. THE AssetManager SHALL be configured to load "Absolute Radiance" from the embedded bundle "radiance.bundle"
2. THE AssetManager SHALL extract the object at path "Boss Control/Absolute Radiance"
3. WHEN the game starts, THE Plugin SHALL preload the Radiance asset asynchronously
4. THE embedded bundle file SHALL be included in the mod assembly as an embedded resource

### Requirement 4: 按键生成 Boss

**User Story:** As a player, I want to spawn the Absolute Radiance boss at my current position by pressing T, so that I can test and interact with the boss.

#### Acceptance Criteria

1. WHEN the player presses the T key, THE Plugin SHALL check if the Radiance asset is loaded
2. IF the Radiance asset is loaded, THEN THE Plugin SHALL instantiate a copy at the hero's current position
3. IF the Radiance asset is not loaded, THEN THE Plugin SHALL log a warning message
4. THE spawned Radiance instance SHALL be activated and positioned at the hero's transform position

### Requirement 5: 日志工具

**User Story:** As a mod developer, I want a logging utility, so that I can debug and monitor the mod's behavior.

#### Acceptance Criteria

1. THE Log utility SHALL support Info, Debug, Warn, and Error log levels
2. THE Log utility SHALL prefix messages with the plugin version
3. THE Log utility SHALL use BepInEx's ManualLogSource for output
