# english.txt.gz

English word forms from [SCOWL](http://wordlist.aspell.net/) 2020.12.07 by Kevin Atkinson
(downloaded from sourceforge.net/projects/wordlist), lists `english`, `american`, `british` of
levels 10 to 80: 281 140 lower-case forms, gzip-compressed. Lines `#level` start the words of that level
(10 is the most common). Proper names and capitalised words are kept lower-cased from level 70 on: they only
protect themselves. Built by `tools/english-words/build.py`.

Used by `FuzzyTerms`: an English word is never replaced by a term ("water" stays "water"), and a garbled
English word becomes the word (levels 10 to 60).

Licence of SCOWL (MIT-like; parts public domain):

    Copyright 2000-2018 by Kevin Atkinson

    Permission to use, copy, modify, distribute and sell these word
    lists, the associated scripts, the output created from the scripts,
    and its documentation for any purpose is hereby granted without fee,
    provided that the above copyright notice appears in all copies and
    that both that copyright notice and this permission notice appear in
    supporting documentation. Kevin Atkinson makes no representations
    about the suitability of this array for any purpose. It is provided
    "as is" without express or implied warranty.

SCOWL is built from other word lists under similar terms (Moby Words II and the UK English
Wordlist with Frequency Classification are public domain; 12Dicts by Alan Beale, ENABLE, and others);
the full notice is the `Copyright` file of the SCOWL distribution.
