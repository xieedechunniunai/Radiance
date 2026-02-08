# Implementation Plan: Radiance Spawn

## Overview

本实现计划将 Radiance mod 的设计分解为可执行的编码任务。任务按依赖顺序排列，确保每个步骤都能在前一步骤的基础上构建。

## Tasks

- [x] 1. 创建项目基础结构
  - [x] 1.1 创建 Source 目录结构 (Tools, Managers)
    - 创建 `Source/Tools/` 和 `Source/Managers/` 目录
    - _Requirements: 1.1_
  - [x] 1.2 更新 csproj 配置嵌入资源
    - 将 `Asset/radiance.bundle` 配置为嵌入资源
    - _Requirements: 3.4_

- [x] 2. 实现日志工具类
  - [x] 2.1 创建 Log.cs
    - 实现 Init、Debug、Info、Warn、Error 方法
    - 使用 BepInEx 的 ManualLogSource
    - 添加版本前缀
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 3. 实现资源管理器
  - [x] 3.1 创建 AssetManager.cs 基础结构
    - 定义 AssetConfig 类
    - 定义资源配置字典（Absolute Radiance）
    - 创建缓存字典和 AssetPool
    - _Requirements: 2.1, 2.4, 3.1_
  - [x] 3.2 实现嵌入资源加载
    - 实现 GetEmbeddedBundleBytes() 从程序集提取 bundle
    - _Requirements: 2.2_
  - [x] 3.3 实现场景加载和对象提取
    - 实现 LoadFromEmbeddedBundle() 协程
    - 加载场景、查找对象、实例化并缓存
    - 实现 FindObjectInScene() 辅助方法
    - _Requirements: 2.5, 2.6, 3.2_
  - [x] 3.4 实现资源获取接口
    - 实现 Get<T>() 方法从缓存获取资源
    - 实现 IsAssetLoaded() 检查方法
    - 实现 PreloadAllAssets() 预加载协程
    - _Requirements: 2.3, 2.7, 3.3_

- [x] 4. 实现插件主类
  - [x] 4.1 更新 RadiancePlugin.cs 初始化逻辑
    - 初始化 Log
    - 注册场景切换事件
    - 创建持久化管理器
    - _Requirements: 1.1, 1.2, 1.3_
  - [x] 4.2 实现按键生成功能
    - 在 Update() 中检测 T 键
    - 检查资源是否已加载
    - 在英雄位置实例化 Radiance
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 5. Checkpoint - 功能验证
  - 编译项目确保无错误
  - 在游戏中测试资源加载
  - 测试按 T 键生成 Radiance
  - 确保所有功能正常工作，如有问题请询问用户

## Notes

- 任务按顺序执行，每个任务依赖前一个任务的完成
- 由于是 Unity mod，测试主要通过运行时验证
- 嵌入资源需要在 csproj 中正确配置才能在运行时访问
