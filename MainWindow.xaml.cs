using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FLogS
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
        private static extern bool ShouldSystemUseDarkMode();

        private readonly static SolidColorBrush[][] brushCombos =
        {   // 0 = Dark mode, 1 = Light mode.
            new SolidColorBrush[] { Brushes.Black, Brushes.White }, // Textboxes
            new SolidColorBrush[] { Brushes.LightBlue, Brushes.Beige }, // Buttons
            new SolidColorBrush[] { new(new() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray }, // Borders
            new SolidColorBrush[] { Brushes.Pink, Brushes.Red }, // Error messages (and the ADL warning)
            new SolidColorBrush[] { Brushes.Yellow, Brushes.DarkRed }, // Warning messages
            new SolidColorBrush[] { new(new() { A = 0xFF, R = 0x4C, G = 0x4C, B = 0x4C }), Brushes.DarkGray }, // TabControl
            new SolidColorBrush[] { Brushes.Transparent, new(new Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }) }, // DatePicker borders
            new SolidColorBrush[] { Brushes.DimGray, Brushes.Beige }, // PanelGrids
            new SolidColorBrush[] { Brushes.LightBlue, Brushes.DarkBlue }, // Hyperlinks
        };
        private static int brushPalette = 1;
        private static FLogS_ERROR directoryError = FLogS_ERROR.NO_SOURCES;
        private static FLogS_WARNING directoryWarning;
        private static FLogS_ERROR fileError = FLogS_ERROR.NO_SOURCE;
        private static FLogS_WARNING fileWarning;
        private static int filesProcessed;
        private static bool overrideFormat = false;
        private static FLogS_ERROR phraseError = FLogS_ERROR.NO_SOURCES;
        private static FLogS_WARNING phraseWarning;
        private static MessagePool? pool;
        private static int reversePalette = 0;
        private readonly static ContextSettings settings = new();

        private enum FLogS_ERROR
        {
            NONE,
            BAD_REGEX,
            DEST_NOT_DIRECTORY,
            DEST_NOT_FILE,
            DEST_NOT_FOUND,
            DEST_SENSITIVE,
            NO_DEST,
            NO_DEST_DIR,
            NO_REGEX,
            NO_SOURCE,
            NO_SOURCES,
            SOURCE_CONFLICT,
            SOURCE_EQUALS_DEST,
            SOURCE_NOT_FOUND,
            SOURCES_NOT_FOUND,
        }

        private enum FLogS_WARNING
        {
            NONE,
            MULTI_OVERWRITE,
            SINGLE_OVERWRITE,
        }

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = settings;
        }

        private static void ChangeStyle(DependencyObject? sender)
        {
            if (sender is null)
                return;

            switch (sender?.DependencyObjectType.Name)
            {
                case "Button":
                    (sender as Button).Background = brushCombos[1][brushPalette];
                    break;
                case "DatePicker":
                    (sender as DatePicker).Background = brushCombos[0][brushPalette];
                    (sender as DatePicker).BorderBrush = brushCombos[6][brushPalette];
                    (sender as DatePicker).Foreground = brushCombos[0][reversePalette];
                    break;
                case "Grid":
                    if ((sender as Grid).Tag != null && (sender as Grid).Tag.Equals("PanelGrid"))
                        (sender as Grid).Background = brushCombos[7][brushPalette];
                    break;
                case "Label":
                    (sender as Label).Foreground = brushCombos[0][reversePalette];
                    break;
                case "ListBox":
                    (sender as ListBox).Background = brushCombos[0][brushPalette];
                    (sender as ListBox).BorderBrush = brushCombos[2][reversePalette];
                    (sender as ListBox).Foreground = brushCombos[0][reversePalette];
                    break;
                case "ProgressBar":
                    (sender as ProgressBar).Background = brushCombos[0][brushPalette];
                    break;
                case "StackPanel":
                    (sender as StackPanel).Background = brushCombos[2][brushPalette];
                    break;
                case "TabControl":
                    (sender as TabControl).Background = brushCombos[5][brushPalette];
                    break;
                case "TextBlock":
                    (sender as TextBlock).Foreground = brushCombos[0][reversePalette];
                    break;
                case "TextBox":
                    (sender as TextBox).Background = brushCombos[0][brushPalette];
                    (sender as TextBox).BorderBrush = brushCombos[2][reversePalette];
                    (sender as TextBox).Foreground = brushCombos[0][reversePalette];
                    break;
            }

            foreach (object dp in LogicalTreeHelper.GetChildren(sender))
                ChangeStyle(dp as DependencyObject);

            if ((sender.GetValue(TagProperty) as string ?? string.Empty).Equals("WarningLabel"))
                sender.SetValue(ForegroundProperty, brushCombos[3][brushPalette]);

            return;
        }

        private void DatePicker_Update(object? sender, RoutedEventArgs e)
        {
            if ((sender as DatePicker).Name.Contains("BeforeDate"))
            {
                BeforeDate.SelectedDate = DirectoryBeforeDate.SelectedDate = PhraseBeforeDate.SelectedDate = (sender as DatePicker).SelectedDate;
                return;
            }

            AfterDate.SelectedDate = DirectoryAfterDate.SelectedDate = PhraseAfterDate.SelectedDate = (sender as DatePicker).SelectedDate;
            return;
        }

        private static string DialogFileSelect(bool outputSelect = false, bool checkExists = false, bool multi = true)
        {
            OpenFileDialog openFileDialog = new()
            {
                CheckFileExists = checkExists,
                Multiselect = multi,
                InitialDirectory = outputSelect ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : Path.Exists(Common.defaultLogDir) ? Common.defaultLogDir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            };
            if (openFileDialog.ShowDialog() == true)
                return string.Join(";", openFileDialog.FileNames.Where(file => !file.Contains(".idx"))); // IDX files contain metadata relating to the corresponding (usually extension-less) log files.
                                                                                                         // They will never contain actual messages, so we exclude them unconditionally.
            return string.Empty;
        }

        private static string DialogFolderSelect(bool outputSelect = false)
        {
            System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new()
            {
                ShowNewFolderButton = true,
                InitialDirectory = outputSelect ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : Path.Exists(Common.defaultLogDir) ? Common.defaultLogDir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            };
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                return folderBrowserDialog.SelectedPath;

            return string.Empty;
        }

        private void DirectoryRunButton_Click(object? sender, RoutedEventArgs e)
        {
            TextboxUpdated(sender, e);
            if (!DirectoryRunButton.IsEnabled)
                return;

            try
            {
                string[] files = DirectorySource.Text.Split(';');
                Common.plaintext = DirectorySaveHTMLCheckbox.IsChecked == false;
                pool = new()
                {
                    destDir = DirectoryOutput.Text,
                    divide = DirectoryDivideLogsCheckbox.IsChecked == true,
                    dtAfter = DirectoryAfterDate.SelectedDate ?? Common.DTFromStamp(1),
                    dtBefore = DirectoryBeforeDate.SelectedDate ?? DateTime.UtcNow,
                    phrase = string.Empty,
                    saveTruncated = DirectorySaveTruncatedCheckbox.IsChecked == true,
                    srcFile = DirectorySource.Text,
                    totalSize = new()
                };

                foreach (string logfile in files)
                {
                    Common.fileListing[logfile] = new(logfile);
                    pool.totalSize += Common.fileListing[logfile].Length;
                }

                ProcessFiles(files);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void DstDirectoryButton_Click(object? sender, RoutedEventArgs e)
        {
            DirectoryOutput.Text = DialogFolderSelect(true);
        }

        private void DstFileButton_Click(object? sender, RoutedEventArgs e)
        {
            FileOutput.Text = DialogFileSelect(outputSelect: true, multi: false);
        }

        private void DstPhraseButton_Click(object? sender, RoutedEventArgs e)
        {
            PhraseOutput.Text = DialogFolderSelect(true);
        }

        private void FormatOverride(object? sender, RoutedEventArgs e)
        {
            if (sender is null)
                return;

            if ((sender as CheckBox).Name.Contains("DivideLogs"))
            {
                DivideLogsCheckbox.IsChecked = DirectoryDivideLogsCheckbox.IsChecked = PhraseDivideLogsCheckbox.IsChecked = (sender as CheckBox).IsChecked;
                return;
            }

            if ((sender as CheckBox).Name.Contains("SaveTruncated"))
            {
                SaveTruncatedCheckbox.IsChecked = DirectorySaveTruncatedCheckbox.IsChecked = PhraseSaveTruncatedCheckbox.IsChecked = (sender as CheckBox).IsChecked;
                return;
            }

            if ((sender as CheckBox).Name.Contains("SaveHTML"))
            {
                SaveHTMLCheckbox.IsChecked = DirectorySaveHTMLCheckbox.IsChecked = PhraseSaveHTMLCheckbox.IsChecked = (sender as CheckBox).IsChecked;
                return;
            }
        }

        private static string GetErrorMessage(FLogS_ERROR eCode, FLogS_WARNING wCode) => (eCode, wCode) switch
        {
            (FLogS_ERROR.BAD_REGEX, _) => "Search text contains an invalid RegEx pattern.",
            (FLogS_ERROR.DEST_NOT_DIRECTORY, _) => "Destination is not a directory.",
            (FLogS_ERROR.DEST_NOT_FILE, _) => "Destination is not a file.",
            (FLogS_ERROR.DEST_NOT_FOUND, _) => "Destination directory does not exist.",
            (FLogS_ERROR.DEST_SENSITIVE, _) => "Destination appears to contain source log data.",
            (FLogS_ERROR.NO_DEST, _) => "No destination file selected.",
            (FLogS_ERROR.NO_DEST_DIR, _) => "No destination directory selected.",
            (FLogS_ERROR.NO_REGEX, _) => "No search text entered.",
            (FLogS_ERROR.NO_SOURCE, _) => "No source log file selected.",
            (FLogS_ERROR.NO_SOURCES, _) => "No source log files selected.",
            (FLogS_ERROR.SOURCE_CONFLICT, _) => "One or more source files exist in the destination.",
            (FLogS_ERROR.SOURCE_EQUALS_DEST, _) => "Source and destination files are identical.",
            (FLogS_ERROR.SOURCE_NOT_FOUND, _) => "Source log file does not exist.",
            (FLogS_ERROR.SOURCES_NOT_FOUND, _) => "One or more source files do not exist.",

            (FLogS_ERROR.NONE, FLogS_WARNING.MULTI_OVERWRITE) => "One or more files will be overwritten.",
            (FLogS_ERROR.NONE, FLogS_WARNING.SINGLE_OVERWRITE) => "Destination file will be overwritten.",
            (FLogS_ERROR.NONE, _) => "",

            (_, FLogS_WARNING.NONE) => "An unknown error has occurred.",
            (_, _) => "An unknown error has occurred.",
        };

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void MainGrid_Loaded(object? sender, RoutedEventArgs e)
        {
            Common.fileListing = new();
            overrideFormat = false;
            pool = new();
            Common.processing = false;

            if (File.Exists(Common.errorFile))
                File.Delete(Common.errorFile);

            try
            {
                if (ShouldSystemUseDarkMode())
                    ThemeSelector_Click(sender, e);
            }
            catch (Exception)
            {
                // Do nothing; default to light mode.
            }
        }

        private void MainGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (Common.processing)
                return;

            // We will rescan for errors upon user interaction, in case of e.g. a source file being deleted after its path has already been entered.
            TextboxUpdated(sender, e);

            RegExLink.Foreground = brushCombos[8][brushPalette];
            if (RegExLink.IsMouseOver)
                RegExLink.Foreground = brushCombos[3][brushPalette];

            if (!overrideFormat)
            {
                if (DirectoryOutput.Text.EndsWith(".html"))
                    DirectorySaveHTMLCheckbox.IsChecked = true;
                if (PhraseOutput.Text.EndsWith(".html"))
                    PhraseSaveHTMLCheckbox.IsChecked = true;
                if (FileOutput.Text.EndsWith(".html"))
                    SaveHTMLCheckbox.IsChecked = true;

                if (DirectoryOutput.Text.EndsWith(".txt"))
                    DirectorySaveHTMLCheckbox.IsChecked = false;
                if (PhraseOutput.Text.EndsWith(".txt"))
                    PhraseSaveHTMLCheckbox.IsChecked = false;
                if (FileOutput.Text.EndsWith(".txt"))
                    SaveHTMLCheckbox.IsChecked = false;
            }
        }

        private void PhraseRunButton_Click(object? sender, RoutedEventArgs e)
        {
            TextboxUpdated(sender, e);
            if (!PhraseRunButton.IsEnabled)
                return;

            try
            {
                string[] files = PhraseSource.Text.Split(';');
                Common.plaintext = PhraseSaveHTMLCheckbox.IsChecked == false;
                pool = new()
                {
                    destDir = PhraseOutput.Text,
                    divide = PhraseDivideLogsCheckbox.IsChecked == true,
                    dtAfter = PhraseAfterDate.SelectedDate ?? Common.DTFromStamp(1),
                    dtBefore = PhraseBeforeDate.SelectedDate ?? DateTime.UtcNow,
                    phrase = PhraseSearch.Text,
                    saveTruncated = PhraseSaveTruncatedCheckbox.IsChecked == true,
                    srcFile = PhraseSource.Text,
                    totalSize = new()
                };

                foreach (string logfile in files)
                {
                    Common.fileListing[logfile] = new(logfile);
                    pool.totalSize += Common.fileListing[logfile].Length;
                }

                ProcessFiles(files);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void ProcessFiles(string[]? args = null, bool batch = true)
        {
            PhraseEXBox.Content = DirectoryEXBox.Content = EXBox.Content = string.Empty;
            filesProcessed = args?.Length ?? 1;
            overrideFormat = false;
            Common.processing = true;

            pool.totalSize.Simplify();
            pool.totalSize.Magnitude(1);
            DirectoryProgress.Maximum = FileProgress.Maximum = PhraseProgress.Maximum = pool.totalSize.bytes;

            pool.ResetStats();
            TransitionMenus(false);
            UpdateLogs();

            BackgroundWorker worker = new()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            worker.DoWork += batch ? pool.BatchProcess : pool.BeginRoutine;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_Completed;

            worker.RunWorkerAsync(args);
        }

        private void RunButton_Click(object? sender, RoutedEventArgs e)
        {
            TextboxUpdated(sender, e);
            if (!RunButton.IsEnabled)
                return;

            try
            {
                Common.plaintext = SaveHTMLCheckbox.IsChecked == false;
                pool = new()
                {
                    destFile = FileOutput.Text,
                    divide = DivideLogsCheckbox.IsChecked == true,
                    dtAfter = AfterDate.SelectedDate ?? Common.DTFromStamp(1),
                    dtBefore = BeforeDate.SelectedDate ?? DateTime.UtcNow,
                    phrase = string.Empty,
                    saveTruncated = SaveTruncatedCheckbox.IsChecked == true,
                    srcFile = FileSource.Text,
                    totalSize = new()
                };

                Common.fileListing[pool.srcFile] = new(pool.srcFile);
                pool.totalSize += Common.fileListing[pool.srcFile].Length;

                ProcessFiles(batch: false);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void SrcDirectoryButton_Click(object? sender, RoutedEventArgs e)
        {
            DirectorySource.Text = DialogFileSelect();
        }

        private void SrcFileButton_Click(object? sender, RoutedEventArgs e)
        {
            FileSource.Text = DialogFileSelect(false, true, false);
        }

        private void SrcPhraseButton_Click(object? sender, RoutedEventArgs e)
        {
            PhraseSource.Text = DialogFileSelect();
        }

        private void ThemeSelector_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                (brushPalette, reversePalette) = (reversePalette, brushPalette);
                DirectoryThemeSelector.Content = PhraseThemeSelector.Content = ThemeSelector.Content = brushPalette == 0 ? "Light" : "Dark";

                ChangeStyle(MainGrid);
                ADLWarning.Foreground = brushCombos[3][brushPalette];
                MainGrid.Background = brushCombos[5][brushPalette];
                RegexCheckBox.Background = brushCombos[1][brushPalette];

                if (fileError == FLogS_ERROR.NONE)
                    WarningLabel.Foreground = brushCombos[4][brushPalette];
                if (directoryError == FLogS_ERROR.NONE)
                    DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (phraseError == FLogS_ERROR.NONE)
                    PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void TextboxUpdated(object? sender, EventArgs e)
        {
            if (Common.processing)
                return;

            try
            {
                pool.regex = (RegexCheckBox?.IsVisible ?? false) && (RegexCheckBox?.IsChecked ?? false);
                fileError = directoryError = phraseError = FLogS_ERROR.NONE;
                fileWarning = directoryWarning = phraseWarning = FLogS_WARNING.NONE;
                PhraseSearchLabel.Content = pool.regex ? "Target Pattern" : "Target Word or Phrase";
                RunButton.IsEnabled = DirectoryRunButton.IsEnabled = PhraseRunButton.IsEnabled = true;
                WarningLabel.Content = DirectoryWarningLabel.Content = PhraseWarningLabel.Content = string.Empty;
                WarningLabel.Foreground = DirectoryWarningLabel.Foreground = PhraseWarningLabel.Foreground = brushCombos[3][brushPalette];

                if (DirectorySource.Text.Length == 0)
                    directoryError = FLogS_ERROR.NO_SOURCES;
                else if (DirectoryOutput.Text.Length == 0)
                    directoryError = FLogS_ERROR.NO_DEST_DIR;
                else if (File.Exists(DirectoryOutput.Text))
                    directoryError = FLogS_ERROR.DEST_NOT_DIRECTORY;
                else if (!Directory.Exists(DirectoryOutput.Text))
                    directoryError = FLogS_ERROR.DEST_NOT_FOUND;
                else
                {
                    foreach (string file in DirectorySource.Text.Split(';'))
                    {
                        string outFile = Path.Join(DirectoryOutput.Text, Path.GetFileNameWithoutExtension(file));

                        if (!Common.plaintext)
                            outFile += ".html";
                        else
                            outFile += ".txt";

                        if (!File.Exists(file))
                            directoryError = FLogS_ERROR.SOURCES_NOT_FOUND;
                        else if (file.Equals(outFile))
                            directoryError = FLogS_ERROR.SOURCE_CONFLICT;

                        if (File.Exists(outFile))
                        {
                            if (Common.LogTest(outFile))
                            {
                                directoryError = FLogS_ERROR.DEST_SENSITIVE;
                                break;
                            }

                            directoryWarning = FLogS_WARNING.MULTI_OVERWRITE;
                        }
                    }
                }

                if (FileSource.Text.Length == 0)
                    fileError = FLogS_ERROR.NO_SOURCE;
                else if (!File.Exists(FileSource.Text))
                    fileError = FLogS_ERROR.SOURCE_NOT_FOUND;
                else if (FileOutput.Text.Length == 0)
                    fileError = FLogS_ERROR.NO_DEST;
                else if (Directory.Exists(FileOutput.Text))
                    fileError = FLogS_ERROR.DEST_NOT_FILE;
                else if (!Directory.Exists(Path.GetDirectoryName(FileOutput.Text)))
                    fileError = FLogS_ERROR.DEST_NOT_FOUND;
                else if (FileSource.Text.Equals(FileOutput.Text))
                    fileError = FLogS_ERROR.SOURCE_EQUALS_DEST;
                else if (File.Exists(FileOutput.Text))
                {
                    if (Common.LogTest(FileOutput.Text))
                        fileError = FLogS_ERROR.DEST_SENSITIVE;

                    fileWarning = FLogS_WARNING.SINGLE_OVERWRITE;
                }

                if (PhraseSource.Text.Length == 0)
                    phraseError = FLogS_ERROR.NO_SOURCES;
                else if (PhraseOutput.Text.Length == 0)
                    phraseError = FLogS_ERROR.NO_DEST_DIR;
                else if (File.Exists(PhraseOutput.Text))
                    phraseError = FLogS_ERROR.DEST_NOT_DIRECTORY;
                else if (!Directory.Exists(PhraseOutput.Text))
                    phraseError = FLogS_ERROR.DEST_NOT_FOUND;
                else if (PhraseSearch.Text.Length == 0)
                    phraseError = FLogS_ERROR.NO_REGEX;
                else if (RegexCheckBox.IsChecked == true && !Common.IsValidPattern(PhraseSearch.Text))
                    phraseError = FLogS_ERROR.BAD_REGEX;
                else
                {
                    foreach (string file in PhraseSource.Text.Split(';'))
                    {
                        string outFile = Path.Join(PhraseOutput.Text, Path.GetFileNameWithoutExtension(file));

                        if (!Common.plaintext)
                            outFile += ".html";
                        else
                            outFile += ".txt";

                        if (!File.Exists(file))
                            phraseError = FLogS_ERROR.SOURCES_NOT_FOUND;
                        else if (file.Equals(outFile))
                            phraseError = FLogS_ERROR.SOURCE_CONFLICT;

                        if (File.Exists(outFile))
                        {
                            if (Common.LogTest(outFile))
                            {
                                phraseError = FLogS_ERROR.DEST_SENSITIVE;
                                break;
                            }

                            phraseWarning = FLogS_WARNING.MULTI_OVERWRITE;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }

            DirectoryWarningLabel.Content = GetErrorMessage(directoryError, directoryWarning);
            PhraseWarningLabel.Content = GetErrorMessage(phraseError, phraseWarning);
            WarningLabel.Content = GetErrorMessage(fileError, fileWarning);
            DirectoryRunButton.IsEnabled = directoryError == FLogS_ERROR.NONE;
            PhraseRunButton.IsEnabled = phraseError == FLogS_ERROR.NONE;
            RunButton.IsEnabled = fileError == FLogS_ERROR.NONE;

            if (fileError == FLogS_ERROR.NONE)
                WarningLabel.Foreground = brushCombos[4][brushPalette];
            if (directoryError == FLogS_ERROR.NONE)
                DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
            if (phraseError == FLogS_ERROR.NONE)
                PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
        }

        private static void TransitionEnableables(DependencyObject sender, bool enabled)
        {
            if (sender is null)
                return;

            if ((sender.GetValue(TagProperty) ?? string.Empty).Equals("Enableable") || !Common.lastException.Equals(string.Empty))
                sender.SetValue(IsEnabledProperty, enabled);

            foreach (object dp in LogicalTreeHelper.GetChildren(sender))
                TransitionEnableables(dp as DependencyObject, enabled);

            return;
        }

        private void TransitionMenus(bool enabled)
        {
            TransitionEnableables(MainGrid, enabled);

            if (!enabled)
            {
                DirectoryRunButton.Content = PhraseRunButton.Content = RunButton.Content = "Scanning...";

                Common.lastException = string.Empty;
                Common.timeBegin = DateTime.Now;

                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {Path.GetFileName(pool.srcFile)}...";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {filesProcessed:N0} files...";

                return;
            }

            DirectoryRunButton.Content = PhraseRunButton.Content = RunButton.Content = "Run";

            if (Common.lastException.Equals(string.Empty))
            {
                double timeTaken = DateTime.Now.Subtract(Common.timeBegin).TotalSeconds;
                string? formattedName = Path.GetFileName(pool.srcFile);
                if (formattedName?.Length > 16)
                    formattedName = formattedName[..14] + "...";
                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {formattedName} in {timeTaken:N2} seconds.";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {filesProcessed:N0} files in {timeTaken:N2} seconds.";
            }

            return;
        }

        private void UpdateLogs(object? sender = null)
        {
            PhraseIMBox.Content = DirectoryIMBox.Content = IMBox.Content = $"Intact Messages: {pool.intactMessages:N0} ({pool.intactBytes:S})";
            PhraseCTBox.Content = DirectoryCTBox.Content = CTBox.Content = $"Corrupted Timestamps: {pool.corruptTimestamps:N0}";
            PhraseTMBox.Content = DirectoryTMBox.Content = TMBox.Content = $"Truncated Messages: {pool.truncatedMessages:N0} ({pool.truncatedBytes:S})";
            PhraseEMBox.Content = DirectoryEMBox.Content = EMBox.Content = $"Empty Messages: {pool.emptyMessages:N0}";
            PhraseUBBox.Content = DirectoryUBBox.Content = UBBox.Content = $"Unread Data: {pool.unreadBytes:S}";

            if (!Common.lastException.Equals(string.Empty))
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = "A critical error has occurred.";
                PhraseEXBox.Content = DirectoryEXBox.Content = EXBox.Content = Common.lastException;
                (sender as BackgroundWorker).CancelAsync();
            }

            return;
        }

        private void Worker_Completed(object? sender, EventArgs e)
        {
            try
            {
                DirectoryProgress.Value = DirectoryProgress.Maximum;
                FileProgress.Value = FileProgress.Maximum;
                PhraseProgress.Value = PhraseProgress.Maximum;

                UpdateLogs(sender);
                TransitionMenus(true);
                Common.processing = false;
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void Worker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            try
            {
                FileProgress.Value = DirectoryProgress.Value = PhraseProgress.Value = e.ProgressPercentage;

                UpdateLogs(sender);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }
    }
}
