"""Check the lilToon structure table against the committed 2.3.4 catalogue without network access.

Fails when the catalogue's structure hash differs from the table's reference, when a tag in the
verified lilToon range (lilToon.verified in Compatibility/dependencies.json: the releases checked to
have the catalogue's structure) has a different structure, or when the table is malformed. Regenerate
the table with `python3 Tools/generate-liltoon-catalogue.py structure --fetch --write`.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import liltoon_catalogue as lc  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]


def check(root=ROOT):
    catalogue = lc.parse_catalogue((root / lc.CATALOGUE_PATH).read_text(encoding="ascii"))
    table = json.loads((root / lc.STRUCTURE_PATH).read_text(encoding="utf-8"))
    config = json.loads((root / "Compatibility/dependencies.json").read_text(encoding="utf-8"))
    bounds = lc.verified_range(config)
    problems = lc.check_structure_table(table, catalogue, bounds)
    definition = table.get("definition") if isinstance(table, dict) else None
    if not isinstance(definition, str) or not (root / definition).is_file():
        problems.append("definition must point to the decision record that defines the hash")
    return catalogue, bounds, problems


def main():
    catalogue, bounds, problems = check()
    for problem in problems:
        print("ERROR " + problem)
    if problems:
        return 1
    print("lilToon %s catalogue structure %s; every verified tag from %s to %s matches"
          % (catalogue["version"], lc.structure_hash(catalogue), bounds[0], bounds[1]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
