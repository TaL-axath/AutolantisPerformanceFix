# Autolantis Performance Fix

Autolantis Demo 的非官方性能修复插件，基于 BepInEx IL2CPP。当前版本 **0.2.0**。

只处理结算回放与表现层卡顿，不修改轮数上限、收益、随机结果或结算规则。保留现有插件 ID 和文件名 Autolantis.SkipFix.dll，方便从旧版升级。

## 当前功能

- 合并已经完成季节内部的空准备协程等待，避免 Skip 仍为上万批次逐帧等待。
- 同帧、同图标、同回放批次的到账晃动和缩放动画各创建一次，减少重复创建和销毁。**此项在非 Skip 模式也适用。**
- 内置帧间隔、计算切片和动画合并计数日志。

仅跳过经检查确定没有工作的准备步骤；季节边界、资源到账通知、真实飞行动画及收尾仍执行。动画合并可能使图标反馈节奏略有变化。

## 兼容环境

- Windows x64，Autolantis Demo，Unity 2022.3.62f3c1。
- BepInEx Unity.IL2CPP-win-x64 **6.0.0-be.790**，提交 4e98359。
- 游戏 GameAssembly.dll SHA256：

~~~text
E8D4E7E8C484562BFA1B00A59B17AD582CE384CE96255B24AC8C6F90DA1DE4F4
~~~

安装器和插件会拒绝不匹配的游戏版本。游戏更新后需要重新适配。BepInEx 核心指纹在 package/loader-sha256.txt 中。

## 安装与日常使用

1. 前往 [Releases](https://github.com/TaL-axath/AutolantisPerformanceFix/releases/latest)，下载 **AutolantisPerformanceFix-0.2.0-Full-win-x64.zip**。
2. 退出游戏，解压整个 ZIP，双击“安装完整补丁.cmd”。
3. 选择包含 Autolantis.exe 的游戏目录。安装器自动安装 BepInEx IL2CPP、配套运行时和性能补丁。
4. 以后正常从 Steam 启动游戏，补丁会自动启用。首次启动可能生成文件并联网下载 Unity 依赖，请等待完成。
5. 正常退出游戏即可：补丁随游戏进程结束，不留独立后台程序，无需执行任何“关闭补丁”操作。

**只需安装一次。** 补丁文件保留在游戏目录，供下次启动时自动加载；退出游戏不等于卸载或禁用下次启动。没有额外启动器、常驻监控或定时任务。

请选择完整包，不要把 GitHub 自动生成的 Source code ZIP 当作安装包。无需单独下载 BepInEx 或安装 .NET SDK。不同版本加载器或冲突文件会被拦截。

升级保留已有配置。如果此前手动将 Optimization.Enabled 改为 false，请改回 true 后重启，才能恢复每次启动自动启用。

## 卸载（仅在不再使用时）

退出游戏后删除 BepInEx/plugins/Autolantis.SkipFix.dll，即可停止以后自动加载。本插件没有需另外结束的后台进程。其他插件可能共用 BepInEx，请勿为卸载本插件删除整个加载器目录。

## 配置

首次启动后生成 BepInEx/config/local.autolantis.skipfix.cfg：

~~~ini
[Optimization]
Enabled = true
CoalesceArrivalAnimations = true

[Diagnostics]
Enabled = true
~~~

- Optimization.Enabled：总开关。
- CoalesceArrivalAnimations：只关闭它可保留原 Skip 加速，同时恢复原图标动画。
- Diagnostics.Enabled：轻量诊断开关。

修改后重启游戏生效。每帧最多合并 256 个空等待，合并循环使用 2 毫秒软预算；这不是所有游戏工作的硬帧时间上限。

## 验证与限制

- 原生准备协程 1000 次验证及季节边界、暂停等保护条件测试通过。
- 5000 次重复动画请求的合并测试通过，跨图标、跨帧和跨回放批次保留。
- 旧实玩日志确认一次回放合并 18,436 次空等待。
- 0.2.0 使用者反馈 Skip 表现明显改善；没有同一存档、同一环境的完整对照数据，不承诺固定加速倍数。
- 非 Skip 的逐事件快照仍可能很重；当前没有强制启用季节快照。
- 曾观察到季节汇总动画超时。当前版本减少重复动画热点，不能保证消除所有收尾超时。

发生卡顿时，结算结束后退出游戏，保留 BepInEx/LogOutput.log 和 Unity 的 Player.log，再次启动可能覆盖日志。提交日志前可自行移除用户名和本机路径。

## 从源码构建

需要可构建 net6.0 的 .NET SDK，以及你自己的游戏安装和经过 BepInEx 首次运行生成的 interop DLL。

~~~powershell
.\Build.ps1 -GameDirectory "<游戏目录>"
~~~

输出为 package/Autolantis.SkipFix.dll；构建不会安装到游戏。源码仓库不包含游戏程序集、反编译源码、用户存档或运行日志。Release 完整包另附未经修改的加载器、许可证和对应源码归档。

开发自测参数 --skipfix-selftest 仅用于隔离游戏副本。不要把它加入日常 Steam 启动参数。自测验证的是局部分支和合并条件，不能替代真实画面的端到端测试。

## 非 Skip 后续优化

见 [非 Skip 优化方案](docs/non-skip.md)。当前发布范围仅包含已经实现的两项表现修复，后续方案尚未实现或验证。