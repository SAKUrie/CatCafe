# many_face

一个 2D 伪装/变脸解谜游戏项目。玩法方向类似“变脸特工”：玩家根据关卡线索选择合适的脸、身份或伪装，骗过检查、完成任务或推进剧情。

## 项目信息

- 引擎：Unity 6000.5.3f1
- 类型：2D 解谜 / 伪装 / 角色切换
- 主分支：`main`
- 仓库：`https://github.com/BREAKUpandF/many_face.git`

## 目录结构

```text
Assets/
  Art/
    Characters/
    Faces/
    UI/
  Audio/
  Prefabs/
  Scenes/
  Scripts/
  Settings/
```

## 资源放置规则

- `Assets/Art/Characters`：角色身体、NPC、外星人、守卫等人物资源。
- `Assets/Art/Faces`：可替换的脸、面具、表情、身份图标。
- `Assets/Art/UI`：按钮、提示框、对话框、关卡图标等 UI 图片。
- `Assets/Audio`：背景音乐、按钮音效、变脸音效、失败/成功音效。
- `Assets/Prefabs`：角色、检查点、UI 面板、可重复使用的关卡物件。
- `Assets/Scenes`：Unity 场景文件。
- `Assets/Scripts`：所有 C# 脚本。
- `Assets/Settings`：Unity 和 URP 设置文件，不随便移动。

## 当前项目设置

- 默认模式已切到 2D。
- 初始场景主摄像机已改为 Orthographic。
- 项目默认窗口大小为 `1280x720`。
- 已清理 Unity 模板自带的教程文件。

## 团队协作注意

- 每次开始做之前先 `git pull origin main`。
- 提交前确认 Unity 没有报错。
- 不要提交 `Library/`、`Logs/`、`Temp/`、`UserSettings/`。
- 素材文件会通过 Git LFS 管理，添加大图片、音频、视频时正常提交即可。
- 移动或删除 Unity 资源时尽量在 Unity 编辑器里操作，避免 `.meta` 文件丢失。

## 代码编辑器

团队成员可以自己选择代码编辑器，例如 Visual Studio、VS Code 或 Rider。

如果使用 VS Code，建议安装这些扩展：

- Unity：`visualstudiotoolsforunity.vstuc`
- C# Dev Kit：`ms-dotnettools.csdevkit`
- C#：`ms-dotnettools.csharp`

Unity 中设置 VS Code：

```text
Edit -> Preferences -> External Tools -> External Script Editor -> Visual Studio Code
```

如果 VS Code 没有自动识别代码，打开 Unity 后执行：

```text
Edit -> Preferences -> External Tools -> Regenerate project files
```

## 初期建议

优先做一个最小可玩原型：

1. 一个目标 NPC。
2. 三张可选择的脸。
3. 一个检查规则，例如“只有外星人脸能通过”。
4. 选择正确则过关，选择错误则失败。

先把核心变脸判断跑通，再扩展更多关卡和剧情。
