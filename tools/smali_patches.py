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


def drop_call(lines, span, callee, then_rule=True, rule_before=False):
    """Remove a call to a composable, and the divider line drawn after (or before) it."""
    s, e = span
    hits = [i for i in range(s, e) if "invoke-static" in lines[i] and f"->{callee}(" in lines[i]]
    assert len(hits) == 1, f"call {callee}: found {len(hits)}"
    lines[hits[0]] = f"    # Stardual: removed {callee}"
    if rule_before:
        rule = next(i for i in range(hits[0], s, -1) if "->GiftRule(" in lines[i])
        assert hits[0] - rule < 12, f"no divider right before {callee}"
        lines[rule] = "    # Stardual: removed the divider above it"
    elif then_rule:
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
# No date row at the bottom (the top screen already shows the date), so the list and
# the villager's details run to the bottom of the screen.
drop_call(lines, page, "GiftGuideFooter", rule_before=True)
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

# 3. Crafting: no strip of recipe tiles along the bottom (it repeats the recipe list on
#    the left), and no divider above it, so the recipes and details fill the screen.
path = find("StardewCraftingKt$StardewCraftingReferencePage$3")
lines = open(path).read().split("\n")
s, e = method_span(lines, "invoke", must=" final ")
hits = [i for i in range(s, e) if "->access$CraftTimeCarousel(" in lines[i]]
assert len(hits) == 1, f"crafting strip call: found {len(hits)}"
rules = [i for i in range(hits[0] - 200, hits[0]) if "->access$CraftRule(" in lines[i]]
assert len(rules) == 1, f"divider above the crafting strip: found {len(rules)}"
lines[hits[0]] = "    # Stardual: removed the crafting strip"
lines[rules[0]] = "    # Stardual: removed the divider above the crafting strip"
write(path, lines)

# 4. Map: the villager row runs the full width with each villager's name under their
#    portrait. Removed: the farm summary on the left (farm, funds, earnings, farmer,
#    pet) with its divider, and the season / day / time row under the villagers.
path = find("StardewScreenKt$MapFooter$1")
lines = open(path).read().split("\n")
s, e = method_span(lines, "invoke", must=" final ")
farm = [i for i in range(s, e) if "->access$MapFarmSummary(" in lines[i]]
assert len(farm) == 1, f"map farm summary call: found {len(farm)}"
boxes = [i for i in range(farm[0], e) if "BoxKt;->Box(Landroidx/compose/ui/Modifier;" in lines[i]]
assert boxes and boxes[0] - farm[0] < 80, "divider after the map farm summary not found"
lines[farm[0]] = "    # Stardual: removed the farm summary"
lines[boxes[0]] = "    # Stardual: removed the divider beside it"
# Bigger villager portraits, using the room freed by the season / day / time row.
# The size depends on how tall the strip is: 31 / 37 / 43dp become 42 / 52 / 64dp.
for old, new in ((31, 42), (37, 52), (43, 64)):
    set_dp(lines, (s, e), old, new)
write(path, lines)

path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "MapVillagerSummary-TN_CM5M")
row = [i for i in range(s, e) if "LazyDslKt;->LazyRow(" in lines[i]]
assert len(row) == 1, f"map villager row: found {len(row)}"
r = row[0]
# Make the row taller so the names fit: height(portraitSize) -> height(portraitSize + 18dp).
h = max(i for i in range(s, r) if "SizeKt;->height-3ABfNKs(Landroidx/compose/ui/Modifier;F)" in lines[i])
m = re.match(r"(\s*)invoke-static \{(v\d+), (v\d+)\}", lines[h])
assert m and m.group(3) == "v3", lines[h]
assert any("const/4 v9," in lines[i] for i in range(h, r)), "v9 is not free after the row height"
lines[h] = (f"{m.group(1)}const/high16 v9, 0x41900000    # Stardual: 18dp for the names\n"
            f"{m.group(1)}add-float/2addr v9, v3\n"
            f"{m.group(1)}invoke-static {{{m.group(2)}, v9}}, Landroidx/compose/foundation/layout/SizeKt;->height-3ABfNKs(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;")
# More room between villagers so their names fit: 3dp -> 14dp.
set_dp(lines, (s, e), 3, 14)
# Line portraits and names up along the top instead of centering each one.
c = max(i for i in range(s, r) if "->getCenterVertically()" in lines[i])
assert r - c < 300
lines[c] = lines[c].replace("getCenterVertically()", "getTop()")
# Drop the divider under the row and the season / day / time stats.
box = [i for i in range(r, e) if "BoxKt;->Box(Landroidx/compose/ui/Modifier;" in lines[i]]
assert box and box[0] - r < 120, "divider under the villager row not found"
lines[box[0]] = "    # Stardual: removed the divider above season / day / time"
stats = [i for i in range(r, e) if "->MapFooterStat(" in lines[i] or "->MapFooterDivider(" in lines[i]]
assert len(stats) == 5, f"season / day / time: found {len(stats)} calls"
for i in stats:
    lines[i] = "    # Stardual: removed a season / day / time stat"
# A small label with the villager's name, drawn under the portrait in the same row item.
lines.insert(len(lines) - 1 if lines[-1] == "" else len(lines), """
.method public static stardualVillagerName(Lcom/wemu/session/secondscreen/StardewVillager;FLandroidx/compose/runtime/Composer;)V
    .locals 25

    # Text(name, Modifier.offset(x = -size / 2).width(1.dp)
    #          .wrapContentWidth(CenterHorizontally, unbounded = true)
    #          .width(size + 14dp).padding(top = size + 3dp), color = MAP_REFERENCE_INK,
    #      fontSize = 10.sp, fontWeight = SemiBold, textAlign = Center,
    #      overflow = Ellipsis, maxLines = 1, style = MaterialTheme.typography.labelSmall)
    # A row item lays its pieces out side by side, so the label takes no width of its own
    # (it starts where the portrait ends) and is shifted back to center under the portrait.
    move-object/from16 v0, p0
    invoke-virtual {v0}, Lcom/wemu/session/secondscreen/StardewVillager;->getName()Ljava/lang/String;
    move-result-object v0

    move/from16 v2, p1
    sget-object v1, Landroidx/compose/ui/Modifier;->Companion:Landroidx/compose/ui/Modifier$Companion;
    check-cast v1, Landroidx/compose/ui/Modifier;
    const/high16 v3, -0x41000000
    mul-float/2addr v3, v2
    const/4 v4, 0x0
    invoke-static {v1, v3, v4}, Landroidx/compose/foundation/layout/OffsetKt;->offset-VpY3zN4(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;
    move-result-object v1
    # 1dp, not 0: Compose doesn't draw a piece that takes up no space.
    const/high16 v4, 0x3f800000
    invoke-static {v1, v4}, Landroidx/compose/foundation/layout/SizeKt;->width-3ABfNKs(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;
    move-result-object v1
    sget-object v3, Landroidx/compose/ui/Alignment;->Companion:Landroidx/compose/ui/Alignment$Companion;
    invoke-virtual {v3}, Landroidx/compose/ui/Alignment$Companion;->getCenterHorizontally()Landroidx/compose/ui/Alignment$Horizontal;
    move-result-object v3
    const/4 v5, 0x1
    invoke-static {v1, v3, v5}, Landroidx/compose/foundation/layout/SizeKt;->wrapContentWidth(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment$Horizontal;Z)Landroidx/compose/ui/Modifier;
    move-result-object v1
    const/high16 v3, 0x41600000
    add-float/2addr v3, v2
    invoke-static {v1, v3}, Landroidx/compose/foundation/layout/SizeKt;->width-3ABfNKs(Landroidx/compose/ui/Modifier;F)Landroidx/compose/ui/Modifier;
    move-result-object v1
    const/high16 v3, 0x40400000
    add-float/2addr v3, v2
    const/4 v4, 0x0
    invoke-static {v1, v4, v3, v4, v4}, Landroidx/compose/foundation/layout/PaddingKt;->padding-qDBjuR0(Landroidx/compose/ui/Modifier;FFFF)Landroidx/compose/ui/Modifier;
    move-result-object v1

    sget-wide v2, Lcom/wemu/session/secondscreen/StardewScreenKt;->MAP_REFERENCE_INK:J

    const/16 v4, 0xa
    invoke-static {v4}, Landroidx/compose/ui/unit/TextUnitKt;->getSp(I)J
    move-result-wide v4

    const/4 v6, 0x0
    sget-object v7, Landroidx/compose/ui/text/font/FontWeight;->Companion:Landroidx/compose/ui/text/font/FontWeight$Companion;
    invoke-virtual {v7}, Landroidx/compose/ui/text/font/FontWeight$Companion;->getSemiBold()Landroidx/compose/ui/text/font/FontWeight;
    move-result-object v7
    const/4 v8, 0x0
    const-wide/16 v9, 0x0
    const/4 v11, 0x0
    sget-object v12, Landroidx/compose/ui/text/style/TextAlign;->Companion:Landroidx/compose/ui/text/style/TextAlign$Companion;
    invoke-virtual {v12}, Landroidx/compose/ui/text/style/TextAlign$Companion;->getCenter-e0LSkKk()I
    move-result v12
    invoke-static {v12}, Landroidx/compose/ui/text/style/TextAlign;->box-impl(I)Landroidx/compose/ui/text/style/TextAlign;
    move-result-object v12
    const-wide/16 v13, 0x0
    sget-object v15, Landroidx/compose/ui/text/style/TextOverflow;->Companion:Landroidx/compose/ui/text/style/TextOverflow$Companion;
    invoke-virtual {v15}, Landroidx/compose/ui/text/style/TextOverflow$Companion;->getEllipsis-gIe3tQ8()I
    move-result v15
    const/16 v16, 0x1
    const/16 v17, 0x1
    const/16 v18, 0x1
    const/16 v19, 0x0

    sget-object v20, Landroidx/compose/material3/MaterialTheme;->INSTANCE:Landroidx/compose/material3/MaterialTheme;
    move-object/from16 v21, p2
    sget v22, Landroidx/compose/material3/MaterialTheme;->$stable:I
    invoke-virtual/range {v20 .. v22}, Landroidx/compose/material3/MaterialTheme;->getTypography(Landroidx/compose/runtime/Composer;I)Landroidx/compose/material3/Typography;
    move-result-object v20
    invoke-virtual/range {v20 .. v20}, Landroidx/compose/material3/Typography;->getLabelSmall()Landroidx/compose/ui/text/TextStyle;
    move-result-object v20

    const/16 v22, 0x0
    const/16 v23, 0x0
    # Defaults: fontStyle, fontFamily, letterSpacing, textDecoration, lineHeight, minLines, onTextLayout.
    const v24, 0xc5d0
    invoke-static/range {v0 .. v24}, Landroidx/compose/material3/TextKt;->Text--4IGK_g(Ljava/lang/String;Landroidx/compose/ui/Modifier;JJLandroidx/compose/ui/text/font/FontStyle;Landroidx/compose/ui/text/font/FontWeight;Landroidx/compose/ui/text/font/FontFamily;JLandroidx/compose/ui/text/style/TextDecoration;Landroidx/compose/ui/text/style/TextAlign;JIZIILkotlin/jvm/functions/Function1;Landroidx/compose/ui/text/TextStyle;Landroidx/compose/runtime/Composer;III)V
    return-void
.end method
""")
write(path, lines)

path = find("StardewScreenKt$MapVillagerSummary_TN_CM5M$lambda$238$lambda$235$lambda$234$$inlined$items$default$4")
lines = open(path).read().split("\n")
s, e = method_span(lines, "invoke", must=" final ")
p = [i for i in range(s, e) if "->access$MapVillagerPortrait--jt2gSs(" in lines[i]]
assert len(p) == 1 and "{v10 .. v16}" in lines[p[0]], "map portrait call not found"
lines.insert(p[0] + 1, "    invoke-static {v11, v13, v15}, Lcom/wemu/session/secondscreen/StardewScreenKt;->stardualVillagerName(Lcom/wemu/session/secondscreen/StardewVillager;FLandroidx/compose/runtime/Composer;)V")
write(path, lines)

# 5. Relationships page.
#    - No "Friendship (0-10 hearts) / Gifts This Week" header, and no date / season /
#      "Talked 0/36" footer.
#    - Hearts in two rows (five and five, or seven and seven once married).
#    - Birthdays without the "Birthday: " label, and a line under them saying who the
#      villager is with ("Single", "Married to Pierre", "Dating you"...), sent by the mod.
#    - Gift and talk checkboxes without the boxes drawn around them, and no "0/2 gifts".
path = find("ProtocolKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "parseGameMessage$lambda$26$lambda$8")
assert any('"talkedToday"' in lines[i] for i in range(s, e)), "villager parser moved"
first = next(i for i in range(s + 1, e) if lines[i].strip() and not lines[i].strip().startswith("."))
lines.insert(first, "    invoke-static/range {p0 .. p0}, Lcom/stardual/StardualStatus;->remember(Ljava/lang/Object;)V\n")
write(path, lines)

path = find("StardewReferenceSocialKt")
lines = open(path).read().split("\n")
page = method_span(lines, "StardewRelationshipsReferencePage")
drop_call(lines, page, "RelationshipHeader", then_rule=False)
s, e = page
hdr = next(i for i in range(s, e) if "Stardual: removed RelationshipHeader" in lines[i])
rule = next(i for i in range(hdr, e) if "->SocialHorizontalRule(" in lines[i])
assert rule - hdr < 20
lines[rule] = "    # Stardual: removed the line under the header"
drop_call(lines, page, "ReferenceSocialFooter", then_rule=False)

row = method_span(lines, "RelationshipRow")
s, e = row
hearts = [i for i in range(s, e) if "->SocialHearts(" in lines[i]]
assert len(hearts) == 1, hearts
m = re.match(r"(\s*)invoke-static \{(v\d+), (v\d+), (v\d+), (v\d+), v\d+\}", lines[hearts[0]])
assert m, lines[hearts[0]]
lines[hearts[0]] = (f"{m.group(1)}invoke-static {{{m.group(2)}, {m.group(3)}, {m.group(4)}, {m.group(5)}}}, "
                    "Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualHearts("
                    "Lcom/wemu/session/secondscreen/StardewState;IILandroidx/compose/runtime/Composer;)V")
# Under the hearts: how far the next heart is ("Next heart: 4 chats or 1 loved gift").
assert any("invoke-virtual {v1}, Lcom/wemu/session/secondscreen/StardewVillager;->getHearts()I" in lines[i]
           for i in range(hearts[0] - 30, hearts[0])), "villager register moved"
lines[hearts[0]] += (f"\n{m.group(1)}invoke-static {{v1, {m.group(5)}}}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;"
                     "->stardualHeartHint(Lcom/wemu/session/secondscreen/StardewVillager;Landroidx/compose/runtime/Composer;)V")
bday = [i for i in range(s, e) if "->socialBirthday(" in lines[i]]
assert len(bday) == 1 and "{v1}" in lines[bday[0]]
text = next(i for i in range(bday[0], e) if "TextKt;->Text--4IGK_g(" in lines[i])
assert "{v80 .. v104}" in lines[text] and any("move-object/from16 v101, v9" in lines[i] for i in range(bday[0], text))
lines[text] += ("\n\n    # Stardual: who they're with, under the birthday\n"
                "    move-object/from16 v80, v1\n"
                "    move-object/from16 v81, v101\n"
                "    invoke-static/range {v80 .. v81}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;"
                "->stardualRelationshipLine(Lcom/wemu/session/secondscreen/StardewVillager;Landroidx/compose/runtime/Composer;)V")
gifts = [i for i in range(s, e) if 'const-string' in lines[i] and '"/2 gifts"' in lines[i]]
assert len(gifts) == 1
gtext = next(i for i in range(gifts[0], e) if "TextKt;->Text--4IGK_g(" in lines[i])
assert gtext - gifts[0] < 250
lines[gtext] = "    # Stardual: removed the \"0/2 gifts\" line"

# Column widths: the hearts need less room in two rows, so give it to the name column
# (its "Married to Demetrius" line). Name 31% -> 39%, hearts 42% -> 34%, checkboxes 27%.
for old, new in (("0x3e9eb852", "0x3ec7ae14"), ("0x3ed70a3d", "0x3eae147b")):
    hits = [i for i in range(*row) if re.search(rf"const v\d+, {old}\b", lines[i])]
    assert len(hits) == 1, f"row weight {old}: found {len(hits)}"
    lines[hits[0]] = re.sub(rf"{old}.*$", f"{new}    # Stardual: was {old}", lines[hits[0]])

s, e = method_span(lines, "RelationshipStatusSlot")
then = [i for i in range(s, e) if "Landroidx/compose/ui/Modifier;->then(" in lines[i]]
assert len(then) == 1
m = re.match(r"(\s*)invoke-interface \{(v\d+), (v\d+)\}", lines[then[0]])
res = next(i for i in range(then[0] + 1, then[0] + 4) if "move-result-object" in lines[i])
dest = lines[res].split()[-1]
lines[then[0]] = f"{m.group(1)}# Stardual: no box drawn around the checkbox\n{m.group(1)}move-object/from16 {dest}, {m.group(2)}"
lines[res] = ""

s, e = method_span(lines, "socialBirthday")
lbl = [i for i in range(s, e) if 'const-string' in lines[i] and '"Birthday: "' in lines[i]]
assert len(lbl) == 1
lines[lbl[0]] = lines[lbl[0]].replace('"Birthday: "', '""')

TEXT_SIG = ("Landroidx/compose/material3/TextKt;->Text--4IGK_g(Ljava/lang/String;Landroidx/compose/ui/Modifier;JJ"
            "Landroidx/compose/ui/text/font/FontStyle;Landroidx/compose/ui/text/font/FontWeight;"
            "Landroidx/compose/ui/text/font/FontFamily;JLandroidx/compose/ui/text/style/TextDecoration;"
            "Landroidx/compose/ui/text/style/TextAlign;JIZIILkotlin/jvm/functions/Function1;"
            "Landroidx/compose/ui/text/TextStyle;Landroidx/compose/runtime/Composer;III)V")
lines.append(f"""
.method public static stardualHeartRow(Lcom/wemu/session/secondscreen/StardewState;IILandroidx/compose/runtime/Composer;)V
    .locals 1
    const/4 v0, 0x0
    invoke-static {{p0, p1, p2, p3, v0}}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->SocialHearts(Lcom/wemu/session/secondscreen/StardewState;IILandroidx/compose/runtime/Composer;I)V
    return-void
.end method

.method public static stardualHearts(Lcom/wemu/session/secondscreen/StardewState;IILandroidx/compose/runtime/Composer;)V
    .locals 20
    # Two rows: the first half of the hearts nudged up 16dp, the second half down 4dp, which
    # leaves room under them for the next-heart hint.
    move-object/from16 v0, p0
    move/from16 v1, p1
    move/from16 v2, p2
    move-object/from16 v3, p3
    add-int/lit8 v4, v2, 0x1
    div-int/lit8 v4, v4, 0x2
    invoke-static {{v1, v4}}, Ljava/lang/Math;->min(II)I
    move-result v5
    sub-int v6, v1, v4
    const/4 v7, 0x0
    invoke-static {{v6, v7}}, Ljava/lang/Math;->max(II)I
    move-result v6
    sub-int v7, v2, v4
    invoke-static {{v6, v7}}, Ljava/lang/Math;->min(II)I
    move-result v6

    new-instance v8, Lcom/stardual/StardualHeartRow;
    invoke-direct {{v8, v0, v5, v4}}, Lcom/stardual/StardualHeartRow;-><init>(Lcom/wemu/session/secondscreen/StardewState;II)V
    const/high16 v11, -0x3e800000
    invoke-static {{v11}}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualOffsetY(F)Landroidx/compose/ui/Modifier;
    move-result-object v13
    const/4 v14, 0x0
    const/4 v15, 0x0
    move-object/from16 v16, v8
    move-object/from16 v17, v3
    const/16 v18, 0x0
    const/16 v19, 0x6
    invoke-static/range {{v13 .. v19}}, Landroidx/compose/foundation/layout/BoxWithConstraintsKt;->BoxWithConstraints(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;ZLkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V

    new-instance v8, Lcom/stardual/StardualHeartRow;
    invoke-direct {{v8, v0, v6, v7}}, Lcom/stardual/StardualHeartRow;-><init>(Lcom/wemu/session/secondscreen/StardewState;II)V
    const/high16 v11, 0x40800000
    invoke-static {{v11}}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualOffsetY(F)Landroidx/compose/ui/Modifier;
    move-result-object v13
    const/4 v14, 0x0
    const/4 v15, 0x0
    move-object/from16 v16, v8
    move-object/from16 v17, v3
    const/16 v18, 0x0
    const/16 v19, 0x6
    invoke-static/range {{v13 .. v19}}, Landroidx/compose/foundation/layout/BoxWithConstraintsKt;->BoxWithConstraints(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;ZLkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V
    return-void
.end method

.method public static stardualOffsetY(F)Landroidx/compose/ui/Modifier;
    .locals 2
    sget-object v0, Landroidx/compose/ui/Modifier;->Companion:Landroidx/compose/ui/Modifier$Companion;
    check-cast v0, Landroidx/compose/ui/Modifier;
    const/4 v1, 0x0
    invoke-static {{v0, v1, p0}}, Landroidx/compose/foundation/layout/OffsetKt;->offset-VpY3zN4(Landroidx/compose/ui/Modifier;FF)Landroidx/compose/ui/Modifier;
    move-result-object v0
    return-object v0
.end method

.method public static stardualHeartHint(Lcom/wemu/session/secondscreen/StardewVillager;Landroidx/compose/runtime/Composer;)V
    .locals 25
    # Text(StardualStatus.nextHeart(villager.npc), Modifier.offset(y = 24dp),
    #      color = SOCIAL_MUTED, fontSize = 8.5.sp, overflow = Ellipsis, maxLines = 1, style = labelSmall)
    move-object/from16 v0, p0
    invoke-virtual {{v0}}, Lcom/wemu/session/secondscreen/StardewVillager;->getNpc()Ljava/lang/String;
    move-result-object v0
    invoke-static {{v0}}, Lcom/stardual/StardualStatus;->nextHeart(Ljava/lang/String;)Ljava/lang/String;
    move-result-object v0
    const/high16 v1, 0x41c00000
    invoke-static {{v1}}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualOffsetY(F)Landroidx/compose/ui/Modifier;
    move-result-object v1
    sget-wide v2, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->SOCIAL_MUTED:J
    const v4, 0x41080000
    invoke-static {{v4}}, Landroidx/compose/ui/unit/TextUnitKt;->getSp(F)J
    move-result-wide v4
    const/4 v6, 0x0
    const/4 v7, 0x0
    const/4 v8, 0x0
    const-wide/16 v9, 0x0
    const/4 v11, 0x0
    const/4 v12, 0x0
    const-wide/16 v13, 0x0
    sget-object v15, Landroidx/compose/ui/text/style/TextOverflow;->Companion:Landroidx/compose/ui/text/style/TextOverflow$Companion;
    invoke-virtual {{v15}}, Landroidx/compose/ui/text/style/TextOverflow$Companion;->getEllipsis-gIe3tQ8()I
    move-result v15
    const/16 v16, 0x0
    const/16 v17, 0x1
    const/16 v18, 0x0
    const/16 v19, 0x0
    sget-object v20, Landroidx/compose/material3/MaterialTheme;->INSTANCE:Landroidx/compose/material3/MaterialTheme;
    move-object/from16 v21, p1
    sget v22, Landroidx/compose/material3/MaterialTheme;->$stable:I
    invoke-virtual/range {{v20 .. v22}}, Landroidx/compose/material3/MaterialTheme;->getTypography(Landroidx/compose/runtime/Composer;I)Landroidx/compose/material3/Typography;
    move-result-object v20
    invoke-virtual/range {{v20 .. v20}}, Landroidx/compose/material3/Typography;->getLabelSmall()Landroidx/compose/ui/text/TextStyle;
    move-result-object v20
    const/16 v22, 0x0
    const/16 v23, 0x0
    # Defaults: fontStyle, fontWeight, fontFamily, letterSpacing, textDecoration, textAlign,
    # lineHeight, softWrap, minLines, onTextLayout. (fontSize is 8.5sp.)
    const v24, 0xd7f0
    invoke-static/range {{v0 .. v24}}, {TEXT_SIG}
    return-void
.end method

.method public static stardualRelationshipLine(Lcom/wemu/session/secondscreen/StardewVillager;Landroidx/compose/runtime/Composer;)V
    .locals 25
    # Text(StardualStatus.get(villager.npc), color = SOCIAL_MUTED, overflow = Ellipsis,
    #      maxLines = 1, style = labelSmall), styled like the birthday line above it.
    move-object/from16 v0, p0
    invoke-virtual {{v0}}, Lcom/wemu/session/secondscreen/StardewVillager;->getNpc()Ljava/lang/String;
    move-result-object v0
    invoke-static {{v0}}, Lcom/stardual/StardualStatus;->get(Ljava/lang/String;)Ljava/lang/String;
    move-result-object v0
    const/4 v1, 0x0
    sget-wide v2, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->SOCIAL_MUTED:J
    const-wide/16 v4, 0x0
    const/4 v6, 0x0
    const/4 v7, 0x0
    const/4 v8, 0x0
    const-wide/16 v9, 0x0
    const/4 v11, 0x0
    const/4 v12, 0x0
    const-wide/16 v13, 0x0
    sget-object v15, Landroidx/compose/ui/text/style/TextOverflow;->Companion:Landroidx/compose/ui/text/style/TextOverflow$Companion;
    invoke-virtual {{v15}}, Landroidx/compose/ui/text/style/TextOverflow$Companion;->getEllipsis-gIe3tQ8()I
    move-result v15
    const/16 v16, 0x0
    const/16 v17, 0x1
    const/16 v18, 0x0
    const/16 v19, 0x0
    sget-object v20, Landroidx/compose/material3/MaterialTheme;->INSTANCE:Landroidx/compose/material3/MaterialTheme;
    move-object/from16 v21, p1
    sget v22, Landroidx/compose/material3/MaterialTheme;->$stable:I
    invoke-virtual/range {{v20 .. v22}}, Landroidx/compose/material3/MaterialTheme;->getTypography(Landroidx/compose/runtime/Composer;I)Landroidx/compose/material3/Typography;
    move-result-object v20
    invoke-virtual/range {{v20 .. v20}}, Landroidx/compose/material3/Typography;->getLabelSmall()Landroidx/compose/ui/text/TextStyle;
    move-result-object v20
    const/16 v22, 0x0
    const/16 v23, 0x0
    # Defaults: modifier, fontSize, fontStyle, fontWeight, fontFamily, letterSpacing,
    # textDecoration, textAlign, lineHeight, softWrap, minLines, onTextLayout.
    const v24, 0xd7fa
    invoke-static/range {{v0 .. v24}}, {TEXT_SIG}
    return-void
.end method
""")

# A picture above each checkbox saying what it's for: a speech bubble over "talked today" and
# a wrapped gift over each of the week's two gifts (com.stardual.StardualSlotIcon). It floats
# 22dp above the checkbox's center, in the space above it.
s, e = method_span(lines, "RelationshipStatusSlot", must="(Lcom/wemu/session/secondscreen/StardewState;ZLjava/lang/String;")
spr = [i for i in range(s, e) if "invoke-static/range {v30 .. v35}" in lines[i] and "->SocialSprite-uFdPcIQ(" in lines[i]]
assert len(spr) == 1, f"checkbox sprite: found {len(spr)}"
assert any("invoke-virtual {v0}, Lcom/wemu/session/secondscreen/StardewState;->getSprites()" in lines[i] for i in range(spr[0] - 120, spr[0])), "slot state register moved"
assert any("invoke-virtual {v5, v2}, Ljava/lang/StringBuilder;->append(Ljava/lang/String;)" in lines[i] for i in range(spr[0] - 60, spr[0])), "slot label register moved"
size = [i for i in range(s, spr[0]) if "SizeKt;->size-3ABfNKs(Landroidx/compose/ui/Modifier;F)" in lines[i]]
assert size and "{v7, v8}" in lines[size[0]] and "move-result-object v7" in lines[size[0] + 2], "checkbox size moved"
lines.insert(size[0] + 3, "\n    # Stardual: down 7dp, so the checkbox and the picture above it sit centered together\n"
             "    const/high16 v8, 0x40e00000\n"
             "    invoke-static {v8}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualOffsetY(F)Landroidx/compose/ui/Modifier;\n"
             "    move-result-object v8\n"
             "    invoke-interface {v8, v7}, Landroidx/compose/ui/Modifier;->then(Landroidx/compose/ui/Modifier;)Landroidx/compose/ui/Modifier;\n"
             "    move-result-object v7")
spr = [i for i in range(s, e + 8) if "invoke-static/range {v30 .. v35}" in lines[i] and "->SocialSprite-uFdPcIQ(" in lines[i]]
lines.insert(spr[0] + 1, "\n    # Stardual: what the checkbox is for, above it\n"
             "    move-object/from16 v30, v0\n    move-object/from16 v31, v2\n    move-object/from16 v32, v33\n"
             "    invoke-static/range {v30 .. v32}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;"
             "->stardualSlotIcon(Lcom/wemu/session/secondscreen/StardewState;Ljava/lang/String;Landroidx/compose/runtime/Composer;)V")
lines.append("""
.method public static stardualSlotIcon(Lcom/wemu/session/secondscreen/StardewState;Ljava/lang/String;Landroidx/compose/runtime/Composer;)V
    .locals 8
    new-instance v3, Lcom/stardual/StardualSlotIcon;
    invoke-direct {v3, p0, p1}, Lcom/stardual/StardualSlotIcon;-><init>(Lcom/wemu/session/secondscreen/StardewState;Ljava/lang/String;)V
    const/high16 v0, -0x3e500000
    invoke-static {v0}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->stardualOffsetY(F)Landroidx/compose/ui/Modifier;
    move-result-object v0
    const/4 v1, 0x0
    const/4 v2, 0x0
    move-object/from16 v4, p2
    const/4 v5, 0x0
    const/4 v6, 0x6
    invoke-static/range {v0 .. v6}, Landroidx/compose/foundation/layout/BoxWithConstraintsKt;->BoxWithConstraints(Landroidx/compose/ui/Modifier;Landroidx/compose/ui/Alignment;ZLkotlin/jvm/functions/Function3;Landroidx/compose/runtime/Composer;II)V
    return-void
.end method

.method public static stardualSlotSprite(Ljava/lang/Object;Landroidx/compose/runtime/Composer;)V
    .locals 6
    # SocialSprite(sprite, 14dp, "", composer)
    move-object/from16 v0, p0
    check-cast v0, Lcom/wemu/session/secondscreen/UiSprite;
    const/high16 v1, 0x41600000
    const-string v2, ""
    move-object/from16 v3, p1
    const/4 v4, 0x0
    const/4 v5, 0x0
    invoke-static/range {v0 .. v5}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;->SocialSprite-uFdPcIQ(Lcom/wemu/session/secondscreen/UiSprite;FLjava/lang/String;Landroidx/compose/runtime/Composer;II)V
    return-void
.end method
""")

# Tapping a villager's row opens Lookup Anything's info panel for them on the game screen
# (the panel its controller button shows). The row's Modifier.fillMaxWidth().height(..)
# gets .clickable { send lookup_npc }; the mod does the rest.
s, e = method_span(lines, "RelationshipRow")
h = [i for i in range(s, e) if "SizeKt;->height-3ABfNKs(" in lines[i]]
assert h and "{v3, v10}" in lines[h[0]] and "move-result-object v3" in lines[h[0] + 2], "row modifier moved"
lines.insert(h[0] + 3, "\n    # Stardual: tap the row to look the villager up in the game\n"
             "    invoke-static {v3, v1}, Lcom/wemu/session/secondscreen/StardewReferenceSocialKt;"
             "->stardualTappable(Landroidx/compose/ui/Modifier;Lcom/wemu/session/secondscreen/StardewVillager;)"
             "Landroidx/compose/ui/Modifier;\n    move-result-object v3")
lines.append("""
.method public static stardualTappable(Landroidx/compose/ui/Modifier;Lcom/wemu/session/secondscreen/StardewVillager;)Landroidx/compose/ui/Modifier;
    .locals 7
    # modifier.clickable(onClick = StardualLookup(villager.npc))
    invoke-virtual {p1}, Lcom/wemu/session/secondscreen/StardewVillager;->getNpc()Ljava/lang/String;
    move-result-object v0
    new-instance v4, Lcom/stardual/StardualLookup;
    invoke-direct {v4, v0}, Lcom/stardual/StardualLookup;-><init>(Ljava/lang/String;)V
    move-object v0, p0
    const/4 v1, 0x0
    const/4 v2, 0x0
    const/4 v3, 0x0
    # Defaults: enabled, onClickLabel, role.
    const/4 v5, 0x7
    const/4 v6, 0x0
    invoke-static/range {v0 .. v6}, Landroidx/compose/foundation/ClickableKt;->clickable-XHw0xAI$default(Landroidx/compose/ui/Modifier;ZLjava/lang/String;Landroidx/compose/ui/semantics/Role;Lkotlin/jvm/functions/Function0;ILjava/lang/Object;)Landroidx/compose/ui/Modifier;
    move-result-object v0
    return-object v0
.end method
""")
write(path, lines)

# 6. Tab order: Backpack, Map, Relationships, Gift Guide, Fields, Crafting, Journal,
#    Machines, Bundles, Calendar, Settings. (Progress, hidden by default, keeps a place
#    before Settings.) TODAY is the page titled "Backpack" (removed by 8b).
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen")
PAGE = "Lcom/wemu/session/secondscreen/StardewPage;->"
old_order = ["TODAY", "CRAFTING", "PROGRESS", "MAP", "JOURNAL", "RELATIONSHIPS", "GIFT_GUIDE",
             "FIELDS", "MACHINES", "BUNDLES", "CALENDAR", "SETTINGS"]
new_order = ["TODAY", "MAP", "RELATIONSHIPS", "GIFT_GUIDE", "FIELDS", "CRAFTING", "JOURNAL",
             "MACHINES", "BUNDLES", "CALENDAR", "PROGRESS", "SETTINGS"]
gets = [i for i in range(s, e) if "sget-object" in lines[i] and PAGE in lines[i]
        and lines[i + 2].strip().startswith("invoke-interface") and "List;->add(" in lines[i + 2]]
names = [re.search(r"->(\w+):", lines[i]).group(1) for i in gets]
assert names == old_order, f"tab list changed upstream: {names}"
for i, name in zip(gets, new_order):
    lines[i] = re.sub(r"->\w+:", f"->{name}:", lines[i])
# Fields gets its own tab icon (a parsnip, sent by the mod as "ui:tab_fields") instead of
# sharing the season picture with Calendar.
s, e = method_span(lines, "pageIcon")
assert lines[s + 1].strip() == ".locals 3"
lines.insert(s + 3, """
    sget-object v0, Lcom/wemu/session/secondscreen/StardewPage;->FIELDS:Lcom/wemu/session/secondscreen/StardewPage;
    if-ne p0, v0, :stardual_not_fields
    const-string v0, "ui:tab_fields"
    return-object v0
    :stardual_not_fields
""")
write(path, lines)

# 7. Fields: no date / weather / time row at the bottom (or the line above it), like the
#    other pages, so the crop list runs to the bottom of the screen.
path = find("StardewReferenceFieldsKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewFieldsReferencePage")
foot = [i for i in range(s, e) if "->FieldsDateFooter(" in lines[i]]
assert len(foot) == 1, f"fields footer: found {len(foot)}"
rule = max(i for i in range(s, foot[0]) if "->FieldsRule(" in lines[i])
assert foot[0] - rule < 12, "no divider right above the fields footer"
lines[foot[0]] = "    # Stardual: removed the date row"
lines[rule] = "    # Stardual: removed the line above it"
write(path, lines)
# The page's own header, location headers and empty state use the parsnip too.
for name in ("FieldsPageHeader", "FieldsLocationHeader", "FieldsEmptyState"):
    s, e = method_span(lines, name)
    hits = [i for i in range(s, e) if 'const-string' in lines[i] and '"ui:season"' in lines[i]]
    assert len(hits) == 1, f"{name}: found {len(hits)} season icons"
    lines[hits[0]] = lines[hits[0]].replace('"ui:season"', '"ui:tab_fields"')
write(path, lines)

# 8. Overview: a new first page with things to do today and ideas for when you're bored.
#    The page is com.stardual.StardualOverview (overlay/src), which shows
#    assets/stardual/overview.html. The mod sends its lists as an "sdv_overview" message.
OVERVIEW = "Lcom/stardual/StardualOverview;->"
PAGE_T = "Lcom/wemu/session/secondscreen/StardewPage;"

# 8a. A new OVERVIEW entry at the end of the page enum (ordinal 12, so the upstream
#     pages keep their numbers), titled "Overview".
path = find("StardewPage")
lines = open(path).read().split("\n")
field = next(i for i, l in enumerate(lines) if l.startswith(".field public static final enum TODAY:"))
lines.insert(field, f".field public static final enum OVERVIEW:{PAGE_T}")
s, e = method_span(lines, "$values")
assert lines[s + 1].strip() == ".locals 12", "page list changed upstream"
lines[s + 1] = "    .locals 13"
arr = next(i for i in range(s, e) if "filled-new-array/range {v0 .. v11}" in lines[i])
lines[arr] = lines[arr].replace("{v0 .. v11}", "{v0 .. v12}")
lines.insert(arr, f"    sget-object v12, {PAGE_T}->OVERVIEW:{PAGE_T}")
s, e = method_span(lines, "<clinit>")
call = next(i for i in range(s, e) if "->$values()" in lines[i])
lines.insert(call, f"""    new-instance v0, {PAGE_T}
    const-string v1, "OVERVIEW"
    const/16 v2, 0xc
    const-string v3, "Overview"
    const/4 v4, 0x0
    const/4 v5, 0x0
    invoke-direct/range {{v0 .. v5}}, {PAGE_T}-><init>(Ljava/lang/String;ILjava/lang/String;ZZ)V
    sput-object v0, {PAGE_T}->OVERVIEW:{PAGE_T}
""")
write(path, lines)

# 8b. It's the first tab, in Backpack's place: the Backpack page is gone.
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen")
first = [i for i in range(s, e) if "sget-object" in lines[i] and f"{PAGE_T}->TODAY:" in lines[i]]
assert len(first) == 1, f"Backpack tab: found {len(first)}"
lines[first[0]] = lines[first[0]].replace("->TODAY:", "->OVERVIEW:")

# 8c. Its tab icon is a stardrop, sent by the mod as "ui:tab_overview".
s, e = method_span(lines, "pageIcon")
lines.insert(s + 3, f"""
    sget-object v0, {PAGE_T}->OVERVIEW:{PAGE_T}
    if-ne p0, v0, :stardual_not_overview
    const-string v0, "ui:tab_overview"
    return-object v0
    :stardual_not_overview
""")

# 8d. The line under its name says how much is left to do. Upstream's page switch throws
#     for a page it doesn't know; that branch now returns the Overview's status instead.
s, e = method_span(lines, "pageStatus")
sw = next(i for i in range(s, e) if lines[i].strip().startswith("packed-switch v0, :pswitch_data_0"))
code = [i for i in range(sw + 1, sw + 12) if lines[i].strip() and not lines[i].strip().startswith(".line")][:3]
assert lines[code[0]].strip().startswith("new-instance v0, Lkotlin/NoWhenBranchMatchedException;"), lines[code[0]]
assert lines[code[1]].strip().startswith("invoke-direct {v0}, Lkotlin/NoWhenBranchMatchedException;")
assert lines[code[2]].strip() == "throw v0"
throw = code[0]
ret = max(i for i in range(s, e) if lines[i].strip() == "return-object v10")
label = next(lines[i].strip() for i in range(ret - 1, ret - 4, -1) if lines[i].strip().startswith(":goto_"))
lines[throw:code[2] + 1] = [f"""    new-instance v10, Lcom/wemu/session/secondscreen/StardewPageStatus;
    invoke-static {{}}, {OVERVIEW}status()Ljava/lang/String;
    move-result-object v11
    const/4 v12, 0x0
    const/4 v13, 0x0
    const/4 v14, 0x6
    const/4 v15, 0x0
    invoke-direct/range {{v10 .. v15}}, Lcom/wemu/session/secondscreen/StardewPageStatus;-><init>(Ljava/lang/String;ZZILkotlin/jvm/internal/DefaultConstructorMarker;)V
    goto/16 {label}"""]
write(path, lines)

# 8e. The page itself. Same idea: the page switch's "unknown page" branch draws the Overview.
path = find("StardewScreenKt$StardewScreen$8$1$1$1")
lines = open(path).read().split("\n")
s, e = method_span(lines, "invoke", must="Landroidx/compose/runtime/Composer;I)V")
sw = [i for i in range(s, e) if lines[i].strip() == "packed-switch v0, :pswitch_data_0"]
assert len(sw) == 1, f"page switch: found {len(sw)}"
block = [l.strip() for l in lines[sw[0] + 1:sw[0] + 14] if l.strip() and not l.strip().startswith(".line")]
assert block[1] == "invoke-interface {v3, v0}, Landroidx/compose/runtime/Composer;->startReplaceGroup(I)V", block
assert block[2] == "invoke-interface {v3}, Landroidx/compose/runtime/Composer;->endReplaceGroup()V", block
assert block[3].startswith("new-instance v0, Lkotlin/NoWhenBranchMatchedException;") and block[5] == "throw v0", block
backpack = next(i for i in range(s, e) if "->StardewBackpackReferencePage(" in lines[i])
done = next(i for i in range(backpack, backpack + 12) if re.match(r"\s*:goto_\w+$", lines[i]))
end_label = lines[done].strip()
end_group = next(i for i in range(sw[0], sw[0] + 14) if "->endReplaceGroup()V" in lines[i])
throw = next(i for i in range(end_group, end_group + 8) if lines[i].strip() == "throw v0")
lines[end_group:throw + 1] = [f"""    invoke-static {{v3}}, {OVERVIEW}page(Landroidx/compose/runtime/Composer;)V
    invoke-interface {{v3}}, Landroidx/compose/runtime/Composer;->endReplaceGroup()V
    sget-object v0, Lkotlin/Unit;->INSTANCE:Lkotlin/Unit;
    goto/16 {end_label}"""]
write(path, lines)

# 8f. Every message from the mod passes through StardualOverview first, which keeps the
#     overview one (the app has no parser for it and ignores it).
path = find("ProtocolKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "parseGameMessage", must="(Ljava/lang/String;)Lcom/wemu/session/secondscreen/GameMessage;")
first = next(i for i in range(s + 1, e) if lines[i].strip() and not lines[i].strip().startswith((".", "#", ":")))
lines.insert(first, f"    invoke-static/range {{p0 .. p0}}, {OVERVIEW}onMessage(Ljava/lang/String;)V")
write(path, lines)

# 8g. The app opens on the Overview instead of Backpack.
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen$lambda$3$lambda$2")
hits = [i for i in range(s, e) if lines[i].strip() == 'const-string v2, "TODAY"']
assert len(hits) == 1, f"starting page: found {len(hits)}"
lines[hits[0]] = lines[hits[0]].replace('"TODAY"', '"OVERVIEW"')
write(path, lines)

# 8h. Settings never offers to hide its "essential" pages (upstream: Backpack and Map).
#     Overview joins them, and Backpack stays in the set so it doesn't show up as a
#     toggle there now that it has no tab.
path = find("StardewSettingsKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "<clinit>")
size = [i for i in range(s, e) if lines[i].strip() == "const/4 v0, 0x2"]
assert len(size) == 1 and "new-array v0, v0, [Ljava/lang/String;" in "\n".join(lines[size[0]:size[0] + 3]), "essential pages changed upstream"
lines[size[0]] = "    const/4 v0, 0x3"
call = next(i for i in range(s, e) if "SetsKt;->setOf(" in lines[i])
lines.insert(call, """    const/4 v1, 0x2
    const-string v2, "OVERVIEW"
    aput-object v2, v0, v1
""")
write(path, lines)

# 8i. Settings' list of pages to show or hide leaves Backpack out (it has no tab), and the
#     "Backpack + map" preset (hide everything but the essential pages) is "Overview + map".
s, e = method_span(lines, "StardewSettingsPage")
skip = [i for i in range(s, e) if lines[i].strip() == f"sget-object v14, {PAGE_T}->SETTINGS:{PAGE_T}"]
assert len(skip) == 1, f"settings page list: found {len(skip)}"
branch = next(i for i in range(skip[0] + 1, skip[0] + 8) if lines[i].strip() and not lines[i].strip().startswith("."))
assert lines[branch].strip().startswith("if-eq v13, v14, :cond_"), "settings page list changed upstream"
target = lines[branch].split(", ")[-1]
lines.insert(branch + 1, f"    sget-object v14, {PAGE_T}->TODAY:{PAGE_T}\n    if-eq v13, v14, {target}")
write(path, lines)
path = find("StardewSettingsKt$StardewSettingsPage$1$3")
lines = open(path).read().split("\n")
hits = [i for i, l in enumerate(lines) if '"Backpack + map"' in l]
assert len(hits) == 1, f"Backpack + map button: found {len(hits)}"
lines[hits[0]] = lines[hits[0]].replace('"Backpack + map"', '"Overview + map"')
write(path, lines)

# 9. Bundles: the page is com.stardual.StardualBundles (overlay/src), which shows
#    assets/stardual/bundles.html: this season's bundles first, backpack items picked out,
#    the rest under "Upcoming". The mod sends it as an "sdv_bundles_page" message, which
#    reaches the page through the hook in 8f. The tab's status line still comes from the
#    app's own bundles data.
path = find("StardewScreenKt$StardewScreen$8$1$1$1")
lines = open(path).read().split("\n")
hits = [i for i, l in enumerate(lines)
        if "->StardewBundlesReferencePage(Lcom/wemu/session/secondscreen/StardewState;Landroidx/compose/ui/Modifier;Landroidx/compose/runtime/Composer;II)V" in l]
assert len(hits) == 1, f"bundles page call: found {len(hits)}"
assert lines[hits[0]].strip().startswith("invoke-static {v0, v2, v3, v1, v4}"), lines[hits[0]]
lines[hits[0]] = "    invoke-static {v3}, Lcom/stardual/StardualBundles;->page(Landroidx/compose/runtime/Composer;)V"
write(path, lines)

# 10. Tapping a row on the Overview opens its page (Fields, Gift Guide, Bundles...). The app
#     keeps the showing page's name in a saved state; hand that state to StardualOverview
#     as soon as the screen creates it.
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen", must="(Landroidx/compose/ui/Modifier;Landroidx/compose/runtime/Composer;II)V")
hits = [i for i in range(s, e) if lines[i].strip() == '.local v9, "chosen$delegate":Landroidx/compose/runtime/MutableState;']
assert len(hits) == 1, f"page state: found {len(hits)}"
lines.insert(hits[0] + 1, "    invoke-static {v9}, Lcom/stardual/StardualOverview;->rememberPage(Landroidx/compose/runtime/MutableState;)V")
write(path, lines)

# 11. Shortcuts: a new second tab (after Overview) with big buttons and a search box. The page
#     is com.stardual.StardualShortcuts (overlay/src), which shows assets/stardual/shortcuts.html.
#     It gets its own page entry (ordinal 13), and the "unknown page" branches added in 8d and
#     8e now pass the page along so StardualOverview can tell its pages apart.

# 11a. A SHORTCUTS entry at the end of the page enum, titled "Shortcuts".
path = find("StardewPage")
lines = open(path).read().split("\n")
field = next(i for i, l in enumerate(lines) if l.startswith(f".field public static final enum OVERVIEW:"))
lines.insert(field + 1, f".field public static final enum SHORTCUTS:{PAGE_T}")
s, e = method_span(lines, "$values")
assert lines[s + 1].strip() == ".locals 13", "page list changed (8a)"
lines[s + 1] = "    .locals 14"
arr = next(i for i in range(s, e) if "filled-new-array/range {v0 .. v12}" in lines[i])
lines[arr] = lines[arr].replace("{v0 .. v12}", "{v0 .. v13}")
lines.insert(arr, f"    sget-object v13, {PAGE_T}->SHORTCUTS:{PAGE_T}")
s, e = method_span(lines, "<clinit>")
call = next(i for i in range(s, e) if "->$values()" in lines[i])
lines.insert(call, f"""    new-instance v0, {PAGE_T}
    const-string v1, "SHORTCUTS"
    const/16 v2, 0xd
    const-string v3, "Shortcuts"
    const/4 v4, 0x0
    const/4 v5, 0x0
    invoke-direct/range {{v0 .. v5}}, {PAGE_T}-><init>(Ljava/lang/String;ILjava/lang/String;ZZ)V
    sput-object v0, {PAGE_T}->SHORTCUTS:{PAGE_T}
""")
write(path, lines)

# 11b. The tab sits right after Overview, with a warp totem icon ("ui:tab_shortcuts").
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen", must="(Landroidx/compose/ui/Modifier;Landroidx/compose/runtime/Composer;II)V")
first = [i for i in range(s, e) if "sget-object" in lines[i] and f"{PAGE_T}->OVERVIEW:" in lines[i]]
assert len(first) == 1, f"Overview tab: found {len(first)}"
add = next(i for i in range(first[0] + 1, first[0] + 4) if "Ljava/util/List;->add(" in lines[i])
reg = lines[first[0]].split()[1].rstrip(",")
lines.insert(add + 1, f"    sget-object {reg}, {PAGE_T}->SHORTCUTS:{PAGE_T}\n{lines[add]}")
s, e = method_span(lines, "pageIcon")
lines.insert(s + 3, f"""
    sget-object v0, {PAGE_T}->SHORTCUTS:{PAGE_T}
    if-ne p0, v0, :stardual_not_shortcuts
    const-string v0, "ui:tab_shortcuts"
    return-object v0
    :stardual_not_shortcuts
""")
# 11c. Each Stardual tab gets its own status line.
s, e = method_span(lines, "pageStatus")
hits = [i for i in range(s, e) if f"invoke-static {{}}, {OVERVIEW}status()Ljava/lang/String;" in lines[i]]
assert len(hits) == 1, f"overview status call: found {len(hits)}"
lines[hits[0]] = f"    move-object/from16 v11, p0\n    invoke-static {{v11}}, {OVERVIEW}status(Ljava/lang/Object;)Ljava/lang/String;"
write(path, lines)

# 11d. The page switch's unknown branch hands over the page it's drawing.
path = find("StardewScreenKt$StardewScreen$8$1$1$1")
lines = open(path).read().split("\n")
hits = [i for i, l in enumerate(lines) if l.strip() == f"invoke-static {{v3}}, {OVERVIEW}page(Landroidx/compose/runtime/Composer;)V"]
assert len(hits) == 1, f"overview page call: found {len(hits)}"
get = [i for i in range(hits[0] - 40, hits[0]) if lines[i].strip() == "invoke-interface {v10, v11}, Ljava/util/List;->get(I)Ljava/lang/Object;"]
assert len(get) == 1, "page switch no longer reads the page from the list"
lines[hits[0]] = f"""    invoke-interface {{v10, v11}}, Ljava/util/List;->get(I)Ljava/lang/Object;
    move-result-object v0
    invoke-static {{v0, v3}}, {OVERVIEW}page(Ljava/lang/Object;Landroidx/compose/runtime/Composer;)V"""
write(path, lines)

# 12. Tab icons and sizes. Every tab uses an unframed game-style icon the mod sends as
#     "ui:page_<PAGE>" (the Overview's is your farmhouse) instead of the framed menu-tab
#     pictures. Overview and Shortcuts, like Settings, show only their icon, at half a tab's width.
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "pageIcon")
lines.insert(s + 3, f"""    invoke-static {{p0}}, {OVERVIEW}pageIcon(Ljava/lang/Object;)Ljava/lang/String;
    move-result-object v0
    return-object v0
""")
s, e = method_span(lines, "PageButton", must="(Lcom/wemu/session/secondscreen/StardewState;")
def next_code(i):
    return next(j for j in range(i + 1, i + 6) if lines[j].strip())
checks = [i for i in range(s, e) if lines[i].strip() == f"sget-object v1, {PAGE_T}->SETTINGS:{PAGE_T}"
          and lines[next_code(i)].strip().startswith("if-eq v2, v1, :cond_")]
assert len(checks) == 2, f"PageButton settings checks: found {len(checks)}"
for i in reversed(checks):
    j = next_code(i)
    target = lines[j].strip().split(", ")[-1]
    lines.insert(j + 1, f"    sget-object v1, {PAGE_T}->OVERVIEW:{PAGE_T}\n    if-eq v2, v1, {target}\n"
                        f"    sget-object v1, {PAGE_T}->SHORTCUTS:{PAGE_T}\n    if-eq v2, v1, {target}")
write(path, lines)
path = find("StardewScreenKt$PageBar$2$2$invoke$lambda$4$lambda$3$$inlined$itemsIndexed$default$3")
lines = open(path).read().split("\n")
hits = [i for i, l in enumerate(lines) if "->$buttonWidth$inlined:F" in l and l.strip().startswith("iget v15,")]
assert len(hits) == 1, f"tab width: found {len(hits)}"
assert any(lines[j].strip() == ".local v10, \"item\":Lcom/wemu/session/secondscreen/StardewPage;" for j in range(hits[0] - 80, hits[0])), "tab page register moved"
lines.insert(hits[0] + 1, f"    invoke-static {{v10, v15}}, {OVERVIEW}tabWidth(Ljava/lang/Object;F)F\n    move-result v15")
write(path, lines)

# 13. Names and a shorter tab bar: Relationships is "People" and Gift Guide is "Gifts", and the
#     tab bar is 38 dp tall instead of 52, with 32 dp buttons instead of 44 (icons stay 20 dp).
path = find("StardewPage")
lines = open(path).read().split("\n")
for old, new in (('"Relationships"', '"People"'), ('"Gift Guide"', '"Gifts"')):
    hits = [i for i, l in enumerate(lines) if l.strip().startswith("const-string") and l.strip().endswith(old)]
    assert len(hits) == 1, f"page title {old}: found {len(hits)}"
    lines[hits[0]] = lines[hits[0]].replace(old, new)
write(path, lines)
path = find("StardewScreenKt")
lines = open(path).read().split("\n")
def shrink(method, must, reg, old, new):
    s, e = method_span(lines, method, must=must)
    hits = [i for i in range(s, e) if lines[i].strip() == f"const/16 {reg}, {old}"
            and any("SizeKt;->height-3ABfNKs" in lines[j] for j in range(i, min(e, i + 30)))]
    assert len(hits) == 1, f"{method} height {old}: found {len(hits)}"
    lines[hits[0]] = lines[hits[0]].replace(old, new)
shrink("PageBar", "(Lcom/wemu/session/secondscreen/StardewState;Ljava/util/List;", "v10", "0x34", "0x26")
shrink("PageButton", "(Lcom/wemu/session/secondscreen/StardewState;", "v12", "0x2c", "0x20")
write(path, lines)

# 14. Gifts page.
#     - No "All / In backpack" filter buttons: the list always shows everyone (villagers with
#       a loved gift in the backpack still come first).
#     - Under the selected villager's name: where they are now and where they go today
#       (com.stardual.StardualGifts, fed by the mod's "sdv_schedules" message).
path = find("StardewReferenceGiftGuideKt")
lines = open(path).read().split("\n")
drop_call(lines, method_span(lines, "StardewGiftGuideReferencePage"), "GiftFilterRail")
s, e = method_span(lines, "StardewGiftGuideReferencePage$lambda$3")
assert "$readyOnly$delegate" in "\n".join(lines[s:e]), "filter state getter moved"
lines[s + 1:e] = ["    .locals 1", "    const/4 v0, 0x0", "    return v0"]
s, e = method_span(lines, "GiftVillagerDetail", must="(Lcom/wemu/session/secondscreen/StardewState;")
assert lines[s + 11].strip() == "move-object/from16 v2, p1", "villager entry register moved"
assert not any(re.match(r"\s*[a-z][\w/-]* v2,", lines[i]) for i in range(s + 12, e)), "v2 reused in GiftVillagerDetail"
rules = [i for i in range(s, e) if "->GiftRule(" in lines[i]]
assert len(rules) == 2 and "invoke-static {v8, v5, v8, v0}" in lines[rules[0]], "divider under the villager header moved"
lines.insert(rules[0] + 1, "    invoke-static {v2, v5}, Lcom/stardual/StardualGifts;->schedule(Ljava/lang/Object;Landroidx/compose/runtime/Composer;)V")
write(path, lines)

# 15. Gifts page: loved gifts in a chest or the fridge count too (the mod marks them as
#     carried). Each card's tag says where the gift is ("BACKPACK", "CHEST" or "FRIDGE"),
#     and the list and header say "in backpack", "in storage" or "in bag & chest".
#     com.stardual.StardualGifts works these out from the mod's "sdv_gift_places" message.
path = find("StardewReferenceGiftGuideKt")
lines = open(path).read().split("\n")
GIFTS = "Lcom/stardual/StardualGifts;->"
s, e = method_span(lines, "GiftItemCard", must="(Lcom/wemu/session/secondscreen/StardewState;")
hits = [i for i in range(s, e) if lines[i].strip() == 'const-string v19, "IN BACKPACK"']
assert len(hits) == 1, f"card tag: found {len(hits)}"
assert any("invoke-virtual/range {p1 .. p1}, Lcom/wemu/session/secondscreen/StardewBundleItem;->getId()" in lines[i] for i in range(s, hits[0])), "card item register moved"
lines[hits[0]] = ("    invoke-virtual/range {p1 .. p1}, Lcom/wemu/session/secondscreen/StardewBundleItem;->getId()Ljava/lang/String;\n"
                  "    move-result-object v19\n"
                  f"    invoke-static/range {{v19 .. v19}}, {GIFTS}tag(Ljava/lang/String;)Ljava/lang/String;\n"
                  "    move-result-object v19")
s, e = method_span(lines, "GiftVillagerDetail", must="(Lcom/wemu/session/secondscreen/StardewState;")
hits = [i for i in range(s, e) if lines[i].strip() == 'const-string v3, " in backpack"']
assert len(hits) == 1, f"detail header wording: found {len(hits)}"
lines[hits[0]] = f"    invoke-static {{v2}}, {GIFTS}where(Ljava/lang/Object;)Ljava/lang/String;\n    move-result-object v3"
s, e = method_span(lines, "giftVillagerRailDetail")
hits = [i for i in range(s, e) if lines[i].strip() == 'const-string v1, " in backpack"']
assert len(hits) == 1, f"list wording: found {len(hits)}"
assert not any(re.match(r"\s*[a-z][\w/-]* p0,", lines[i]) for i in range(s, e)), "p0 reused in giftVillagerRailDetail"
lines[hits[0]] = f"    invoke-static/range {{p0 .. p0}}, {GIFTS}where(Ljava/lang/Object;)Ljava/lang/String;\n    move-result-object v1"
write(path, lines)

# 16. Gifts page: tapping a birthday on the Overview opens Gifts on that villager. The page's
#     reads of its "selected villager" state go through StardualGifts.selected, which hands
#     back (and stores) a villager picked from the Overview.
path = find("StardewReferenceGiftGuideKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewGiftGuideReferencePage$lambda$7")
assert "$selectedName$delegate" in "\n".join(lines[s:e]), "selected villager getter moved"
lines[s + 1:e] = ["    .locals 1",
                  "    invoke-static {p0}, Lcom/stardual/StardualGifts;->selected(Landroidx/compose/runtime/MutableState;)Ljava/lang/String;",
                  "    move-result-object v0",
                  "    return-object v0"]
write(path, lines)

# 17. People page: a sort bar where the header was (Name, Hearts, Single), from
#     com.stardual.StardualPeople, which also hands back the villagers in the chosen order.
path = find("StardewReferenceSocialKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewRelationshipsReferencePage")
hit = [i for i in range(s, e) if lines[i].strip() == "# Stardual: removed the line under the header"]
assert len(hit) == 1, f"people header spot: found {len(hit)}"
nxt = next(i for i in range(hit[0] + 1, e) if lines[i].strip().startswith("invoke"))
assert lines[nxt].strip() == "invoke-interface {v12}, Ljava/util/List;->isEmpty()Z", "villager list register moved"
assert any("->sourceInformationMarkerStart(" in lines[i] and "{v15," in lines[i] for i in range(hit[0] - 30, hit[0])), "page composer register moved"
lines.insert(hit[0] + 1, "    # Stardual: sort bar, and the villagers in its order\n"
             "    invoke-static {v12, v15}, Lcom/stardual/StardualPeople;->bar(Ljava/util/List;Landroidx/compose/runtime/Composer;)Ljava/util/List;\n"
             "    move-result-object v12")
write(path, lines)

# 18. Pokémon: a new tab after Gifts, for the Pokemon Ranch mod. The page is
#     com.stardual.StardualPokemon (overlay/src), which shows assets/stardual/pokemon.html; the
#     mod sends it as "sdv_pokemon". Like Shortcuts (11), it's a new page entry (ordinal 14)
#     drawn by StardualOverview.page, with its icon sent as "ui:page_POKEMON".
path = find("StardewPage")
lines = open(path).read().split("\n")
field = next(i for i, l in enumerate(lines) if l.startswith(".field public static final enum SHORTCUTS:"))
lines.insert(field + 1, f".field public static final enum POKEMON:{PAGE_T}")
s, e = method_span(lines, "$values")
assert lines[s + 1].strip() == ".locals 14", "page list changed (11a)"
lines[s + 1] = "    .locals 15"
arr = next(i for i in range(s, e) if "filled-new-array/range {v0 .. v13}" in lines[i])
lines[arr] = lines[arr].replace("{v0 .. v13}", "{v0 .. v14}")
lines.insert(arr, f"    sget-object v14, {PAGE_T}->POKEMON:{PAGE_T}")
s, e = method_span(lines, "<clinit>")
call = next(i for i in range(s, e) if "->$values()" in lines[i])
lines.insert(call, f"""    new-instance v0, {PAGE_T}
    const-string v1, "POKEMON"
    const/16 v2, 0xe
    const-string v3, "Pok\\u00e9mon"
    const/4 v4, 0x0
    const/4 v5, 0x0
    invoke-direct/range {{v0 .. v5}}, {PAGE_T}-><init>(Ljava/lang/String;ILjava/lang/String;ZZ)V
    sput-object v0, {PAGE_T}->POKEMON:{PAGE_T}
""")
write(path, lines)

path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen", must="(Landroidx/compose/ui/Modifier;Landroidx/compose/runtime/Composer;II)V")
gift = [i for i in range(s, e) if "sget-object" in lines[i] and f"{PAGE_T}->GIFT_GUIDE:" in lines[i]
        and any("Ljava/util/List;->add(" in lines[j] for j in range(i + 1, i + 4))]
assert len(gift) == 1, f"Gifts tab: found {len(gift)}"
add = next(i for i in range(gift[0] + 1, gift[0] + 4) if "Ljava/util/List;->add(" in lines[i])
reg = lines[gift[0]].split()[1].rstrip(",")
lines.insert(add + 1, f"    sget-object {reg}, {PAGE_T}->POKEMON:{PAGE_T}\n{lines[add]}")
write(path, lines)

# 19. Animals: a new tab after Pokémon with your farm animals (not Pokémon), coops and barns,
#     hay and your pet. The page is com.stardual.StardualAnimals, which shows
#     assets/stardual/animals.html; the mod sends it as "sdv_animals_page". A new page entry
#     (ordinal 15), drawn by StardualOverview.page, with its icon sent as "ui:page_ANIMALS".
path = find("StardewPage")
lines = open(path).read().split("\n")
field = next(i for i, l in enumerate(lines) if l.startswith(".field public static final enum POKEMON:"))
lines.insert(field + 1, f".field public static final enum ANIMALS:{PAGE_T}")
s, e = method_span(lines, "$values")
assert lines[s + 1].strip() == ".locals 15", "page list changed (18)"
lines[s + 1] = "    .locals 16"
arr = next(i for i in range(s, e) if "filled-new-array/range {v0 .. v14}" in lines[i])
lines[arr] = lines[arr].replace("{v0 .. v14}", "{v0 .. v15}")
lines.insert(arr, f"    sget-object v15, {PAGE_T}->ANIMALS:{PAGE_T}")
s, e = method_span(lines, "<clinit>")
call = next(i for i in range(s, e) if "->$values()" in lines[i])
lines.insert(call, f"""    new-instance v0, {PAGE_T}
    const-string v1, "ANIMALS"
    const/16 v2, 0xf
    const-string v3, "Animals"
    const/4 v4, 0x0
    const/4 v5, 0x0
    invoke-direct/range {{v0 .. v5}}, {PAGE_T}-><init>(Ljava/lang/String;ILjava/lang/String;ZZ)V
    sput-object v0, {PAGE_T}->ANIMALS:{PAGE_T}
""")
write(path, lines)

path = find("StardewScreenKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewScreen", must="(Landroidx/compose/ui/Modifier;Landroidx/compose/runtime/Composer;II)V")
poke = [i for i in range(s, e) if "sget-object" in lines[i] and f"{PAGE_T}->POKEMON:" in lines[i]]
assert len(poke) == 1, f"Pokémon tab: found {len(poke)}"
add = next(i for i in range(poke[0] + 1, poke[0] + 4) if "Ljava/util/List;->add(" in lines[i])
reg = lines[poke[0]].split()[1].rstrip(",")
lines.insert(add + 1, f"    sget-object {reg}, {PAGE_T}->ANIMALS:{PAGE_T}\n{lines[add]}")
write(path, lines)

# 20. Settings lists the pages to show or hide in the same order as the tabs (every page can
#     be hidden, the new ones too; Settings itself always stays).
path = find("StardewSettingsKt")
lines = open(path).read().split("\n")
s, e = method_span(lines, "StardewSettingsPage")
skip = next(i for i in range(s, e) if lines[i].strip() == f"sget-object v14, {PAGE_T}->SETTINGS:{PAGE_T}")
cast = next(i for i in range(skip, skip + 80) if lines[i].strip() == "check-cast v4, Ljava/util/List;")
assert any('"configurablePages"' in lines[j] for j in range(cast, cast + 20)), "settings page list moved"
lines.insert(cast + 1, f"    invoke-static {{v4}}, {OVERVIEW}settingsOrder(Ljava/util/List;)V")
write(path, lines)
