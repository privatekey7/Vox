# Fonts

Owner's choice (22.10.2026): headings — **Unbounded**, all other text — **Onest**. Both SIL OFL 1.1
(licences next to the fonts), from [google/fonts](https://github.com/google/fonts) `ofl/unbounded`,
`ofl/onest`.

WPF renders only the default instance of a variable font, so the static weights Vox uses were cut
from the variable `[wght]` files with fontTools:

```python
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
instantiateVariableFont(TTFont("Onest[wght].ttf"), {"wght": 500}, updateFontNames=True).save("Onest-Medium.ttf")
```

Onest: Regular 400, Medium 500, SemiBold 600. Unbounded: Medium 500.
