#!/usr/bin/env python3
"""Check the app's English UI text against main ASD-STE100 writing rules.

Reads the English keys of lang/ru.tsv (every string the app shows).
Rules checked (a practical subset of ASD-STE100, not the full dictionary):
  LEN   procedural sentence (starts with a verb) > 20 words, other sentence > 25 words
  PASS  passive voice ("is set", "are removed")
  WORD  common unapproved word, with the approved word to use
  SEMI  semicolon joins two sentences
  ABBR  "e.g.", "i.e.", "etc."
  PARA  paragraph with more than 6 sentences

Usage: python3 tools/ste_check.py [--list] [--max N]
Exit code 1 when the count of findings is more than --max (default: no limit).
"""
import re, sys

UNAPPROVED = {
    "utilize": "use", "utilise": "use", "ensure": "make sure", "via": "through",
    "prior to": "before", "approximately": "about", "obtain": "get",
    "require": "need", "requires": "needs", "required": "necessary",
    "commence": "start", "terminate": "stop", "sufficient": "enough",
    "additional": "more", "numerous": "many", "attempt": "try",
    "attempts": "tries", "perform": "do", "performs": "does",
    "currently": "now", "in order to": "to", "subsequently": "then",
    "in addition": "also", "whilst": "while", "regarding": "about",
}

PASSIVE = re.compile(
    r"\b(is|are|was|were|be|been|being|gets|get)\s+(?:\w+ly\s+|not\s+)?"
    r"(\w+ed|set|put|shown|known|done|made|given|taken|kept|left|lost|built|run|"
    r"read|written|broken|hidden|cut|sent|found|held|seen|turned)\b",
    re.I)
# Words that look like participles but act as adjectives (state, not action).
NOT_PASSIVE = {"enabled", "disabled", "allowed", "needed", "used", "signed",
               "connected", "installed", "supported", "limited", "advanced",
               "detailed", "experienced", "interested", "required", "selected",
               "applied", "ticked", "locked", "paused", "covered", "excluded",
               "undocumented", "configured", "preferred", "blocked", "compressed",
               "set", "left", "done", "known", "shown", "turned", "loaded",
               "logged", "unchanged", "changed", "reverted", "fixed", "hidden",
               "missing", "broken", "running", "armed", "ready",
               "unsigned", "unknown", "signalled"}
VERB_START = re.compile(r"^\W*(Click|Pick|Select|Open|Run|Restart|Turn|Set|Use|Make|Close|Press|"
                        r"Check|Tick|Untick|Choose|Type|Enter|Save|Load|Copy|Add|Remove|Delete|Install|"
                        r"Update|Wait|Try|Do|Keep|Leave|Apply|Revert|Undo|Find|Go|Start|Stop|Plug|Unplug|"
                        r"Disable|Enable|Read|Write|Put|Move|Reboot|Sign|Log)\b")


def unescape(s):
    return s.replace("\\n", "\n").replace("\\r", "").replace("\\t", "\t").replace("\\\\", "\\")


def sentences(par):
    par = re.sub(r"\b(e\.g|i\.e|etc|vs|approx)\.", lambda m: m.group(0).replace(".", "․"), par)
    return [s.strip() for s in re.split(r"(?<=[.!?])\s+(?=[A-Z0-9\"“(«'+≈])", par) if s.strip()]


def words(s):
    return [w for w in re.split(r"\s+", s) if re.search(r"[A-Za-z0-9]", w)]


def check(text):
    out = []
    for par in re.split(r"\n+", unescape(text)):
        ss = sentences(par)
        if len(ss) > 6:
            out.append(("PARA", "%d sentences in one paragraph" % len(ss)))
        for s in ss:
            w = words(s)
            lim = 20 if VERB_START.match(s) else 25
            if len(w) > lim:
                out.append(("LEN", "%d words (max %d): %s" % (len(w), lim, s)))
            for m in PASSIVE.finditer(s):
                if m.group(2).lower() in NOT_PASSIVE and m.group(1).lower() in ("is", "are", "was", "were", "be"):
                    continue
                out.append(("PASS", "'%s': %s" % (m.group(0), s)))
            low = s.lower()
            for bad, good in UNAPPROVED.items():
                m2 = re.search(r"\b" + re.escape(bad) + r"\b", low)
                # A capitalised word mid-sentence is a name (for example the "Required" diagnostic level).
                if m2 and not (m2.start() > 0 and s[m2.start()].isupper()):
                    out.append(("WORD", "'%s' -> '%s': %s" % (bad, good, s)))
            if ";" in s and len(w) > 8:
                out.append(("SEMI", s))
            if re.search(r"\b(e․g|i․e|etc)․", s):
                out.append(("ABBR", s.replace("․", ".")))
    return out


def main():
    keys = [l.split("\t", 1)[0] for l in open("lang/ru.tsv", encoding="utf-8").read().split("\n") if "\t" in l]
    counts, found = {}, []
    for k in keys:
        for rule, msg in check(k):
            counts[rule] = counts.get(rule, 0) + 1
            found.append((rule, msg))
    if "--list" in sys.argv:
        for rule, msg in found:
            print(rule + "\t" + msg.replace("\n", " "))
    total = sum(counts.values())
    print("STE check: %d strings, %d findings %s" % (len(keys), total,
          " ".join("%s=%d" % kv for kv in sorted(counts.items()))))
    if "--max" in sys.argv:
        limit = int(sys.argv[sys.argv.index("--max") + 1])
        if total > limit:
            sys.exit(1)


if __name__ == "__main__":
    main()
