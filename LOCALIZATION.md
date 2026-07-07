# Midgard Studio 中文化说明（汉化维护指南）

本文档面向需要维护/扩展中文翻译、以及跟踪上游更新的开发者。读完它你就能独立完成：
新增翻译、切换语言、跟随上游更新而不丢汉化。

---

## 一、汉化架构总览

汉化采用 **WPF ResourceDictionary + DynamicResource** 单一系统，设计目标是
**上游更新时几乎零冲突**。

### 文件位置

所有汉化基础设施都在新建目录里，**上游永远不会改动这些文件**：

```
src/MidgardStudio.App/Localization/
├── LocalizationService.cs   # 运行时语言管理：加载/切换/查表（带英文兜底）
├── en.xaml                  # 英文基准资源表（兜底，每个 key 都必须有）
└── zh-CN.xaml               # 简体中文资源表（与 en.xaml 一一对应）
```

### 工作原理

1. **字符串以 `x:Key` 形式存在**两个字典里，例如 `Menu_File`：
   - `en.xaml`：`<s:String x:Key="Menu_File">_File</s:String>`
   - `zh-CN.xaml`：`<s:String x:Key="Menu_File">文件(_F)</s:String>`

2. **XAML 里用 `{DynamicResource Key}` 绑定**，不再写死英文：
   ```xml
   <MenuItem Header="{DynamicResource Menu_File}" />
   ```
   切语言时，字典被换掉，所有 `DynamicResource` 自动刷新，**无需重启程序**。

3. **C# 里用 `LocalizationService.Get("Key")` 查表**（带英文兜底，找不到也不会崩）：
   ```csharp
   Views.ConfirmDialog.Show(LocalizationService.Get("Msg_Reload_Title"), ...);
   ```

4. **语言选择持久化**在 `app-settings.json` 的 `Language` 字段（默认 `zh-CN`）。

### key 命名约定

`<区域>_<元素>[_<细节>]`，例如：
- `Menu_File`（菜单）
- `Nav_Items`（导航区段）
- `Settings_Save_Manual`（设置页）
- `Msg_Reload_Title`（C# 消息）
- `Btn_Save_Tooltip`（按钮工具提示）

---

## 二、新增一条翻译（标准流程）

当上游新增了一个英文字符串、或你想汉化一个还没处理的界面，照下面做：

### 步骤

1. **想一个 key**（遵循上面的命名约定），例如 `Btn_Reset`。

2. **在 `en.xaml` 和 `zh-CN.xaml` 里各加一行**（两处都要加！）：
   ```xml
   <!-- en.xaml -->
   <s:String x:Key="Btn_Reset">Reset</s:String>
   <!-- zh-CN.xaml -->
   <s:String x:Key="Btn_Reset">重置</s:String>
   ```

3. **在 XAML 里把英文换成 `{DynamicResource Key}`**：
   ```xml
   <ui:Button Content="Reset" />            <!-- 改前 -->
   <ui:Button Content="{DynamicResource Btn_Reset}" />   <!-- 改后 -->
   ```

   或在 C# 里用 `Get`：
   ```csharp
   LocalizationService.Get("Btn_Reset")
   ```

4. `dotnet build src/MidgardStudio.App/MidgardStudio.App.csproj` 编译验证。

### 注意事项

- **带占位符的字符串**用 `{0}`、`{1}`，C# 端用 `string.Format`：
  ```xml
  <s:String x:Key="Msg_Compat_More">…and {0} more.</s:String>
  ```
  ```csharp
  string.Format(LocalizationService.Get("Msg_Compat_More"), count)
  ```

- **访问键助记符（&前缀下划线）**：中文里仍可用，例如 `文件(_F)`。

- **找不到的 key 会怎样**？`Get` 返回 key 本身，XAML 显示空白——不报错、不崩溃。
  所以**漏翻译只会显示空白，不会让程序崩**，但请保证 `en.xaml` 里每个用到的 key 都在。

- ⚠️ **Style Setter 里的 DynamicResource 不支持热切换**：
  例如 `MainWindow` 里“无更改/未保存更改”的状态文本放在 `<Setter Property="Text">` 里。
  这类文本**切语言后不会实时刷新**，需要重开视图才更新。这是 WPF 的已知限制。
  新增翻译时尽量避免在 Style Setter 里放 DynamicResource，改用直接绑定。

---

## 三、切换语言

- **用户侧**：设置（Settings）▸ 常规（General）▸ 语言下拉，选 中文 / English，**立即生效，无需重启**。
- **代码侧**：`LocalizationService.SetLanguage("en")`（或 `"zh-CN"`）。
- **持久化**：选择会写入 `app-settings.json` 的 `Language`，下次启动自动恢复。

---

## 四、跟随上游更新（最重要的维护操作）

本项目跟踪 `https://github.com/fahhadalsubaie/MidgardStudio`（活跃维护，两周内发了 4 个版本）。
我们用**双分支模型**让上游更新尽可能无痛：

### 分支约定

| 分支 | 用途 | 规则 |
|------|------|------|
| `main` | 上游镜像 | **永远只做 `git pull`，不在这里改任何东西** |
| `localization-zh` | 汉化工作 | 所有汉化改动都在这里 |

### 日常更新流程

```bash
cd H:/MidgardStudio

# 1) 把上游最新代码拉到 main（main 是干净的，不会有冲突）
git checkout main
git pull

# 2) 切到汉化分支，把你的汉化“重放”到新 main 之上
git checkout localization-zh
git rebase main

# 3) 如果 rebase 报冲突，解决它（见下文）；否则直接完成
```

### rebase 冲突怎么解？

由于翻译集中在 `Localization/` 新目录里（上游不碰），冲突几乎只会发生在**被汉化的源文件**上
（比如上游改了 `MainWindow.xaml` 某行的英文）。处理很简单：

1. 打开冲突文件，找到 `<<<<<<<` 标记。
2. **保留你的 `{DynamicResource Key}` 绑定**那一侧（这是汉化侧）。
3. 如果上游那一侧**新增了英文文本**：
   - 给它想个新 key
   - 在 `en.xaml` + `zh-CN.xaml` 各加一行
   - 把那一行的英文换成 `{DynamicResource 新Key}`
4. 删掉冲突标记，`git add` 后 `git rebase --continue`。

> 提示：绝大多数情况下，上游改的是**另一行**（比如改某个工具提示的措辞），而你的改动在
> **不同行**（把那一行换成 DynamicResource）—— git 会自动合并，根本不冲突。

### 实在搞不定怎么办

随时可以“重置回干净状态”重来——因为汉化改动都是增量的、可复现的：

```bash
git checkout localization-zh
git reset --hard main          # 汉化分支退回和 main 一致（丢弃汉化改动）
# 然后按“新增翻译”流程重新加，或从备份分支 cherry-pick
```

所以建议：**每次做完一批翻译就 commit 一次**，方便回退。

---

## 五、汉化进度

### Phase 1（已完成 ✅）— 核心界面

- [x] 主窗口菜单栏（文件/编辑/视图/工具/帮助）
- [x] 标题栏：品牌名、版本胶囊、更新胶囊
- [x] 状态栏：配置文件、保存状态、撤销/重做
- [x] 记录操作按钮（YAML / 复制到 Import / 新建组合 / 保存）
- [x] 命令面板（Ctrl+K 搜索框）
- [x] 左侧导航区段名 + 描述 + 分组标题
- [x] 模式标签（Renewal / Pre-Renewal）
- [x] 设置页（常规/快捷键/关于 + 语言下拉）
- [x] 确认对话框（ConfirmDialog：是/取消/确定/保存/不保存）
- [x] 关于对话框（AboutDialog）
- [x] 启动画面（SplashWindow）
- [x] 常见 C# 消息（保存失败、重载、切换配置、兼容性、更新提示、保存摘要）

### Phase 2（待办）— 次要视图

以下视图仍含英文（多为业务编辑器/对话框），按需用同样的流程汉化：

| 文件 | 内容 |
|------|------|
| `Views/OnboardingView.xaml` | 首次启动引导 |
| `Views/ConfigurationWizardView.xaml` | 配置文件/工作区向导 |
| `Views/DbWorkspaceView.xaml` | 数据库工作区（列表/详情/搜索） |
| `Views/ForgeView.xaml` | 物品打造器 |
| `Views/GrfBrowserView.xaml` | GRF 浏览器 |
| `Views/ValidationView.xaml` | 校验面板（含 Core 的校验消息） |
| `Views/CashShopManagerView.xaml` | 商城管理器 |
| `Views/MapCacheEditorView.xaml` | 地图缓存编辑器 |
| `Views/BackupManagerView.xaml` | 备份管理器 |
| `Views/ClientItemsView.xaml` | 客户端物品编辑器 |
| `Views/ClientSkillsView.xaml` | 客户端技能编辑器 |
| `Views/ComboEditorView.xaml` | 物品组合编辑器 |
| 各类 Picker/Builder/Editor 对话框 | `IconPickerDialog`、`SkillPickerDialog`、`SpritePickerDialog`、`BonusBuilderDialog`、`DropEditDialog`、`FieldEditorDialog`、`ChangeIdDialog`、`IdInputDialog`、`RecordPickerDialog`、`ReferenceInputDialog`、`SaveSummaryDialog`、`UpdateDialog`、`YamlPreviewDialog` |
| `Services/WorkspaceValidator.cs` | 校验消息（散落的插值字符串，工作量最大） |
| 各 ViewModel 里的 `ConfirmDialog` 调用 | 备份/客户端物品/技能/组合等 |

### 不汉化的部分

- 内部日志 / 异常消息（用户不可见，仅用于排错）。
- 游戏数据本身（物品名、技能名等来自 rAthena YAML/Lua）。

---

## 六、验证汉化是否正常

```bash
cd H:/MidgardStudio
dotnet build src/MidgardStudio.App/MidgardStudio.App.csproj
# 期望：0 警告 0 错误

dotnet run --project src/MidgardStudio.App/MidgardStudio.App.csproj --no-build
# 启动后：界面应为中文；设置里切英文立即生效；缺 key 处回退英文（不空白不报错）
```

日志位置：`%LOCALAPPDATA%\Midgard Studio\logs\MidgardStudio-<日期>.log`
（启动行 `Midgard Studio starting up.` 之后无 Error 即正常。）
