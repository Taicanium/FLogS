using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

        public MainWindow()
        {
            InitializeComponent();
        }

        private readonly static SolidColorBrush[][] brushCombos =
        {
            new SolidColorBrush[] { Brushes.Black, Brushes.White }, // Textboxes
            new SolidColorBrush[] { Brushes.LightBlue, Brushes.Beige }, // Buttons
            new SolidColorBrush[] { new SolidColorBrush(new Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray }, // Grids/Borders
            new SolidColorBrush[] { Brushes.Pink, Brushes.Red }, // Error messages (And the ADL warning)
            new SolidColorBrush[] { Brushes.Yellow, Brushes.DarkOrange }, // Warning messages
        };
        private int brushPalette = 0;
        private uint bytesRead;
        private uint corruptTimestamps;
        private readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
        private string? destDir;
        private string? destFile;
        private uint directoryReadyToRun = 1;
        private uint discardedBytes;
        private uint discardedMessages;
        private DateTime? dtAfter;
        private DateTime? dtBefore;
        private uint emptyMessages;
        private readonly static DateTime epoch = new(1970, 1, 1, 0, 0, 0);
        private uint fileReadyToRun = 1;
        private uint filesProcessed;
        private double finalBytes;
        private byte[]? idBuffer;
        private bool intact;
        private uint intactBytes;
        private uint intactMessages;
        private int lastDiscrepancy;
        private uint lastPosition;
        private static uint lastTimestamp;
        private int nextByte;
        private string? phrase;
        private uint phraseReadyToRun = 1;
        private readonly static string[] prefixes = { "k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q" }; // We count bytes, so in practice this app will overflow upon reaching 2 GB.
        private int prefixIndex = 0;
        private int result;
        private int reversePalette;
        private bool saveTruncated;
        private string? srcFile;
        private byte[]? streamBuffer;
        private DateTime timeBegin;
        private uint truncatedBytes;
        private uint truncatedMessages;
        private int unreadBytes;
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
            "",
            "",
            "",
            "Destination file will be overwritten.",
            "One or more files will be overwritten.",
        };

        private enum MessageType
        {
            EOF = -1,
            Regular = 0,
            Me = 1,
            Ad = 2,
            DiceRoll = 3,
            Warning = 4,
            Headless = 5,
            Announcement = 6,
        }

        private static uint BEInt(byte[] buffer)
        {
            return buffer[0]
                + buffer[1] * 256U
                + buffer[2] * 65536U
                + buffer[3] * 16777216U;
        }

        private string ByteSizeString(double bytes)
        {
            finalBytes = bytes;
            prefixIndex = -1;
            while (finalBytes >= 921.6 && prefixIndex < 10)
            {
                finalBytes *= 0.0009765625; // 1/1024
                prefixIndex++;
            }

            if (prefixIndex == -1)
                return $"{finalBytes:N0} B";
            return $"{finalBytes:N1} {prefixes[prefixIndex]}B";
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
                string[] files = DirectorySource.Text.Split(';');
                filesProcessed = (uint)files.Length;
                if (filesProcessed == 1)
                    srcFile = files[0];

                destDir = DirectoryOutput.Text;
                dtAfter = DirectoryAfterDate.SelectedDate ?? DTFromStamp(1);
                dtBefore = DirectoryBeforeDate.SelectedDate ?? DateTime.UtcNow;
                saveTruncated = DirectorySaveTruncated.SelectedIndex != 0;

                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                long totalSize = 0;
                foreach (string logfile in files)
                {
                    totalSize += new FileInfo(logfile).Length;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(logfile) + ".txt");
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                }
                FileProgress.Maximum = totalSize;
                DirectoryProgress.Maximum = totalSize;
                PhraseProgress.Maximum = totalSize;

                TransitionMenus(false);
                ResetStats();
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true
                };
                worker.DoWork += Worker_BatchProcess;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                worker.RunWorkerAsync(files);
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
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

        private static DateTime DTFromStamp(uint stamp)
        {
            try
            {
                return epoch.AddSeconds(stamp);
            }
            catch (Exception)
            {
                return new DateTime();
            }
        }

        private static bool IsValidTimestamp(uint timestamp)
        {
            if (timestamp < 1) // If it came before Jan. 1, 1970, there's a problem.
                return false;
            if (timestamp > UNIXTimestamp()) // If it's in the future, also a problem.
                return false;
            if ((DTFromStamp(timestamp).ToString(dateFormat) ?? "").Equals("")) // If it can't be translated to a date, also a problem.
                return false;
            if (timestamp < lastTimestamp)  // If it isn't sequential, also a problem, because F-Chat would never save it that way.
                                            // In this case specifically, there's an extremely high chance we're about to produce garbage data in the output.
                return false;
            return true;
        }

        private static string LogException(Exception e)
        {
            File.AppendAllText("FLogS_ERROR.txt", DateTime.Now.ToString(dateFormat) + " - " + e.Message + "\n");
            return e.Message;
        }

        private void MainGrid_Loaded(object? sender, RoutedEventArgs e)
        {
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

        private void PhraseRunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string[] files = PhraseSource.Text.Split(';');
                filesProcessed = (uint)files.Length;
                if (filesProcessed == 1)
                    srcFile = files[0];

                dtAfter = PhraseAfterDate.SelectedDate ?? DTFromStamp(1);
                dtBefore = PhraseBeforeDate.SelectedDate ?? DateTime.UtcNow;
                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                destDir = PhraseOutput.Text;
                phrase = PhraseSearch.Text;
                saveTruncated = PhraseSaveTruncated.SelectedIndex != 0;

                long totalSize = 0;
                foreach (string logfile in files)
                {
                    totalSize += new FileInfo(logfile).Length;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(logfile) + ".txt");
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                }
                FileProgress.Maximum = totalSize;
                DirectoryProgress.Maximum = totalSize;
                PhraseProgress.Maximum = totalSize;

                TransitionMenus(false);
                ResetStats();
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true
                };
                worker.DoWork += Worker_BatchProcess;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                worker.RunWorkerAsync(files);
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
        }

        private void ResetStats()
        {
            bytesRead = 0;
            corruptTimestamps = 0U;
            discardedBytes = 0U;
            discardedMessages = 0U;
            emptyMessages = 0U;
            idBuffer = new byte[4];
            intactBytes = 0U;
            intactMessages = 0U;
            lastPosition = 0U;
            nextByte = 255;
            result = 0;
            timeBegin = DateTime.Now;
            truncatedBytes = 0U;
            truncatedMessages = 0U;
            unreadBytes = 0;

            return;
        }

        private void RunButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                destFile = FileOutput.Text;
                dtAfter = AfterDate.SelectedDate ?? DTFromStamp(1);
                dtBefore = BeforeDate.SelectedDate ?? DateTime.UtcNow;
                filesProcessed = 1;
                saveTruncated = SaveTruncated.SelectedIndex != 0;
                srcFile = FileSource.Text;

                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                DirectoryProgress.Maximum = new FileInfo(srcFile).Length;
                FileProgress.Maximum = new FileInfo(srcFile).Length;
                PhraseProgress.Maximum = new FileInfo(srcFile).Length;

                if (File.Exists(destFile))
                    File.Delete(destFile);

                TransitionMenus(false);
                ResetStats();
                UpdateLogs();

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true
                };
                worker.DoWork += Worker_DoWork;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;
                worker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
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
                brushPalette = 1;
                reversePalette = 0;

                if (ThemeSelector.Content.ToString().Equals("Dark"))
                {
                    brushPalette = 0;
                    reversePalette = 1;

                    DirectoryThemeSelector.Content = "Light";
                    PhraseThemeSelector.Content = "Light";
                    ThemeSelector.Content = "Light";
                }
                else
                {
                    DirectoryThemeSelector.Content = "Dark";
                    PhraseThemeSelector.Content = "Dark";
                    ThemeSelector.Content = "Dark";
                }

                // Buttons
                DirectoryRunButton.Background = brushCombos[1][brushPalette];
                DirectoryThemeSelector.Background = brushCombos[1][brushPalette];
                DstDirectoryButton.Background = brushCombos[1][brushPalette];
                DstFileButton.Background = brushCombos[1][brushPalette];
                DstPhraseButton.Background = brushCombos[1][brushPalette];
                PhraseRunButton.Background = brushCombos[1][brushPalette];
                PhraseThemeSelector.Background = brushCombos[1][brushPalette];
                RunButton.Background = brushCombos[1][brushPalette];
                SrcDirectoryButton.Background = brushCombos[1][brushPalette];
                SrcFileButton.Background = brushCombos[1][brushPalette];
                SrcPhraseButton.Background = brushCombos[1][brushPalette];
                ThemeSelector.Background = brushCombos[1][brushPalette];

                // Grids
                DirectoryGrid.Background = brushCombos[2][brushPalette];
                FileGrid.Background = brushCombos[2][brushPalette];
                HelpGrid.Background = brushCombos[2][brushPalette];
                PhraseGrid.Background = brushCombos[2][brushPalette];

                // Labels
                ADLWarning.Foreground = brushCombos[3][brushPalette];
                AfterDateLabel.Foreground = brushCombos[0][reversePalette];
                BeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryAfterDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryBeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryOutputLabel.Foreground = brushCombos[0][reversePalette];
                DirectorySourceLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryVersionNumber.Foreground = brushCombos[0][reversePalette];
                DirectoryWarningLabel.Foreground = brushCombos[3][brushPalette];
                FileOutputLabel.Foreground = brushCombos[0][reversePalette];
                FileSourceLabel.Foreground = brushCombos[0][reversePalette];
                FileVersionNumber.Foreground = brushCombos[0][reversePalette];
                HelpHeader1.Foreground = brushCombos[0][reversePalette];
                HelpHeader2.Foreground = brushCombos[0][reversePalette];
                HelpText1.Foreground = brushCombos[0][reversePalette];
                HelpText2.Foreground = brushCombos[0][reversePalette];
                HelpVersionNumber.Foreground = brushCombos[0][reversePalette];
                PhraseAfterDateLabel.Foreground = brushCombos[0][reversePalette];
                PhraseBeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                PhraseOutputLabel.Foreground = brushCombos[0][reversePalette];
                PhraseSearchLabel.Foreground = brushCombos[0][reversePalette];
                PhraseSourceLabel.Foreground = brushCombos[0][reversePalette];
                PhraseVersionNumber.Foreground = brushCombos[0][reversePalette];
                PhraseWarningLabel.Foreground = brushCombos[3][brushPalette];
                WarningLabel.Foreground = brushCombos[3][brushPalette];

                // TabControl
                TabMenu.Background = brushCombos[2][brushPalette];

                // Textboxes
                DirectoryLogWindow.Background = brushCombos[0][brushPalette];
                DirectoryLogWindow.BorderBrush = brushCombos[2][reversePalette];
                DirectoryLogWindow.Foreground = brushCombos[0][reversePalette];
                DirectoryOutput.Background = brushCombos[0][brushPalette];
                DirectoryOutput.BorderBrush = brushCombos[2][reversePalette];
                DirectoryOutput.Foreground = brushCombos[0][reversePalette];
                DirectorySource.Background = brushCombos[0][brushPalette];
                DirectorySource.BorderBrush = brushCombos[2][reversePalette];
                DirectorySource.Foreground = brushCombos[0][reversePalette];
                FileOutput.Background = brushCombos[0][brushPalette];
                FileOutput.BorderBrush = brushCombos[2][reversePalette];
                FileOutput.Foreground = brushCombos[0][reversePalette];
                FileSource.Background = brushCombos[0][brushPalette];
                FileSource.BorderBrush = brushCombos[2][reversePalette];
                FileSource.Foreground = brushCombos[0][reversePalette];
                LogWindow.Background = brushCombos[0][brushPalette];
                LogWindow.BorderBrush = brushCombos[2][reversePalette];
                LogWindow.Foreground = brushCombos[0][reversePalette];
                PhraseLogWindow.Background = brushCombos[0][brushPalette];
                PhraseLogWindow.BorderBrush = brushCombos[2][reversePalette];
                PhraseLogWindow.Foreground = brushCombos[0][reversePalette];
                PhraseOutput.Background = brushCombos[0][brushPalette];
                PhraseOutput.BorderBrush = brushCombos[2][reversePalette];
                PhraseOutput.Foreground = brushCombos[0][reversePalette];
                PhraseSearch.Background = brushCombos[0][brushPalette];
                PhraseSearch.BorderBrush = brushCombos[2][reversePalette];
                PhraseSearch.Foreground = brushCombos[0][reversePalette];
                PhraseSource.Background = brushCombos[0][brushPalette];
                PhraseSource.BorderBrush = brushCombos[2][reversePalette];
                PhraseSource.Foreground = brushCombos[0][reversePalette];

                if (directoryReadyToRun == 0)
                    DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (phraseReadyToRun == 0)
                    PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (fileReadyToRun == 0)
                    WarningLabel.Foreground = brushCombos[4][brushPalette];
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
        }

        private void TransitionMenus(bool enabled)
        {
            DirectoryOutput.IsEnabled = enabled;
            DirectoryRunButton.IsEnabled = enabled;
            DirectorySource.IsEnabled = enabled;
            DstDirectoryButton.IsEnabled = enabled;
            DstFileButton.IsEnabled = enabled;
            DstPhraseButton.IsEnabled = enabled;
            FileOutput.IsEnabled = enabled;
            FileSource.IsEnabled = enabled;
            PhraseOutput.IsEnabled = enabled;
            PhraseRunButton.IsEnabled = enabled;
            PhraseSource.IsEnabled = enabled;
            RunButton.IsEnabled = enabled;
            SrcDirectoryButton.IsEnabled = enabled;
            SrcFileButton.IsEnabled = enabled;
            SrcPhraseButton.IsEnabled = enabled;

            if (!enabled)
            {
                DirectoryRunButton.Content = "Scanning...";
                PhraseRunButton.Content = "Scanning...";
                RunButton.Content = "Scanning...";

                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {Path.GetFileName(srcFile)}...";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {filesProcessed:N0} files...";

                return;
            }

            DirectoryRunButton.Content = "Run";
            PhraseRunButton.Content = "Run";
            RunButton.Content = "Run";

            double timeTaken = DateTime.Now.Subtract(timeBegin).TotalSeconds;
            if (filesProcessed == 1)
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {Path.GetFileName(srcFile)} in {timeTaken:N2} seconds.";
            else
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Processed {filesProcessed:N0} files in {timeTaken:N2} seconds.";

            return;
        }

        private void TextboxUpdated(object? sender, EventArgs e)
        {
            try
            {
                fileReadyToRun = directoryReadyToRun = phraseReadyToRun = 0;
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
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
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

        private static uint UNIXTimestamp()
        {
            return (uint)Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds);
        }

        private void UpdateLogs()
        {
            int intactCount;
            int byteCount;
            string byteString;

            intactCount = (int)intactMessages - (int)discardedMessages;
            byteCount = (int)intactBytes - (int)discardedBytes;
            byteString = ByteSizeString(byteCount);
            IMBox.Content = $"Intact Messages: {intactCount:N0} ({byteString})";
            CTBox.Content = $"Corrupted Timestamps: {corruptTimestamps:N0}";
            byteString = ByteSizeString(truncatedBytes);
            TMBox.Content = $"Truncated Messages: {truncatedMessages:N0} ({byteString})";
            EMBox.Content = $"Empty Messages: {emptyMessages:N0}";
            byteString = ByteSizeString(unreadBytes);
            UBBox.Content = $"Unread Bytes: {unreadBytes:N0} ({byteString})";
            PhraseIMBox.Content = DirectoryIMBox.Content = IMBox.Content;
            PhraseCTBox.Content = DirectoryCTBox.Content = CTBox.Content;
            PhraseTMBox.Content = DirectoryTMBox.Content = TMBox.Content;
            PhraseEMBox.Content = DirectoryEMBox.Content = EMBox.Content;
            PhraseUBBox.Content = DirectoryUBBox.Content = UBBox.Content;

            return;
        }

        private void Worker_BatchProcess(object? sender, DoWorkEventArgs e)
        {
            try
            {
                string[]? files = (string[]?)e.Argument;

                foreach (string logfile in files)
                {
                    srcFile = logfile;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(srcFile) + ".txt");
                    result = 0;
                    nextByte = 255;
                    lastPosition = 0U;

                    Worker_DoWork(sender, e);
                    bytesRead += (uint)new FileInfo(logfile).Length;
                }
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
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

                UpdateLogs();
                TransitionMenus(true);
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
        }

        private void Worker_DoWork(object? sender, DoWorkEventArgs e)
        {
            FileStream srcFS = File.OpenRead(srcFile);

            using (StreamWriter dstFS = new(destFile, true))
            {
                lastPosition = 0U;
                lastTimestamp = 0;
                DateTime lastUpdate = DateTime.Now;

                while (srcFS.Position < srcFS.Length - 1)
                {
                    Worker_TranslateMessage(srcFS, dstFS);

                    if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > 10)
                    {
                        (sender as BackgroundWorker).ReportProgress((int)(bytesRead + srcFS.Position));
                        lastUpdate = DateTime.Now;
                    }
                }
            }

            srcFS.Close();

            return;
        }

        private void Worker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            try
            {
                FileProgress.Value = e.ProgressPercentage;
                DirectoryProgress.Value = e.ProgressPercentage;
                PhraseProgress.Value = e.ProgressPercentage;

                UpdateLogs();
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="srcFS">A FileStream opened to the source log file.</param>
        /// <param name="dstFS">A StreamWriter opened to the destination file.</param>
        /// <returns>'true' if a message was written to file; 'false' if, for any reason, that did not occur.</returns>
        private bool Worker_TranslateMessage(FileStream srcFS, StreamWriter dstFS)
        {
            int discrepancy;
            idBuffer = new byte[4];
            intact = true;
            bool matchPhrase = false;
            string messageData = "";
            uint messageLength = 0U;
            string messageOut = "";
            MessageType msId;
            bool nextTimestamp = false;
            string profileName;
            DateTime thisDT;
            uint timestamp;
            bool withinRange = true;
            bool written = false;

            try
            {
                discrepancy = (int)srcFS.Position - (int)lastPosition; // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
                lastDiscrepancy += discrepancy;
                unreadBytes += discrepancy;
                result = srcFS.Read(idBuffer, 0, 4); // Read the timestamp.
                if (result < 4)
                    return written;
                timestamp = BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
                if (IsValidTimestamp(timestamp))
                {
                    lastTimestamp = timestamp;
                    thisDT = DTFromStamp(timestamp);
                    messageOut += "[" + thisDT.ToString(dateFormat) + "] ";
                    if (thisDT.CompareTo(dtBefore) > 0 || thisDT.CompareTo(dtAfter) < 0)
                        withinRange = false;
                }
                else
                {
                    corruptTimestamps++;
                    intact = false;
                    messageOut = "[BAD TIMESTAMP] ";
                    if (timestamp > 0 && timestamp < UNIXTimestamp())
                        lastTimestamp = timestamp; // On the very off chance an otherwise-valid set of messages was made non-sequential, say, by F-Chat's client while trying to repair corruption.
                                                   // This should never happen, but you throw 100% of the exceptions you don't catch.
                }
                nextByte = srcFS.ReadByte(); // Read the delimiter.
                if (nextByte == -1)
                    return written;
                msId = (MessageType)nextByte;
                if (msId != MessageType.Headless)
                {
                    nextByte = srcFS.ReadByte(); // 1-byte length of profile name.
                    if (nextByte == -1)
                        return written;
                    streamBuffer = new byte[nextByte];
                    result = srcFS.Read(streamBuffer, 0, nextByte); // Read the profile name.
                    if (result < nextByte)
                    {
                        emptyMessages++;
                        intact = false;
                        messageOut += "[EMPTY MESSAGE]";
                    }
                    else
                    {
                        profileName = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                        messageOut += profileName;
                        switch (msId)
                        {
                            case MessageType.EOF:
                                return written;
                            case MessageType.Regular:
                                messageOut += ": ";
                                break;
                            case MessageType.Me:
                                messageOut += "";
                                break;
                            case MessageType.Ad:
                                messageOut += " (ad): ";
                                break;
                            case MessageType.DiceRoll: // These also include bottle spins and other 'fun' commands.
                                messageOut += "";
                                break;
                            case MessageType.Warning:
                                messageOut += " (warning): ";
                                break;
                            case MessageType.Announcement:
                                messageOut += " (announcement): ";
                                break;
                        }
                    }
                }
                else
                    nextByte = srcFS.ReadByte(); // Headless messages have a null terminator here, representing a lack of a profile name.
                result = srcFS.Read(idBuffer, 0, 2); // 2-byte length of message.
                if (result < 2)
                    result = 0;
                else
                {
                    idBuffer[2] = 0;
                    idBuffer[3] = 0;
                    messageLength = BEInt(idBuffer);
                    if (messageLength < 1)
                        result = 0;
                    else
                    {
                        streamBuffer = new byte[messageLength];
                        result = srcFS.Read(streamBuffer, 0, (int)messageLength); // Read the message text.
                    }
                }
                if (result == 0)
                {
                    emptyMessages++;
                    intact = false;
                    messageOut += "[EMPTY MESSAGE]";
                }
                else if (result < messageLength)
                {
                    intact = false;
                    truncatedBytes += (uint)result;
                    truncatedMessages++;
                    messageOut += "[TRUNCATED MESSAGE] ";
                }
                if (result > 0)
                    messageData = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                messageData = Regex.Replace(messageData, @"[^\u0009\u000A\u000D\u0020-\u007E]", ""); // Remove everything that's not a printable or newline character.
                if (phrase is null || (messageOut + messageData).Contains(phrase, StringComparison.OrdinalIgnoreCase)) // Either the profile name or the message can contain our search text.
                    matchPhrase = true;
                if (intact)
                {
                    intactBytes += (uint)messageOut.Length + (uint)messageData.Length;
                    intactMessages++;
                    if (withinRange && matchPhrase)
                    {
                        if (lastDiscrepancy > 0)
                        {
                            dstFS.Write(string.Format("({0:#,0} missing bytes)", lastDiscrepancy));
                            dstFS.Write(dstFS.NewLine);
                        }
                        dstFS.Write(messageOut);
                        dstFS.Write(messageData);
                        dstFS.Write(dstFS.NewLine);
                        lastDiscrepancy = 0;
                        written = true;
                    }
                    else // If the message doesn't match our criteria, we won't count it.
                    {
                        discardedBytes += (uint)messageOut.Length + (uint)messageData.Length;
                        discardedMessages++;
                    }
                }
                else if (saveTruncated)
                {
                    if (withinRange && matchPhrase)
                    {
                        if (lastDiscrepancy > 0)
                        {
                            dstFS.Write(string.Format("({0:#,0} missing bytes)", lastDiscrepancy));
                            dstFS.Write(dstFS.NewLine);
                        }
                        dstFS.Write(messageOut);
                        dstFS.Write(messageData);
                        dstFS.Write(dstFS.NewLine);
                        lastDiscrepancy = 0;
                        written = true;
                    }
                }
                lastPosition = (uint)srcFS.Position;
                while (!nextTimestamp) // Search for the next message by locating its timestamp and delimiter. It's the latter we're *really* looking for; the timestamp just helps us identify it.
                {
                    srcFS.ReadByte();
                    srcFS.Read(idBuffer, 0, 4);
                    nextByte = srcFS.ReadByte();
                    if (nextByte == -1)
                        return written;
                    srcFS.Seek(-6, SeekOrigin.Current);
                    if (nextByte < 7)
                    {
                        discrepancy = (int)srcFS.Position - (int)lastPosition;
                        lastDiscrepancy += discrepancy;
                        lastPosition = (uint)srcFS.Position;
                        nextTimestamp = true;
                        unreadBytes += discrepancy;
                        srcFS.ReadByte();
                    }
                    else
                    {
                        srcFS.ReadByte();
                        srcFS.ReadByte();
                        srcFS.Read(idBuffer, 0, 4);
                        nextByte = srcFS.ReadByte();
                        if (nextByte == -1)
                            return written;
                        srcFS.Seek(-7, SeekOrigin.Current);
                        srcFS.ReadByte();
                        srcFS.ReadByte();
                        if (nextByte < 7)
                        {
                            discrepancy = (int)srcFS.Position - (int)lastPosition - 2;
                            lastDiscrepancy += discrepancy;
                            lastPosition = (uint)srcFS.Position;
                            nextTimestamp = true;
                            unreadBytes += discrepancy;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
            }

            return written;
        }
    }
}
