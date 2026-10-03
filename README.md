# Folder Size Pro

[Tiếng Việt](README.vi.md)

A Windows app that measures the storage used by drives, folders and files **down to the byte**, and **explains** every difference from Explorer, `chkdsk` and the drive's "Used" figure.

- GUI: `FolderSizePro.exe` (WPF, follows the Windows light/dark theme, English / Vietnamese)
- Command line: `fsp.exe` (same measuring core as the GUI, so the same path gives the same numbers)
- No telemetry, no auto-update, no background service, nothing sent over the network. It only **reads** metadata and never reads file contents.

## Why the numbers can be trusted

Every number follows fixed **measurement rules** (see `use-cases.md`, Q1–Q10):

| | |
|---|---|
| **Two numbers side by side** | *Size* (logical) and *On disk* (real clusters: rounded to the cluster, minus NTFS compression / sparse ranges / CompactOS savings) |
| **Hard links** | a file is counted **once** (owner = the smallest path), so WinSxS and drive totals never exceed "Used" |
| **Reparse points** | junctions / symlinks / mount points are **not followed**: no double counting, no infinite loops |
| **Cloud files** (OneDrive…) | only attributes are read, so files are **never** downloaded; online-only files are 0 on disk |
| **ADS** | added to the owning file (required to get CompactOS files right) |
| **Small files resident in the MFT** | 0 clusters, tracked separately, not double-counted with `$MFT` |
| **Long paths** (> 260), unusual names | measured (`\\?\`) |
| **Volume reconciliation** | splits "Used" into *measured* + *NTFS metadata* + *pagefile…* + *Recycle Bin* + *System Volume Information* + **unexplained**; the last row is never hidden |
| **Timestamps** | every result records when it was scanned |

Sizes in Windows *directory listings* can be **stale** for hard links and **0** for CompactOS files, so the default mode opens each file (attributes only) to get the real numbers. "Fast scan" skips this step and is always labelled **LESS ACCURATE**.

## Running

- **GUI:** open `FolderSizePro.exe`, then pick a drive on the left, use "Pick a folder or drive to scan", type a path and press Enter, or drag a folder onto the window.
- **Administrator** ("Run as Administrator" button, through the Windows UAC prompt): also measures blocked areas (`System Volume Information`…) and enables **MFT scan** on NTFS drives (a whole drive in seconds).
- **Command line:**

```text
fsp scan D:\Data --depth 2 --top 20
fsp scan D:\ --json --out d.json --save d.fsp
fsp reconcile D:
fsp compare old.fsp new.fsp
```

Exit codes: `0` complete · `1` some items inaccessible · `2` bad argument/path · `3` cancelled/partial · `4` Administrator required.

## Features

Folder tree · Treemap · File types · Largest files · Volume reconciliation · Search/filter (name, extension, size, date, attributes, regex) · Details (exact byte counts, hard links, ADS, owner, link target) · Rescan a branch · `.fsp` snapshots + **compare** two snapshots · Export HTML / CSV / JSON · Live change tracking · Move to Recycle Bin (the confirmation tells the truth: *frees 0 B now*, *X after emptying the Recycle Bin*, minus hard links that live elsewhere).

## Safety

- The app **never deletes permanently**. It only moves items to the Recycle Bin, and refuses up front if the Recycle Bin is disabled or full, the drive has no Recycle Bin, or the path is too long (Windows would delete outright in those cases).
- Deletion is blocked for: drive roots, `Windows` (including System32/WinSxS…), `Program Files`, `ProgramData`, `Users`, the current profile, `System Volume Information`, `$Recycle.Bin`, system files, and junctions/mount points/directory symlinks, even when running as Administrator.
- The app's own files (settings, log, session) are written only to `%LOCALAPPDATA%\FolderSizePro`, never to the drive being measured.

## Verification

`tools\FspVerify` runs the **real** scanner over sample files (hard links, junction loops, long paths, compression, sparse files, ADS, permission-denied folders, files changing during the scan…) and prints the real numbers next to independent sources (`FSCTL_GET_RETRIEVAL_POINTERS`, `FindFirstStream`, PowerShell). Results are in `gap-analysis.md` (Vietnamese).
MFT mode needs Administrator: run `tools\verify-mft.ps1` from an elevated PowerShell.

## Known limitations

- MFT mode reads the on-disk image of `$MFT`; the most recent changes not yet flushed by NTFS may be missing.
- Cloud folders whose listing has not been downloaded (`RECALL_ON_OPEN`) are **not listed** (listing them would trigger a download); they show as "listing not downloaded".
- On Windows 11 the context-menu entry is under "Show more options".
- ADS on **folders** are not counted (rare).
- Out of scope: duplicate finding, automatic cleanup, scheduling, reading file contents, multiple tabs, drivers/services.

## Build

```text
dotnet build FolderSizePro.slnx -c Release
powershell scripts\publish.ps1          # portable zip
powershell scripts\build-installer.ps1  # installer (needs Inno Setup 6)
```

## Download

The installer and portable build are on the [Releases](https://github.com/nguyennhuanhle/folder-size-pro/releases) page. The build is not code-signed, so Windows SmartScreen may warn on first run ("More info" → "Run anyway").

## License

[MIT](LICENSE)
