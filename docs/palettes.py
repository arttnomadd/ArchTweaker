# Contrast check for the Carbon tokens in src/Ui/Theme.cs (WCAG 2.1). Run: python3 docs/palettes.py
def h(x): x=x.lstrip('#'); return tuple(int(x[i:i+2],16)/255 for i in (0,2,4))
def L(c):
    f=lambda v: v/12.92 if v<=0.03928 else ((v+0.055)/1.055)**2.4
    r,g,b=map(f,h(c)); return 0.2126*r+0.7152*g+0.0722*b
def cr(a,b):
    la,lb=sorted([L(a),L(b)],reverse=True); return (la+0.05)/(lb+0.05)
P={
 "Carbon/dark": dict(Bg="#0B0B0C",Panel="#161618",PanelHover="#1F1F22",Side="#070708",Accent="#E11D2E",AccentDark="#B5121F",OnAccent="#FFFFFF",Border="#2A2A2E",PendingBg="#3A1418",Text="#ECECEE",Muted="#A1A1A8",Strong="#FFFFFF",Ok="#34C759",Warn="#FFB020",Bad="#FF6B6B",Grid="#232327",Sel="#4A1219",AltRow="#121214",LogBg="#050506",LogFg="#D9A0A4",AccentOff="#2E2E33"),
 "Carbon/light":dict(Bg="#F4F4F6",Panel="#FFFFFF",PanelHover="#F0F0F3",Side="#EAEAEE",Accent="#C8102E",AccentDark="#9E0C24",OnAccent="#FFFFFF",Border="#D9D9DE",PendingBg="#FDE3E6",Text="#16161A",Muted="#5C5C66",Strong="#000000",Ok="#0E7434",Warn="#7E4A00",Bad="#AE1219",Grid="#E4E4E9",Sel="#FCD2D8",AltRow="#F9F9FB",LogBg="#FCFCFD",LogFg="#8E1B29",AccentOff="#CFCFD6"),
}
checks=[("Text","Bg",7),("Text","Panel",7),("Muted","Bg",4.5),("Muted","Panel",4.5),("Muted","Side",4.5),("OnAccent","Accent",4.5),("OnAccent","AccentDark",4.5),("Accent","Bg",3),("Accent","Panel",3),("Ok","Panel",3),("Warn","Panel",3),("Bad","Panel",3),("Text","Sel",4.5),("LogFg","LogBg",4.5),("Text","PendingBg",4.5),("Muted","PendingBg",4.5),("Ok","PendingBg",4.5 if 0 else 3),("Warn","PendingBg",3),("Bad","PendingBg",3),("Strong","Panel",3),("Muted","Panel",3)]
bad=0
for n,p in P.items():
    out=[]
    for a,b,mn in checks:
        v=cr(p[a],p[b]); ok=v>=mn; bad+= (not ok)
        out.append(("" if ok else "!!")+f"{a}/{b} {v:.1f}")
    print(n.ljust(17)," ".join(out))
print("FAILS:",bad)

# ---- 2.9 accent colours: read the AccentSets table from src/Ui/Theme.cs and check each on both Carbon themes
import re, os
src = "".join(open(os.path.join(os.path.dirname(__file__), "..", "src", "Ui", f), encoding="utf-8").read() for f in ("Theme.cs", "Theme.Palettes.cs"))   # 3.32: colour data in its own file
rows = re.findall(r'new\[\] \{ ("#[0-9A-F]{6}"(?:,\s*"#[0-9A-F]{6}"){11}) \}', src)
names = re.search(r'AccentNames = \{([^}]*)\}', src).group(1).replace('"', '').split(',')
acc_checks = [("OnAccent","Accent",4.5),("OnAccent","AccentDark",4.5),("Accent","Bg",3),("Accent","Panel",3),("Accent","Side",3),
              ("Text","Sel",4.5),("Text","PendingBg",4.5),("Muted","PendingBg",4.5),("Warn","PendingBg",3),("LogFg","LogBg",4.5)]
for name, row in zip(names, rows):
    v = [x.strip().strip('"') for x in row.split(',')]
    for mode, off in (("dark", 0), ("light", 6)):
        p = dict(P["Carbon/" + mode]); p.update(Accent=v[off], AccentDark=v[off+1], OnAccent=v[off+2], PendingBg=v[off+3], Sel=v[off+4], LogFg=v[off+5])
        fails = [f"{a}/{b} {cr(p[a],p[b]):.1f}" for a, b, mn in acc_checks if cr(p[a], p[b]) < mn]
        bad += len(fails)
        print(("accent " + name.strip() + "/" + mode).ljust(22), "OK" if not fails else "!! " + " ".join(fails))
print("TOTAL FAILS:", bad)

# ---- 3.10: every scheme in Theme.cs (Palettes rows, in SchemeNames order), each accent on each scheme, and the progress colours
K = "Bg Panel PanelHover Side Accent AccentDark OnAccent Border PendingBg Text Muted Strong Ok Warn Bad Grid Sel AltRow LogBg LogFg AccentOff".split()
pal_rows = re.findall(r'new\[\] \{ ("#[0-9A-F]{6}"(?:,\s*"#[0-9A-F]{6}"){20}) \}', src)
scheme_names = re.search(r'SchemeNames = \{([^}]*)\}', src).group(1).replace('"', '').split(',')
light = {"Carbon light", "Blossom"}
prog_rows = re.findall(r'new\[\] \{ "(#[0-9A-F]{6})", "(#[0-9A-F]{6})" \}', src)
bad2 = 0
for sname, row in zip(scheme_names, pal_rows):
    sname = sname.strip(); p = dict(zip(K, [x.strip().strip('"') for x in row.split(',')]))
    fails = [f"{a}/{b} {cr(p[a],p[b]):.1f}" for a, b, mn in checks if cr(p[a], p[b]) < mn]
    off = 6 if sname in light else 0
    for name, arow in zip(names, rows):
        v = [x.strip().strip('"') for x in arow.split(',')]
        q = dict(p); q.update(Accent=v[off], AccentDark=v[off+1], OnAccent=v[off+2], PendingBg=v[off+3], Sel=v[off+4], LogFg=v[off+5])
        fails += [f"{name.strip()}:{a}/{b} {cr(q[a],q[b]):.1f}" for a, b, mn in acc_checks if cr(q[a], q[b]) < mn]
    for d, l in prog_rows:
        c = l if sname in light else d
        if cr(c, p["Panel"]) < 3: fails.append(f"progress {c}/Panel {cr(c, p['Panel']):.1f}")
    bad2 += len(fails)
    print(("scheme " + sname).ljust(22), "OK" if not fails else "!! " + " ".join(fails))
print("ALL SCHEMES FAILS:", bad2)
