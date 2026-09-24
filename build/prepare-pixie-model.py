"""Prepare the pinned, model-bundled PIXIE release assets; never invoked by the runtime.

Run with Python 3.12 and build/pixie-model-requirements.txt installed in an isolated
environment. Downloads only immutable official model URLs, validates SHA-256,
converts to dynamic UINT8 and validates the exact release output hash. --validate
also runs FP32/UINT8 inference and tokenizer fixtures. Source weights stay local.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/rag/Mythosia.AI.Rag.Search.Pixie/models/pixie"
REVISION = "730b515a74727b5f57f031a4c8108743b99e689f"
UPSTREAM = f"https://huggingface.co/telepix/PIXIE-Splade-v1.0/resolve/{REVISION}/"
FILES = {
    "model.onnx": ("onnx/model.onnx", "d887920f8bb98b0367942e169b29e74eb57dd83386ab2ca6479cd85617eee10e"),
    "tokenizer.json": ("tokenizer.json", "36bdc1f1fe0135d10667322a493fd6a32eb93e8f4f68d09004eeb92f39fc8f25"),
    "LICENSE": ("LICENSE", "c700489bf364cc3d1a060b0304469695ccafe181a2f2eb7858689f4ab2388e7d"),
}
DERIVED_HASH = "dcf25f9fa452e61a48f5330a63a2aa68988ddac5bfc574a59c338ef1cff06e42"


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def prepare_assets() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    for name, (source, expected) in FILES.items():
        path = ASSETS / name
        if not path.exists():
            partial = path.with_suffix(path.suffix + ".download")
            print(f"Downloading official pinned asset: {source}", flush=True)
            urllib.request.urlretrieve(UPSTREAM + source + "?download=true", partial)
            if digest(partial) != expected:
                raise RuntimeError(f"Upstream SHA-256 mismatch for {name}; refusing to use download")
            os.replace(partial, path)
        if digest(path) != expected:
            raise RuntimeError(f"SHA-256 mismatch for {path}; refusing to replace an existing local file")


def quantize() -> None:
    from onnxruntime.quantization import quantize_dynamic, QuantType
    for name, version in {"onnx": "1.20.1", "onnxruntime": "1.24.4", "numpy": "2.5.3", "protobuf": "7.36.2"}.items():
        if importlib.metadata.version(name) != version:
            raise RuntimeError(f"Use {name}=={version} to reproduce the pinned derived model")
    target = ASSETS / "model.int8.onnx"
    if not target.exists():
        print("Quantizing MatMul and Gather weights to dynamic per-channel UINT8", flush=True)
        quantize_dynamic(str(ASSETS / "model.onnx"), str(target), per_channel=True,
                         weight_type=QuantType.QUInt8, op_types_to_quantize=["MatMul", "Gather"])
    if digest(target) != DERIVED_HASH:
        raise RuntimeError("Derived model SHA-256 differs from the reviewed release artifact")
    manifest = {
        "model": "telepix/PIXIE-Splade-v1.0", "revision": REVISION,
        "license": "Apache-2.0", "upstreamOnnxSha256": FILES["model.onnx"][1],
        "modelFile": "model.int8.onnx", "modelSha256": DERIVED_HASH,
        "modelBytes": target.stat().st_size, "tokenizerSha256": FILES["tokenizer.json"][1],
        "quantization": {"method": "onnxruntime.quantize_dynamic", "weightType": "QUInt8",
                         "perChannel": True, "operators": ["MatMul", "Gather"]},
        "tools": {name: importlib.metadata.version(name) for name in ["onnx", "onnxruntime", "numpy", "protobuf"]},
        "vocabularySize": 50000, "inputNames": ["input_ids", "attention_mask"],
        "output": "logits [batch, sequence, 50000]",
        "pooling": "max(log(1 + relu(logits)), sequence); exclude special token IDs",
        "tokenizer": "NFC, case-preserving BertPreTokenizer, WordPiece, <s>/</s> boundaries",
        "notice": "Derived quantized weights; original upstream benchmark scores do not measure this artifact.",
    }
    (ASSETS / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Verified bundled model: {target.stat().st_size:,} bytes, SHA-256 {DERIVED_HASH}", flush=True)


def validate(report_path: Path) -> None:
    import numpy as np
    import onnxruntime as ort
    from tokenizers import Tokenizer
    if importlib.metadata.version("tokenizers") != "0.22.2":
        raise RuntimeError("Tokenizer fixtures require tokenizers==0.22.2")
    tok = Tokenizer.from_file(str(ASSETS / "tokenizer.json"))
    edge_texts = [
        "", " ", "hello !", "C# C++ C", "GetCompletionAsync CancellationToken",
        "텔레픽스는 어떤 산업 분야에서 위성 데이터를 활용하나요?", "한국어 문서 검색",
        "한글", "café cafe\u0301", "HELLO hello Hello", "A/B\\C.D:E;F",
        "<s>hello</s>", "<\\s><unk><mask><pad>", "<unused0><unused30>",
        "a\u00a0b\tC\r\nd", "a\u2003b\u2028c", "emoji 😀 👨‍👩‍👧‍👦", "中文漢字。한글",
        "a" * 100, "a" * 101, "e\u0301" * 60, "a\x00b", "zero\u200bwidth",
        "snake_case camelCase API-v2.1", "‘quote’—test…", "\\n &amp; #hashtag @name", "대한민국\n서울",
        "a\u061db", "a\u2e4fb", "a\u2e5db", "a\U00011f43b", "a\U00010eadb",
        "a\u061d b", "a\u001cb", "a\u0085b", "\u2060a\ufeffb",
    ]
    fixtures = [{"text": text, "ids": tok.encode(text).ids} for text in edge_texts]
    fixture_path = ROOT / "tests/Mythosia.AI.Rag.Search.Pixie.Tests/Fixtures/tokenizer-parity.json"
    fixture_path.parent.mkdir(parents=True, exist_ok=True)
    fixture_path.write_text(json.dumps({"revision": REVISION, "tokenizersVersion": "0.22.2", "cases": fixtures},
                                       ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    docs = [
        "C#에서는 CancellationToken을 전달하여 비동기 HTTP 요청을 취소합니다.",
        "C++에서는 std::vector와 스마트 포인터를 사용하여 메모리를 관리합니다.",
        "텔레픽스는 위성 데이터를 분석하여 해양과 농업에 솔루션을 제공합니다.",
        "PostgreSQL 하이브리드 검색은 의미 벡터 검색과 키워드 검색 결과를 통합합니다.",
        "환불은 결제일로부터 7일 이내에 고객센터에 신청합니다.",
        "High-resolution satellite images support defense and reconnaissance.",
        "GetCompletionAsync forwards CancellationToken through the HTTP request.",
        "UpdateDocumentAsync replaces stale chunks when a document is empty.",
    ]
    queries = ["C# 비동기 작업 취소", "C++ 메모리 관리", "농업 분야 위성 데이터", "검색 결과를 섞는 방법",
               "환불 신청 기한", "satellite imagery for defense", "cancel an HTTP request", "빈 문서로 갱신"]
    special = {entry["id"] for entry in json.loads((ASSETS / "tokenizer.json").read_text(encoding="utf-8"))["added_tokens"] if entry["special"]}
    embeddings = []
    for file in ["model.onnx", "model.int8.onnx"]:
        settings = ort.SessionOptions()
        settings.intra_op_num_threads = 4
        session = ort.InferenceSession(str(ASSETS / file), sess_options=settings, providers=["CPUExecutionProvider"])
        vectors = []
        for text in docs + queries:
            ids = tok.encode(text).ids
            logits = session.run(["logits"], {"input_ids": np.array([ids], dtype=np.int64),
                                               "attention_mask": np.ones((1, len(ids)), dtype=np.int64)})[0]
            vec = np.log1p(np.maximum(logits, 0)).max(axis=1)[0]
            vec[list(special)] = 0
            if not np.isfinite(vec).all():
                raise RuntimeError(f"Non-finite output from {file}")
            vectors.append(vec)
        embeddings.append(np.array(vectors))
        del session
    original, derived = embeddings
    cosines = np.sum(original * derived, axis=1) / (np.linalg.norm(original, axis=1) * np.linalg.norm(derived, axis=1))
    ranks = [np.argsort(-(matrix[len(docs):] @ matrix[:len(docs)].T), axis=1) for matrix in embeddings]
    report = {"kind": "Engineering smoke/parity check, not a retrieval-quality benchmark",
              "revision": REVISION, "quantizedSha256": DERIVED_HASH, "texts": len(original),
              "minimumVectorCosine": float(cosines.min()), "meanVectorCosine": float(cosines.mean()),
              "sameTop1Count": int(np.sum(ranks[0][:, 0] == ranks[1][:, 0])), "queries": len(queries),
              "tokenizerParityFixtures": len(fixtures),
              "originalTop1": ranks[0][:, 0].tolist(), "quantizedTop1": ranks[1][:, 0].tolist()}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)
    if float(cosines.min()) < 0.95:
        raise RuntimeError("Quantized vector fidelity failed the engineering smoke threshold (0.95)")
    parity = {"text": queries[0], "tokenIds": tok.encode(queries[0]).ids,
              "indices": np.flatnonzero(derived[len(docs)] > 0).tolist(),
              "values": derived[len(docs)][derived[len(docs)] > 0].tolist()}
    (fixture_path.parent / "inference-parity.json").write_text(json.dumps(parity, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--validate", action="store_true")
    parser.add_argument("--report", type=Path, default=ROOT / ".artifacts/pixie/model-validation.json")
    arguments = parser.parse_args()
    prepare_assets()
    quantize()
    if arguments.validate:
        validate(arguments.report)
