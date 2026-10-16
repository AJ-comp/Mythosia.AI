#!/usr/bin/env python3
"""Check curated archive integrity offline; never retrieve keys or call providers."""

import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "archive"
SECTIONS = ("pixie-evaluation", "api-diagnostics", "playground-video")
SECRET = re.compile(
    rb"sk-(?:ant-)?[A-Za-z0-9_-]{24,}|AIza[A-Za-z0-9_-]{30,}|"
    rb"gh[pousr]_[A-Za-z0-9]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY"
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def relative_path(value):
    require(isinstance(value, str) and bool(value), "Empty manifest path")
    path = PurePosixPath(value)
    require(not path.is_absolute() and ".." not in path.parts
            and "\\" not in value and ":" not in value,
            "Manifest paths must be repository-relative POSIX paths")
    return path


def check_manifests():
    count = size = 0
    for section in SECTIONS:
        base = ARCHIVE / section
        manifest = read_json(base / "manifest.json")
        require(manifest["schemaVersion"] == 1, f"Unsupported manifest: {section}")
        seen = set()
        for entry in manifest["files"]:
            relative = relative_path(entry["path"])
            require(str(relative) not in seen, f"Duplicate entry in {section}")
            seen.add(str(relative))
            path = base / relative
            require(path.resolve().is_relative_to(base.resolve()), "Archive path escapes section")
            data = path.read_bytes()
            require(len(data) < 100 * 1024 * 1024, f"Oversized archive file: {section}/{relative}")
            require(hashlib.sha256(data).hexdigest() == entry["sha256"],
                    f"Archive hash mismatch: {section}/{relative}")
            relative_path(entry["source"])
            require(re.fullmatch(r"[a-f0-9]{64}", entry["sourceSha256"]),
                    f"Missing original hash: {section}/{relative}")
            require(isinstance(entry["transformations"], list), "Missing transformation ledger")
            if path.suffix in {".json", ".md", ".csv", ".trx", ".vtt", ".cjs", ".txt", ".ps1"}:
                require(not SECRET.search(data), f"Potential credential in {section}/{relative}")
            if path.suffix == ".json":
                read_json(path)
            if path.suffix == ".trx":
                ET.fromstring(data)
            count += 1
            size += len(data)
    print(f"Archive manifests: {count} preserved files, {size:,} bytes; hashes and formats valid.")


def check_vectors():
    section = ARCHIVE / "pixie-evaluation"
    for run in ("original-openai", "formal-openai"):
        corpus = read_json(section / run / "corpus.json")
        require(len(corpus["records"]) == 364, f"Changed chunk count: {run}")
        rows = corpus["records"]
        if run == "formal-openai":
            require(len(corpus["queryVectors"]) == 32, "Changed archived query vector count")
            rows = rows + corpus["queryVectors"]
        else:
            require(len(corpus["queries"]) == 32, "Changed archived query count")
        for row in rows:
            vector = row["vector"]
            require(len(vector) == 1536 and all(math.isfinite(x) for x in vector)
                    and any(x != 0 for x in vector), f"Invalid archived vector: {run}")
    print("PIXIE corpora: 364 document vectors per run; 32 formal query vectors valid.")


def check_live_outcomes():
    base = ARCHIVE / "api-diagnostics" / "live-openai-claude-2026-10-03"
    expected = {
        "openai/openai-live.trx": (110, 0),
        "openai/openai-common-live.trx": (18, 0),
        "anthropic/anthropic-current.trx": (60, 3),
        "anthropic/anthropic-common.trx": (14, 2),
        "anthropic/anthropic-refusal-retry.trx": (0, 3),
    }
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    for name, (passed, failed) in expected.items():
        tree = ET.parse(base / name)
        results = tree.findall(".//t:UnitTestResult", namespace)
        outcomes = [result.attrib["outcome"] for result in results]
        require(len(outcomes) == passed + failed and outcomes.count("Passed") == passed
                and outcomes.count("Failed") == failed, f"Historical outcomes changed: {name}")
    print("Historical API outcomes preserved: OpenAI 128/128; Claude 74/79; retry 0/3.")


if __name__ == "__main__":
    check_manifests()
    check_vectors()
    check_live_outcomes()
    print("PASS (offline integrity checks only; no new live API validation)")
