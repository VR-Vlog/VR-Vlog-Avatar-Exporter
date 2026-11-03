"""Read the eye closure corpus and report what the one-eye blink rule relies on.

Input is the folder written by collect-eye-closure-corpus.py (one JSON per
avatar). For each avatar this finds the bilateral blink by the exporter's
exact names, then reports:

  * whether the exporter's name rules already give it left and right sides
    (fixed pairs, Unified Expressions, X_L / X_R forms of the blink's name);
  * the name-blind split of the blink into one shape per side of the face
    and its residual |left + right - blink|^2 / |blink|^2;
  * whether side markers in names agree with where the shapes move, after
    finding which axis is the avatar's left in each file from all marked
    one-sided shapes in it (FBX files differ);
  * the words in the names of the shapes that rebuild a blink, compared
    with the closing words the exporter knows.

Nothing here names an avatar: the output uses the IDs of the input files.

Usage:
  uv run --quiet --no-project python Tools/summarize-eye-closure-corpus.py CORPUS_DIR [--json OUT.json]
"""
import argparse
import glob
import json
import os
import re
import sys
import unicodedata
from collections import Counter

BOTH = ["vrc.Blink", "Blink", "Fcl_EYE_Close", "まばたき", "eye_blink_1", "eye_blink_2"]
PAIRS = [("Blink_L", "Blink_R"), ("BlinkLeft", "BlinkRight"), ("EyeBlinkLeft", "EyeBlinkRight"),
         ("Fcl_EYE_Close_L", "Fcl_EYE_Close_R"), ("eye_blink_1_L", "eye_blink_1_R"), ("eye_blink_2_L", "eye_blink_2_R")]
LATE_BOTH = ["eyelid_Blink"]
LATE_PAIRS = [("eyelid_Blink.L", "eyelid_Blink.R"), ("vrc.blink_left", "vrc.blink_right")]
UE_BOTH = {"eyeclosed", "eyesclosed"}
UE_LEFT = {"eyeclosedleft", "eyeleftclosed"}
UE_RIGHT = {"eyeclosedright", "eyerightclosed"}
MARKERS = [("L", "R", False), ("Left", "Right", True), ("左", "右", True)]
SEPARATORS = ["_", ".", " ", "-", ""]
# The same marker grammar as BlinkShapeNames.MarkedSide.
SUFFIX = re.compile(r"^(?P<root>.+?)(?:[ ._-]?(?P<side>left|right|左|右)目?|[ ._-](?P<side2>l|r))$", re.I)
PREFIX = re.compile(r"^(?:(?P<side>left|right|左|右)目?[ ._-]?|(?P<side2>l|r)[ ._-])(?P<root>.+)$", re.I)
# Closing words, to see which ones the shapes that rebuild a blink use and
# what the wink clips and menus are called.
KNOWN_CLOSING = ["blink", "close", "wink", "ウィンク", "ウインク", "まばたき", "瞬き", "閉じ", "とじ"]
WINK = re.compile(r"wink|ウィンク|ウインク|ウィンク", re.I)
REBUILT = .05


def norm(name):
    return unicodedata.normalize("NFKC", name or "")


def marked_side(name):
    """'L', 'R' or None, and the name without the marker."""
    name = norm(name)
    for pattern in (SUFFIX, PREFIX):
        m = pattern.match(name)
        if m:
            side = (m.group("side") or m.group("side2")).lower()
            return ("L" if side in ("l", "left", "左") else "R"), m.group("root")
    return None, name


def unique(names, name):
    found = [n for n in names if n.lower() == name.lower()]
    return found[0] if len(found) == 1 else None


def derived_sides(names, both):
    for left, right, joined in MARKERS:
        for sep in SEPARATORS:
            if not sep and not joined:
                continue
            for a, b in ((both + sep + left, both + sep + right), (left + sep + both, right + sep + both)):
                if unique(names, a) and unique(names, b):
                    return unique(names, a), unique(names, b)
    return None


def compact(name):
    return re.sub(r"[ ._-]", "", norm(name)).lower()


def bilateral(names):
    for name in BOTH:
        if unique(names, name):
            return unique(names, name), "named"
    for name in names:
        if compact(name) in UE_BOTH:
            return name, "ue"
    for name in LATE_BOTH:
        if unique(names, name):
            return unique(names, name), "late"
    return None, None


def named_sides(names, both):
    for pairs in (PAIRS, LATE_PAIRS):
        for left, right in pairs:
            if unique(names, left) and unique(names, right):
                return unique(names, left), unique(names, right), "fixed pair"
    lefts = [n for n in names if compact(n) in UE_LEFT]
    rights = [n for n in names if compact(n) in UE_RIGHT]
    if len(lefts) == 1 and len(rights) == 1:
        return lefts[0], rights[0], "unified expressions"
    if both:
        found = derived_sides(names, both)
        if found:
            return found[0], found[1], "forms of the blink's name"
    return None


def file_orientation(meshes):
    """Per FBX file: +1 when +X is the avatar's left by its marked names."""
    votes = Counter()
    for mesh in meshes:
        for shape in mesh["shapes"]:
            side = shape.get("side")
            if side is None or .1 < side < .9:
                continue
            marker, _ = marked_side(shape["name"])
            if marker:
                votes[mesh["file"]] += 1 if (side >= .9) == (marker == "L") else -1
    return {f: (1 if v > 0 else -1 if v < 0 else 0) for f, v in votes.items()}


def words(name):
    _, root = marked_side(name)
    return [w for w in re.split(r"[ ._\-\d]+|(?<=[a-z])(?=[A-Z])", norm(root)) if w]


def summarize(paths):
    rows, vocabulary, disagreements = [], Counter(), []
    residuals = []
    for path in paths:
        data = json.load(open(path, encoding="utf-8"))
        avatar = os.path.splitext(os.path.basename(path))[0]
        orientation = file_orientation(data["meshes"])
        row = {"id": avatar, "blink": None, "before": None, "split": None, "after": None}
        for mesh in data["meshes"]:
            names = [s["name"] for s in mesh["shapes"]]
            both, _ = bilateral(names)
            if not both:
                continue
            row["blink"] = row["blink"] or both
            before = named_sides(names, both)
            if before and not row["before"]:
                row["before"] = {"left": before[0], "right": before[1], "rule": before[2]}
            for split in mesh["splits"]:
                if split["both"] != both:
                    continue
                o = orientation.get(mesh["file"], 0)
                left, right = (split["plus"], split["minus"]) if o >= 0 else (split["minus"], split["plus"])
                ml, _ = marked_side(left); mr, _ = marked_side(right)
                agrees = (ml in (None, "L")) and (mr in (None, "R"))
                residuals.append(split["residual"])
                row["split"] = {"left": left, "right": right, "residual": split["residual"],
                                "orientation_known": o != 0, "markers_agree": agrees}
                if not agrees:
                    disagreements.append((avatar, left, right))
                if split["residual"] <= REBUILT:
                    for name in (left, right):
                        vocabulary.update(set(w.lower() for w in words(name)))
        # FX clips and menus named as a wink, and whether they close exactly
        # one of the split's shapes and nothing else on the eyes.
        split = row["split"]
        winks = [c for c in data["clips"] if WINK.search(norm(c["name"])) or
                 any(WINK.search(norm(s.get("state", ""))) for s in c.get("states", []))]
        row["wink_clips"] = len(winks)
        row["wink_clips_driving_a_side"] = sum(1 for c in winks if split and any(
            x["shape"] in (split["left"], split["right"]) and x["max"] >= 90 for x in c["curves"]))
        row["wink_menus"] = sum(1 for m in data["menus"] if WINK.search(norm(m["name"])))
        if row["before"]:
            row["after"] = row["before"]["rule"]
        elif row["split"] and row["split"]["residual"] <= REBUILT and row["split"]["markers_agree"]:
            row["after"] = "split of the blink"
        rows.append(row)
    return rows, vocabulary, disagreements, residuals


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("corpus")
    parser.add_argument("--json")
    args = parser.parse_args()
    paths = sorted(glob.glob(os.path.join(args.corpus, "*.json")))
    if not paths:
        sys.exit("no corpus files in " + args.corpus)
    rows, vocabulary, disagreements, residuals = summarize(paths)
    with_blink = [r for r in rows if r["blink"]]
    before = sum(1 for r in with_blink if r["before"])
    after = sum(1 for r in with_blink if r["after"])
    print(f"avatars {len(rows)}, with a bilateral blink {len(with_blink)}")
    print(f"left/right before {before}, after {after}")
    print("rules before:", dict(Counter(r["before"]["rule"] for r in with_blink if r["before"])))
    print("split residuals of the blink:", sorted(residuals))
    print("splits whose markers disagree with the mesh:", len(disagreements))
    for avatar, left, right in disagreements:
        print("  ", avatar, left, "/", right)
    print("words in shapes that rebuild a blink:", vocabulary.most_common(30))
    print("wink clips:", sum(r.get("wink_clips", 0) for r in rows), "of which drive a split side at 90+:",
          sum(r.get("wink_clips_driving_a_side", 0) for r in rows), "wink menus:", sum(r.get("wink_menus", 0) for r in rows))
    print("known closing words used:", [w for w in KNOWN_CLOSING if any(w in v for v in vocabulary)])
    print("words not covered by a known closing word:",
          [w for w, _ in vocabulary.most_common() if not any(k in w for k in KNOWN_CLOSING)])
    for r in rows:
        split = r["split"]
        print(r["id"], "blink=" + str(r["blink"]), "before=" + (r["before"]["rule"] if r["before"] else "-"),
              "split=" + (f"{split['left']}/{split['right']} {split['residual']}" if split else "-"),
              "after=" + (r["after"] or "-"))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump({"rows": rows, "vocabulary": vocabulary.most_common(), "residuals": residuals,
                       "disagreements": disagreements}, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
