#!/usr/bin/env python3
"""Generate a CycloneDX SBOM for the solution and enforce the reviewed package allowlist.

    python scripts/sbom/generate.py                 # write artifacts/sbom.cdx.json
    python scripts/sbom/generate.py --check         # fail on an unreviewed package

The allowlist is a review gate, not licence detection: adding a package is a decision that
someone records in docs/licenses/README.md, and CI refuses to ship a dependency nobody
looked at.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
ALLOWLIST = ROOT / "docs" / "licenses" / "allowed-packages.txt"
OUTPUT = ROOT / "artifacts" / "sbom.cdx.json"
SOLUTION = "Tuesday.slnx"


def packages() -> dict[str, dict]:
    result = subprocess.run(
        ["dotnet", "list", SOLUTION, "package", "--include-transitive", "--format", "json"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=True,
    )

    # The CLI writes a banner before the JSON when it restores; take the document only.
    text = result.stdout
    start = text.find("{")
    document = json.loads(text[start:])

    found: dict[str, dict] = {}

    for project in document.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []) or []:
                    identifier = package["id"]
                    version = package.get("resolvedVersion") or package.get("requestedVersion") or "0.0.0"
                    entry = found.setdefault(identifier.lower(), {
                        "id": identifier,
                        "version": version,
                        "direct": kind == "topLevelPackages",
                    })
                    entry["direct"] = entry["direct"] or kind == "topLevelPackages"
                    if version != entry["version"]:
                        entry["versions"] = sorted({entry["version"], version})

    return dict(sorted(found.items()))


def read_allowlist() -> set[str]:
    entries: set[str] = set()

    for line in ALLOWLIST.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            entries.add(line.split()[0].lower())

    return entries


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail when a package is not allowlisted")
    args = parser.parse_args()

    found = packages()
    allowed = read_allowlist()

    unreviewed = sorted(package["id"] for key, package in found.items() if key not in allowed)

    if unreviewed:
        print("Packages missing from docs/licenses/allowed-packages.txt:", file=sys.stderr)
        for identifier in unreviewed:
            print(f"  - {identifier}", file=sys.stderr)
        print("Review the licence and add the package name to the allowlist.", file=sys.stderr)

        if args.check:
            return 1
    else:
        print(f"Allowlist covers all {len(found)} packages.")

    components = []

    for package in found.values():
        version = package.get("versions", [package["version"]])[0]
        components.append({
            "type": "library",
            "bom-ref": f"pkg:nuget/{package['id']}@{version}",
            "name": package["id"],
            "version": version,
            "purl": f"pkg:nuget/{package['id']}@{version}",
            "scope": "required",
            "properties": [{"name": "tuesday:direct", "value": "true" if package["direct"] else "false"}],
        })

    sbom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.5",
        "serialNumber": f"urn:uuid:{uuid.uuid5(uuid.NAMESPACE_URL, 'tuesday-com:' + '-'.join(sorted(found)))}",
        "version": 1,
        "metadata": {
            "component": {
                "type": "application",
                "name": "Tuesday.com",
                "version": _app_version(),
            }
        },
        "components": components,
    }

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(sbom, indent=2) + "\n", encoding="utf-8")

    print(f"Wrote {OUTPUT.relative_to(ROOT)} with {len(components)} components.")

    return 0


def _app_version() -> str:
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>([^<]+)</Version>", props)

    return match.group(1) if match else "0.0.0"


if __name__ == "__main__":
    raise SystemExit(main())
