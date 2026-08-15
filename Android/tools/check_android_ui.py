#!/usr/bin/env python3
"""Static checks for Android localization and accessibility resources.

The checker intentionally uses only the Python standard library so it can run
locally and in the Android CI job without an extra dependency.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


ANDROID_NS = "http://schemas.android.com/apk/res/android"
TOOLS_NS = "http://schemas.android.com/tools"
LOCALES = ("values", "values-en", "values-de", "values-es", "values-zh-rCN")
TRANSLATABLE_UI_ATTRIBUTES = {"text", "hint", "contentDescription", "title"}
UI_SINKS = re.compile(
    r"(?:Toast\.makeText|set(?:Title|Message|PositiveButton|NegativeButton|NeutralButton|Items|Text)|"
    r"setContent(?:Title|Text)|setName|(?:^|\.)contentDescription\s*=|"
    r"(?:^|\.)text\s*=|(?:^|\.)hint\s*=|showError\s*\(|"
    r"applyOnlineStatus\s*\(|setTextViewText\s*\()"
)
TECHNICAL_LINE = re.compile(
    r"(?:^|\b)(?:const\s+val|putExtra|getStringExtra|getBooleanExtra|getLongExtra|"
    r"getIntExtra|equals\(|setTypeface|Typeface\.create|SimpleDateFormat|"
    r"DateTimeFormatter|Locale\.|File\(|ClipDescription\(|Regex\(|\.format\(|"
    r"fileId|messageId|chat_id|SELF_LABEL|Log\.|TAG\b)"
)
STRING_LITERAL = re.compile(r'""".*?"""|"(?:\\.|[^"\\])*"', re.DOTALL)
PLACEHOLDER = re.compile(r"%(?!%)(?:\d+\$)?[0-9.+\-()]*[a-zA-Z]")
ALPHABETIC = re.compile(r"[A-Za-zА-Яа-яЁё]")


def local_name(name: str) -> str:
    return name.rsplit("}", 1)[-1]


def text_of(element: ET.Element) -> str:
    return "".join(element.itertext())


def resource_entries(values_dir: Path) -> dict[tuple[str, str], dict]:
    entries: dict[tuple[str, str], dict] = {}
    for xml_file in sorted(values_dir.glob("*.xml")):
        try:
            root = ET.parse(xml_file).getroot()
        except ET.ParseError as error:
            raise ValueError(f"{xml_file}: invalid XML: {error}") from error

        for element in root:
            kind = local_name(element.tag)
            if kind not in {"string", "plurals", "string-array"}:
                continue
            name = element.attrib.get("name")
            if not name:
                continue

            if kind == "string":
                value = text_of(element)
            else:
                value = {
                    child.attrib.get("quantity", str(index)): text_of(child)
                    for index, child in enumerate(element)
                }

            entries[(kind, name)] = {
                "value": value,
                "translatable": element.attrib.get("translatable", "true") != "false",
                "file": xml_file,
            }
    return entries


def placeholder_signature(value: str) -> Counter[str]:
    return Counter(PLACEHOLDER.findall(value))


def resource_signature(value: str | dict[str, str]) -> tuple:
    if isinstance(value, str):
        return tuple(sorted(placeholder_signature(value).items()))

    # Plural resources legitimately have a different number of forms per
    # locale (for example ru has one/few/many while zh has only other). The
    # placeholder signature must therefore be compared per form, not counted
    # once again for every grammatical form.
    return tuple(
        sorted(
            tuple(sorted(placeholder_signature(item).items()))
            for item in value.values()
        )
    )


def check_resources(res_dir: Path) -> list[str]:
    loaded = {
        locale: resource_entries(res_dir / locale)
        for locale in LOCALES
    }
    base = loaded["values"]
    errors: list[str] = []

    for resource_id, base_entry in sorted(base.items()):
        kind, name = resource_id
        if not base_entry["translatable"]:
            continue

        for locale in LOCALES[1:]:
            entry = loaded[locale].get(resource_id)
            if entry is None:
                errors.append(f"{locale}: missing {kind}/{name}")
                continue

            expected = resource_signature(base_entry["value"])
            actual = resource_signature(entry["value"])
            if kind == "plurals":
                # Locale-specific quantity forms may differ, but every form
                # must preserve the same placeholder signature.
                expected = tuple(sorted(set(expected)))
                actual = tuple(sorted(set(actual)))
            if expected != actual:
                errors.append(
                    f"{locale}: placeholder mismatch for {kind}/{name}: "
                    f"expected {dict(expected)}, got {dict(actual)}"
                )

    for locale in LOCALES[1:]:
        for resource_id in sorted(set(loaded[locale]) - set(base)):
            kind, name = resource_id
            errors.append(f"{locale}: extra {kind}/{name} not present in values")

    return errors


def hardcoded_xml_value(value: str) -> bool:
    value = value.strip()
    if not value or value.startswith(("@", "?", "${")):
        return False
    return bool(ALPHABETIC.search(value))


def check_ui_xml(res_dir: Path) -> list[str]:
    errors: list[str] = []
    xml_dirs = sorted(
        path for path in res_dir.iterdir()
        if path.is_dir() and (path.name.startswith("layout") or path.name == "menu")
    )
    for xml_dir in xml_dirs:
        for xml_file in sorted(xml_dir.rglob("*.xml")):
            try:
                root = ET.parse(xml_file).getroot()
            except ET.ParseError as error:
                errors.append(f"{xml_file}: invalid XML: {error}")
                continue

            for element in root.iter():
                for attribute, value in element.attrib.items():
                    if attribute.startswith(f"{{{TOOLS_NS}}}"):
                        continue
                    if local_name(attribute) not in TRANSLATABLE_UI_ATTRIBUTES:
                        continue
                    if hardcoded_xml_value(value):
                        errors.append(
                            f"{xml_file}:{local_name(attribute)} contains hardcoded UI text "
                            f"{value!r}"
                        )
    return errors


def strip_comments(source: str) -> str:
    result: list[str] = []
    index = 0
    length = len(source)

    while index < length:
        if source.startswith("//", index):
            newline = source.find("\n", index)
            if newline == -1:
                break
            result.extend(" " for _ in range(newline - index))
            result.append("\n")
            index = newline + 1
            continue

        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            end = length if end == -1 else end + 2
            comment = source[index:end]
            result.extend("\n" if char == "\n" else " " for char in comment)
            index = end
            continue

        if source.startswith('"""', index):
            end = source.find('"""', index + 3)
            end = length if end == -1 else end + 3
            result.append(source[index:end])
            index = end
            continue

        if source[index] == '"':
            end = index + 1
            while end < length:
                if source[end] == "\\":
                    end += 2
                    continue
                if source[end] == '"':
                    end += 1
                    break
                end += 1
            result.append(source[index:end])
            index = end
            continue

        if source[index] == "'":
            end = index + 1
            while end < length:
                if source[end] == "\\":
                    end += 2
                    continue
                if source[end] == "'":
                    end += 1
                    break
                end += 1
            result.append(source[index:end])
            index = end
            continue

        result.append(source[index])
        index += 1

    return "".join(result)


def check_kotlin(source_root: Path) -> list[str]:
    errors: list[str] = []
    for kotlin_file in sorted(source_root.rglob("*.kt")):
        source = strip_comments(kotlin_file.read_text(encoding="utf-8"))
        lines = source.splitlines()
        for match in STRING_LITERAL.finditer(source):
            literal = match.group(0)
            value = literal[3:-3] if literal.startswith('"""') else literal[1:-1]
            if not ALPHABETIC.search(value):
                continue

            line_index = source.count("\n", 0, match.start())
            context = "\n".join(lines[max(0, line_index - 5):line_index + 1])
            current_line = lines[line_index]
            if TECHNICAL_LINE.search(current_line):
                continue
            if not UI_SINKS.search(context[-320:]):
                continue
            sink_position = UI_SINKS.search(context).start()
            if re.search(r"(?:Log\.|println\(|logger\.)[^\n]{0,160}$", context[sink_position:]):
                continue

            line = line_index + 1
            errors.append(f"{kotlin_file}:{line}: hardcoded UI literal {value!r}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-kotlin", action="store_true")
    args = parser.parse_args()

    project_root = Path(__file__).resolve().parents[1]
    res_dir = project_root / "Barkfluff.Client.Android" / "app" / "src" / "main" / "res"
    source_root = project_root / "Barkfluff.Client.Android" / "app" / "src" / "main" / "java"

    errors: list[str] = []
    errors.extend(check_resources(res_dir))
    errors.extend(check_ui_xml(res_dir))
    if not args.skip_kotlin:
        errors.extend(check_kotlin(source_root))

    if errors:
        print(f"Android UI checks failed with {len(errors)} issue(s):", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Android UI checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
