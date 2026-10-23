"""Метка для синтезированной фразы в стиле вывода исходной GigaAM v3 (docs/plan-gigaam-finetune.md).

v3 распознаёт синтезированный клип; слова вне терминов берутся из её вывода (регистр, пунктуация, числа
как пишет сама модель), участки терминов заменяются целевым написанием из text. Если слова вне терминов
сильно расходятся с text (плохой синтез), клип отбрасывается.
"""
import difflib, re

TOKEN = re.compile(r"\S+")


def norm(word):
    return re.sub(r"[^\w+#]", "", word.lower().replace("ё", "е"))


def merge(hyp, text, terms, max_mismatch=0.2):
    """Метка или None. hyp — вывод v3 по клипу, text — фраза с терминами латиницей, terms — термины фразы."""
    target = TOKEN.findall(text)
    term_word = [False] * len(target)
    for term in terms:
        parts = term.split()
        for i in range(len(target) - len(parts) + 1):
            if all(norm(target[i + k]) == norm(parts[k]) for k in range(len(parts))):
                for k in range(len(parts)):
                    term_word[i + k] = True
    hyp_words = TOKEN.findall(hyp)
    a, b = [norm(w) for w in hyp_words], [norm(w) for w in target]
    out, mismatched, plain = [], 0, sum(1 for t in term_word if not t)
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes():
        if op == "equal":
            out.extend(hyp_words[i1:i2])
            continue
        if any(term_word[j1:j2]):
            # участок с термином: целевое написание, хвостовая пунктуация v3 сохраняется
            words = list(target[j1:j2])
            tail = re.search(r"[.,!?;:…]+$", hyp_words[i2 - 1]) if i2 > i1 else None
            words[-1] = re.sub(r"[.,!?;:…]+$", "", words[-1]) + (tail.group(0) if tail else re.search(r"[.,!?;:…]*$", target[j2 - 1]).group(0))
            out.extend(words)
            mismatched += sum(1 for t in term_word[j1:j2] if not t)
        else:
            out.extend(hyp_words[i1:i2])
            mismatched += max(i2 - i1, j2 - j1)
    if plain and mismatched / plain > max_mismatch:
        return None
    label = " ".join(out)
    return label if all(t in label for t in terms) else None


if __name__ == "__main__":
    cases = [
        ("Залей этот проект на Гитхаб.", "Залей этот проект на GitHub.", ["GitHub"]),
        ("Открой фигму и скинь макет, пожалуйста.", "Открой Figma и скинь макет, пожалуйста.", ["Figma"]),
        ("Разверни на кубер нетис сервис", "Разверни на Kubernetes сервис.", ["Kubernetes"]),
        ("Совсем другой текст про погоду.", "Залей этот проект на GitHub.", ["GitHub"]),
        ("Поставь Python 3.12 и пип.", "Поставь Python 3.12 и pip.", ["Python", "pip"]),
    ]
    for hyp, text, terms in cases:
        print(f"{hyp!r} -> {merge(hyp, text, terms)!r}")
