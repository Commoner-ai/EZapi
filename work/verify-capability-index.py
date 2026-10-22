"""Check capability IDs declared by the product against the published capability catalog."""
from pathlib import Path
import json
import re

root = Path(__file__).resolve().parent.parent
source = (root / "src/EZapi/FrameworkInfo.cs").read_text(encoding="utf-8-sig")
declared = re.findall(r'"([a-z][a-z0-9.-]+)"', source)
assert len(declared) == len(set(declared)), "duplicate FrameworkInfo capability"
doc = json.loads((root / "docs/capabilities.json").read_text(encoding="utf-8-sig"))
rows = doc["capabilities"]
assert len({r["id"] for r in rows}) == len(rows), "duplicate documented capability"
assert doc["version"] == doc["implementationVersion"] == re.search(r'Version\s*=\s*"([^"]+)"', source).group(1)
actual = {r["id"] for r in rows if r.get("declaredByFrameworkInfo")}
assert actual == set(declared), (actual - set(declared), set(declared) - actual)
for row in rows:
    if row.get("declaredByFrameworkInfo"):
        assert row["implemented"] and row["compileVerified"], row["id"]
assert doc["entryPoint"]["count"] == len(declared)
report = {"passed": True, "declared": len(declared), "documented": len(rows),
          "legacyOrDescriptiveCategories": len(rows) - len(declared),
          "scope": "source/catalog ID and declaration-version consistency; not native runtime verification"}
(root / "work/capability-index-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report))
