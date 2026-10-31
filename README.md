# PCL Pro（Plain Craft Launcher Pro）

PCL Pro 是基于 [PCL2（Plain Craft Launcher 2）](https://github.com/Meloong-Git/PCL) 二次开发的 Minecraft 启动器修改版，作者：paopao340。

> 本项目为个人修改版，非 PCL 官方仓库。PCL 版权归原作者 Meloong（龙腾猫跃）所有，遵循上游协议（AGPL-3.0），本项目同样以 AGPL-3.0 开源。

## 相比上游的主要改动

- 内置陶瓦联机（Terracotta），开房即用，房间码形如 `U/XXXX-XXXX-XXXX-XXXX`，无需端口映射
- 外置登录（Authlib-Injector）与微软登录流程修复
- 精简冗余模块，界面统一（MyCard／圆角／阴影规范一致）
- 材质下载回退、地图壁纸支持
- 移除动态视频壁纸（避免随包分发素材的版权与体积问题），仅保留原生静态壁纸

## 目录结构

| 路径 | 说明 |
| --- | --- |
| `PCLPro/` | 启动器主工程（VB.NET + WPF，.NET Framework 4.8） |
| `PCLCS/` | 伴随 C# 工程 |
| `MeloongCore/` | 上游核心库（已随仓库以源码形式内置，不依赖子模块） |
| `PCLPro.sln` | 解决方案文件 |

## 构建

环境要求：Windows 10/11、Visual Studio 2022（含 .NET 桌面开发与 VB 工作负载）、.NET Framework 4.8 目标包。

```powershell
# 还原并编译 Release
msbuild PCLPro.sln -t:Restore -p:Configuration=Release
msbuild PCLPro\PCLPro.vbproj -p:Configuration=Release
```

输出位于 `PCLPro\bin\PCLPro.exe`。

## 说明

- 仓库不再包含动态视频壁纸资源与相关代码
- 仓库不包含历史备份目录、编译产物（`bin/`、`obj/`）与更新补丁包
- 微软登录 OAuth Client ID、CurseForge API Key 未写入仓库，需通过环境变量 `PCL_MS_CLIENT_ID`、`PCL_CURSEFORGE_API_KEY` 提供

## 致谢

- [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)：PCL2 本体
- [LTCatt/PCL2Help](https://github.com/LTCatt/PCL2Help)：帮助文档库
- Terracotta 联机核心及其贡献者
