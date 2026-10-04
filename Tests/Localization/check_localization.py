"""Validate UI key coverage and formatting arguments in the shipped catalogs."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
catalogs = {}
for path in sorted((root / "Localization").glob("*.json")):
    entries = json.loads(path.read_text(encoding="utf-8-sig"))["translations"]
    values = {}
    for entry in entries:
        key, value = entry["key"], entry["value"]
        assert key not in values, f"{path.name}: duplicate key {key}"
        assert isinstance(value, str) and value.strip(), f"{path.name}: empty value {key}"
        values[key] = value
    catalogs[path.stem] = values

reference = catalogs["en-US"]
arguments = re.compile(r"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")
for language, values in catalogs.items():
    assert values.keys() == reference.keys(), f"{language}: missing/extra keys {values.keys() ^ reference.keys()}"
    for key, value in values.items():
        assert set(arguments.findall(value)) == set(arguments.findall(reference[key])), f"{language}: argument mismatch in {key}"

# Include keys passed through variables/field definitions, not only literal Get calls.
key_pattern = re.compile(r'"((?:ui|net|scene|vehicle|loot|settings|ai|chat)\.[A-Za-z0-9_.]+)"')
for path in (root / "EscapeFromDuckovCoopMod").rglob("*.cs"):
    if "bin" in path.parts or "obj" in path.parts:
        continue
    for key in key_pattern.findall(path.read_text(encoding="utf-8-sig")):
        if key.endswith("."):
            continue  # Namespace prefixes used to validate incoming reason keys.
        assert key in reference, f"{path.relative_to(root)}: missing key {key}"

print(f"PASS: {len(catalogs)} languages, {len(reference)} keys each; no missing keys, empty values, duplicates or argument mismatches.")
