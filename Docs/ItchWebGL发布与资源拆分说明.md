# CatCafe：itch.io WebGL 发布与资源拆分说明

## 当前问题

itch.io 的 HTML5 游戏包要求 ZIP 内的单个文件不超过 200 MB。`WEBGL0035` 的
`Build/WEBGL0035.data.br` 为 201.55 MB，因此虽能上传，但 itch.io 无法加载该网页游戏。

这不是 ZIP 总大小的限制，也不是把 ZIP 再压缩一次能够解决的问题；需要让 WebGL 首包中的
单个数据文件变小。

## 本项目采用的方式

首屏 StartUI 的六张大图从 `Assets/Resources/CatCafe/StartUI` 移至
`Assets/CatCafeBundled/StartUI`，不再随 `Resources` 打入首包。

构建前，`CatCafeStartUiBundleBuildProcessor` 会将这些图以独立 AssetBundle 写入临时目录
`Assets/StreamingAssets/CatCafeStartUiBundles`。游戏启动时，
`CatCafeStartUiBundleCache` 通过 `UnityWebRequestAssetBundle` 读取它们；构建结束后临时目录会被清理。

- 没有缩小图片尺寸、降低色深或使用有损纹理压缩。
- AssetBundle 使用 LZ4 容器压缩，仅用于传输与拆分，不改变美术源图质量。
- Editor 模式保留从 `Assets/CatCafeBundled/StartUI` 读取的回退路径，便于本地调试。

相关代码：

- `Assets/Editor/CatCafeStartUiBundleBuildProcessor.cs`
- `Assets/Scripts/CatCafe/Core/CatCafeStartUiBundleCache.cs`
- `Assets/Scripts/CatCafe/CatCafeStartController.cs`

## 手动 WebGL 构建

1. 先退出 Play Mode，确认 Console 没有编译错误。
2. 在 Unity 打开 **File > Build Settings**，选择 **WebGL**，点击 **Switch Platform**（若已切换可跳过）。
3. 点击 **Build**，输出目录选择新的、明确的版本目录，例如 `D:\Builds\WEBGL0036`。
4. 构建完成后检查输出根目录存在 `index.html` 和 `Build` 目录。
5. 检查 `Build` 目录中每个文件都小于 200 MB；尤其确认 `.data.br` 已小于 200 MB。
6. 在浏览器或本地 Web 服务器实际打开构建，确认开始界面六张图正常显示。

构建中生成的 `Assets/StreamingAssets/CatCafeStartUiBundles` 是临时目录：构建成功后应自动删除；若构建中断，可在 Unity 菜单 **Tools > CatCafe > Clean Start UI Bundle Staging** 清理。

## 上传到 itch.io

仓库的 Actions Secret 已配置为 `BUTLER_API_KEY`。上传方式二选一：

### GitHub Actions（适合可重复发布）

将 WebGL 成品 ZIP（ZIP 根目录必须直接包含 `index.html`）放到仓库约定的发布输入位置，推送触发部署工作流。发布前仍应执行上面的“单文件小于 200 MB”检查；butler 无法绕过 itch.io 的 HTML5 文件上限。

### 本机 butler（适合手动验证后的立即上传）

在 PowerShell 中执行：

```powershell
butler push "D:\Builds\WEBGL0036" toffeerie/catcafe:html5 --userversion 0036
```

若尚未安装 butler，可从 itch.io 的 butler 页面下载 Windows 版本并将其目录加入 `PATH`。上传完成后，在 itch.io 的 **Edit game > Uploads** 确认该上传被标记为 **HTML5**。

## 发布验收

1. itch.io 项目页没有 “Zip contains file that is too large”。
2. 网页版能加载且开始界面美术完整。
3. 排行榜弹窗能够显示列表行和“加载更多”按钮。
4. 在无缓存窗口重新打开一次，确认不是旧版本缓存。

