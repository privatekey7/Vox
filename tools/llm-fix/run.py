"""Experiment: a small local LLM fixes only the contested words of a dictation (not in the app).

Input: JSON lines from `Vox.Replay --contested` (Final, contested spans, closest terms by sound).
The LLM gets the phrase, the contested words and their candidates, and returns the phrase. The answer
is kept only if every other word is unchanged (the spans may change); otherwise the phrase stays.

    python3 run.py --model model.gguf --terms anglicisms.jsonl --plain corpusB.jsonl [--out results.tsv]

Prints: terms on anglicisms-30 (score-terms.py) before/after, phrases of plain Russian changed,
share of phrases that call the LLM, mean milliseconds per call, model size.
"""
import argparse
import json
import os
import re
import time

from llama_cpp import Llama

HERE = os.path.dirname(os.path.abspath(__file__))

SYSTEM = (
    "Ты исправляешь ошибки распознавания речи в русской диктовке программиста. "
    "Меняй только слова из списка «спорные»: замени спорное слово на правильное написание из кандидатов "
    "или оставь его как есть, если ни один кандидат не подходит по смыслу. Остальные слова, знаки и порядок "
    "не меняй. Ответь только исправленной фразой целиком, без пояснений."
)


def prompt(item):
    lines = [f"Фраза: {item['final'].strip()}", "Спорные:"]
    for span in item["spans"]:
        lines.append(f"- «{span['text']}»: кандидаты {', '.join(span['candidates'])}")
    return "\n".join(lines)


def accepted(item, answer):
    """The answer with every non-contested piece of the phrase unchanged, or None."""
    final = item["final"].strip()
    pattern, pos = "", 0
    for span in sorted(item["spans"], key=lambda s: s["start"]):
        start = span["start"]  # Vox.Replay trims the Final
        pattern += re.escape(final[pos:start]) + r"(.+?)"
        pos = start + len(span["text"])
    pattern += re.escape(final[pos:])
    answer = answer.strip().strip("«»\"").strip()
    return answer if re.fullmatch(pattern, answer, re.DOTALL) else None


def terms_score(finals):
    """score-terms.py: terms of each anglicisms-30 phrase found in its Final."""
    src = open(os.path.join(HERE, "..", "eval", "score-terms.py"), encoding="utf-8").read()
    K = eval(re.search(r"^K=(\[.*\])$", src, re.M).group(1))  # in the order of anglicisms-30.tsv rows
    ok = 0
    for k, v in zip(K, finals):
        ok += sum(1 for t in k if t in v or (t[0].islower() and t.lower() in v.lower()))
    return ok, sum(len(k) for k in K)


def run(llm, items, thinking_off):
    results, times = [], []
    for item in items:
        if not item["spans"]:
            results.append((item, item["final"].strip(), "no call"))
            continue
        user = prompt(item) + ("\n/no_think" if thinking_off else "")
        t = time.perf_counter()
        out = llm.create_chat_completion(
            messages=[{"role": "system", "content": SYSTEM}, {"role": "user", "content": user}],
            temperature=0, max_tokens=len(item["final"]) * 2 + 32)
        times.append(time.perf_counter() - t)
        answer = re.sub(r"<think>.*?</think>", "", out["choices"][0]["message"]["content"], flags=re.DOTALL)
        fixed = accepted(item, answer)
        results.append((item, fixed or item["final"].strip(), "kept" if fixed else "rolled back: " + answer.strip()))
    return results, times


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--terms", required=True, help="anglicisms-30 gigaam_raw through Vox.Replay --contested")
    ap.add_argument("--plain", required=True, help="plain Russian through Vox.Replay --contested")
    ap.add_argument("--threads", type=int, default=os.cpu_count() // 2)
    ap.add_argument("--out")
    a = ap.parse_args()

    llm = Llama(model_path=a.model, n_ctx=2048, n_threads=a.threads, verbose=False)
    thinking_off = "qwen3" in os.path.basename(a.model).lower()
    terms = [json.loads(l) for l in open(a.terms, encoding="utf-8")]
    plain = [json.loads(l) for l in open(a.plain, encoding="utf-8")]

    t_res, t_times = run(llm, terms, thinking_off)
    p_res, p_times = run(llm, plain, thinking_off)
    before, total = terms_score([i["final"].strip() for i in terms])
    after, _ = terms_score([r[1] for r in t_res])
    false_changes = sum(1 for item, fixed, _ in p_res if fixed != item["final"].strip())
    times = t_times + p_times
    calls = len(times)
    print(f"{os.path.basename(a.model)}: terms {before}/{total} → {after}/{total}; "
          f"plain changed {false_changes}/{len(plain)}; "
          f"calls {calls}/{len(terms) + len(plain)} ({100 * calls / (len(terms) + len(plain)):.0f}%), "
          f"{1000 * sum(times) / max(calls, 1):.0f} ms per call; "
          f"rolled back {sum(1 for r in t_res + p_res if r[2].startswith('rolled'))}; "
          f"{os.path.getsize(a.model) / 2**20:.0f} MB")
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            for set_name, res in (("terms", t_res), ("plain", p_res)):
                for item, fixed, note in res:
                    if note != "no call":
                        f.write(f"{set_name}\t{item['final'].strip()}\t{fixed}\t{note}\n")


if __name__ == "__main__":
    main()
