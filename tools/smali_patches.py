"""Stardual Valley's changes to the upstream app's compiled code (smali).

Run by tools/repack-apk.sh on the apktool-unpacked upstream APK:

    python3 -I tools/smali_patches.py <unpacked app folder>

Each patch checks that the code it changes is exactly what it expects, and stops the
build if not, so a newer upstream release can't be patched in the wrong place.
"""

import glob
import re
import struct
import sys

APP = sys.argv[1]
PKG = "com/wemu/session/secondscreen"


def find(name):
    paths = glob.glob(f"{APP}/smali*/{PKG}/{name}.smali")
    assert len(paths) == 1, f"{name}: expected one file, found {paths}"
    return paths[0]


def method_span(lines, name, must=""):
    starts = [i for i, l in enumerate(lines)
              if l.startswith(".method") and l.split()[-1].startswith(name + "(") and must in l]
    assert len(starts) == 1, f"method {name}: found {len(starts)}"
    s = starts[0]
    e = next(i for i in range(s, len(lines)) if lines[i].startswith(".end method"))
    return s, e


def set_dp(lines, span, old, new, expected=1):
    """Change every `old`.dp size in a method to `new`.dp.

    Compose sizes compile to `const vA, old` ... `int-to-float vB, vA` ...
    `Dp.constructor-impl(vB)`. The int-to-float is swapped for a float constant so only
    that one size changes, even if vA is used for something else too.
    """
    s, e = span
    bits = struct.unpack(">I", struct.pack(">f", float(new)))[0]
    assert bits & 0xFFFF == 0
    done = 0
    for i in range(s, e):
        if "Dp;->constructor-impl(F)F" not in lines[i]:
            continue
        reg = re.search(r"\{(\w+)\}", lines[i]).group(1)
        for j in range(i - 1, max(s, i - 12), -1):
            m = re.match(rf"(\s*)int-to-float {reg}, (\w+)$", lines[j])
            if m:
                break
        else:
            continue
        src = m.group(2)
        for k in range(j - 1, s, -1):
            c = re.match(rf"\s*const(?:/4|/16)? {src}, (-?0x[0-9a-f]+|-?\d+)$", lines[k])
            if c:
                if int(c.group(1), 0) == old:
                    lines[j] = f"{m.group(1)}const/high16 {reg}, {hex(bits)}    # {new}.0f (was {old}dp)"
                    done += 1
                break
            if re.match(rf"\s*[a-z][\w/-]* {src},", lines[k]):
                break
    assert done == expected, f"{old}dp -> {new}dp: changed {done}, expected {expected}"


def drop_call(lines, span, callee, then_rule=True):
    """Remove a call to a composable (and the divider line drawn after it)."""
    s, e = span
    hits = [i for i in range(s, e) if "invoke-static" in lines[i] and f"->{callee}(" in lines[i]]
    assert len(hits) == 1, f"call {callee}: found {len(hits)}"
    lines[hits[0]] = f"    # Stardual: removed {callee}"
    if then_rule:
        rule = next(i for i in range(hits[0], e) if "->GiftRule(" in lines[i])
        assert rule - hits[0] < 12, f"no divider right after {callee}"
        lines[rule] = "    # Stardual: removed the divider under it"


def write(path, lines):
    open(path, "w").write("\n".join(lines))
    print("  patched", path.split("/smali")[-1])


# 1. Bundles page crash. Each item in a bundle's "still missing" row is keyed by
#    id:name:count:quality, and Compose crashes on a repeated key. The Construction
#    Bundle asks for 99 Wood twice, so scrolling to it always crashed the app. Pass no
#    key instead (items are keyed by position), which is all a short row of icons needs.
path = find("StardewReferenceBundlesKt")
src = open(path).read()
cls = ("Lcom/wemu/session/secondscreen/StardewReferenceBundlesKt$BundleProgressRow"
       "$lambda$26$lambda$25$lambda$24$$inlined$items$default$2;")
old = (f"    new-instance v6, {cls}\n\n"
       f"    invoke-direct {{v6, v1, v0}}, {cls}-><init>(Lkotlin/jvm/functions/Function1;Ljava/util/List;)V\n\n"
       f"    check-cast v6, Lkotlin/jvm/functions/Function1;\n")
assert src.count(old) == 1, "bundle row key code not found; upstream changed"
write(path, src.replace(old, "    const/4 v6, 0x0\n").split("\n"))

# 2. Gift Guide: fit more villagers on the small screen.
path = find("StardewReferenceGiftGuideKt")
lines = open(path).read().split("\n")
page = method_span(lines, "StardewGiftGuideReferencePage")
# No "Gift Guide / N villagers" title block, and no list header (big icon, "Villagers",
# "N shown", "Select a villager to view loved gifts"): the All / In backpack filter
# buttons already show the counts.
drop_call(lines, page, "GiftGuideHeader")
drop_call(lines, page, "GiftVillagerRailHeader")
# Shorter villager rows with smaller portraits.
row = method_span(lines, "GiftVillagerRow")
set_dp(lines, row, 67, 50)
set_dp(lines, row, 48, 34)
# Slimmer header for the selected villager.
detail = method_span(lines, "GiftVillagerDetail")
set_dp(lines, detail, 87, 60)
set_dp(lines, detail, 64, 40)
# Smaller loved-gift icons.
set_dp(lines, method_span(lines, "GiftItemCard"), 47, 32)
# No birthday: the line under the villager's hearts then only shows "Spoken today".
s, e = method_span(lines, "giftBirthdayDetail")
lines[s + 1:e] = ["    .locals 1", "    const-string v0, \"\"", "    return-object v0"]
write(path, lines)

# Loved-gift grid rows, to match the smaller icons.
path = find("StardewReferenceGiftGuideKt$GiftVillagerDetail$lambda$66$lambda$65$lambda$64$$inlined$items$default$4")
lines = open(path).read().split("\n")
set_dp(lines, method_span(lines, "invoke", must=" final "), 89, 66)
write(path, lines)
