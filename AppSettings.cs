using System;
using System.IO;
using System.Text.Json;

namespace CCMergerMac
{
    // Persists the same four values the Windows version kept in app.config,
    // stored under ~/Library/Application Support/CCMerger/settings.json.
    public sealed class AppSettings
    {
        public string Folder { get; set; } = "";
        public long FileSize { get; set; } = 100_000_000; // bytes
        public uint FileCount { get; set; } = 1000;
        public bool Log { get; set; } = false;

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CCMerger");
        private static string FilePath => Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { /* fall through to defaults */ }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort; not worth interrupting the user */ }
        }
    }
}
