using Microsoft.Win32;
using System;
using System.ComponentModel;
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
            new SolidColorBrush[] { new SolidColorBrush(new Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray }, // Borders
            new SolidColorBrush[] { Brushes.Pink, Brushes.Red }, // Error messages (And the ADL warning)
            new SolidColorBrush[] { Brushes.Yellow, Brushes.DarkRed }, // Warning messages
            new SolidColorBrush[] { new SolidColorBrush(new Color() { A = 0xFF, R = 0x4C, G = 0x4C, B = 0x4C }), Brushes.DarkGray }, // TabControl
            new SolidColorBrush[] { Brushes.Transparent, new SolidColorBrush(new Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }) }, // DatePicker borders
            new SolidColorBrush[] { Brushes.DimGray, Brushes.AntiqueWhite }, // PanelGrids
        };
        private static int brushPalette = 1;
        private static uint directoryReadyToRun = 1;
        private static uint fileReadyToRun = 1;
        private static int filesProcessed;
        private static uint phraseReadyToRun = 1;
        private static int reversePalette = 0;
        private readonly static string[] warnings =
        {
            "",
            "No source log files selected.",
            "No destination directory selected.",
            "Destination is not a directory.",
            "Destination directory does not exist.",
            "One or more source files do not exist.",
            "One or more source files exist in the destination.",
            "No source log file selected.",
            "Source log file does not exist.",
            "No destination file selected.",
            "Destination is not a file.",
            "Source and destination files are identical.",
            "No search text entered.",
            "Search text contains an invalid RegEx pattern.",
            "",
            "",
            "Destination file will be overwritten.",
            "One or more files will be overwritten.",
        };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ChangeStyle(DependencyObject? sender)
        {
            if (sender is null)
                return;

            switch (sender.DependencyObjectType.Name)
            {
                case "Button":
                    (sender as Button).Background = brushCombos[1][brushPalette];
                    break;
                case "DatePicker":
                    (sender as DatePicker).Background = brushCombos[0][brushPalette];
                    (sender as DatePicker).BorderBrush = brushCombos[6][brushPalette]; // The inner white border of a DatePicker is pretty much impossible to remove programmatically.
                                                                                       // So just set the outer one to be black in light mode and transparent otherwise.
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

            if ((sender.GetValue(TagProperty) as string ?? "").Equals("WarningLabel"))
                sender.SetValue(ForegroundProperty, brushCombos[3][brushPalette]);

            return;
        }

        private void ComboBox_Update(object? sender, RoutedEventArgs e)
        {
            if (DirectorySaveTruncated is null || PhraseSaveTruncated is null || SaveTruncated is null)
                return;

            DirectorySaveTruncated.SelectedIndex = (sender as ComboBox).SelectedIndex;
            PhraseSaveTruncated.SelectedIndex = (sender as ComboBox).SelectedIndex;
            SaveTruncated.SelectedIndex = (sender as ComboBox).SelectedIndex;

            return;
        }

        private void DatePicker_Update(object? sender, RoutedEventArgs e)
        {
            if ((sender as DatePicker).Name.Contains("BeforeDate"))
            {
                BeforeDate.SelectedDate = (sender as DatePicker).SelectedDate;
                DirectoryBeforeDate.SelectedDate = (sender as DatePicker).SelectedDate;
                PhraseBeforeDate.SelectedDate = (sender as DatePicker).SelectedDate;

                return;
            }

            AfterDate.SelectedDate = (sender as DatePicker).SelectedDate;
            DirectoryAfterDate.SelectedDate = (sender as DatePicker).SelectedDate;
            PhraseAfterDate.SelectedDate = (sender as DatePicker).SelectedDate;

            return;
        }

        private static string DialogFileSelect(bool checkExists = false, bool multi = true)
        {
            OpenFileDialog openFileDialog = new()
            {
                CheckFileExists = checkExists,
                Multiselect = multi,
            };
            if (openFileDialog.ShowDialog() == true)
                return string.Join(";", openFileDialog.FileNames.Where(file => !file.Contains(".idx"))); // IDX files contain metadata relating to the corresponding (usually extension-less) log files.
                                                                                                         // They will never contain actual messages, so we exclude them unconditionally.
            return "";
        }

        private static string DialogFolderSelect()
        {
            System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new()
            {
                ShowNewFolderButton = true
            };
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                return folderBrowserDialog.SelectedPath;
            return "";
        }

        private void DirectoryRunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                MessagePool.destDir = DirectoryOutput.Text;
                MessagePool.dtAfter = DirectoryAfterDate.SelectedDate ?? Common.DTFromStamp(1);
                MessagePool.dtBefore = DirectoryBeforeDate.SelectedDate ?? DateTime.UtcNow;
                string[] files = DirectorySource.Text.Split(';');
                filesProcessed = files.Length;
                MessagePool.saveTruncated = DirectorySaveTruncated.SelectedIndex != 0;
                long totalSize = 0;

                foreach (string logfile in files)
                {
                    totalSize += new FileInfo(logfile).Length;
                    MessagePool.destFile = Path.Join(MessagePool.destDir, Path.GetFileNameWithoutExtension(logfile) + ".txt");
                    if (File.Exists(MessagePool.destFile))
                        File.Delete(MessagePool.destFile);
                }

                FileProgress.Maximum = totalSize;
                DirectoryProgress.Maximum = totalSize;
                PhraseProgress.Maximum = totalSize;

                MessagePool.ResetStats();
                TransitionMenus(false);
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true,
                    WorkerSupportsCancellation = true
                };
                worker.DoWork += MessagePool.BatchProcess;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                worker.RunWorkerAsync(files);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void DstDirectoryButton_Click(object? sender, RoutedEventArgs e)
        {
            DirectoryOutput.Text = DialogFolderSelect();
        }

        private void DstFileButton_Click(object? sender, RoutedEventArgs e)
        {
            FileOutput.Text = DialogFileSelect(multi: false);
        }

        private void DstPhraseButton_Click(object? sender, RoutedEventArgs e)
        {
            PhraseOutput.Text = DialogFolderSelect();
        }

        private void MainGrid_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (ShouldSystemUseDarkMode())
                    ThemeSelector_Click(sender, e);

                if (File.Exists(Common.errorFile))
                    File.Delete(Common.errorFile);
            }
            catch (Exception)
            {
                // Do nothing; default to light mode.
            }
        }

        private void PhraseRunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                MessagePool.dtAfter = PhraseAfterDate.SelectedDate ?? Common.DTFromStamp(1);
                MessagePool.dtBefore = PhraseBeforeDate.SelectedDate ?? DateTime.UtcNow;
                MessagePool.destDir = PhraseOutput.Text;
                string[] files = PhraseSource.Text.Split(';');
                filesProcessed = files.Length;
                MessagePool.phrase = PhraseSearch.Text;
                MessagePool.saveTruncated = PhraseSaveTruncated.SelectedIndex != 0;
                long totalSize = 0;

                foreach (string logfile in files)
                {
                    totalSize += new FileInfo(logfile).Length;
                    MessagePool.destFile = Path.Join(MessagePool.destDir, Path.GetFileNameWithoutExtension(logfile) + ".txt");
                    if (File.Exists(MessagePool.destFile))
                        File.Delete(MessagePool.destFile);
                }

                FileProgress.Maximum = totalSize;
                DirectoryProgress.Maximum = totalSize;
                PhraseProgress.Maximum = totalSize;

                MessagePool.ResetStats();
                TransitionMenus(false);
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true,
                    WorkerSupportsCancellation = true
                };
                worker.DoWork += MessagePool.BatchProcess;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                worker.RunWorkerAsync(files);
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void RunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                MessagePool.destFile = FileOutput.Text;
                MessagePool.dtAfter = AfterDate.SelectedDate ?? Common.DTFromStamp(1);
                MessagePool.dtBefore = BeforeDate.SelectedDate ?? DateTime.UtcNow;
                filesProcessed = 1;
                MessagePool.saveTruncated = SaveTruncated.SelectedIndex != 0;
                MessagePool.srcFile = FileSource.Text;

                if (File.Exists(MessagePool.destFile))
                    File.Delete(MessagePool.destFile);

                DirectoryProgress.Maximum = new FileInfo(MessagePool.srcFile).Length;
                FileProgress.Maximum = new FileInfo(MessagePool.srcFile).Length;
                PhraseProgress.Maximum = new FileInfo(MessagePool.srcFile).Length;

                MessagePool.ResetStats();
                TransitionMenus(false);
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true,
                    WorkerSupportsCancellation = true
                };
                worker.DoWork += MessagePool.DoWork;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                worker.RunWorkerAsync();
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
            FileSource.Text = DialogFileSelect(true, false);
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

                if (directoryReadyToRun > 0xF)
                    DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (phraseReadyToRun > 0xF)
                    PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (fileReadyToRun > 0xF)
                    WarningLabel.Foreground = brushCombos[4][brushPalette];
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }
        }

        private void TextboxUpdated(object? sender, EventArgs e)
        {
            try
            {
                MessagePool.regex = RegexCheckBox.IsVisible && (RegexCheckBox.IsChecked ?? false);
                fileReadyToRun = directoryReadyToRun = phraseReadyToRun = 0;
                PhraseSearchLabel.Content = MessagePool.regex ? "Target Pattern" : "Target Word or Phrase";
                RunButton.IsEnabled = DirectoryRunButton.IsEnabled = PhraseRunButton.IsEnabled = true;
                WarningLabel.Content = DirectoryWarningLabel.Content = PhraseWarningLabel.Content = "";
                WarningLabel.Foreground = DirectoryWarningLabel.Foreground = PhraseWarningLabel.Foreground = brushCombos[3][brushPalette];

                if (DirectorySource.Text.Length == 0)
                    directoryReadyToRun = 1;
                else if (DirectoryOutput.Text.Length == 0)
                    directoryReadyToRun = 2;
                else if (File.Exists(DirectoryOutput.Text))
                    directoryReadyToRun = 3;
                else if (Directory.Exists(DirectoryOutput.Text) == false)
                    directoryReadyToRun = 4;
                else
                {
                    foreach (string file in DirectorySource.Text.Split(';'))
                    {
                        if (File.Exists(file) == false)
                            directoryReadyToRun = 5;
                        else if (file.Equals(Path.Join(DirectoryOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            directoryReadyToRun = 6;
                        else if (directoryReadyToRun == 0 && File.Exists(Path.Join(DirectoryOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            directoryReadyToRun = 0x11;
                    }
                }

                if (FileSource.Text.Length == 0)
                    fileReadyToRun = 7;
                else if (File.Exists(FileSource.Text) == false)
                    fileReadyToRun = 8;
                else if (FileOutput.Text.Length == 0)
                    fileReadyToRun = 9;
                else if (Directory.Exists(FileOutput.Text))
                    fileReadyToRun = 0xA;
                else if (Directory.Exists(Path.GetDirectoryName(FileOutput.Text)) == false)
                    fileReadyToRun = 4;
                else if (FileSource.Text.Equals(FileOutput.Text))
                    fileReadyToRun = 0xB;
                else if (File.Exists(FileOutput.Text))
                    fileReadyToRun = 0x10;

                if (PhraseSource.Text.Length == 0)
                    phraseReadyToRun = 1;
                else if (PhraseOutput.Text.Length == 0)
                    phraseReadyToRun = 2;
                else if (File.Exists(PhraseOutput.Text))
                    phraseReadyToRun = 3;
                else if (Directory.Exists(PhraseOutput.Text) == false)
                    phraseReadyToRun = 4;
                else if (PhraseSearch.Text.Length == 0)
                    phraseReadyToRun = 0xC;
                else if (RegexCheckBox.IsChecked == true && Common.IsValidPattern(PhraseSearch.Text) == false)
                    phraseReadyToRun = 0xD;
                else
                {
                    foreach (string file in PhraseSource.Text.Split(';'))
                    {
                        if (File.Exists(file) == false)
                            phraseReadyToRun = 5;
                        else if (file.Equals(Path.Join(PhraseOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            phraseReadyToRun = 6;
                        else if (phraseReadyToRun == 0 && File.Exists(Path.Join(PhraseOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            phraseReadyToRun = 0x11;
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
                return;
            }

            DirectoryWarningLabel.Content = warnings[directoryReadyToRun];
            DirectoryRunButton.IsEnabled = directoryReadyToRun == 0 || directoryReadyToRun > 0xF;
            PhraseWarningLabel.Content = warnings[phraseReadyToRun];
            PhraseRunButton.IsEnabled = phraseReadyToRun == 0 || phraseReadyToRun > 0xF;
            WarningLabel.Content = warnings[fileReadyToRun];
            RunButton.IsEnabled = fileReadyToRun == 0 || fileReadyToRun > 0xF;

            if (directoryReadyToRun > 0xF)
                DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
            if (phraseReadyToRun > 0xF)
                PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
            if (fileReadyToRun > 0xF)
                WarningLabel.Foreground = brushCombos[4][brushPalette];
        }

        private void TransitionEnableables(DependencyObject sender, bool enabled)
        {
            if (sender is null)
                return;

            if ((sender.GetValue(TagProperty) ?? "").Equals("Enableable"))
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
                DirectoryRunButton.Content = "Scanning...";
                PhraseRunButton.Content = "Scanning...";
                RunButton.Content = "Scanning...";

                Common.lastException = "";
                Common.timeBegin = DateTime.Now;

                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {Path.GetFileName(MessagePool.srcFile)}...";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {filesProcessed:N0} files...";

                return;
            }

            DirectoryRunButton.Content = "Run";
            PhraseRunButton.Content = "Run";
            RunButton.Content = "Run";

            if (Common.lastException.Equals(""))
            {
                double timeTaken = DateTime.Now.Subtract(Common.timeBegin).TotalSeconds;
                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {Path.GetFileName(MessagePool.srcFile)} in {timeTaken:N2} seconds.";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {filesProcessed:N0} files in {timeTaken:N2} seconds.";
            }

            return;
        }

        private void UpdateLogs(object? sender = null)
        {
            PhraseIMBox.Content = DirectoryIMBox.Content = IMBox.Content = $"Intact Messages: {MessagePool.intactMessages - MessagePool.discardedMessages:N0} ({MessagePool.intactBytes - MessagePool.discardedBytes:S})";
            PhraseCTBox.Content = DirectoryCTBox.Content = CTBox.Content = $"Corrupted Timestamps: {MessagePool.corruptTimestamps:N0}";
            PhraseTMBox.Content = DirectoryTMBox.Content = TMBox.Content = $"Truncated Messages: {MessagePool.truncatedMessages:N0} ({MessagePool.truncatedBytes:S})";
            PhraseEMBox.Content = DirectoryEMBox.Content = EMBox.Content = $"Empty Messages: {MessagePool.emptyMessages:N0}";
            PhraseUBBox.Content = DirectoryUBBox.Content = UBBox.Content = $"Unread data: {MessagePool.unreadBytes:S}";

            if (Common.lastException.Equals("") == false)
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
                FileProgress.Value = e.ProgressPercentage;
                DirectoryProgress.Value = e.ProgressPercentage;
                PhraseProgress.Value = e.ProgressPercentage;

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
