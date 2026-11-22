# 怎么自己写一份 `.lmm`（发给同事看这个）

`.lmm` 就是**一个 ZIP 文件改了后缀**，里面是 JSON + 图片。所以有两种写法：

- **路线 A（推荐、零门槛）**：把试卷写成纯文本 → 在墨案里「导入…」→「另存为」得到 `.lmm`。全程不用碰 JSON。
- **路线 B（要精细控制）**：自己写 JSON、打包成 ZIP。适合要挖空、证据高亮、中英对照、课堂小练、多章节的卷子。

---

## 路线 A：纯文本导入

### 文本怎么写

整段的规则只有四条：

1. **文章在前，题目在后。** 第一个「编号行」（`1. ` / `2、` / `3）` 这种）出现之前的全部内容算文章。
2. **文章段落之间空一行。** 一个空行 = 一个段落；段落内部的换行会被拼成一行。
3. **题目以编号行开头**，编号 1–2 位数字，后面跟 `.`、`、`、`．` 或 `)`。
4. **选项写在题干后面同一块里**，形如 `A. xxx B. xxx`；答案写 `答案：B`，解析写 `解析：……`。

一个能直接用的例子（存成 UTF-8 的 .txt；仓库里也有一份 `docs/sample-import.txt`，桌面版是 `示例\03-纯文本模板.txt`）：

```text
The ocean has quietly absorbed close to a third of the carbon dioxide that humans release.
The service is invisible, but the machinery behind it is not.

Scientists call this slow cascade the biological carbon pump. It begins when phytoplankton
use sunlight to turn carbon dioxide into living tissue.

1. What does the ocean absorb?
A. Oxygen B. Carbon dioxide C. Sand D. Salt
答案：B
解析：第一段第一句直接给出 "absorbed close to a third of the carbon dioxide"。

2. Why does the author mention phytoplankton?
A. To explain how the pump starts
B. To criticise their role
C. To compare them with humans
D. To predict their extinction
答案：A
解析：第二段说明光合作用把二氧化碳变成生物组织，是碳泵的起点。
```

> 注意：文件里**不要写注释行**（`#` 之类也不行）。第一个编号行之前的所有文字都会被当成文章正文。

### 导入后是什么样

- 导入只会生成**一个章节**（一节 = 一篇文章 + 一组题），**也不会生成证据高亮/挖空**——要高亮得走路线 B 手写 span。有文章时类型是 `reading-c`，只有题目时是 `grammar-fill`。
- 标题取文件名；导入结果会在状态栏列出提示（比如「第 3 题没解析到答案，暂以 A 占位，请核对。」）。
- **选择题必须有答案**，否则保存时会被校验拦住。导入时缺答案会先用 A 占位并明确告诉你，请自己点开核对改成正确答案。
- 想要多章节、挖空、高亮、中英对照：导入得到第一版后打开它再手改不方便，直接走路线 B，或者在导入的多节卷里逐节编辑。

### 交付

「另存为…」保存出一个 `.lmm`。**发这一个文件给对方就行**（可以发微信/邮件/网盘），对方装上墨案双击就能开。图片、笔迹、答案都在里面。

---

## 路线 B：手写 JSON 再打包

### 目录结构

```
我的试卷.lmm            ← 其实是个 zip
├── manifest.json       ← 必须有，先读它
└── sections/
    └── section-0001.json
```

图片放 `assets/`（可选），笔迹放 `ink/`（可选，一般不用手写）。

### manifest.json（最小可用）

```json
{
  "formatVersion": "1.0",
  "id": "paper-demo-0001",
  "moduleId": "cn.examreview.subject.english.v1",
  "title": "示例：海洋的碳泵",
  "subtitle": "",
  "createdAt": "2026-01-01T00:00:00+08:00",
  "modifiedAt": "2026-01-01T00:00:00+08:00",
  "sections": [
    { "id": "sec-0001", "file": "sections/section-0001.json", "order": 0 }
  ],
  "assets": [],
  "moduleExtensions": {}
}
```

- `moduleId` 对英语卷**固定**是 `cn.examreview.subject.english.v1`，写错会提示「这个文件需要模块 …，但本机没有安装该模块」。
- `sections[].order` 决定章节顺序，文件名本身没有语义；加一节就是再加一个文件 + 在这里加一项（`order` 从 0 递增）。
- `formatVersion` 必须是 `"1.0"`。

### section-0001.json（最小可用，一篇文章 + 一道题）

```json
{
  "id": "sec-0001",
  "typeId": "reading-c",
  "title": "阅读理解 C",
  "meta": "说明文 · 约 60 词 · 难度中等",
  "gist": "海洋如何把碳带进深海。",
  "article": {
    "paragraphs": [
      {
        "index": 0,
        "text": "For decades the ocean has quietly absorbed close to a third of the carbon dioxide that humans release.",
        "spans": [
          { "kind": "evidence", "start": 67, "length": 35, "questionRef": 1 }
        ]
      },
      {
        "index": 1,
        "text": "Scientists call this slow cascade the biological carbon pump.",
        "spans": []
      }
    ]
  },
  "structure": [],
  "questions": [
    {
      "number": 1,
      "id": "q-001",
      "skillTag": "细节定位",
      "stem": "What does the ocean absorb?",
      "options": [
        { "letter": "A", "text": "Oxygen" },
        { "letter": "B", "text": "Carbon dioxide" },
        { "letter": "C", "text": "Sand" },
        { "letter": "D", "text": "Salt" }
      ],
      "correctAnswer": "B",
      "explanation": "第一段直接说 absorbed close to a third of the carbon dioxide。",
      "distractorAnalysis": "",
      "assetRefs": []
    }
  ],
  "bilingual": [],
  "drills": []
}
```

### 字段速查

| 字段 | 说明 |
|---|---|
| `typeId` | 可用值：`reading-c`（阅读理解）、`cloze`（完形）、`seven-of-five`（七选五）、`grammar-fill`（语法填空）、`writing`（写作）、`listening`（听力）。前三种**必须有 article**，否则保存被拦。 |
| `article.paragraphs[].text` | 纯文本，不要放 HTML 标签。 |
| `spans[]` | `kind`：`evidence`（证据高亮）或 `blank`（挖空）；`start`/`length` 是**在这段 text 里的字符下标**（从 0 开始，空格也算一个字符）；`questionRef` 指向题号，点题会跳过去。越界会报 Error。 |
| `questions[].number` | 不能重复；填空题答案可以写词语（`correctAnswer: "carbon"`），选择题必须写选项字母。 |
| `questions[].options[]` | 有选项就算选择题；选项字母不能重复，答案必须在字母里。 |
| `structure[]` | 段落结构表：`paraLabel` / `function` / `contentNote` / `questionRefs[]`，可不写。 |
| `bilingual[]` | 中英对照：`en` / `zh`，两边都不能空。 |
| `drills[]` | 课堂小练：`heading` / `question` / `answer`。 |

### 打包

PowerShell（目录里放好 manifest.json 和 sections/）：

```powershell
Compress-Archive -Path manifest.json, sections -DestinationPath 我的试卷.zip -Force
Rename-Item 我的试卷.zip 我的试卷.lmm
```

Python 也一样：

```python
import zipfile
with zipfile.ZipFile("我的试卷.lmm", "w", zipfile.ZIP_DEFLATED) as z:
    z.write("sections/section-0001.json")
    z.write("manifest.json")   # 最后写
```

（顺序不影响读取，但生成器惯例是 manifest 最后写。）

### 交出去之前自检

用墨案打开它：如果保存时弹校验，说明有不合法的地方；**分享前先打开确认没有 Error**。常见错误：

- 选择题没写 `correctAnswer`，或答案字母不在选项里；
- `spans` 的下标算错（尤其按 HTML 源算、或漏算空格）；
- 题号/题目 id 重复；
- `reading-c`/`cloze`/`seven-of-five` 没写 article；
- `moduleId` 拼错。

---

## 给同事发什么

1. `墨案` 文件夹（或 `.lmm` 文件 + 程序）；
2. 对方机器要装 **.NET 10 桌面运行时**（框架依赖发布，只有几百 KB）；
3. 双击 `.lmm` 就能开，笔迹和答案都随文件走，不需要联网。
