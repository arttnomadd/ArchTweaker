#!/usr/bin/env python3
# Writes a Win32 .res with the app manifest (requireAdministrator), the icon and a VERSIONINFO block, for mcs -win32res.
# usage: mkres.py <app.manifest> <app.ico> <a.b.c.d> <out.res>
import struct, sys

def entry(typ, name, data, lang=0x0409, flags=0x1030):
    hdr = struct.pack("<HHHH", 0xFFFF, typ, 0xFFFF, name)
    hdr += struct.pack("<IHHII", 0, flags, lang, 0, 0)
    hsize = 8 + len(hdr)
    out = struct.pack("<II", len(data), hsize) + hdr + data
    return out + b"\0" * ((4 - len(out) % 4) % 4)

def u16(s): return s.encode("utf-16-le") + b"\0\0"
def pad4(b): return b + b"\0" * ((4 - len(b) % 4) % 4)

def node(key, value=b"", children=(), text=False):
    body = struct.pack("<HHH", 0, 0, 1 if text else 0) + u16(key)
    body = pad4(body)
    vlen = (len(value) // 2) if text else len(value)
    body += value
    body = pad4(body)
    for c in children: body += pad4(c)
    return struct.pack("<HHH", len(body), vlen, 1 if text else 0) + body[6:]

def version(v):
    a, b, c, d = [int(x) for x in v.split(".")]
    ms, ls = (a << 16) | b, (c << 16) | d
    fixed = struct.pack("<13I", 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x4, 0x1, 0, 0, 0)
    pv = "%d.%d.%d" % (a, b, c)
    strings = [("CompanyName", "Archie"), ("FileDescription", "ArchTweaker"), ("FileVersion", v),
               ("InternalName", "ArchTweaker.exe"), ("LegalCopyright", "Archie"), ("OriginalFilename", "ArchTweaker.exe"),
               ("ProductName", "ArchTweaker"), ("ProductVersion", pv)]
    table = node("040904b0", b"", [node(k, u16(s), text=True) for k, s in strings], text=True)
    sfi = node("StringFileInfo", b"", [table], text=True)
    vfi = node("VarFileInfo", b"", [node("Translation", struct.pack("<HH", 0x0409, 1200))], text=True)
    return node("VS_VERSION_INFO", fixed, [sfi, vfi])

def main():
    man, ico, ver, out = sys.argv[1:5]
    res = struct.pack("<II", 0, 32) + struct.pack("<HHHH", 0xFFFF, 0, 0xFFFF, 0) + struct.pack("<IHHII", 0, 0, 0, 0, 0)
    d = open(ico, "rb").read()
    _, typ, n = struct.unpack_from("<HHH", d, 0)
    grp = struct.pack("<HHH", 0, 1, n)
    for i in range(n):
        w, h, cc, r, planes, bpp, size, off = struct.unpack_from("<BBBBHHII", d, 6 + 16 * i)
        res += entry(3, i + 1, d[off:off + size])
        grp += struct.pack("<BBBBHHIH", w, h, cc, r, planes, bpp, size, i + 1)
    res += entry(14, 32512, grp)
    res += entry(16, 1, version(ver))
    res += entry(24, 1, open(man, "rb").read())
    open(out, "wb").write(res)

main()
