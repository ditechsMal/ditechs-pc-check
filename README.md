# Ditechs PC Check

Reads a handful of things about a Windows PC and shows a plain-English report card: what is
fine, what is worth a look, and what is worth sorting out. Built for people who are not
comfortable with computers.

Delivered at **https://www.ditechs.co.uk/tools/pc-check/**

## The promise, and how it is enforced

This tool's whole value is that it is safe, so the promise is enforced by the build rather
than trusted to good intentions:

| Promise | How it is kept honest |
|---|---|
| It never changes anything | Every check is a read. No writes, no deletes, no installs. |
| It never asks for administrator rights | `app.manifest` requests `asInvoker`. **`build.sh` and CI both refuse to package a build that requests anything else.** |
| It never connects to the internet | No network calls anywhere in the code. Nothing about the PC leaves it. |
| It never fails in the customer's face | A check that cannot be read reports "couldn't check" and the report carries on. No error dialogs. |
| It never uses jargon | A test sweeps every customer-facing string against a list of banned words (`registry`, `service`, `process`, `driver`, `BIOS`, `corrupt`…), plus no exclamation marks. |

If you change the wording, run the tests. The wording *is* the product.

## Building

```bash
./build.sh            # tests + the client exe + installer files + client zip
./build.sh small      # only the small .NET Framework 4.8 exe (the one clients get)
./build.sh big        # only the self-contained .NET 8 exe
./build.sh test       # the logic and wording tests
./build.sh aptest     # PSScriptAnalyzer on the installer
```

Output lands in `dist/`. The MSI is built by CI on a Windows runner, because WiX only
supports Windows.

## Tests

`tests/` runs anywhere — it exercises the *decision and wording* half of the program
(`PcCheckRules.cs`), which is the half most likely to be wrong in a way that matters.
The Windows-reading half lives in `Collector.cs` and can only be exercised on Windows.

```
307 assertions, plus a sweep of every customer-facing string against the banned-word list
```

Source layout:

- `PcCheckRules.cs` — every threshold and every word the customer sees. No Windows APIs.
- `Collector.cs` — reads the PC. Read-only, every read wrapped so a failure degrades to
  "couldn't check" rather than an error.
- `MainForm.cs` — the report card window. Colours are the Ditechs palette read off the site.
- `make-icon.py` — generates `app.ico`, so the icon is reproducible.

## Branches

- **`main`** — released code. A `v*` tag publishes a GitHub release.
- **`test`** — for updates at a later date. Builds in CI but publishes nothing.

## Known gaps

- The apps are **not code-signed yet**, so Windows SmartScreen warns on first run. The
  download page says so plainly. See the repository notes for the certificate options.
- The backup check can only recognise OneDrive folder sync, File History, and a list of
  known backup programs. It says "we couldn't find a backup" rather than asserting there is
  none, precisely because it cannot see everything.
