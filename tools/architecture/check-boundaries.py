#!/usr/bin/env python3
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import sys


@dataclass(frozen=True)
class Rule:
    name: str
    root: Path
    suffixes: tuple[str, ...]
    forbidden: tuple[str, ...]


RULES = (
    Rule(
        name="windows-core-purity",
        root=Path("apps/windows/src/Xueqing.Windows.Core"),
        suffixes=(".cs", ".csproj"),
        forbidden=(
            "Microsoft.UI.Xaml",
            "Xueqing.Windows.Infrastructure",
            "Xueqing.Windows.Presentation",
            "Microsoft.WindowsAppSDK",
            "<UseWinUI>",
        ),
    ),
    Rule(
        name="windows-presentation-purity",
        root=Path("apps/windows/src/Xueqing.Windows.Presentation"),
        suffixes=(".cs", ".csproj"),
        forbidden=(
            "Microsoft.UI.Xaml",
            "Xueqing.Windows.Infrastructure",
            "Microsoft.WindowsAppSDK",
            "<UseWinUI>",
        ),
    ),
    Rule(
        name="android-application-does-not-depend-on-presentation",
        root=Path("apps/android/app/src/main/java/com/xueqing/app/application"),
        suffixes=(".kt",),
        forbidden=("com.xueqing.app.presentation",),
    ),
    Rule(
        name="android-infrastructure-does-not-depend-on-presentation",
        root=Path("apps/android/app/src/main/java/com/xueqing/app/infrastructure"),
        suffixes=(".kt",),
        forbidden=("com.xueqing.app.presentation",),
    ),
    Rule(
        name="android-durability-does-not-depend-on-presentation",
        root=Path("apps/android/app/src/main/java/com/xueqing/app/durability"),
        suffixes=(".kt",),
        forbidden=("com.xueqing.app.presentation",),
    ),
    Rule(
        name="android-presentation-does-not-import-provider-sdks",
        root=Path("apps/android/app/src/main/java/com/xueqing/app/presentation"),
        suffixes=(".kt",),
        forbidden=(
            "io.github.jan.supabase",
            "postgrest.",
            "okhttp3.",
            "retrofit2.",
        ),
    ),
)


def source_files(rule: Rule) -> list[Path]:
    if not rule.root.exists():
        return []
    return sorted(
        path
        for path in rule.root.rglob("*")
        if path.is_file() and path.suffix in rule.suffixes
    )


def main() -> int:
    failures: list[str] = []

    for rule in RULES:
        for path in source_files(rule):
            text = path.read_text(encoding="utf-8")
            for token in rule.forbidden:
                if token in text:
                    failures.append(
                        f"{rule.name}: {path} contains forbidden dependency marker {token!r}"
                    )

    if failures:
        print("Architecture boundary violations detected:", file=sys.stderr)
        for failure in failures:
            print(f"  - {failure}", file=sys.stderr)
        return 1

    print(f"Architecture boundaries OK ({len(RULES)} rules).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
