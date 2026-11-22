# ExamReview

英语试卷讲评工具的 C# 14 / .NET 10 / WPF 重写版，替代原先的单文件 HTML/JS
方案。本文档面向接手后续开发的人／协作 AI，目标是不依赖这次对话的上下文
也能看懂现状、架构和下一步。

> `.lmm` 文件格式的完整规范见 [`docs/lmm-format-spec.md`](docs/lmm-format-spec.md)。

---

## 这是什么

一个桌面端英语（未来可扩展到其他学科）试卷讲评工作台：导入试卷
（PDF/DOCX/图片 + OCR）→ 自动结构化解析成章节/题目/答案/解析 → 逐题讲评
UI（中英对照、课堂小练）→ 手写板书批注（含"美化"笔迹算法）→ 保存为
`.lmm` 文件、支持翻译与试卷库管理。

旧版是一个 ~16MB 的单文件 HTML/JS（ES5，约 3200 行业务逻辑），本项目是
对它的彻底重写，不是简单翻译——架构、文件格式、UI 技术栈都重新设计过，
但手写笔迹"美化"算法是从旧版逐变量移植并保留了其中两轮真实踩过的坑
（见下文"笔迹美化算法"一节），不是重新发明。

## 架构：通用引擎 + 学科插件

主框架（引擎/外壳）与学科无关：UI、渲染、笔迹批注、导入、翻译、讲评 UI
都是通用的。每个学科（英语、以后可能数学等）是一个插件模块；一个 `.lmm`
文件在 `manifest.json` 里声明自己属于哪个模块（`moduleId`），主框架据此
加载对应插件来解释/渲染/编辑内容。

```
ExamReview.sln
├── src/
│   ├── Lmm.Core/                  (net10.0，不引用任何 WPF/UI 程序集)
│   │     领域模型（Paper/Section/Question/Article/...）
│   │     笔迹几何算法（Lmm.Core/Ink 下，纯点数组进出，无 UI 依赖）
│   ├── Lmm.Format/                → Lmm.Core
│   │     .lmm 容器（ZIP）读写 + 版本迁移
│   ├── Lmm.Plugins.Abstractions/  → Lmm.Core
│   │     ISubjectModule 等插件契约（刻意跟 Lmm.Core 分开，方便第三方
│   │     模块只引用这个小契约库）
│   ├── Lmm.Plugins.English/       → Lmm.Plugins.Abstractions, Lmm.Core
│   │     英语学科模块（题型处理器、试卷校验）
│   ├── Lmm.Import/                → Lmm.Core
│   │     纯文本/粘贴文本 → 试卷结构化解析（已实现）
│   │     PDF / OCR / DOCX 导入（未实现）
│   ├── Lmm.Translation/           → Lmm.Core
│   │     ITranslationProvider + MyMemory 实现
│   └── ExamReview.App/            (net10.0-windows, WPF) → 以上全部
│         讲评主窗口：文章 / 题目 / 批注 / 结构 / 对照 / 练习
└── tests/
      Lmm.Core.Tests / Lmm.Format.Tests / Lmm.Plugins.English.Tests /
      Lmm.Import.Tests (xUnit)
```

依赖方向单向无环：`App → {插件, Import, Translation, Format} → Core`。

`Lmm.Core` 是唯一必须保持"跟 UI 框架无关、依赖最轻"的项目——领域模型和
笔迹几何算法都在这，纯粹"点数组进、点数组出"，可以脱离 WPF 单独跑单元
测试。

### 插件契约

`Lmm.Plugins.Abstractions`（只有契约，实现在学科模块里）：

```csharp
public interface ISubjectModule {
    string ModuleId { get; }          // 如 "cn.examreview.subject.english.v1"
    string DisplayName { get; }
    SubjectCapabilities Capabilities { get; }
    IReadOnlyList<string> SupportedQuestionTypeIds { get; }
    IQuestionTypeHandler ResolveHandler(string questionTypeId);
    ValidationResult Validate(Section section);
}

public interface IQuestionTypeHandler {
    string QuestionTypeId { get; }
    object CreateReviewViewModel(Question question);   // 讲评面板用
    object CreateEditorViewModel(Question question);   // 试卷修正面板用
}
```

加载方式分阶段：前期用一个手动注册的 `ManualModuleCatalog`（只有英语一个
模块时，上 assembly-scanning/MEF 纯属过度设计）；等需要支持第三方/独立
分发的插件 dll 时，再换成一个基于 `AssemblyLoadContext` + 反射扫描
`[SubjectModule]` 特性的 `AssemblyScanningModuleCatalog`。宿主代码只认
`IModuleCatalog` 这一个接口，所以这次替换不应该牵动其他代码。

`.lmm` → 模块匹配规则：读 manifest 的 `moduleId`，`IModuleCatalog.Resolve`
找不到对应模块时必须明确报错"这个文件需要模块 X，但没安装"，不能静默
降级或崩溃——这是模块独立分发后真实会发生的失败场景，第一版就要把这条
错误路径做对。

## 手写笔迹"美化"算法

`Lmm.Core/Ink/` 下的 `InkGeometry` / `StrokeBeautifier` / `InkRibbon` /
`InkWidths` / `RibbonBuilder` 分两代：几何部分（重采样、角点识别、
Catmull-Rom 拟合）是从旧版 HTML/JS 应用的 `beautifyStroke`/`finishStroke`
逐变量移植的；**笔宽/描边**那部分在第二轮里换掉了旧版的"固定锥度拉伸"，
改成参照 [shardulvs/xnotes-android](https://github.com/shardulvs/xnotes-android)
的变宽缎带模型（沿中心线扫一个半径变化的圆盘，因此圆帽、圆连接、压感变粗
是天然结果，而不是事后补的锥度）。旧实现被否掉的原因是它只会"整体等宽 +
两端收窄"，写出来的板书像马克笔描的等宽线，没有笔锋。

旧几何部分踩过两个容易再踩一次的坑：

1. **角点识别**：早期按局部转弯角度判断"尖角"，会把连笔手写里又急又圆
   的转弯误判成尖角，拼接处描边拧成针眼状死结，手写签名变成锯齿。现在
   用 `CornerSplit`：先做 Douglas-Peucker 化简（`SimplifyDpIndices`），
   只有"化简后存活 + 前后臂长都够长（真正由两段直笔画夹出的角，如打勾/
   叉号/箭头）"才切；连笔书法本身没有直笔画，不会被误伤。**不要**把这个
   判断换回纯角度阈值。
2. **段与段交界处不去重**：`StrokeBeautifier` 里 `cornerSplit` 切出的
   段拼接时故意保留重复的角点（两份，不 `.slice(1)`）。缎带描边
   （`RibbonBuilder`）是按"前一点→后一点"算法线方向的，角点如果只留一
   份，两段的方向会在同一点上互相抵消，描边在尖角处会拧成一个针眼状的
   死结。**不要**为了"去重更干净"而合并这个重复点。

`Lmm.Core.Tests/Ink/` 下的测试（`InkGeometryTests`/`StrokeBeautifierTests`/
`RibbonBuilderTests`/`InkRibbonTests`）就是这些坑的回归用例（打勾角点识别、
连笔圆滑不误切、抖动鲁棒性、重复角点不产生 NaN、宽度因子确实产生粗细变化）
——修改这段算法后务必跑一遍。

笔宽模型（`InkWidths`）：`width = base/2 * 宽度因子 * 端部锥度`。宽度因子来自
两路输入——速度（`(中位速度/当前速度)**0.38`，慢则粗，限幅 0.60–1.30）和
压感（只在输入设备真的给出压力变化时启用，走 logistic 曲线）。端部锥度在
前/后各 12% 的弧长上降到 0.34，正在写的那一笔（live）只降到 0.75，收笔前
不会突然变细。速度 EMA 必须用第一个非零速度做种子，从 0 起算会让每一笔的
开头都被判成"极慢"从而最粗——这个 bug 已经修过，别退回去。

## 性能：WPF 怎么保证不卡

笔迹渲染目前是自定义 `FrameworkElement` + `OnRender`（`Controls/InkCanvasElement.cs`）：
每笔一次 `StrokeBeautifier.Beautify`，把结果多边形塞进 `StreamGeometry` 画出来；
正在写的那一笔也走同一套 `InkRibbon.Build(live: true)`，所以落笔时看到的粗细
和松手后一致（旧版这里画的是细线预览，抬笔会"跳"一下）。所有
`Brush`/`Pen`/`Geometry` `.Freeze()` 复用。长列表（题目侧栏）必须正确启用虚拟化
（`VirtualizingPanel.IsVirtualizing` + `VirtualizationMode="Recycling"`，
避免外层多包一层 `ScrollViewer`、非虚拟化面板模板、`Auto` 尺寸、整体
`Clear()` 这几个会"悄悄打破虚拟化"的坑）。这部分 UI 还没写，上面是设计
约束，不是已实现的代码。

## 构建现状 / 已知限制

历史：这个项目的骨架最早是在一个 **Linux 容器**里搭的，当时
`ExamReview.App`（WPF）根本编不过、3 个 xUnit 测试项目也因为容器出站网络
不通而从未跑过。这两条现在都不成立——在 Windows + .NET SDK 11.0.100 上
已经全量构建、全量测试、发布并实机截图验证过，见文末"Windows 上的实测记录"。

选型注意事项（选第三方库时仍然适用）：

- `ExamReview.App` 是 `net10.0-windows` + `UseWPF`，只能在 Windows 上编译；
  `.csproj` 里的 `EnableWindowsTargeting=true` 只是让 `dotnet restore`/IDE
  解析不报错，别指望在 Linux 上出可运行产物。
- 发布一律 **框架依赖**（`--self-contained false`）：目标机器需要装 .NET 10
  桌面运行时（`Microsoft.WindowsDesktop.App` 10.x）。不要改成 self-contained。

## 已完成 / 未完成

**已完成：**
- [x] Phase 0：解决方案骨架，10 个项目，依赖关系按架构图接好
- [x] Phase 1：`Lmm.Core` 领域模型（`Paper/Section/Article/
      Paragraph/Span/StructureRow/Question/Option/BilingualPair/Drill/
      InkStroke/PaperAsset`）
- [x] Phase 1：`Lmm.Format` 的 `.lmm` ZIP 容器读写
      （`LmmPackageReader`/`LmmPackageWriter`），含版本迁移骨架
      （`IMigrationStep`/`MigrationRunner`，目前无迁移步骤）
- [x] Phase 2a：笔迹美化几何库（`Lmm.Core/Ink/`），含 xUnit 测试
      （`ResamplePts` 的弧长累加 bug 已修）
- [x] Phase 2b：变宽笔迹（`InkWidths`/`InkRibbon`，压感+速度+端部锥度，
      圆帽圆连接）+ WPF `InkCanvasElement`（Stylus 取压力与时间戳，live 与
      落笔同一套渲染），笔迹按节存入 `.lmm`；离屏渲染截图验证过
- [x] 插件契约（`Lmm.Plugins.Abstractions`）：`ISubjectModule`、
      `IQuestionTypeHandler`、`IModuleCatalog`/`ManualModuleCatalog`、
      `SubjectCapabilities`、`ValidationResult`、题目卡片视图模型
- [x] `Lmm.Plugins.English`：`EnglishSubjectModule`、选择/兜底两种
      `IQuestionTypeHandler`、`EnglishSectionValidator`（题号、选项、
      span、pair、structure 引用校验）
- [x] 讲评主窗口（照 `完形——中年危机-课件.html` 的视觉重做）：自绘标题栏、
      横向章节 tab、卡片式文章区 + 批注画布、右侧"题目/段落结构/中英对照/
      课堂小练"四页、工具栏（打开·导入·保存·另存为｜批注·撤销一笔·清除本节｜
      显示全部答案｜A−/A+ 字号）、空态引导、拖放打开、Ctrl+O/S/I
- [x] 空壳原则：程序本身**不内置任何试卷**，启动就是空态；内容全部来自
      `.lmm` 文件或导入。命令行参数可挂文件关联（`ExamReview.App.exe x.lmm`）
- [x] 导入（`Lmm.Import.PaperTextImporter`）：纯文本/粘贴文本 → 试卷
      （文章分段、题号/选项/答案/解析识别），解析不到答案时占位并在
      `Notes` 里明说，不静默造假
- [x] 翻译（`Lmm.Translation`）：`ITranslationProvider` + MyMemory 实现
      （长段按句分块、配额告警转成可读错误），主窗口"翻译本节"按钮接好
- [x] 校验：保存前逐节 `Validate`，error 阻断并列出；缺模块时明确报错
- [x] 崩溃日志：`%APPDATA%\ExamReview\crash.log`（WinExe 无控制台，
      不留日志的话异常会静默消失）

**未完成（按原计划的下一步顺序）：**
- [ ] Phase 3：PDF/OCR/DOCX 导入管线（`Lmm.Import`，选型见下表；
      目前只有纯文本导入）
- [ ] Phase 2b 收尾：橡皮、多笔并存的手感细节，真机用绘图板试写后调参
      （当前只有笔，无橡皮；撤销是"撤销本节最后一笔"）
- [ ] Phase 5：导出（PDF/打印）、试卷库抽屉（文件夹 + 可重建的 SQLite 检索
      索引，索引不是数据源）、设置、快捷键自定义
- [ ] Phase 6：`ManualModuleCatalog` → `AssemblyScanningModuleCatalog`，
      并实现第二个学科模块验证插件契约
- [ ] 试卷修正面板（参考 HTML 里能改题号/名字的那个 fixBox）未移植

## 第三方库选型（尚未落到 csproj 里，供后续参考）

| 需求 | 选型 | 理由 |
|---|---|---|
| PDF 文字提取 | PdfPig | Apache 2.0，纯托管 |
| PDF 页面光栅化 | PDFtoImage（包装 pdfium） | MIT |
| OCR 主选 | Windows.Media.Ocr（WinRT） | 系统自带、免费、中英文都支持 |
| OCR 备选 | Tesseract（`Tesseract` NuGet） | Apache 2.0，系统 OCR 不够时兜底 |
| DOCX 解析 | DocumentFormat.OpenXml | 微软官方 OOXML SDK，MIT |
| 翻译 | `ITranslationProvider` 抽象 + 默认 MyMemory | 免费、不用注册 key |

明确避免：iText7（AGPL/商业双授权）、Aspose.PDF（商业收费）。

## Windows 上的首次实测记录（2026-11-22）

README 上面说的"到有正常网络的开发机上第一件事是 `dotnet test`"，已在这台
Windows 机器上做过：

- `dotnet build ExamReview.slnx -c Release` —— 0 警告 0 错误（.NET SDK 11.0.100）。
- `dotnet test ExamReview.slnx -c Release` —— **34/34 全绿**
  （Lmm.Core.Tests 19、Lmm.Format.Tests 2、Lmm.Plugins.English.Tests 8、
  Lmm.Import.Tests 5）。
- 首次跑测试挂了 1 个：`InkGeometryTests.ResamplePts_ProducesApproximatelyEqualSpacing`
  实测间距 1.17（期望 3.5–4.5）。原因是 `InkGeometry.ResamplePts` 的记账 bug：
  它用"上一个输出点到 `p[i]` 的弦长"来累计已走弧长，而这条弦会跨过已经
  走过的输入段，导致路径长度被重复计数，于是 `step=4` 时出现成串的
  ~0.03–1.2px 近重复点。已改为按输入段（`p[i-1]`→`p[i]`）逐段累计
  `carried` 弧长、到 step 再插值输出；同一用例现在稳定输出 3.997 间距。
- 发布：`dotnet publish src/ExamReview.App/ExamReview.App.csproj -c Release -r win-x64 --self-contained false -o <目录>`，
  产物可直接双击 `ExamReview.App.exe` 运行（框架依赖版约 0.4MB / 18 个文件）。
  发布一律用**框架依赖**（framework-dependent），不做 self-contained：目标机器需要装
  .NET 10 桌面运行时（Microsoft.WindowsDesktop.App 10.x）。
- 另外两条在本机踩到的、换台机器也大概率会踩的：
  - **DPI**：屏幕 1920x1080 @150% 时，不受 DPI 感知的截图/测量工具报的是
    被虚拟化的 DIP 坐标（如 1293x733），看起来像窗口被裁掉一角，其实不是。
    已用 `app.manifest` 声明 PerMonitorV2，窗口尺寸按
    `SystemParameters.WorkArea` 钳制、小屏自动最大化。
  - **静默退出**：`WinExe` 没有控制台，异常会无声消失（WER 也不一定留痕）。
    已加 `crash.log`；再有"窗口一闪就没了"，先看
    `%APPDATA%\ExamReview\crash.log`。

