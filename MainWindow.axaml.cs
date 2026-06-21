using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace CCMergerMac
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();

            // Default to the Aspyr Mac Sims 2 Downloads folder if we have no saved
            // value. Two known layouts: the sandboxed Mac App Store container, and
            // the non-sandboxed (direct-Aspyr) location. Use whichever exists.
            if (string.IsNullOrEmpty(_settings.Folder))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] candidates =
                {
                    // Mac App Store (sandboxed) build
                    System.IO.Path.Combine(home,
                        "Library", "Containers", "com.aspyr.sims2.appstore", "Data",
                        "Library", "Application Support", "Aspyr", "The Sims 2", "Downloads"),
                    // Non-sandboxed (direct-Aspyr) build
                    System.IO.Path.Combine(home,
                        "Library", "Application Support", "Aspyr", "The Sims 2", "Downloads"),
                };
                foreach (var c in candidates)
                {
                    if (System.IO.Directory.Exists(c)) { _settings.Folder = c; break; }
                }
            }

            FolderBox.Text = _settings.Folder;
            PackSize.Value = Math.Clamp(_settings.FileSize / 1_000_000m, 0, 1000);
            PackFiles.Value = Math.Clamp(_settings.FileCount, 0u, 8000u);
            LogBox.IsChecked = _settings.Log;

            BrowseButton.Click += BrowseButton_Click;
            MergeButton.Click += MergeButton_Click;
            CancelButton.Click += (_, _) => _cts?.Cancel();
            FolderBox.TextChanged += (_, _) => { _settings.Folder = FolderBox.Text ?? ""; _settings.Save(); };
            PackSize.ValueChanged += (_, _) =>
            {
                _settings.FileSize = (long)(PackSize.Value ?? 0) * 1_000_000;
                _settings.Save();
            };
            PackFiles.ValueChanged += (_, _) =>
            {
                _settings.FileCount = (uint)(PackFiles.Value ?? 0);
                _settings.Save();
            };
            LogBox.IsCheckedChanged += (_, _) => { _settings.Log = LogBox.IsChecked == true; _settings.Save(); };
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the folder with the packages to merge",
                AllowMultiple = false
            });
            var picked = folders.FirstOrDefault();
            if (picked != null)
            {
                FolderBox.Text = picked.Path.LocalPath;
            }
        }

        private async void MergeButton_Click(object sender, RoutedEventArgs e)
        {
            var source = FolderBox.Text ?? "";
            if (!System.IO.Directory.Exists(source))
            {
                await Message("Invalid directory", "That folder doesn't exist. Pick the folder with your packages.");
                return;
            }

            var save = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save merged packages as…",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(source),
                SuggestedFileName = "Merged",
                DefaultExtension = "package",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Sims 2 package") { Patterns = new[] { "*.package" } }
                }
            });
            if (save == null) return;

            var opt = new MergeOptions
            {
                SourceFolder = source,
                TargetPath = save.Path.LocalPath,
                MaxPackageSizeBytes = (long)(PackSize.Value ?? 0) * 1_000_000,
                MaxFileCount = (uint)(PackFiles.Value ?? 0),
                WriteLog = LogBox.IsChecked == true
            };

            SetBusy(true);
            StatusText.Text = "Scanning…";
            PercentText.Text = "0%";
            Bar.Value = 0;

            // Progress<T> created on the UI thread marshals callbacks back to it.
            var progress = new Progress<MergeProgress>(p =>
            {
                Bar.Value = p.Percent;
                PercentText.Text = $"{p.Percent:0}%";
                StatusText.Text = p.Status;
            });

            _cts = new CancellationTokenSource();
            MergeResult result = null;
            Exception fault = null;
            try
            {
                result = await Task.Run(() => MergeEngine.Run(opt, progress, _cts.Token), _cts.Token);
            }
            catch (OperationCanceledException) { /* handled below */ }
            catch (Exception ex) { fault = ex; }

            SetBusy(false);

            if (fault != null)
            {
                StatusText.Text = "Merge failed.";
                await Message("Error", "Something went wrong:\n\n" + fault.Message);
                return;
            }
            if (result == null || result.Cancelled)
            {
                StatusText.Text = "Cancelled.";
                PercentText.Text = "0%";
                Bar.Value = 0;
                return;
            }

            if (result.Failed)
            {
                StatusText.Text = "Merge failed.";
                await Message("Error",
                    "Failed to merge the packages. See CCMerger.log in the output folder for details.");
            }
            else if (result.TotalEntries == 0)
            {
                await Message("Nothing to merge", "There are no packages to merge in there, or they're all empty.");
            }
            else if (result.Errors.Count > 0)
            {
                await Message("Finished with warnings",
                    $"Merged {result.SourcePackages} packages into {result.OutputPackages}.\n\n" +
                    $"{result.Errors.Count} package(s) couldn't be read — see CCMerger.log for details.");
            }
            else
            {
                await Message("Done",
                    $"Merged {result.SourcePackages} packages into {result.OutputPackages} file(s).");
            }
        }

        private void SetBusy(bool busy)
        {
            MergeButton.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
            FolderBox.IsEnabled = !busy;
            PackSize.IsEnabled = !busy;
            PackFiles.IsEnabled = !busy;
            LogBox.IsEnabled = !busy;
            CancelButton.IsVisible = busy;
        }

        private async Task Message(string title, string body)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Padding = new Avalonia.Thickness(24, 8) };
            ok.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(22),
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok
                }
            };
            await dialog.ShowDialog(this);
        }
    }
}
