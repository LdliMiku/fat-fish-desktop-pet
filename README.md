# 大肥鱼桌宠 · v0.2

Windows 原生透明桌宠：拖动、八方向视线跟随、自然眨眼、拎起落地、摸头回应与爱心粒子、左右张望、可调的主动搭话，以及一个能真的聊天的窗口（接 DeepSeek 的 OpenAI 兼容接口）。大小与各类动画速度都能独立调节。

## 直接运行

下载并解压整个项目，双击根目录的 **大肥鱼桌宠.exe**。需要 Windows 自带的 .NET Framework 4.x，不需要安装 Python、Node.js 或其他运行库。

- 按住鼠标左键拖动，播放拎起和落地动作；滚轮或右键菜单调节大小。
- 角色根据鼠标相对位置平滑看向八个方向，鼠标静止 5 秒后回正。
- 每 3–5 秒自然眨眼，待机时有轻微呼吸。
- 在头顶来回轻扫可以摸头：她会闭眼微笑、双手收在胸前，周围飘起淡粉爱心；停手后柔和放手。
- 鼠标和交互安静一段时间后，她会左右张望一次（间隔可在 10–30 秒之间调）。
- 她还会主动搭话：开机与跨时段问候、久坐提醒、按可调间隔说句闲话，也会在你摸头、点击、拎起放下时接一句。
- 右键 →「和我说话…」打开对话窗口，接 DeepSeek 接口聊天；聊天记录与 API Key 只保存在本机 `data` 目录，不会进入 Git。
- 她说的话会以手绘气泡出现在角色侧边；点气泡可以直接打开对话窗口。

## v0.2 特点

- 姿势过渡每一帧只使用一套完整角色绘画，解决头发、尾巴和脸部双轮廓；八个方向都有真实半程姿势。
- 眨眼只替换眼睛区域；摸头使用专用九宫格绘画，不经过旧闭眼遮罩。
- 新增「平滑缩放」：滚轮、滑块与数值输入共用平滑目标尺寸，缩放时脚底锚点固定。
- 新增「左右张望」与「摸头回应」等动作，各自拥有独立倍率（0.25–5 倍，默认 1 倍，保存在本地）。
- 新增对话功能：默认接 `https://api.deepseek.com/v1` 的 `deepseek-chat`，API Key 用 Windows DPAPI 加密保存；失败时退回本地台词，不会卡住。
- 全部动画帧的色调统一到正视基准，切换姿势时不再出现明暗跳变。
- 说话气泡按参考素材九宫格绘制：四角与角落装饰按原比例，只有中段随文字拉伸。

![九个朝向](docs/previews/directions.png)

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `大肥鱼桌宠.exe` | 已编译的 v0.2.0.0 程序 |
| `src` | WPF 窗口、交互、动画状态、粒子、对话与气泡绘制代码 |
| `assets/animations/v6` | 当前 59 帧图集、过渡源图、定位和运动场数据 |
| `assets/particles` | 摸头爱心贴图 |
| `assets/ui` | 说话气泡素材（参考图、本体、尾巴、装饰与 `bubble-meta.json`） |
| `assets/reference` | 原始角色参考图 |
| `tests` | 原生 WPF 回归检查（动画、缩放、对话引擎、气泡、主动搭话） |
| `docs/previews` | 当前方向和切换预览 |
| `AGENTS.md` | 后续开发必须遵守的维护约定 |

程序运行所需素材已经嵌入 EXE。仓库同时保留完整构建资源，方便继续开发。运行数据（`data/settings.txt`、`data/chat-history.jsonl`、`data/chat-key.dat` 等）不会上传到 Git。

## 从源码构建

在项目根目录打开 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

默认生成根目录的 EXE。程序正在运行时可先退出，或指定另一个输出路径：

```powershell
.\build.ps1 -OutputPath .build\PetAnimated.exe
```

构建使用 Windows .NET Framework 4.x 自带的 C# 编译器，不需要额外依赖。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check-resize.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check-chat.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check-bubble.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\check-talk.ps1
```

分别检查动画倍率与单源画面约束、缩放边界与脚底锚点、对话引擎（配置与 Key 加密、历史文件、请求与错误兜底）、说话气泡（九宫格渲染、装饰位置、接缝、跟随放置）与主动搭话规则（问候、久坐提醒、每日上限、冷却）。自动检查不等于视觉验收；色调、气泡观感与动作手感均由用户实机确认。离屏耗时不等于显示器实际帧率。

修改动画素材、重新生成图集或运动场前，请先阅读开发文档——它只保存在本地开发目录（`开发文档.md`），不随本仓库发布。

调试素材观感时可以用 `tests/` 里的预览工具（都用与程序相同的渲染路径，输出 PNG 便于比对）：`make-chat-mock.ps1`（聊天窗口布局）、`make-particle-preview.ps1`（摸头爱心粒子）、`make-bubble-shot.ps1`（指定文本的气泡）。

本仓库未指定开源许可证。
