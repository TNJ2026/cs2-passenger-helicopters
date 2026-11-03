# CS2 Passenger Helicopters

[English](README.md) | [简体中文](README.zh-CN.md)

为《都市：天际线 II》加入可运营的直升机客运系统，包括双机位直升机场、独立屋顶停机坪和 Mil Mi-26TC 客运直升机。

[![Paradox Mods](https://img.shields.io/badge/Paradox%20Mods-立即订阅-5b69c6)](https://mods.paradoxplaza.com/mods/165769/Windows)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

![CS2 Passenger Helicopters](README-banner.png)

*AI 生成的宣传横幅，非游戏实机截图。*

## 主要功能

- 创建、查看和管理直升机客运线路
- 双机位直升机场与独立屋顶停机坪
- 机型选择、车辆配额、票价和运营时段设置
- 起降排队、乘客上下机、步行接驳与自动返库
- 城市存档保存与恢复
- Mil Mi-26TC：60 个座位，最高巡航速度 350 km/h
- 简体中文与英文界面

## 安装

推荐从 [Paradox Mods](https://mods.paradoxplaza.com/mods/165769/Windows) 订阅：

1. 将模组加入当前播放集。
2. 等待下载完成，并在提示后重启游戏。
3. 进入“交通运输 → 直升机客运”开始建造。

请勿同时安装订阅版和本地版，以免重复加载运行库。运行模组不需要 CityWeaver MCP、Node.js 或 AI 客户端。

## 快速使用

1. 沿道路建造一座带机库的直升机场。
2. 放置另一座直升机场或独立停机坪，并确保设施连接道路和步行通道。
3. 从机场登机点开始连接各站，最后返回起点形成闭合线路。
4. 在线路详情中选择 Mil Mi-26TC，并设置车辆数量。

机场的两个机位可以分别服务不同线路。更多说明见[中文玩家指南](PLAYER-GUIDE.zh-CN.md)。

## 开发

### 环境要求

- Windows
- .NET SDK 8
- .NET Runtime 6.0.36
- 《都市：天际线 II》及官方 Modding Toolchain
- Node.js 20 或更高版本（测试与打包）
- Git LFS（获取最终原生资源）
- 由游戏工具链配置的 `CSII_*` 环境变量

首次克隆后拉取 LFS 资源：

```powershell
git lfs install
git lfs pull
```

### 构建与测试

```powershell
./build.ps1 -Configuration Release -Stage
npm test
```

`-Stage` 只会在项目的 `artifacts/` 目录生成构建产物，不会安装到游戏。

生成本地安装包并进行只读校验：

```powershell
node package.mjs --output artifacts/packages/PassengerHelicopters-local
./artifacts/packages/PassengerHelicopters-local/Install.ps1 -VerifyOnly
```

运行中的游戏无法热重载 DLL。测试新构建前请先退出游戏，再安装并重新启动。

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `Runtime/` | 运输业务运行时与最小存档兼容层 |
| `Loader/` | 模组入口 |
| `assets/native/` | 由 Git LFS 管理的最终游戏原生资源 |
| `assets/helicopter/` | 轻量发布素材与资产来源清单 |
| `Properties/` | 发布配置、封面与截图 |
| `test/` | 自动回归测试 |
| `tools/` | 资产处理工具 |

由于旧资产会序列化 `CityWeaver.*` 类型身份，项目保留最小兼容程序集 `CityWeaver.dll`。运输、模型、渲染和界面逻辑位于 `TransportFever3Helicopters.Runtime.dll`，模组入口为 `PassengerHelicopters.dll`。这些程序集均不包含 MCP 服务。

## 文档

- [中文玩家指南](PLAYER-GUIDE.zh-CN.md)
- [发布流程](PUBLICATION.md)
- [更新记录](CHANGELOG.md)
- [模型与资产来源](CREDITS.md)

## 许可证与声明

源代码按 [Apache License 2.0](LICENSE) 发布。第三方模型和资产的来源及署名见 [CREDITS.md](CREDITS.md)，相关权利归各自权利人所有。

本项目是社区模组，与 Colossal Order、Paradox Interactive 或 Urban Games 无隶属或官方背书关系。

原始导出模型和资产转换中间物不保存在 Git 仓库中；可通过现有工具链重新生成。可发布版本只依赖 `assets/native/ImportedData` 中的最终资源。

模组上传账号、认证信息、发布配置及内部上传记录不纳入仓库。发布时通过 `--publish-config <本地配置路径>` 或 `CSII_PUBLISH_CONFIG` 提供未跟踪的本地配置。
