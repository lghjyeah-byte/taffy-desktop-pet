# 永雏塔菲桌宠

非官方 Windows 桌面宠物，当前版本 v1.4。

## 下载与安装

[下载 Windows 安装包](downloads/TaffyDesktopPet-Setup-v1.4.exe?raw=true)（约17.8 MB）

支持 Windows 10/11，需要 .NET Framework 4.x 完整组件。下载安装包后双击，可选择安装位置、桌面快捷方式和开机启动。开机启动默认关闭。安装后可通过系统应用列表卸载。

## 功能

- 透明桌宠、拖动、缩放、置顶、托盘与静音。
- 走路、摸头、伸懒腰、坐下、跳舞、跳跃、休息、拖动反应。
- 10条既有录音；可选择开机启动与随机自主动作。
- 按角色可见轮廓贴近主显示器工作区边缘。

![走路预览](outputs/走路动作预览.gif)

## 直接运行与构建

下载整个仓库后，双击 outputs/TaffyPet.exe；保留旁边的 sprites.png、motions 和 audio 文件夹。

Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\src\build.ps1
```

构建依赖 Windows 自带的 .NET Framework 4.x C# 编译器及 WPF。安装包构建另需官方 NSIS 3.13，解压到 work/installer-tools/nsis-3.13 后运行 src/build-installer.ps1；编译器本身不包含在仓库中。

## 素材来源与当前限制

本项目为非官方同人项目，角色及既有音频的相关权利归原权利人，不代表获得官方授权。未将第三方素材声明为本项目原创或授予开源许可。

角色图片依据参考设计生成，保留第一版原始母版；新动作是参考母版制作的新绘图。动画为四帧2D序列，不是完整 Live2D/3D 模型，不保证所有动作画风完全一致。移动范围为主显示器工作区。

10条语音来自[公开音频资料库](https://huggingface.co/datasets/XzJosh/audiodataset/tree/main)，未使用TTS或声音克隆。[语音映射及文件哈希](outputs/语音来源.json)采用第三方转写，尚未逐条定位原直播时间戳。

详细操作见[使用说明](outputs/使用说明.md)。安装包未进行代码签名；当前验证覆盖本机隔离安装、文件校验、程序启动及卸载，未验证全部电脑配置。
