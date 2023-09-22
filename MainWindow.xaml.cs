﻿using Microsoft.Win32;
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

        private uint bytesRead;
        private uint corruptTimestamps;
        private readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
        private string? destDir = "";
        private string? destFile = "";
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
        private readonly string[] prefixes = { "k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q" }; // Always futureproof...
        private int prefixIndex = 0;
        private int result;
        private bool saveTruncated;
        private string? srcFile = "";
        private byte[]? streamBuffer;
        private DateTime timeBegin;
        private uint truncatedBytes;
        private uint truncatedMessages;
        private int unreadBytes;

        private enum MessageType
        {
            EOF = -1,
            Regular = 0,
            Me = 1,
            Ad = 2,
            DiceRoll = 3,
            Warning = 4,
            Headless = 5,
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

            if ((sender as ComboBox).SelectedIndex == 0)
            {
                DirectorySaveTruncated.SelectedIndex = 0;
                PhraseSaveTruncated.SelectedIndex = 0;
                SaveTruncated.SelectedIndex = 0;
                return;
            }

            DirectorySaveTruncated.SelectedIndex = 1;
            PhraseSaveTruncated.SelectedIndex = 1;
            SaveTruncated.SelectedIndex = 1;
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

                TransitionMenus(true);
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

                TransitionMenus(true);
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

        private void ProcessWarnings()
        {
            switch (directoryReadyToRun)
            {
                case 0:
                    DirectoryRunButton.IsEnabled = true;
                    break;
                case 1:
                    DirectoryWarningLabel.Content = "No source log files selected.";
                    break;
                case 2:
                    DirectoryWarningLabel.Content = "No destination directory selected.";
                    break;
                case 3:
                    DirectoryWarningLabel.Content = "Destination directory does not exist.";
                    break;
                case 4:
                    DirectoryWarningLabel.Content = "One or more source files do not exist.";
                    break;
                case 5:
                    DirectoryWarningLabel.Content = "One or more source files exist in the destination.";
                    break;
                default:
                    DirectoryWarningLabel.Content = "An unknown error has occurred.";
                    break;
            }

            switch (fileReadyToRun)
            {
                case 0:
                    RunButton.IsEnabled = true;
                    break;
                case 1:
                    WarningLabel.Content = "No source log file selected.";
                    break;
                case 2:
                    WarningLabel.Content = "Source log file does not exist.";
                    break;
                case 3:
                    WarningLabel.Content = "No destination file selected.";
                    break;
                case 4:
                    WarningLabel.Content = "Destination is not a file.";
                    break;
                case 5:
                    WarningLabel.Content = "Destination directory does not exist.";
                    break;
                case 6:
                    WarningLabel.Content = "Source and destination files are identical.";
                    break;
                default:
                    WarningLabel.Content = "An unknown error has occurred.";
                    break;
            }

            switch (phraseReadyToRun)
            {
                case 0:
                    PhraseRunButton.IsEnabled = true;
                    break;
                case 1:
                    PhraseWarningLabel.Content = "No source log files selected.";
                    break;
                case 2:
                    PhraseWarningLabel.Content = "No destination directory selected.";
                    break;
                case 3:
                    PhraseWarningLabel.Content = "Destination directory does not exist.";
                    break;
                case 4:
                    PhraseWarningLabel.Content = "No search text entered.";
                    break;
                case 5:
                    PhraseWarningLabel.Content = "One or more source files do not exist.";
                    break;
                case 6:
                    PhraseWarningLabel.Content = "One or more source files exist in the destination.";
                    break;
                default:
                    PhraseWarningLabel.Content = "An unknown error has occurred.";
                    break;
            }

            return;
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

                TransitionMenus(true);
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
                SolidColorBrush[][] brushCombos =
                {
                    new SolidColorBrush[] { Brushes.Black, Brushes.White },
                    new SolidColorBrush[] { Brushes.DarkGray, Brushes.LightGray },
                    new SolidColorBrush[] { Brushes.LightBlue, Brushes.Beige },
                    new SolidColorBrush[] { new SolidColorBrush(new Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray },
                    new SolidColorBrush[] { Brushes.Pink, Brushes.Red },
                    new SolidColorBrush[] { Brushes.Yellow, Brushes.DarkOrange },
                };

                int brushPalette = 1;
                int reversePalette = 0;

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

                // Grids
                DirectoryGrid.Background = brushCombos[3][brushPalette];
                FileGrid.Background = brushCombos[3][brushPalette];
                HelpGrid.Background = brushCombos[3][brushPalette];
                PhraseGrid.Background = brushCombos[3][brushPalette];

                // TabControl
                TabMenu.Background = brushCombos[3][brushPalette];

                // Textboxes
                DirectoryLogWindow.Background = brushCombos[0][brushPalette];
                DirectoryLogWindow.BorderBrush = brushCombos[3][reversePalette];
                DirectoryLogWindow.Foreground = brushCombos[0][reversePalette];
                DirectoryOutput.Background = brushCombos[0][brushPalette];
                DirectoryOutput.BorderBrush = brushCombos[3][reversePalette];
                DirectoryOutput.Foreground = brushCombos[0][reversePalette];
                DirectorySource.Background = brushCombos[0][brushPalette];
                DirectorySource.BorderBrush = brushCombos[3][reversePalette];
                DirectorySource.Foreground = brushCombos[0][reversePalette];
                FileOutput.Background = brushCombos[0][brushPalette];
                FileOutput.BorderBrush = brushCombos[3][reversePalette];
                FileOutput.Foreground = brushCombos[0][reversePalette];
                FileSource.Background = brushCombos[0][brushPalette];
                FileSource.BorderBrush = brushCombos[3][reversePalette];
                FileSource.Foreground = brushCombos[0][reversePalette];
                LogWindow.Background = brushCombos[0][brushPalette];
                LogWindow.BorderBrush = brushCombos[3][reversePalette];
                LogWindow.Foreground = brushCombos[0][reversePalette];
                PhraseLogWindow.Background = brushCombos[0][brushPalette];
                PhraseLogWindow.BorderBrush = brushCombos[3][reversePalette];
                PhraseLogWindow.Foreground = brushCombos[0][reversePalette];
                PhraseOutput.Background = brushCombos[0][brushPalette];
                PhraseOutput.BorderBrush = brushCombos[3][reversePalette];
                PhraseOutput.Foreground = brushCombos[0][reversePalette];
                PhraseSearch.Background = brushCombos[0][brushPalette];
                PhraseSearch.BorderBrush = brushCombos[3][reversePalette];
                PhraseSearch.Foreground = brushCombos[0][reversePalette];
                PhraseSource.Background = brushCombos[0][brushPalette];
                PhraseSource.BorderBrush = brushCombos[3][reversePalette];
                PhraseSource.Foreground = brushCombos[0][reversePalette];

                // ListBoxItems
                foreach (ListBoxItem lbItem in LogWindow.Items)
                {
                    lbItem.Foreground = brushCombos[0][reversePalette];
                    lbItem.Background = brushCombos[0][brushPalette];
                }

                // Buttons
                DirectoryRunButton.Background = brushCombos[2][brushPalette];
                DirectoryThemeSelector.Background = brushCombos[2][brushPalette];
                DstDirectoryButton.Background = brushCombos[2][brushPalette];
                DstFileButton.Background = brushCombos[2][brushPalette];
                DstPhraseButton.Background = brushCombos[2][brushPalette];
                PhraseRunButton.Background = brushCombos[2][brushPalette];
                PhraseThemeSelector.Background = brushCombos[2][brushPalette];
                RunButton.Background = brushCombos[2][brushPalette];
                SrcDirectoryButton.Background = brushCombos[2][brushPalette];
                SrcFileButton.Background = brushCombos[2][brushPalette];
                SrcPhraseButton.Background = brushCombos[2][brushPalette];
                ThemeSelector.Background = brushCombos[2][brushPalette];

                // Labels
                ADLWarning.Foreground = brushCombos[4][brushPalette];
                AfterDateLabel.Foreground = brushCombos[0][reversePalette];
                BeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryAfterDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryBeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryOutputLabel.Foreground = brushCombos[0][reversePalette];
                DirectorySourceLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryVersionNumber.Foreground = brushCombos[0][reversePalette];
                DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
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
                PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
                WarningLabel.Foreground = brushCombos[4][brushPalette];

                if (directoryReadyToRun == 0)
                    DirectoryWarningLabel.Foreground = brushCombos[5][brushPalette];
                if (phraseReadyToRun == 0)
                    PhraseWarningLabel.Foreground = brushCombos[5][brushPalette];
                if (fileReadyToRun == 0)
                    WarningLabel.Foreground = brushCombos[5][brushPalette];
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
        }

        private void TransitionMenus(bool isProcessing)
        {
            if (isProcessing)
            {
                DirectoryOutput.IsEnabled = false;
                DirectoryRunButton.IsEnabled = false;
                DirectoryRunButton.Content = "Scanning...";
                DirectorySource.IsEnabled = false;
                DstDirectoryButton.IsEnabled = false;
                DstFileButton.IsEnabled = false;
                DstPhraseButton.IsEnabled = false;
                FileOutput.IsEnabled = false;
                FileSource.IsEnabled = false;
                PhraseOutput.IsEnabled = false;
                PhraseRunButton.Content = "Scanning...";
                PhraseRunButton.IsEnabled = false;
                PhraseSource.IsEnabled = false;
                RunButton.Content = "Scanning...";
                RunButton.IsEnabled = false;
                SrcDirectoryButton.IsEnabled = false;
                SrcFileButton.IsEnabled = false;
                SrcPhraseButton.IsEnabled = false;

                if (filesProcessed == 1)
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {Path.GetFileName(srcFile)}...";
                else
                    HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = $"Scanning {filesProcessed:N0} files...";
                return;
            }

            DirectoryOutput.IsEnabled = true;
            DirectoryRunButton.IsEnabled = true;
            DirectoryRunButton.Content = "Run";
            DirectorySource.IsEnabled = true;
            DstDirectoryButton.IsEnabled = true;
            DstFileButton.IsEnabled = true;
            DstPhraseButton.IsEnabled = true;
            FileOutput.IsEnabled = true;
            FileSource.IsEnabled = true;
            PhraseOutput.IsEnabled = true;
            PhraseRunButton.Content = "Run";
            PhraseRunButton.IsEnabled = true;
            PhraseSource.IsEnabled = true;
            RunButton.Content = "Run";
            RunButton.IsEnabled = true;
            SrcDirectoryButton.IsEnabled = true;
            SrcFileButton.IsEnabled = true;
            SrcPhraseButton.IsEnabled = true;

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
                RunButton.IsEnabled = DirectoryRunButton.IsEnabled = PhraseRunButton.IsEnabled = false;
                WarningLabel.Content = DirectoryWarningLabel.Content = PhraseWarningLabel.Content = "";
                WarningLabel.Foreground = DirectoryWarningLabel.Foreground = PhraseWarningLabel.Foreground = (ThemeSelector.Content as string).Equals("Light") ? Brushes.Pink : Brushes.Red;

                if (DirectorySource.Text.Length == 0)
                    directoryReadyToRun = 1;
                else if (DirectoryOutput.Text.Length == 0)
                    directoryReadyToRun = 2;
                else if (Directory.Exists(DirectoryOutput.Text) == false)
                    directoryReadyToRun = 3;
                else
                {
                    foreach (string file in DirectorySource.Text.Split(';'))
                    {
                        if (File.Exists(file) == false)
                            directoryReadyToRun = 4;
                        else if (file.Equals(Path.Join(DirectoryOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            directoryReadyToRun = 5;
                        else if (directoryReadyToRun == 0 && File.Exists(Path.Join(DirectoryOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                        {
                            DirectoryWarningLabel.Foreground = (ThemeSelector.Content as string).Equals("Light") ? Brushes.Yellow : Brushes.DarkOrange;
                            DirectoryWarningLabel.Content = "One or more files will be overwritten.";
                        }
                    }
                }

                if (FileSource.Text.Length == 0)
                    fileReadyToRun = 1;
                else if (File.Exists(FileSource.Text) == false)
                    fileReadyToRun = 2;
                else if (FileOutput.Text.Length == 0)
                    fileReadyToRun = 3;
                else if (Directory.Exists(FileOutput.Text))
                    fileReadyToRun = 4;
                else if (Directory.Exists(Path.GetDirectoryName(FileOutput.Text)) == false)
                    fileReadyToRun = 5;
                else if (FileSource.Text.Equals(FileOutput.Text))
                    fileReadyToRun = 6;
                else if (File.Exists(FileOutput.Text))
                {
                    WarningLabel.Foreground = (ThemeSelector.Content as string).Equals("Light") ? Brushes.Yellow : Brushes.DarkOrange;
                    WarningLabel.Content = "Destination file will be overwritten.";
                }

                if (PhraseSource.Text.Length == 0)
                    phraseReadyToRun = 1;
                else if (PhraseOutput.Text.Length == 0)
                    phraseReadyToRun = 2;
                else if (Directory.Exists(PhraseOutput.Text) == false)
                    phraseReadyToRun = 3;
                else if (PhraseSearch.Text.Length == 0)
                    phraseReadyToRun = 4;
                else
                {
                    foreach (string file in PhraseSource.Text.Split(';'))
                    {
                        if (File.Exists(file) == false)
                            phraseReadyToRun = 5;
                        else if (file.Equals(Path.Join(PhraseOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                            phraseReadyToRun = 6;
                        else if (phraseReadyToRun == 0 && File.Exists(Path.Join(PhraseOutput.Text, Path.GetFileNameWithoutExtension(file) + ".txt")))
                        {
                            PhraseWarningLabel.Foreground = (ThemeSelector.Content as string).Equals("Light") ? Brushes.Yellow : Brushes.DarkOrange;
                            PhraseWarningLabel.Content = "One or more files will be overwritten.";
                        }
                    }
                }

                ProcessWarnings();
            }
            catch (Exception ex)
            {
                HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = LogException(ex);
                return;
            }
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
                TransitionMenus(false);
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
                thisDT = DTFromStamp(timestamp);
                if (IsValidTimestamp(timestamp) == false)
                {
                    corruptTimestamps++;
                    intact = false;
                    messageOut = "[BAD TIMESTAMP] ";
                }
                else
                {
                    lastTimestamp = timestamp;
                    messageOut += "[" + thisDT.ToString(dateFormat) + "] ";
                    if (thisDT.CompareTo(dtBefore) > 0 || thisDT.CompareTo(dtAfter) < 0)
                        withinRange = false;
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
                    if (nextByte < 6)
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
                        if (nextByte < 6)
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
