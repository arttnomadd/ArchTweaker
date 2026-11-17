#!/usr/bin/env python3
"""Write a signed latest.json for the ArchTweaker self-update (ADR-0008). No extra packages needed.

    python3 tools/sign_manifest.py --key ArchTweaker-update-key.json --exe out/ArchTweaker.exe \
        --version 3.35.0 --url https://github.com/arttnomadd/ArchTweaker/releases/download/v3.35.0/ArchTweaker.exe \
        [--notes "text"] [--serial N] [--overlay URL --overlay-file overlay.xml] [--out latest.json]

--serial defaults to the current Unix time. The app refuses a manifest whose serial is lower than one it has seen,
so an old signed manifest can't be replayed to roll an update offer or an overlay back.
    python3 tools/sign_manifest.py --verify latest.json --pub-xml public.xml

The private key file must never go into the repository, a release or the zip. The exe holds only the public key
(Updater.PublicKeyXml). The signed text must match Updater.SignedText exactly: "archtweaker-manifest-v1", then
version, serial, url, sha256, notes, overlay, overlay_sha256, one per line, a missing field as an empty line.
"""
import argparse, base64, hashlib, json, re, sys, time
from urllib.parse import urlparse

# must match Updater.AllowedHosts in src/App/Update.cs
ALLOWED_HOSTS = {"raw.githubusercontent.com", "cdn.jsdelivr.net", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"}
SAFE = re.compile(r"^[\x20-\x7e]*\Z")   # printable ASCII; no quote or backslash (checked below)

FIELDS = ["version", "serial", "url", "sha256", "notes", "overlay", "overlay_sha256"]
# DER prefix of DigestInfo for SHA-256 (RFC 8017, section 9.2)
SHA256_PREFIX = bytes.fromhex("3031300d060960864801650304020105000420")


def signed_text(m):
    return "archtweaker-manifest-v1" + "".join("\n" + (m.get(k) or "") for k in FIELDS)


def emsa(msg, k):
    t = SHA256_PREFIX + hashlib.sha256(msg).digest()
    if k < len(t) + 11:
        raise ValueError("key too short")
    return b"\x00\x01" + b"\xff" * (k - len(t) - 3) + b"\x00" + t


def sign(msg, n, d):
    k = (n.bit_length() + 7) // 8
    s = pow(int.from_bytes(emsa(msg, k), "big"), d, n)
    return base64.b64encode(s.to_bytes(k, "big")).decode()


def verify(msg, sig_b64, n, e):
    k = (n.bit_length() + 7) // 8
    try:
        raw = base64.b64decode(sig_b64, validate=True)
    except Exception:
        return False
    if len(raw) != k:
        return False
    s = int.from_bytes(raw, "big")
    if s >= n:
        return False
    return pow(s, e, n).to_bytes(k, "big") == emsa(msg, k)


def sha_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def pub_from_xml(path):
    x = open(path, encoding="utf-8").read()
    g = lambda tag: int.from_bytes(base64.b64decode(re.search("<%s>(.*?)</%s>" % (tag, tag), x).group(1)), "big")
    return g("Modulus"), g("Exponent")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key"); ap.add_argument("--exe"); ap.add_argument("--version"); ap.add_argument("--url")
    ap.add_argument("--notes", default=""); ap.add_argument("--overlay"); ap.add_argument("--overlay-file")
    ap.add_argument("--out", default="latest.json"); ap.add_argument("--verify"); ap.add_argument("--pub-xml")
    ap.add_argument("--serial", type=int, default=int(time.time()))
    a = ap.parse_args()

    if a.verify:
        raw = open(a.verify, encoding="utf-8").read()
        seen = {}
        def hook(pairs):
            for k, _ in pairs:
                if k in seen:
                    raise SystemExit("duplicate key %r: the app reads it as missing" % k)
                seen[k] = 1
            return dict(pairs)
        m = json.loads(raw, object_pairs_hook=hook)
        n, e = pub_from_xml(a.pub_xml)
        ok = verify(signed_text(m).encode("utf-8"), m.get("sig", ""), n, e)
        print("signature OK" if ok else "signature BAD")
        return 0 if ok else 1

    for need in ("key", "exe", "version", "url"):
        if not getattr(a, need):
            ap.error("--%s is required" % need)
    if not re.match(r"^\d+(\.\d+){0,3}$", a.version):
        ap.error("version must look like 3.35.0")
    for u in [a.url] + ([a.overlay] if a.overlay else []):
        pu = urlparse(u)
        if pu.scheme != "https" or (pu.hostname or "").lower() not in ALLOWED_HOSTS:
            ap.error("%s: must be https on %s" % (u, ", ".join(sorted(ALLOWED_HOSTS))))
    if a.serial <= 0:
        ap.error("--serial must be positive")
    key = json.load(open(a.key, encoding="utf-8"))
    n, d = int(key["n"], 16), int(key["d"], 16)
    if n.bit_length() < 3072:
        ap.error("the key must be RSA-3072 or larger")
    if a.pub_xml and pub_from_xml(a.pub_xml)[0] != n:
        ap.error("this private key does not match --pub-xml (the key built into the exe)")
    m = {"version": a.version, "serial": str(a.serial), "url": a.url, "sha256": sha_file(a.exe), "notes": a.notes}
    if a.overlay:
        if not a.overlay_file:
            ap.error("--overlay needs --overlay-file")
        m["overlay"] = a.overlay
        m["overlay_sha256"] = sha_file(a.overlay_file)
    for k, v in m.items():
        if '"' in v or "\\" in v or not SAFE.match(v):
            ap.error("%s must be printable ASCII without quotes or backslashes (the app reads the manifest with a strict parser)" % k)
    m["sig"] = sign(signed_text(m).encode("utf-8"), n, d)
    if not verify(signed_text(m).encode("utf-8"), m["sig"], n, int(key.get("e", 65537))):
        raise SystemExit("self-check failed: the signature does not verify")
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(m, f, indent=1)
    print("wrote", a.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
