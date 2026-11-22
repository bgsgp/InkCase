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
│   │     英语学科模块（尚未实现，见"未完成"一节）
│   ├── Lmm.Import/                → Lmm.Core
│   │     PDF / OCR / DOCX 导入与试卷自动结构化解析（尚未实现）
│   ├── Lmm.Translation/           → Lmm.Core
│   │     ITranslationProvider + 具体实现（尚未实现）
│   └── ExamReview.App/            (net10.0-windows, WPF) → 以上全部
│         （尚未实现：目前是模板生成的空壳 WPF 项目）
└── tests/
      Lmm.Core.Tests / Lmm.Format.Tests / Lmm.Plugins.English.Tests (xUnit)
```

依赖方向单向无环：`App → {插件, Import, Translation, Format} → Core`。

`Lmm.Core` 是唯一必须保持"跟 UI 框架无关、依赖最轻"的项目——领域模型和
笔迹几何算法都在这，纯粹"点数组进、点数组出"，可以脱离 WPF 单独跑单元
测试。

### 插件契约

`Lmm.Plugins.Abstractions`（目前只有接口定义，没有实现）：

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

`Lmm.Core/Ink/` 下的 `InkGeometry` / `StrokeBeautifier` / `RibbonBuilder`
是从旧版 HTML/JS 应用的 `beautifyStroke`/`finishStroke`/`drawRibbonPx`
逐变量移植过来的，**不是重新设计**，因为这段算法这次对话里被反复调参、
用 Playwright 截图验证过，踩过两个容易再踩一次的坑：

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
`RibbonBuilderTests`）就是复刻这两个坑的回归用例（打勾角点识别、连笔圆滑
不误切、抖动鲁棒性、重复角点不产生 NaN）——修改这段算法后务必跑一遍。

## 性能：WPF 怎么保证不卡

笔迹渲染用 `DrawingVisual`（不是每笔一个 `Path` 元素，也不是
`WriteableBitmap`）：自定义 `FrameworkElement` + `VisualCollection`，每一
笔完成后的笔迹对应一个 `DrawingVisual`，几何体从 `RibbonBuilder` 算出的
多边形构建 `StreamGeometry`。画的时候分两层：笔按着时只在一个"实时预览"
`DrawingVisual` 上画便宜的细线预览；真正的美化分类/角点切分/
Catmull-Rom 拟合只在抬笔那一刻跑一次。所有 `Brush`/`Pen`/`Geometry`
`.Freeze()` 复用。长列表（题目侧栏）必须正确启用虚拟化
（`VirtualizingPanel.IsVirtualizing` + `VirtualizationMode="Recycling"`，
避免外层多包一层 `ScrollViewer`、非虚拟化面板模板、`Auto` 尺寸、整体
`Clear()` 这几个会"悄悄打破虚拟化"的坑）。这部分 UI 还没写，上面是设计
约束，不是已实现的代码。

## 构建现状 / 已知限制

这个项目是在一个 **Linux 容器**里搭的骨架，有两个和 Windows/VS2026 开发
机不一样的地方，交接时需要知道：

- **`ExamReview.App`（WPF）在 Linux 上编不过**，`.csproj` 里加了
  `EnableWindowsTargeting=true` 只是让 `dotnet restore`/IDE 解析不报错，
  真正的编译/运行必须在 Windows + VS2026 上验证。
- **3 个 xUnit 测试项目在这个容器里没法 `dotnet restore`**：容器的出站
  网络只放行了 apt/npm/pip，`api.nuget.org` 被代理拒绝（403）。测试代码
  已经写好（见下），但从未在这台机器上真正跑过——到有正常网络的开发机
  上第一件事应该是 `dotnet test` 确认全绿。

已验证能在 Linux 上干净编译（`dotnet build`，0 警告 0 错误）：
`Lmm.Core`、`Lmm.Format`、`Lmm.Plugins.Abstractions`、
`Lmm.Plugins.English`（目前是空壳）、`Lmm.Import`（空壳）、
`Lmm.Translation`（空壳）。

## 已完成 / 未完成

**已完成：**
- [x] Phase 0：解决方案骨架，10 个项目，依赖关系按架构图接好
- [x] Phase 1（部分）：`Lmm.Core` 领域模型（`Paper/Section/Article/
      Paragraph/Span/StructureRow/Question/Option/BilingualPair/Drill/
      InkStroke/PaperAsset`）
- [x] Phase 1（部分）：`Lmm.Format` 的 `.lmm` ZIP 容器读写
      （`LmmPackageReader`/`LmmPackageWriter`），含版本迁移骨架
      （`IMigrationStep`/`MigrationRunner`，目前无迁移步骤）
- [x] Phase 2a：笔迹美化几何库移植（`Lmm.Core/Ink/`），含 xUnit 测试
      （未在真实环境跑过，见上）

**未完成（按原计划的下一步顺序）：**
- [ ] Phase 1 收尾：内置英语示例卷（把旧版 DEMO 数据迁过来）+ 最简只读
      WPF 壳子，验证"主框架↔格式↔插件"端到端打通
- [ ] `Lmm.Plugins.English`：`ISubjectModule`/`IQuestionTypeHandler` 的
      实际实现（目前只有空的类库项目）
- [ ] Phase 2b：WPF `DrawingVisual` 笔迹画布 + 真机手感测试
- [ ] Phase 3：PDF/OCR/DOCX 导入管线（`Lmm.Import`，选型已定：
      PdfPig + PDFtoImage + Windows.Media.Ocr/Tesseract +
      DocumentFormat.OpenXml，见原计划）
- [ ] Phase 4：翻译（`Lmm.Translation`，`ITranslationProvider` + 默认
      接 MyMemory）
- [ ] Phase 5：保存/导出、试卷库抽屉（文件夹 + 可重建的 SQLite 检索
      索引，索引不是数据源）、设置、快捷键、打印等
- [ ] Phase 6：`ManualModuleCatalog` → `AssemblyScanningModuleCatalog`，
      并实现第二个学科模块验证插件契约

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
- `dotnet test ExamReview.slnx -c Release` —— **17/17 全绿**
  （Lmm.Core.Tests 15、Lmm.Format.Tests 2、Lmm.Plugins.English.Tests 暂无测试）。
- 首次跑测试挂了 1 个：`InkGeometryTests.ResamplePts_ProducesApproximatelyEqualSpacing`
  实测间距 1.17（期望 3.5–4.5）。原因是 `InkGeometry.ResamplePts` 的记账 bug：
  它用"上一个输出点到 `p[i]` 的弦长"来累计已走弧长，而这条弦会跨过已经
  走过的输入段，导致路径长度被重复计数，于是 `step=4` 时出现成串的
  ~0.03–1.2px 近重复点。已改为按输入段（`p[i-1]`→`p[i]`）逐段累计
  `carried` 弧长、到 step 再插值输出；同一用例现在稳定输出 3.997 间距。
- 发布：`dotnet publish src/ExamReview.App/ExamReview.App.csproj -c Release -r win-x64 --self-contained false -o <目录>`，
  产物可直接双击 `ExamReview.App.exe` 运行（当前 UI 还是模板空壳，只有一个 MainWindow）。
  发布一律用**框架依赖**（framework-dependent），不做 self-contained：目标机器需要装
  .NET 10 桌面运行时（Microsoft.WindowsDesktop.App 10.x）。

