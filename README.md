# DashKey

一个macOS 菜单栏启动器：读取 Dock 里的应用，给它分配 `Option + 键` 的全局快捷键，按住 `Option` 或按 `⌥Space` 即可弹出图标面板快速切换。

**核心特点：内存占用极低。** 空闲时实测常驻内存约 **30 MB**（RSS），远低于同类启动器。

## 特性

- **快捷键切换应用**：按键盘物理顺序自动分配 `Option` 组合键，与输入法、键盘布局无关
  `1 2 3 4 5 6 7 8 9 0` → `Q W E R T Y U I O P` → `A S D F G H J K L` → `Z X C V B N M`，共 36 个
- **两种呼出方式**，可在设置里分别开关：
  - 长按 `Option`（轮询系统修饰键状态，**免辅助功能授权**）
  - `⌥Space` 热键开/关面板
- **图标面板**：透明背景、迷你图标、单行居中，角标叠在图标右下角
- **无需辅助功能权限**：不再反复弹系统授权框
- **单实例**：同一时间只允许运行一个 DashKey
- **图标清晰度可调**：省内存 / 标准 / 高清，兼顾清晰度与内存
- **登录时启动**：基于 `SMAppService`（macOS 13+）

## 内存优化做了哪些事

1. **图标降采样**：Dock 原始图标通常是 512×512，几十个应用叠起来很吃内存。加载时就重绘成小位图（默认 128px），显示最大才 36pt，足够清晰。
2. **只加载前 36 个**：面板与快捷键上限即 36，Dock 中多余应用不创建图标。
3. **面板用完即释放**：HUD 隐藏时销毁窗口与内部视图，空闲不常驻。
4. **图标只解码一次**：启动时解码并复用，避免每次呼出重新解码。

> 实测：启动后空闲 RSS ≈ 31 MB；弹出面板时会短暂上升，隐藏后释放回落。

## 系统要求

- macOS 13.0 及以上
- 构建需要 Xcode Command Line Tools（`swiftc`）

## 构建

```bash
git clone git@github.com:linghuoya/Dashkey.git
cd Dashkey
bash build.sh
```

产物在 `Build/DashKey.app`。

构建脚本会优先使用钥匙串中名为 `DashKey` 的代码签名证书；没有则自动回退 ad-hoc 签名。

## 安装与使用

```bash
cp -R Build/DashKey.app /Applications/
open /Applications/DashKey.app
```

首次打开后，菜单栏会出现 DashKey 图标。使用方式：

1. 按住 `Option` 约 2 秒（可在设置中调整），弹出图标面板，松开收起；
2. 或按 `⌥Space` 开/关面板；
3. 面板中按对应的 `Option + 键`（或直接点击图标）切换应用。

菜单栏菜单里可打开「设置…」「重新读取 Dock」「退出」。

## 设置项

| 项目 | 说明 |
| --- | --- |
| 长按触发时长 | 0.5 – 3.0 秒，调整多久呼出面板 |
| 调出面板 | 分别开关「长按 Option」与「⌥Space 热键」 |
| 图标清晰度 | 省内存 / 标准 / 高清（高清更清晰，但更占内存） |
| 登录时启动 | 开机自动启动 |

## 说明

- 长按 `Option` 通过轮询 `CGEventSource.flagsState(.hidSystemState)` 实现，不建立 event tap，因此**不需要辅助功能授权**。若个别系统版本读不到状态，可用 `⌥Space` 热键兜底。
- 快捷键通过 Carbon `RegisterEventHotKey` 注册，同样无需辅助功能权限。
- `⌥Space` 在个别输入场景可能与特殊字符冲突，但全局热键会优先拦截。

## License

MIT
