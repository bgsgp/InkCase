# `.lmm` 文件格式规范

版本：`formatVersion = "1.0"`（本规范描述的就是这个版本）

参考实现：`src/Lmm.Format/`（`LmmPackageReader`/`LmmPackageWriter`/
`LmmManifest`）+ `src/Lmm.Core/Model/`（领域模型类）。本文档和参考实现如
有出入，以代码为准，并请回头更新本文档。

---

## 1. 这是什么

`.lmm` = 一份试卷（一张 exam paper）。旧版 HTML 应用是"一个 localStorage
大 JSON 塞着所有试卷"；新版是"一个 `.lmm` 文件 = 一份试卷"，试卷库变成
"某个文件夹里的多个 `.lmm` 文件"，更像 Word 文档的使用方式（可复制、发
邮件、放网盘、能被同步盘增量同步）。库抽屉界面对这个文件夹建一个**可
随时重建、非数据源**的 SQLite 检索索引（用于搜索/预览，详见第 6 节），
不属于 `.lmm` 格式本身。

## 2. 容器格式：ZIP

`.lmm` 文件就是一个标准 ZIP 容器（跟 `.docx`/`.pptx` 同一路数），不是
单个大 JSON，也不是 SQLite 数据库。改后缀成 `.zip` 可以直接用任何解压
工具打开查看。

**为什么是 ZIP，不是别的：**

- **不是单文件 JSON**：会把每张扫描图片都 base64（体积涨约 33%），而且
  改一道题就要整份反序列化/重新写盘。
- **不是 SQLite**：SQLite 更适合"检索一个文件夹里有哪些试卷"这种索引
  场景（见第 6 节），不适合"一份试卷"这种用户期望能复制、发邮件、丢
  文件夹里的文档语义，二进制格式也没法 diff/合并。
- **ZIP**：二进制资源存真二进制（不用 base64 税），JSON 部件人可读可
  调试，`System.IO.Compression.ZipArchive` 原生支持，不需要任何第三方
  依赖。

### 2.1 容器内部布局

```
paper.lmm  (ZIP)
├── manifest.json
├── sections/
│   ├── section-0001.json
│   ├── section-0002.json
│   └── ...
├── assets/
│   ├── img-0001.png
│   └── ...
└── ink/
    ├── strokes-0001.json
    ├── strokes-0002.json
    └── ...
```

- `sections/section-NNNN.json`：每个 section 一个文件，文件名与内容
  无语义绑定（真正的顺序由 `manifest.json` 的 `sections[].order` 决
  定），参考实现按写入顺序从 `0001` 开始编号。
- `assets/`：二进制资源原样存储（不编码），文件名 = `assets/{assetId}
  {guessedExtension}`，扩展名由 `mimeType` 猜测（`image/png` → `.png`
  等，未知类型 → `.bin`）。
- `ink/strokes-NNNN.json`：笔迹按**每 500 条一个分块文件**写入（参考
  实现常量 `InkStrokesPerChunk = 500`），避免单个笔迹多的试卷产生一个
  巨大 JSON。读取时枚举所有 `ink/` 前缀的条目并按文件名顺序拼接，调用
  方不需要关心具体分了几块。

### 2.2 写入顺序 / 原子性

`manifest.json` 必须最后写入——写入时，它引用的每一个 section/asset/ink
文件都已经存在于归档里。`LmmPackageWriter.Write(Paper, string path)` 的
文件路径重载会先写到 `{path}.tmp`，成功后再 `File.Move(..., overwrite:
true)` 原子替换目标文件，避免崩溃导致半写坏档看起来像一个"能打开但被
截断"的合法文件。

## 3. `manifest.json`

第一个被读取的文件——必须先读它才能知道这份试卷属于哪个学科模块
（`moduleId`），再决定用哪个插件去解释 sections 里的内容。

```json
{
  "formatVersion": "1.0",
  "id": "paper-0001",
  "moduleId": "cn.examreview.subject.english.v1",
  "title": "2024 杭州一模",
  "subtitle": "",
  "createdAt": "2026-01-01T00:00:00+00:00",
  "modifiedAt": "2026-01-02T00:00:00+00:00",
  "sections": [
    { "id": "sec-0001", "file": "sections/section-0001.json", "order": 0 }
  ],
  "assets": [
    { "id": "img-0001", "file": "assets/img-0001.png", "mimeType": "image/png" }
  ],
  "moduleExtensions": {}
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `formatVersion` | string | 当前恒为 `"1.0"`。读取时若不等于当前版本，走 `Lmm.Format.Migrations.MigrationRunner`；找不到对应迁移步骤会抛 `LmmFormatException`（"这份文件可能比当前程序版本新"）。 |
| `id` | string | 试卷的稳定标识，与磁盘上的文件名/路径无关（重命名文件不影响它）。 |
| `moduleId` | string | 这份试卷属于哪个学科模块，如 `"cn.examreview.subject.english.v1"`。**`Lmm.Format` 只负责把这个字符串读出来，不负责解析成 `ISubjectModule`**——找不到对应已安装模块时该怎么报错，是宿主（`IModuleCatalog`，见 `README.md`"插件契约"一节）的职责，不是这一层的职责。 |
| `title` / `subtitle` | string | 对应旧版 `store.papers[i].name` 等显示用字段。 |
| `createdAt` / `modifiedAt` | ISO 8601 (`DateTimeOffset`) | — |
| `sections[]` | array | 见下。 |
| `assets[]` | array | 见下。 |
| `moduleExtensions` | object | 学科模块私有的扩展字段，宿主/格式层不解释，原样读出写回，供模块自己使用，避免每加一个学科特有字段都要改公共 schema。 |

`sections[]` 每项：`id`（对应 section JSON 里的 `Id`）、`file`（ZIP 内
路径）、`order`（显示顺序，读取时按它排序，不依赖文件名字典序）。

`assets[]` 每项：`id`、`file`（ZIP 内路径）、`mimeType`。

## 4. Section / Question 等 JSON schema

这些字段是**逐一对照旧版 HTML 应用里 `Section` 对象的真实形状**
（`tab/meta/title/gist/article/structure/qs/bi/drill`）翻译过来的，不是
泛化重新设计——下表里"旧字段"一列就是旧版 JS 对象里的原始属性名，方便
照着旧版 localStorage 导出数据写迁移脚本。

### 4.1 `Section`（`sections/section-NNNN.json`，一个文件一个 Section）

| 新字段 | 类型 | 旧字段 | 说明 |
|---|---|---|---|
| `id` | string | — | 新增：稳定标识 |
| `typeId` | string | `tab` | 章节类型，模块自定义词表（如 `"reading-c"`、`"cloze"`、`"七选五"`） |
| `title` | string | `title` | — |
| `meta` | string | `meta` | 简短描述行（字数/难度标签等） |
| `gist` | string | `gist` | 一句话摘要 |
| `article` | Article? | `article` | 旧版是拼好的一段 HTML 字符串，**现在是结构化数据**，见 4.2 |
| `structure` | StructureRow[] | `structure` | 段落-功能对照表 |
| `questions` | Question[] | `qs` | — |
| `bilingual` | BilingualPair[] | `bi` | 中英对照句对 |
| `drills` | Drill[] | `drill` | 课堂小练 |

### 4.2 `Article` / `Paragraph` / `Span`

旧版文章正文里直接嵌了 `<span class="hl" data-q="N">`（证据高亮）和
`<span class="blank" data-q="N">`（完形/七选五挖空）这样的 HTML
字符串。新格式**不存 HTML**，改成结构化的 `Paragraph.Spans`：

```json
{
  "paragraphs": [
    {
      "index": 0,
      "text": "This is the first paragraph of the passage.",
      "spans": [
        { "kind": "evidence", "start": 0, "length": 4, "questionRef": 1 }
      ]
    }
  ]
}
```

- `Span.kind`：`"evidence"`（旧 `hl`，某题答案的证据高亮）或 `"blank"`
  （旧 `blank`，完形/七选五的挖空）。
- `start`/`length`：对 `Paragraph.text` 的字符偏移/长度，不是 HTML
  偏移。
- `questionRef`：这个 span 关联的题号。

这样做是为了不用把"裁剪过的 HTML 解析逻辑"也一起搬过来——WPF 侧渲染
成 `Run`/`Inline` 时自己决定高亮/挖空的样式。

### 4.3 `StructureRow`（旧 `structure`: `[{p,f,c,q}]`）

| 新字段 | 旧字段 | 说明 |
|---|---|---|
| `paraLabel` | `p` | 段落标签，如 "Para 1" |
| `function` | `f` | 该段的修辞功能，如"点题"/"举例论证" |
| `contentNote` | `c` | 内容简注 |
| `questionRefs` | `q` | 关联的题号列表 |

### 4.4 `Question` / `Option`（旧 `qs`: 题目数组）

| 新字段 | 旧字段 | 说明 |
|---|---|---|
| `number` | `n` | 章节内显示题号 |
| `id` | — | 新增：稳定标识，独立于可能被重新编号的 `number` |
| `skillTag` | `key` | 技能/知识点标签，模块自定义词表 |
| `stem` | `stem` | 题干 |
| `options` | — | `Option[]`，每项 `letter`（旧 `l`）+ `text`（旧 `t`） |
| `correctAnswer` | `ans` | 正确选项字母（非选择题类型可以是自由文本答案） |
| `explanation` | `body` | 讲评时展示的解析 |
| `distractorAnalysis` | `wrong` | 各干扰项错误原因分析 |
| `assetRefs` | — | 引用 `manifest.assets[].id`，例如这道题依赖的配图 |

### 4.5 `BilingualPair`（旧 `bi`: `[{en,zh}]`）

`en` / `zh`，中英对照面板展示的句对。

### 4.6 `Drill`（旧 `drill`: `[{h,q,a}]`）

`heading`（旧 `h`）/ `question`（旧 `q`）/ `answer`（旧 `a`），课堂小练
一条记录。

## 5. 笔迹（ink）schema

`ink/strokes-NNNN.json` 每个文件是一个 `InkStroke` 的 JSON 数组：

```json
[
  {
    "id": "stroke-0042",
    "anchor": { "sectionId": "sec-0001" },
    "color": "#D33A2C",
    "baseWidth": 3.0,
    "points": [{ "x": 0.1234, "y": 0.5678 }],
    "widthFactors": [0.9, 1.0, 1.1]
  }
]
```

| 字段 | 说明 |
|---|---|
| `id` | 笔迹稳定标识 |
| `anchor.sectionId` | 这笔画锚定在哪个 section 的讲评 UI 上 |
| `color` | 笔色，如 `"#D33A2C"` |
| `tool` | 笔种：`"pen"`（缺省）/`"dashed"`/`"calligraphy"`/`"speed"`/`"taper"`/`"highlighter"`，决定 alpha、虚线、以及宽度来自压感/笔尖/速度 |
| `baseWidth` | 基础笔宽（像素，相对于采集时的画布尺寸） |
| `points[]` | **原始采集点**，分数坐标（0..1，相对画布宽高），与旧版一致，不受画布缩放/DPI 影响 |
| `widthFactors[]` | 与 `points` 等长的逐点**半径乘子**（该点半径 = `baseWidth/2 * widthFactors[i]`），由 `Lmm.Core.Ink.InkEngine` 按笔种算出；缺失或长度不足按 1.0 兜底 |

**关键决定：只存 `points` + `widthFactors`（美化后、重采样后的点列），
不存最终渲染用的缎带多边形。** 多边形是派生数据，每次打开文件时由
`Lmm.Core.Ink.InkRibbon` 用当前算法重新算一遍。好处：以后再调美化
算法（笔宽曲线、笔尖/收锋参数等），旧文件自动享受到改进效果，文件格式也不
会因为"渲染器变了"而被迫升版本。

> 这是实现时的默认选择，不是旧版本来就有的行为。如果需要"以前存的笔迹
> 必须像素级原样保留、不允许因为算法升级而变形"，那就需要把多边形也
> 缓存进去（比如加一个 `ink/polygons-NNNN.json`），这是一个需要重新
> 决定的产品取舍，不是纯技术细节。

笔迹的原始捕获点列在进 `InkBuilder.Build` 之前是"美化前"的 `RawStroke`
（含压感与时间戳）；写入 `.lmm` 的是 `InkEngine` 处理之后的中心线 +
逐点半径乘子（经过按弧长的 EMA 平滑与抽稀，不是笔尖每一次 `pointermove`
的原始采样）。原始压力/时间戳不入库——它们只用于算出 `widthFactors`。

## 6. 试卷库索引（不属于 `.lmm` 格式本身）

试卷库 = 某个文件夹里的多个 `.lmm` 文件。库抽屉界面需要搜索/预览，这
部分建一个 **SQLite 索引**，但这个索引：

- **可随时整个重建**（扫描文件夹、对每个 `.lmm` 读 `manifest.json` 抽取
  标题/模块/时间戳等摘要字段），删掉重建不会丢任何数据；
- **不是数据源**，真正的数据永远以文件夹里的 `.lmm` 文件为准；
- 因此不需要跟着 `.lmm` 格式版本走自己的迁移流程——改了索引 schema
  直接删了重建就行。

参考实现里还没有这部分代码（属于原计划 Phase 5"收尾"）。

## 7. 版本与迁移

- `manifest.json.formatVersion` 标识格式版本，当前只有 `"1.0"`。
- `Lmm.Format.Migrations.IMigrationStep`：一个步骤知道如何把 manifest
  从恰好 `FromVersion` 升到恰好 `ToVersion`；`MigrationRunner` 负责顺序
  串联、读取时自动跑到 `LmmFormatVersions.Current`。
- 目前没有注册任何迁移步骤（因为只有一个版本）。新增 `"1.1"`/`"2.0"`
  时：
  1. 实现一个新的 `IMigrationStep`（`FromVersion="1.0"`,
     `ToVersion="1.1"`），把它注册进 `LmmPackageReader` 构造时传入的
     `MigrationRunner`；
  2. **在第一次迁移写回之前，原地备份一份 `.bak`**（这一步在参考实现
     里还没写，属于 `LmmPackageReader` 该做的事，因为只有它知道原始
     文件路径）；
  3. 更新本文档对应版本的 schema 说明。

## 8. 与旧版的结构性差异（需要知道，不是纯实现细节）

旧版："一个 localStorage 大 JSON 塞着所有试卷"。
新版："一个 `.lmm` = 一份试卷"，库变成文件夹 + 可重建索引。

这更像 Word 文档的使用方式（可复制、发邮件、放网盘、能被同步盘增量同
步），更适合桌面软件，但如果后续开发方（dsh）认为产品形态应该保留"单
文件囊括所有试卷"的旧习惯，这是需要重新确认的产品决定，而不是只改
`Lmm.Format` 内部实现就能撤销的事。