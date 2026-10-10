# Editor Translator (AI Bilingual)

Unity 编辑器界面**实时双语翻译**插件：自动 AI 翻译编辑器窗口里出现的所有文本（包括以后新装的第三方插件），以 **中文 (English)** 双语形式显示而不是替换原文，菜单一键开关、立即生效。

## 安装

**方式一（推荐，嵌入式 UPM 包）：** 把整个 `EditorTranslator` 文件夹复制到项目的 `Packages/` 目录下。

**方式二：** 直接把 `EditorTranslator` 文件夹复制到项目的 `Assets/` 目录下。

> 包内 `Editor/Plugins/0Harmony.dll`（Harmony 2.2.2，MIT 许可，来自 [Pardeike/Harmony](https://github.com/pardeike/Harmony)）是运行时拦截所必需的，随包分发，无需额外安装。

适用于 Unity **2021.3+**（在 2022.3.22f1 上开发验证）。

## 使用

| 菜单 | 作用 |
|---|---|
| `Tools → Editor Translator → Enable Translation`（**Ctrl+Alt+T**） | 开 / 关翻译，立即生效，按项目记忆状态 |
| `Tools → Editor Translator → Settings...` | 翻译后端、双语格式、黑名单等配置 |
| `Tools → Editor Translator → Translation Manager...` | 浏览 / 修改 / 忽略 / 删除译文，导入导出缓存 |

启用后：

1. 界面上每出现一段没见过的新文本（不管来自 Unity 还是哪个插件），都会自动进入翻译队列，翻好后所有窗口自动刷新成双语 —— **新装插件无需任何配置**。
2. 译文缓存在 `Library/EditorTranslator/cache.json`，只在首次出现时翻译一次，之后零开销显示。
3. 关闭开关后界面瞬间恢复原文；重新打开立刻恢复双语。

## 翻译后端

**默认：Google 免费接口**（`translate.googleapis.com`，无需任何密钥），要求网络能访问 Google。机翻质量，开箱即用。

**推荐：OpenAI 兼容接口**（LLM 翻译，质量明显更好）。Settings 里切换后端，填入任意 OpenAI 兼容服务的：

- Endpoint：如 `https://api.openai.com/v1`（自动拼接 `/chat/completions`，中转站地址同样适用）
- Model：如 `gpt-4o-mini` / `deepseek-chat` 等
- API Key（存放在 EditorPrefs，**不会**写入工程文件，不会进 git）

文本按批（默认 20 条/批、最多 2 批并发）异步发送，失败自动冷却重试，3 次后停止并在 Settings / Manager 里可手动重试。

## 双语显示格式（Settings 可选）

| 格式 | 效果 |
|---|---|
| `Inline`（默认） | `中文 (English)` |
| `Stacked` | 中文换行 English（部分高度固定的控件会被裁切，慎用） |
| `ChineseOnly` | 仅中文 |

"过宽时仅显示译文" 默认开启：译文加原文超过原宽度太多时自动只显示中文，避免按钮/标签文字被截断。

## 覆盖范围

✅ **覆盖**：Inspector、所有 EditorWindow（第三方插件的 IMGUI 窗口）、Project Settings 面板等一切 IMGUI 绘制的标签、按钮、标题。

❌ **不覆盖**（受 Unity 机制限制）：顶部菜单栏与右键原生菜单、`EditorUtility.DisplayDialog` 等系统对话框、Tooltip 悬浮提示、使用 UI Toolkit（UITK）编写的新式窗口、输入框（TextField）里的内容 —— 输入框不翻译是刻意设计，保证资产名、路径等数据安全。

默认窗口黑名单（可编辑）：Hierarchy、Project、Console、Scene、Game、Animator、Timeline —— 这些窗口显示的是你的资产与对象名，必须保持原文。Settings 里有 **"翻译资产名"** 开关：默认关闭；开启后黑名单不再拦截，场景物体与资产名也会被 AI 翻译（它们是数据名称，译文可能不符合预期，可在 Manager 里逐条修正或忽略）。也可在 Settings 里用正则永久忽略特定文本，或在 Manager 里对单条译文点"忽略"。

## 技术原理

- `[InitializeOnLoad]` 时通过反射加载包内 `0Harmony.dll`，对 `GUIStyle.Draw(Rect, GUIContent, ...)` 的全部重载做 Harmony prefix/postfix（IMGUI 所有控件的最终绘制入口，官方源码已核对）。
- 绘制前把 `GUIContent.text` 换成双语形式，绘制后立即恢复原文 —— 不污染 GUIContent 池，不影响布局，开关即时生效。
- 文本先经启发式过滤（跳过路径、纯数字、常见缩写、camelCase 标识符等），再按窗口黑名单过滤，剩下的才进翻译队列。
- 翻译在后台线程执行，主线程只做一次字典查找，不卡编辑器。域重载（改脚本/重启）后自动重新挂钩、缓存自动恢复。

## 隐私说明

启用后，界面上出现的文本内容会发送到你选择的翻译服务（Google 或你配置的 LLM API）。介意的话请勿在含敏感内容的项目中启用，或切换到本地/内网 LLM 后端。

## 已知局限

- Unity 未来大版本若改动 `GUIStyle.Draw` 内部结构，插件会自动检测失败并在 Console 打 `[EditorTranslator]` 错误提示，不会导致编辑器崩溃。
- 机翻/LLM 对 Unity 专业术语的译法可能不统一，可在 Manager 里人工修正（修正结果持久保存，也可导出共享）。
