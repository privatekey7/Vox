# Fonts

Owner's choice (22.10.2026, replaces Unbounded + Onest): **Golos Text** (Paratype) everywhere — text
Regular 400 / Medium 500, headings SemiBold 600, the wizard greeting Bold 700. SIL OFL 1.1
(`OFL-GolosText.txt`), from [google/fonts](https://github.com/google/fonts) `ofl/golostext`.

WPF renders only the default instance of a variable font, so the static weights were cut from
`GolosText[wght].ttf` with fontTools:

```python
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
instantiateVariableFont(TTFont("GolosText[wght].ttf"), {"wght": 500}, updateFontNames=True).save("GolosText-Medium.ttf")
```

Checked: Cyrillic and «ё»/«Ё» are present in every weight.
