# CC-Merger for Mac

A native macOS port of **CCMerger** by Lazy Duchess — a tool that merges a folder
full of *The Sims 2* `.package` files into a handful of larger packages (or a
single one), the way Sims 3 and Sims 4 content can be merged. Fewer, larger
packages load faster and are easier to manage than thousands of loose files.

This port targets the Aspyr **Sims 2 Super Collection** on macOS. The merge
engine is carried over from the original essentially unchanged, so it produces
the same `.package` output; only the Windows-specific shell has been replaced.

## What's different from the Windows original

- **Cross-platform UI** — the WinForms interface is rebuilt in
  [Avalonia](https://avaloniaui.net/) (.NET 8), so it runs natively on macOS.
- **No `Delimon.Win32.IO`** — that dependency only existed to work around
  Windows' 260-character path limit, which macOS doesn't have. Replaced with
  plain `System.IO`.
- **No Windows taskbar code** — replaced by a prominent in-window progress bar
  with a live status line and percentage.
- **Honest progress** — the progress percentage is computed from the actual
  byte-copy pass and climbs 0 → 100 monotonically. (The Windows 1.4.1 release
  fought with a bar that could overshoot 100%; this avoids that class of bug.)
- **Faster** — the original called `GC.Collect()` on every entry during the
  write pass; that's removed.
- **Mac defaults** — the Downloads picker defaults to the Aspyr Super Collection
  Downloads folder, checking the sandboxed Mac App Store container first and the
  non-sandboxed location (older macOS / older installs) as a fallback.

## Building

Requires the .NET 8 SDK.

```sh
dotnet build -c Release
dotnet run            # or run the built CCMerger.dll
```

## Usage

1. Click **Browse…** and choose the folder of packages to merge (defaults to your
   Sims 2 Downloads folder).
2. Optionally adjust **Max package size (MB)** and **Max files per package**
   (`0` = no limit). The defaults — 100 MB / 1000 files — are recommended;
   setting them too high can make the game crash on boot.
3. Click **Merge** and pick where to save. The merged files are written next to
   that name (`Merged0.package`, `Merged1.package`, …), along with a
   `CCMerger.log`.

**Back up first.** Keep your unmerged CC somewhere safe and test your merged
content. As with the original tool, hacks and CAS parts are the safest to merge;
some objects are known to misbehave when merged (see below).

## Known issues (inherited from the original merge logic)

These are properties of how DBPF merging handles certain resources, not of the
Mac port specifically:

- Some objects can appear invisible after merging (e.g. Graverobber's add-ons,
  Dahlen bookcases, the Alienware computer).
- Fences are unreliable when merged.

## Credits & license

- Original **CCMerger** by **Lazy Duchess** — <https://github.com/LazyDuchess/CC-Merger>
- DBPF package-reading code derived from **FreeSO**
  (<https://github.com/riperiperi/FreeSO>) and **SimUnity2**.

This project as a whole is released under the **GNU General Public License v3.0**
(see [LICENSE](LICENSE)). The DBPF engine source files under [`Engine/`](Engine/)
that originate from CCMerger/FreeSO retain their original **Mozilla Public
License 2.0** headers; MPL-2.0 is GPL-compatible, so they are included here under
those terms while the combined work is distributed under the GPL.
