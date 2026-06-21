/*
 * MergeEngine — the Mac/Avalonia port of CCMerger's merge routine.
 *
 * This is a faithful port of CCMerger 1.4.1's Form1.ThreadedStuff() by Lazy
 * Duchess (MPL 2.0), with the WinForms/Windows pieces removed:
 *   - Delimon.Win32.IO (a MAX_PATH workaround) -> plain System.IO; macOS has no
 *     such path-length limit.
 *   - Windows taskbar progress -> reported through IProgress<MergeProgress>.
 *   - MessageBox / control state -> a returned MergeResult.
 *   - GC.Collect() on every entry in the data pass -> removed (it crippled
 *     throughput for no benefit).
 *
 * The on-disk DBPF output is byte-for-byte the same format the original writes,
 * including the automatic rewrite of 0xFFFFFFFF group IDs (done in DBPFFile on
 * read) so unrelated objects can share one package.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FSO.Files.Formats.DBPF;

namespace CCMergerMac
{
    public enum MergePhase { Scanning, Writing, Done }

    public sealed class MergeProgress
    {
        public MergePhase Phase;
        public double Percent;      // 0..100, honest and monotonic during Writing
        public string Status = "";  // human-readable line under the bar
    }

    public sealed class MergeOptions
    {
        public string SourceFolder = "";
        public string TargetPath = "";       // chosen base name; files get 0,1,2... appended
        public long MaxPackageSizeBytes = 100_000_000;
        public uint MaxFileCount = 1000;
        public bool WriteLog = false;
    }

    public sealed class MergeResult
    {
        public int SourcePackages;
        public int OutputPackages;
        public long TotalEntries;
        public bool Failed;
        public string FailTrace = "";
        public string LogPath = "";
        public List<(string File, string Error)> Errors = new();
        public bool Cancelled;
    }

    // One output package: a bin of source packages whose entries get concatenated.
    internal sealed class PackageBin
    {
        public string Name = "";
        public List<DBPFFile> Packages = new();
        public bool Compress = false;
    }

    public static class MergeEngine
    {
        private const uint DirTypeID = 0xE86B1EEF;

        public static MergeResult Run(MergeOptions opt, IProgress<MergeProgress> progress, CancellationToken token)
        {
            var result = new MergeResult();

            // ---- Phase 1: scan & bin -------------------------------------------------
            var bins = new List<PackageBin>();
            var current = new PackageBin();
            bins.Add(current);
            long currentSize = 0;
            uint currentCount = 0;
            int scanned = 0;

            foreach (var file in Directory.EnumerateFiles(opt.SourceFolder, "*.package", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var pack = new DBPFFile(file);
                    long len = new FileInfo(file).Length;

                    // Start a new bin if adding this package would exceed either limit.
                    if ((opt.MaxPackageSizeBytes != 0 && currentSize + len >= opt.MaxPackageSizeBytes) ||
                        (opt.MaxFileCount != 0 && currentCount + pack.NumEntries >= opt.MaxFileCount))
                    {
                        current = new PackageBin();
                        bins.Add(current);
                        currentSize = 0;
                        currentCount = 0;
                    }

                    currentSize += len;
                    currentCount += pack.NumEntries;
                    if (pack.hasCompression)
                        current.Compress = true;
                    current.Packages.Add(pack);
                    scanned++;

                    if ((scanned & 0x1F) == 0)
                        progress?.Report(new MergeProgress
                        {
                            Phase = MergePhase.Scanning,
                            Percent = 0,
                            Status = $"Scanning… {scanned} packages found"
                        });
                }
                catch (Exception e)
                {
                    result.Errors.Add((file, e.ToString()));
                }
            }
            result.SourcePackages = scanned;

            // Count the real work up front: every data entry we will copy. Driving
            // the bar off this — the slow byte-copy pass — keeps it honest (0..100,
            // never overshooting, which is the bug the original never fully fixed).
            long totalEntries = 0;
            foreach (var bin in bins)
                foreach (var p in bin.Packages)
                    foreach (var kv in p.m_EntryByID)
                        if (kv.Value.TypeID != DirTypeID)
                            totalEntries++;
            result.TotalEntries = totalEntries;

            if (totalEntries == 0)
            {
                progress?.Report(new MergeProgress { Phase = MergePhase.Done, Percent = 0, Status = "No packages to merge." });
                result.OutputPackages = 0;
                return result;
            }

            // ---- Phase 2: write merged packages -------------------------------------
            long processed = 0;
            try
            {
                for (int i = 0; i < bins.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var bin = bins[i];

                    // Build the DIR resource (compressed-entry -> uncompressed size).
                    byte[] dirfil;
                    using (var dirStream = new MemoryStream())
                    using (var dirWriter = new BinaryWriter(dirStream))
                    {
                        foreach (var p in bin.Packages)
                            foreach (var kv in p.m_EntryByID)
                            {
                                var e = kv.Value;
                                if (e.TypeID != DirTypeID && e.uncompressedSize != 0)
                                {
                                    dirWriter.Write(e.TypeID);
                                    dirWriter.Write(e.GroupID);
                                    dirWriter.Write(e.InstanceID);
                                    dirWriter.Write(e.InstanceID2);
                                    dirWriter.Write(e.uncompressedSize);
                                }
                            }
                        dirfil = dirStream.ToArray();
                    }

                    var fname = Path.Combine(
                        Path.GetDirectoryName(opt.TargetPath) ?? "",
                        Path.GetFileNameWithoutExtension(opt.TargetPath) + i + ".package");
                    bin.Name = fname;

                    using var mStream = new FileStream(fname, FileMode.Create);
                    using var mWriter = new BinaryWriter(mStream);

                    // --- Header (DBPF 1.2, index 7.2) ---
                    mWriter.Write(new[] { 'D', 'B', 'P', 'F' });
                    mWriter.Write((int)1);   // major
                    mWriter.Write((int)2);   // minor
                    mWriter.Write(new byte[12]);
                    mWriter.Write((int)0);   // date created
                    mWriter.Write((int)0);   // date modified
                    mWriter.Write((int)7);   // index major
                    var entryCountOffset = mStream.Position;
                    mWriter.Write((int)0);   // num entries (patched later)
                    var indexOffField = mStream.Position;
                    mWriter.Write((int)0);   // index offset (patched later)
                    var indexSizeField = mStream.Position;
                    mWriter.Write((int)0);   // index size (patched later)
                    mWriter.Write((int)0);   // trash entry count
                    mWriter.Write((int)0);   // trash index offset
                    mWriter.Write((int)0);   // trash index size
                    mWriter.Write((int)2);   // index minor
                    mWriter.Write(new byte[32]);

                    // --- Index records (offsets patched in during data pass) ---
                    var indexStart = mStream.Position;
                    int entryCount = 0;
                    long dirFilOffsetField = 0;

                    if (bin.Compress)
                    {
                        mWriter.Write(DirTypeID);       // TypeID
                        mWriter.Write(DirTypeID);       // GroupID
                        mWriter.Write(0x286B1F03u);     // InstanceID
                        mWriter.Write(0x00000000u);     // ResourceID
                        dirFilOffsetField = mStream.Position;
                        mWriter.Write((int)0);          // file offset (patched)
                        mWriter.Write(dirfil.Length);   // file size
                        entryCount++;
                    }

                    foreach (var p in bin.Packages)
                        foreach (var kv in p.m_EntryByID)
                        {
                            var e = kv.Value;
                            if (e.TypeID == DirTypeID) continue;
                            mWriter.Write(e.TypeID);
                            mWriter.Write(e.GroupID);
                            mWriter.Write(e.InstanceID);
                            mWriter.Write(e.InstanceID2);
                            e.writeOff = mStream.Position;
                            mWriter.Write((int)0);       // file offset (patched)
                            mWriter.Write(e.FileSize);
                            entryCount++;
                        }

                    // Patch header counts/offsets now that the index size is known.
                    var afterIndex = mStream.Position;
                    mStream.Position = entryCountOffset;
                    mWriter.Write(entryCount);
                    mStream.Position = indexOffField;
                    mWriter.Write((int)indexStart);
                    mStream.Position = indexSizeField;
                    mWriter.Write((int)(afterIndex - indexStart));
                    mStream.Position = afterIndex;

                    // DIR resource body.
                    if (bin.Compress)
                    {
                        var pos = mStream.Position;
                        mStream.Position = dirFilOffsetField;
                        mWriter.Write((int)pos);
                        mStream.Position = pos;
                        mWriter.Write(dirfil);
                    }

                    // --- Data pass: copy each entry's bytes, patch its offset ---
                    foreach (var p in bin.Packages)
                        foreach (var kv in p.m_EntryByID)
                        {
                            token.ThrowIfCancellationRequested();
                            var e = kv.Value;
                            if (e.TypeID == DirTypeID) continue;

                            var pos = mStream.Position;
                            mStream.Position = e.writeOff;
                            mWriter.Write((int)pos);
                            mStream.Position = pos;
                            mWriter.Write(p.GetEntry(e));

                            processed++;
                            if ((processed & 0x3F) == 0 || processed == totalEntries)
                            {
                                double pct = Math.Clamp(processed * 100.0 / totalEntries, 0, 100);
                                progress?.Report(new MergeProgress
                                {
                                    Phase = MergePhase.Writing,
                                    Percent = pct,
                                    Status = $"Writing package {i + 1} of {bins.Count} — {processed:N0} of {totalEntries:N0} files ({pct:0}%)"
                                });
                            }
                        }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                return result;
            }
            catch (Exception e)
            {
                result.Failed = true;
                result.FailTrace = e.ToString();
            }

            result.OutputPackages = bins.Count;

            // ---- Logs ---------------------------------------------------------------
            WriteLogs(opt, bins, result);

            progress?.Report(new MergeProgress
            {
                Phase = MergePhase.Done,
                Percent = result.Failed ? 0 : 100,
                Status = result.Failed ? "Merge failed." : "Done."
            });
            return result;
        }

        private static void WriteLogs(MergeOptions opt, List<PackageBin> bins, MergeResult result)
        {
            var dir = Path.GetDirectoryName(opt.TargetPath) ?? "";
            var logName = Path.Combine(dir, "CCMerger.log");
            using (var log = new StreamWriter(logName))
            {
                log.WriteLine("CCMerger log - Mac port (engine v1.4.1)");
                log.WriteLine();
                log.WriteLine("[Settings Used]");
                log.WriteLine("Max files per package: " + opt.MaxFileCount);
                log.WriteLine("Max package size: " + (opt.MaxPackageSizeBytes / 1_000_000) + " mb");
                log.WriteLine();
                log.WriteLine("[Results]");
                log.WriteLine($"Merged {result.SourcePackages} packages containing {result.TotalEntries} files into {result.OutputPackages} packages.");
                log.WriteLine();
                log.WriteLine("[Errors]");
                log.WriteLine(result.Errors.Count + " packages failed to merge.");
                foreach (var (file, error) in result.Errors)
                {
                    log.WriteLine("Package name: " + file);
                    log.WriteLine(error);
                }
                log.WriteLine(result.Failed ? "Failed to merge packages :(\n" + result.FailTrace
                                            : "Packages were merged succesfully :)");
            }

            if (!opt.WriteLog) return;
            foreach (var bin in bins)
            {
                if (string.IsNullOrEmpty(bin.Name)) continue;
                var txt = Path.Combine(Path.GetDirectoryName(bin.Name) ?? "",
                                       Path.GetFileNameWithoutExtension(bin.Name) + ".txt");
                using var w = new StreamWriter(txt);
                w.WriteLine("Contents of " + Path.GetFileName(bin.Name) + ":");
                w.WriteLine();
                foreach (var p in bin.Packages)
                    w.WriteLine(Path.GetFileName(p.fname));
                w.WriteLine();
            }
        }
    }
}
