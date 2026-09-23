#!/usr/bin/env python3
from __future__ import annotations

import argparse
import pathlib
import xml.etree.ElementTree as ET

from tools.release.verify_release_identity import read_properties

ROOT = pathlib.Path(__file__).resolve().parents[2]


def render(publisher: str, output: pathlib.Path) -> None:
    publisher = publisher.strip()
    if not publisher or publisher == "__XUEQING_WINDOWS_PUBLISHER__":
        raise ValueError("Windows Publisher must be an explicit certificate subject")

    values = read_properties(ROOT / "release" / "version.properties")
    template = (ROOT / "release" / "windows" / "Package.production.appxmanifest.template").read_text(encoding="utf-8")
    rendered = template.replace("__XUEQING_WINDOWS_PUBLISHER__", publisher).replace(
        "__XUEQING_WINDOWS_PACKAGE_VERSION__", values["windowsPackageVersion"]
    )
    if "__XUEQING_" in rendered:
        raise ValueError("unresolved Windows production manifest token")

    root = ET.fromstring(rendered)
    ns = {"f": "http://schemas.microsoft.com/appx/manifest/foundation/windows10"}
    identity = root.find("f:Identity", ns)
    if identity is None:
        raise ValueError("production manifest has no Identity")
    if identity.attrib.get("Name") != values["windowsPackageName"]:
        raise ValueError("production Windows package name drift")
    if identity.attrib.get("Publisher") != publisher:
        raise ValueError("production Windows Publisher drift")
    if identity.attrib.get("Version") != values["windowsPackageVersion"]:
        raise ValueError("production Windows package version drift")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(rendered, encoding="utf-8", newline="\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--publisher", required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    render(args.publisher, args.output)
    print(f"Rendered Windows production manifest: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
