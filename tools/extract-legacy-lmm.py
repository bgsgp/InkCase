#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract papers from the legacy single-file courseware HTML into .lmm files.

The old app kept every paper in one localStorage blob.  A saved courseware page
therefore contains a single line like::

    PRELOAD = {"v":2,"papers":[{"id":...,"name":...,"data":[<section>...]}]}

This script walks that blob and writes each selected paper as a new-format
.lmm ZIP container (manifest.json + sections/section-NNNN.json), converting the
legacy section shape (tab/article HTML/qs/bi/drill) into the structured model
described in docs/lmm-format-spec.md.

Usage::

    python tools/extract-legacy-lmm.py 课件.html --out ./示例 --papers demo,pmwke0e8y2yi
    python tools/extract-legacy-lmm.py 课件.html --out ./示例 --all
"""
import argparse
import datetime
import html
import io
import json
import os
import re
import zipfile

MODULE = "cn.examreview.subject.english.v1"

# legacy Chinese tab label -> Lmm.Plugins.English SectionTypes id
TYPE_MAP = {
    "阅读 A": "reading-c", "阅读 B": "reading-c", "阅读 C": "reading-c", "阅读 D": "reading-c",
    "七选五": "seven-of-five",
    "完形填空": "cloze",
    "语法填空": "grammar-fill",
    "写作": "writing",
}

TAG = re.compile(r"<[^>]+>|[^<]+")
SPAN_OPEN = re.compile(r'<span[^>]*class="(hl|blank)"[^>]*data-q="(\\d+)"[^>]*>', re.I)
BLANK_RUN = re.compile(r"_{2,}\\s*(\\d+)\\s*_{2,}")


def strip_tags(s):
    s = re.sub(r"<br\\s*/?>", "\\n", s or "", flags=re.I)
    s = re.sub(r"</p>", "\\n", s, flags=re.I)
    return html.unescape(re.sub(r"<[^>]+>", "", s)).strip()


def parse_paragraph(fragment):
    """Return (text, spans) for the inner HTML of one <p>."""
    text, spans, stack = [], [], []
    for tok in TAG.finditer(fragment):
        s = tok.group(0)
        if s.startswith("<"):
            m = SPAN_OPEN.match(s)
            if m:
                stack.append((m.group(1), int(m.group(2)), sum(len(t) for t in text)))
            elif re.match(r"</span", s, re.I) and stack:
                kind, q, start = stack.pop()
                length = sum(len(t) for t in text) - start
                if length > 0:
                    spans.append({"kind": "evidence" if kind == "hl" else "blank",
                                  "start": start, "length": length, "questionRef": q})
            continue
        text.append(html.unescape(s))
    return "".join(text), spans


def parse_article(raw):
    paragraphs = []
    # <p class="byline"> only repeats section.meta, so it is dropped.
    body = [(attrs, inner) for attrs, inner in
            re.findall(r"<p([^>]*)>(.*?)</p>", raw or "", flags=re.I | re.S)
            if "byline" not in attrs.lower()]
    if not body:
        body = [("", chunk) for chunk in (raw or "").split("\\n") if chunk.strip()]
    for _attrs, chunk in body:
        text, spans = parse_paragraph(chunk)
        text = re.sub(r"[ \\t]+", " ", text).strip()
        if text:
            paragraphs.append({"index": len(paragraphs), "text": text, "spans": spans})
    return {"paragraphs": paragraphs}


def question_refs(value):
    if isinstance(value, list):
        return [int(v) for v in value if str(v).strip().isdigit()]
    return [int(v) for v in re.findall(r"\\d+", str(value or ""))]


def convert_section(section):
    converted = {
        "id": section.get("id") or "",
        "typeId": TYPE_MAP.get((section.get("tab") or "").strip(), "reading-c"),
        "title": strip_tags(section.get("title")),
        "meta": strip_tags(section.get("meta")),
        "gist": strip_tags(section.get("gist")),
        "article": parse_article(section.get("article") or ""),
        "structure": [{"paraLabel": strip_tags(row.get("p")),
                       "function": strip_tags(row.get("f")),
                       "contentNote": strip_tags(row.get("c")),
                       "questionRefs": question_refs(row.get("q"))}
                      for row in (section.get("structure") or [])],
        "questions": [{"number": int(q.get("n") or 0),
                       "id": "q" + str(q.get("n") or 0),
                       "skillTag": strip_tags(q.get("key")),
                       "stem": strip_tags(q.get("stem")),
                       "options": [{"letter": o.get("l", ""), "text": strip_tags(o.get("t"))}
                                   for o in (q.get("options") or [])],
                       "correctAnswer": (q.get("ans") or "").strip(),
                       "explanation": strip_tags(q.get("body")),
                       "distractorAnalysis": strip_tags(q.get("wrong")),
                       "assetRefs": []}
                      for q in (section.get("qs") or [])],
        "bilingual": [{"en": strip_tags(b.get("en")), "zh": strip_tags(b.get("zh"))}
                      for b in (section.get("bi") or [])],
        "drills": [{"heading": strip_tags(d.get("h")),
                    "question": strip_tags(d.get("q")),
                    "answer": strip_tags(d.get("a"))}
                   for d in (section.get("drill") or [])],
    }

    # Cloze passages in the legacy files mark blanks as literal ___41___ text
    # instead of <span class="blank">; promote those markers to real spans so the
    # reader renders them as blanks and can jump from a question to its slot.
    if converted["typeId"] == "cloze":
        for para in converted["article"]["paragraphs"]:
            if para["spans"]:
                continue
            for m in BLANK_RUN.finditer(para["text"]):
                para["spans"].append({"kind": "blank", "start": m.start(),
                                      "length": m.end() - m.start(),
                                      "questionRef": int(m.group(1))})
    return converted


def iso(timestamp_ms):
    moment = datetime.datetime.fromtimestamp((timestamp_ms or 0) / 1000, datetime.timezone.utc)
    return moment.strftime("%Y-%m-%dT%H:%M:%S+00:00")


def safe_name(text):
    return re.sub(r'[\\\\/:*?"<>|]+', "-", text).strip() or "paper"


def write_lmm(paper, path):
    sections = [convert_section(s) for s in paper.get("data") or []]
    manifest = {
        "formatVersion": "1.0",
        "id": paper.get("id") or "",
        "moduleId": MODULE,
        "title": paper.get("name") or "",
        "subtitle": paper.get("sub") or "",
        "createdAt": iso(paper.get("ts")),
        "modifiedAt": iso(paper.get("ts")),
        "sections": [],
        "assets": [],
        "moduleExtensions": {},
    }
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        for order, section in enumerate(sections, 1):
            entry = f"sections/section-{order:04d}.json"
            archive.writestr(entry, json.dumps(section, ensure_ascii=False, indent=2))
            manifest["sections"].append({"id": section["id"], "file": entry, "order": order - 1})
        archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    return sections


def load_preload(html_path):
    with io.open(html_path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if "PRELOAD =" in line and '"papers"' in line:
                return json.loads(line[line.find("{"):line.rfind("}") + 1])
    raise SystemExit("no PRELOAD blob found in this HTML file")


def main():
    parser = argparse.ArgumentParser(description="legacy courseware HTML -> .lmm")
    parser.add_argument("html", help="saved courseware .html file")
    parser.add_argument("--out", default=".", help="output directory for .lmm files")
    parser.add_argument("--papers", default="", help="comma separated legacy paper ids")
    parser.add_argument("--all", action="store_true", help="convert every paper in the file")
    args = parser.parse_args()

    papers = load_preload(args.html)["papers"]
    wanted = None if args.all or not args.papers else {p.strip() for p in args.papers.split(",") if p.strip()}

    for paper in papers:
        if wanted is not None and paper.get("id") not in wanted:
            continue
        target = os.path.join(args.out, safe_name(paper.get("name") or paper.get("id") or "paper") + ".lmm")
        sections = write_lmm(paper, target)
        flagged = sum(1 for s in sections for q in s["questions"] if not q["correctAnswer"])
        print(f"{target}  <- {paper.get('name')}  sections={len(sections)}  unanswered={flagged}")


if __name__ == "__main__":
    main()