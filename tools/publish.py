#!/usr/bin/env python3
"""
Publishes the Ditechs PC Check zip to ditechs.co.uk and updates the tool page.

The filename carries the version on purpose. Cloudflare serves a stale copy of an
un-versioned upload URL and this agent's access is read-only, so the cache cannot be
purged - a new filename is the only reliable way to ship a new build.

Re-runnable: it clears any previous upload of the *same* version first, because
WordPress silently appends "-1" when a file of that name already exists, which
produces a different URL from the one this script then verifies.
"""

import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import zipfile

SITE = "https://ditechs.co.uk"
CONF = "/opt/data/.secrets/wp-curl.conf"

PAGE_ID = 203
VERSION = "1.0.1"
OLD_VERSION = "1.0.0"
ZIP_NAME = f"DitechsPcCheck-{VERSION}.zip"

HERE = pathlib.Path(__file__).resolve().parent.parent
DIST = HERE / "dist"
BUILT = DIST / ZIP_NAME


def curl(*args):
    result = subprocess.run(["curl", "-sS", "-K", CONF, *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("curl failed: " + result.stderr.strip())
    return result.stdout


def human_size(n):
    """Match how the other tool pages state it: KB below a megabyte, MB above."""
    return f"{round(n / 1048576)} MB" if n >= 1048576 else f"{round(n / 1024)} KB"


def check_zip():
    if not BUILT.exists():
        raise RuntimeError(f"{BUILT} is missing - run ./build.sh small first")

    with zipfile.ZipFile(BUILT) as z:
        if z.testzip() is not None:
            raise RuntimeError("the built zip is corrupt")
        names = z.namelist()

    print(f"  {BUILT.name}: {BUILT.stat().st_size:,} bytes ({human_size(BUILT.stat().st_size)})")
    for n in names:
        print(f"    {n}")
    return BUILT


def clear_previous_upload():
    """Drop an earlier upload of this exact version, so WordPress reuses the name."""
    media = json.loads(curl(f"{SITE}/wp-json/wp/v2/media?per_page=100"))
    for m in media:
        if m["source_url"].split("/")[-1] == ZIP_NAME:
            curl("-X", "DELETE", f"{SITE}/wp-json/wp/v2/media/{m['id']}?force=true")
            print(f"  removed the previous {ZIP_NAME} (media {m['id']})")


def upload():
    raw = curl("-X", "POST", f"{SITE}/wp-json/wp/v2/media",
               "-H", f"Content-Disposition: attachment; filename={ZIP_NAME}",
               "-H", "Content-Type: application/zip",
               "--data-binary", "@" + str(BUILT),
               "--max-time", "900")
    created = json.loads(raw)
    if "source_url" not in created:
        raise RuntimeError("upload failed: " + raw[:400])

    url = created["source_url"]
    if url.split("/")[-1] != ZIP_NAME:
        raise RuntimeError(f"WordPress renamed the file to {url.split('/')[-1]} - the URL would not match")
    print(f"  uploaded as media {created['id']}: {url}")
    return url


def update_page(url):
    page = json.loads(curl(f"{SITE}/wp-json/wp/v2/pages/{PAGE_ID}?context=edit"))
    content = page["content"]["raw"]
    before = len(content)

    def swap(old, new, exactly=1):
        nonlocal content
        found = content.count(old)
        if found != exactly:
            raise RuntimeError(f"expected {exactly} of '{old[:60]}' on the page, found {found}")
        content = content.replace(old, new)

    swap(f"DitechsPcCheck-{OLD_VERSION}.zip", f"DitechsPcCheck-{VERSION}.zip")
    swap(f"Download Ditechs PC Check v{OLD_VERSION}", f"Download Ditechs PC Check v{VERSION}")
    swap(f"Version {OLD_VERSION}.", f"Version {VERSION}.")

    # The size line was wrong - it claimed 45 KB for a file that is nearly 60 MB.
    size_text = human_size(BUILT.stat().st_size)
    if not re.search(r"Zip, \d+ (KB|MB)\.", content):
        raise RuntimeError("could not find the 'Zip, NN KB.' line")
    content = re.sub(r"Zip, \d+ (KB|MB)\.", f"Zip, {size_text}.", content, count=1)

    if url not in content:
        raise RuntimeError("the new download URL did not make it onto the page")

    payload = json.dumps({"content": content})
    handle, path = tempfile.mkstemp(prefix="page-", suffix=".json")
    try:
        with os.fdopen(handle, "w", encoding="utf-8") as fh:
            fh.write(payload)
        raw = curl("-X", "POST", f"{SITE}/wp-json/wp/v2/pages/{PAGE_ID}",
                   "-H", "Content-Type: application/json", "--data-binary", "@" + path)
    finally:
        os.unlink(path)

    result = json.loads(raw)
    if "id" not in result:
        raise RuntimeError("the page was not updated: " + raw[:400])
    print(f"  page {result['id']} updated: {result.get('link')}  ({before} -> {len(content)} bytes)")
    print(f"  size line now: Zip, {size_text}.")


def main():
    check_zip()
    clear_previous_upload()
    url = upload()
    update_page(url)

    print()
    print("  verify at the origin (cache-busted):")
    for target in (url, f"{SITE}/tools/pc-check/"):
        out = subprocess.run(
            ["curl", "-sS", "-L", "-o", "/dev/null", "-w", "%{http_code} %{size_download}B",
             "--max-time", "900", f"{target}?cb={os.getpid()}"],
            capture_output=True, text=True).stdout
        print(f"    {out}  {target}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
