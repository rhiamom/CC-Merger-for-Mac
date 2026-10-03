using System;
using System.Threading;
using Avalonia;

namespace CCMergerMac
{
    // Minimal synchronous progress sink for headless runs (ordered console output).
    internal sealed class ConsoleProgress : IProgress<MergeProgress>
    {
        private int _lastPct = -1;
        public void Report(MergeProgress p)
        {
            int pct = (int)p.Percent;
            if (p.Phase == MergePhase.Writing && pct == _lastPct) return;
            _lastPct = pct;
            Console.WriteLine($"[{p.Phase}] {pct,3}%  {p.Status}");
        }
    }

    internal static class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things
        // aren't initialized yet and stuff might break.
        [System.STAThread]
        public static void Main(string[] args)
        {
            // Headless CLI:  --merge <sourceFolder> <targetPathBase> [maxSizeMB] [maxFileCount]
            // maxSizeMB/maxFileCount of 0 = no limit for that axis.
            if (args.Length >= 3 && args[0] == "--merge")
            {
                RunHeadlessMerge(args);
                return;
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static void RunHeadlessMerge(string[] args)
        {
            var opt = new MergeOptions
            {
                SourceFolder = args[1],
                TargetPath   = args[2],
                MaxPackageSizeBytes = args.Length > 3 ? long.Parse(args[3]) * 1_000_000L : 100_000_000L,
                MaxFileCount = args.Length > 4 ? uint.Parse(args[4]) : 1000u,
                WriteLog = true,
            };
            Console.WriteLine($"Merging '{opt.SourceFolder}'");
            Console.WriteLine($"  -> base '{opt.TargetPath}'  (maxSize={opt.MaxPackageSizeBytes/1_000_000}MB, maxFiles={opt.MaxFileCount})");
            var res = MergeEngine.Run(opt, new ConsoleProgress(), CancellationToken.None);
            Console.WriteLine();
            Console.WriteLine($"Source packages : {res.SourcePackages}");
            Console.WriteLine($"Output packages : {res.OutputPackages}");
            Console.WriteLine($"Total entries   : {res.TotalEntries}");
            Console.WriteLine($"Per-package errs: {res.Errors.Count}");
            foreach (var (f, e) in res.Errors) Console.WriteLine("  ERR: " + f);
            if (res.Failed) Console.WriteLine("MERGE FAILED:\n" + res.FailTrace);
            else Console.WriteLine("OK");
            Environment.Exit(res.Failed ? 1 : 0);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
