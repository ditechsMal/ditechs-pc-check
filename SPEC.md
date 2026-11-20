# Ditechs PC Check — specification (v1)

**Purpose.** One button. Reads the PC, changes nothing. Produces a plain-English report
card a non-technical person can read in thirty seconds, ending with one clear next step.

Status: **wording APPROVED by Mal, 20 November 2026.** "Book a free health check" points at
`/contact/` on ditechs.co.uk (Mal's instruction, 20 Nov 2026).

### Wording adjusted during implementation (flagged to Mal, awaiting his nod)

Two claims the spec promised that cannot be honestly read from a PC without a slow, flaky
live Windows Update search (30–120 s, fails outright when the update service is stopped).
Both were narrowed rather than faked:

1. **Updates.** Was: *"Windows updates are installed and up to date."* Now reports the last
   successful install date: `Windows updates were last installed on 3 November 2026.` plus the
   reboot-pending line. Amber when that date is more than 35 days old. This is a weaker claim
   but a true one.
2. **Security.** Was: *"...and up to date."* Now: `Virus protection is switched on.` — the
   "up to date" half only appears when the definition-age bit is actually readable. The
   remaining states (off / not found / out of date / couldn't check) are unchanged.

---

## Absolute rules (v1)

1. **Read-only.** It never changes a setting, never deletes, never installs. Said on screen
   and on the download page.
2. **No administrator rights.** No UAC prompt — it just opens. Everything listed below is
   readable by a standard user.
3. **No internet access.** Nothing is sent anywhere, ever. This is a stated feature:
   *"Nothing leaves your PC."*
4. **Never fails.** A check it cannot read reports "couldn't check" and it carries on.
   No error dialogs, ever.
5. **Plain English only.** Banned from any output: *registry, service, process, driver,
   SMART, defrag, cache, kernel, BIOS, firmware, corrupt, exception*.
6. **Never frighten.** No exclamation marks, no "critical error", and no red result without
   a next step in the same breath.
7. **Never blame the user.** "This PC hasn't been restarted in 21 days" — not "you haven't
   restarted".

---

## The report

**Header:** `Ditechs PC Check` · computer name · date
**Standing line:** `This only looks at your PC. It doesn't change anything, and nothing is sent anywhere.`

**Overall verdict — one line, one of:**

| Colour | Wording |
|---|---|
| Green | `Your PC looks healthy. Nothing here needs attention.` |
| Amber | `Your PC is fine to use, but there are 3 things worth a look.` |
| Red | `There are 2 things here worth sorting out.` |

### 1. Windows — is it still supported?
- Win 11 · green · `Windows 11 — still supported. Microsoft is still sending security updates for it.`
- Win 10 · red · `Windows 10 — Microsoft stopped sending free security updates for this in October 2025. It still works, but newly found security flaws will not be fixed.`
- Older · red · `This version of Windows is no longer supported at all. It should not be used for anything important — banking, email or shopping.`

### 2. Storage
- ≥20% free · green · `392 GB free out of 476 GB — plenty of room.`
- 10–20% · amber · `78 GB free. That's getting tight — Windows starts behaving oddly when the disk fills up.`
- <10% · red · `Only 21 GB free. On its own that makes a PC slow, and it can stop updates from installing.`

### 3. Memory
- ≥8 GB · green · `This PC has 16 GB of memory — plenty for everyday use.`
- 4 GB · amber · `4 GB of memory. That's the minimum Windows will run in, so expect it to feel slow with several things open.`
- <4 GB · red · `Under 4 GB of memory — below what Windows needs to run comfortably.`

### 4. Startup (read-only in v1 — it does not offer to turn anything off)
- ≤6 · green · `8 programs start by themselves when you switch this PC on. That's normal.`
- 7–12 · amber · `14 programs start by themselves. Each one adds to how long switching on takes, and most people don't need them all.`
- >12 · red · `27 programs start by themselves. That's a lot, and it's one of the most common reasons a PC feels slow to start.`

### 5. Updates
- Up to date · green · `Windows updates are installed and up to date.`
- Waiting · amber · `There are updates waiting to be installed. They'll go on next time you restart.`
- Restart pending · amber · `This PC is waiting to restart to finish an update that has already downloaded.`
- Unknown · grey · `Couldn't check for updates.`

### 6. Security
- On and current · green · `Virus and threat protection is switched on and up to date.`
- Stale · amber · `Protection is switched on but hasn't updated for 9 days. Connect to the internet and let it catch up.`
- Off · red · `Real-time protection is switched off. This PC is not currently protected against viruses.`
- Unknown · grey · `Couldn't check your protection settings.`

### 7. Backup
- Found · green · `A backup of your files appears to be set up.`
- Not found · red · `We couldn't find a backup of the files on this PC. If it stopped working tomorrow, your photos and documents would go with it.`

*(Always "we couldn't find" — never "you have no backup". Honest about the limit of the check.)*

### 8. Battery — laptops only; omitted entirely on a desktop
- ≥80% · green · `Battery is in good health — it still holds about 92% of what it did when new.`
- 60–79% · amber · `Battery has worn to about 68% of its original capacity. It won't last as long as it used to.`
- <60% · red · `Battery is worn to about 51% of what it once held, and will start failing to hold charge.`

### 9. Last restarted
- ≤14 days · green · `Last restarted 3 days ago.`
- 15–30 days · amber · `Last restarted 21 days ago. Restarting now and again keeps Windows healthy.`
- >30 days · amber · `Last restarted 47 days ago. Worth restarting it when convenient.`

**Footer:**

```
Checked 20 November 2026 at 14:05. Nothing on this PC was changed, and nothing was sent anywhere.

[ Copy this report ]   [ Save as a text file ]   [ Book a free health check ]

Anything here we can help with? Ditechs — computer, laptop and network help in
Great Yarmouth, Gorleston and Lowestoft. ditechs.co.uk
```

*Copy this report* matters most: it lets a customer paste the whole thing into an email to
Mal, which is worth more than a phone call.

---

## Deliberately NOT in v1

- It fixes nothing. No cleanup, no turning off startup items. Fixes come later, once the
  wording and the trust are proven.
- No internet speed test, no software inventory, no "how much have I got left on my
  warranty" — each one adds a promise to keep.

## v2 candidates (not agreed, listed so they aren't forgotten)

- Offer to clear temporary files, with the same "here is exactly what will be removed,
  confirm first" treatment as Fix My Printer.
- Offer to switch off chosen startup programs.

## Acceptance criteria

- Runs without a UAC prompt on Windows 10 **and** Windows 11.
- Every check above exercised, including at least one "couldn't check" path.
- The report copies cleanly and pastes into an email as readable text.
- No banned word appears in any output.
- Mal reads it on his own PC and says "yes, that's what I'd want a customer to see".

## Delivery

Same shape as the existing two: small .exe + MSI, shipped as one zip with installer and
readme, its own page under `/tools/`, and a third card on the Tools hub.
