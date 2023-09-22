using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
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
        private uint filesProcessed;

        // 0 if we are ready to process files; all other values indicate the nature of an error.
        private uint fileReadyToRun = 1;
        private uint directoryReadyToRun = 1;
        private uint phraseReadyToRun = 1;

        private readonly static DateTime epoch = new(1970, 1, 1, 0, 0, 0);
        private DateTime timeBegin;
        private DateTime? dtBefore;
        private DateTime? dtAfter;

        private readonly string[] prefixes = { "", "k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q" }; // Always futureproof...
        private int prefixIndex = 0;
        double finalBytes;

        private readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
        private string? srcFile = "";
        private string? destFile = "";
        private string? destDir = "";
        private string? phrase = "";
        private int result;
        private byte[]? idBuffer;
        private byte[]? streamBuffer;
        private int nextByte;
        private uint lastPosition;
        private uint lastTimestamp;
        private int lastDiscrepancy;
        private bool intact;
        private bool saveTruncated;

        private uint intactMessages;
        private uint discardedMessages;
        private uint intactBytes;
        private uint discardedBytes;
        private uint corruptTimestamps;
        private uint truncatedMessages;
        private uint truncatedBytes;
        private uint emptyMessages;
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
            return (uint)(buffer[0] + buffer[1] * 256U + buffer[2] * 65536U + buffer[3] * 16777216U);
        }

        private static uint UNIXTimestamp()
        {
            return (uint)(Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds));
        }

        private static DateTime DTFromStamp(uint stamp)
        {
            try
            {
                return epoch.AddSeconds(stamp);
            }
            catch (Exception)
            {
                return epoch;
            }
        }

        private static void LogException(Exception e)
        {
            File.AppendAllText("FLogS_ERROR.txt", DateTime.Now.ToString(dateFormat) + " - " + e.Message + "\n");
            return;
        }

        private void MainGrid_Loaded(object sender, RoutedEventArgs e)
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

        private void ThemeSelector_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SolidColorBrush[][] brushCombos =
                {
                    new SolidColorBrush[] { Brushes.Black, Brushes.White },
                    new SolidColorBrush[] { Brushes.DarkGray, Brushes.LightGray },
                    new SolidColorBrush[] { Brushes.LightBlue, Brushes.Beige },
                    new SolidColorBrush[] { new SolidColorBrush(new System.Windows.Media.Color() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray },
                    new SolidColorBrush[] { Brushes.Pink, Brushes.Red },
                    new SolidColorBrush[] { Brushes.Yellow, Brushes.DarkOrange },
                };

                int brushPalette = 1;
                int reversePalette = 0;

                if (ThemeSelector.Content.ToString().Equals("Dark"))
                {
                    brushPalette = 0;
                    reversePalette = 1;
                    ThemeSelector.Content = "Light";
                    DirectoryThemeSelector.Content = "Light";
                    PhraseThemeSelector.Content = "Light";
                }
                else
                {
                    ThemeSelector.Content = "Dark";
                    DirectoryThemeSelector.Content = "Dark";
                    PhraseThemeSelector.Content = "Dark";
                }

                // Grids
                FileGrid.Background = brushCombos[3][brushPalette];
                DirectoryGrid.Background = brushCombos[3][brushPalette];
                PhraseGrid.Background = brushCombos[3][brushPalette];
                HelpGrid.Background = brushCombos[3][brushPalette];

                // TabControl
                TabMenu.Background = brushCombos[3][brushPalette];

                // Textboxes
                LogWindow.Background = brushCombos[0][brushPalette];
                LogWindow.Foreground = brushCombos[0][reversePalette];
                LogWindow.BorderBrush = brushCombos[3][reversePalette];
                FileSource.Background = brushCombos[0][brushPalette];
                FileSource.Foreground = brushCombos[0][reversePalette];
                FileSource.BorderBrush = brushCombos[3][reversePalette];
                FileOutput.Background = brushCombos[0][brushPalette];
                FileOutput.Foreground = brushCombos[0][reversePalette];
                FileOutput.BorderBrush = brushCombos[3][reversePalette];
                DirectoryLogWindow.Background = brushCombos[0][brushPalette];
                DirectoryLogWindow.Foreground = brushCombos[0][reversePalette];
                DirectoryLogWindow.BorderBrush = brushCombos[3][reversePalette];
                DirectorySource.Background = brushCombos[0][brushPalette];
                DirectorySource.Foreground = brushCombos[0][reversePalette];
                DirectorySource.BorderBrush = brushCombos[3][reversePalette];
                DirectoryOutput.Background = brushCombos[0][brushPalette];
                DirectoryOutput.Foreground = brushCombos[0][reversePalette];
                DirectoryOutput.BorderBrush = brushCombos[3][reversePalette];
                PhraseLogWindow.Background = brushCombos[0][brushPalette];
                PhraseLogWindow.Foreground = brushCombos[0][reversePalette];
                PhraseLogWindow.BorderBrush = brushCombos[3][reversePalette];
                PhraseSource.Background = brushCombos[0][brushPalette];
                PhraseSource.Foreground = brushCombos[0][reversePalette];
                PhraseSource.BorderBrush = brushCombos[3][reversePalette];
                PhraseOutput.Background = brushCombos[0][brushPalette];
                PhraseOutput.Foreground = brushCombos[0][reversePalette];
                PhraseOutput.BorderBrush = brushCombos[3][reversePalette];
                PhraseSearch.Background = brushCombos[0][brushPalette];
                PhraseSearch.Foreground = brushCombos[0][reversePalette];
                PhraseSearch.BorderBrush = brushCombos[3][reversePalette];

                // ListBoxItems
                foreach (ListBoxItem lbItem in LogWindow.Items)
                {
                    lbItem.Foreground = brushCombos[0][reversePalette];
                    lbItem.Background = brushCombos[0][brushPalette];
                }

                // Buttons
                ThemeSelector.Background = brushCombos[2][brushPalette];
                SrcFileButton.Background = brushCombos[2][brushPalette];
                DstFileButton.Background = brushCombos[2][brushPalette];
                RunButton.Background = brushCombos[2][brushPalette];
                DirectoryThemeSelector.Background = brushCombos[2][brushPalette];
                SrcDirectoryButton.Background = brushCombos[2][brushPalette];
                DstDirectoryButton.Background = brushCombos[2][brushPalette];
                DirectoryRunButton.Background = brushCombos[2][brushPalette];
                PhraseThemeSelector.Background = brushCombos[2][brushPalette];
                SrcPhraseButton.Background = brushCombos[2][brushPalette];
                DstPhraseButton.Background = brushCombos[2][brushPalette];
                PhraseRunButton.Background = brushCombos[2][brushPalette];

                // Labels
                FileSourceLabel.Foreground = brushCombos[0][reversePalette];
                FileOutputLabel.Foreground = brushCombos[0][reversePalette];
                FileVersionNumber.Foreground = brushCombos[0][reversePalette];
                BeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                AfterDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectorySourceLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryOutputLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryVersionNumber.Foreground = brushCombos[0][reversePalette];
                DirectoryBeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryAfterDateLabel.Foreground = brushCombos[0][reversePalette];
                PhraseSourceLabel.Foreground = brushCombos[0][reversePalette];
                PhraseOutputLabel.Foreground = brushCombos[0][reversePalette];
                PhraseSearchLabel.Foreground = brushCombos[0][reversePalette];
                PhraseVersionNumber.Foreground = brushCombos[0][reversePalette];
                PhraseBeforeDateLabel.Foreground = brushCombos[0][reversePalette];
                PhraseAfterDateLabel.Foreground = brushCombos[0][reversePalette];
                HelpVersionNumber.Foreground = brushCombos[0][reversePalette];
                HelpHeader1.Foreground = brushCombos[0][reversePalette];
                HelpHeader2.Foreground = brushCombos[0][reversePalette];
                HelpText1.Foreground = brushCombos[0][reversePalette];
                HelpText2.Foreground = brushCombos[0][reversePalette];
                ADLWarning.Foreground = brushCombos[4][brushPalette];
                WarningLabel.Foreground = brushCombos[4][brushPalette];
                DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
                PhraseWarningLabel.Foreground = brushCombos[4][brushPalette];
                if (fileReadyToRun == 0)
                    WarningLabel.Foreground = brushCombos[5][brushPalette];
                if (directoryReadyToRun == 0)
                    DirectoryWarningLabel.Foreground = brushCombos[5][brushPalette];
                if (phraseReadyToRun == 0)
                    PhraseWarningLabel.Foreground = brushCombos[5][brushPalette];
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void TransitionMenus(bool isProcessing)
        {
            if (isProcessing)
            {
                RunButton.IsEnabled = false;
                RunButton.Content = "Scanning...";
                FileSource.IsEnabled = false;
                FileOutput.IsEnabled = false;
                SrcFileButton.IsEnabled = false;
                DstFileButton.IsEnabled = false;
                DirectoryRunButton.IsEnabled = false;
                DirectoryRunButton.Content = "Scanning...";
                DirectorySource.IsEnabled = false;
                DirectoryOutput.IsEnabled = false;
                SrcDirectoryButton.IsEnabled = false;
                DstDirectoryButton.IsEnabled = false;
                PhraseRunButton.IsEnabled = false;
                PhraseRunButton.Content = "Scanning...";
                PhraseSource.IsEnabled = false;
                PhraseOutput.IsEnabled = false;
                SrcPhraseButton.IsEnabled = false;
                DstPhraseButton.IsEnabled = false;
                if (filesProcessed == 1)
                {
                    headerBox.Content = "Scanning " + Path.GetFileName(srcFile) + "...";
                    DirectoryheaderBox.Content = "Scanning " + Path.GetFileName(srcFile) + "...";
                    PhraseheaderBox.Content = "Scanning " + Path.GetFileName(srcFile) + "...";
                }
                else
                {
                    headerBox.Content = $"Scanning {filesProcessed:N0} files...";
                    DirectoryheaderBox.Content = $"Scanning {filesProcessed:N0} files...";
                    PhraseheaderBox.Content = $"Scanning {filesProcessed:N0} files...";
                }
                return;
            }

            RunButton.IsEnabled = true;
            RunButton.Content = "Run";
            FileSource.IsEnabled = true;
            FileOutput.IsEnabled = true;
            SrcFileButton.IsEnabled = true;
            DstFileButton.IsEnabled = true;
            DirectoryRunButton.IsEnabled = true;
            DirectoryRunButton.Content = "Run";
            DirectorySource.IsEnabled = true;
            DirectoryOutput.IsEnabled = true;
            SrcDirectoryButton.IsEnabled = true;
            DstDirectoryButton.IsEnabled = true;
            PhraseRunButton.IsEnabled = true;
            PhraseRunButton.Content = "Run";
            PhraseSource.IsEnabled = true;
            PhraseOutput.IsEnabled = true;
            SrcPhraseButton.IsEnabled = true;
            DstPhraseButton.IsEnabled = true;
            double timeTaken = DateTime.Now.Subtract(timeBegin).TotalSeconds;
            if (filesProcessed == 1)
            {
                headerBox.Content = string.Format("Processed {0} in {1:#,0.00} seconds.", Path.GetFileName(srcFile), timeTaken);
                DirectoryheaderBox.Content = string.Format("Processed {0} in {1:#,0.00} seconds.", Path.GetFileName(srcFile), timeTaken);
                PhraseheaderBox.Content = string.Format("Processed {0} in {1:#,0.00} seconds.", Path.GetFileName(srcFile), timeTaken);
            }
            else
            {
                headerBox.Content = string.Format("Processed {0} files in {1:#,0.00} seconds.", filesProcessed, timeTaken);
                DirectoryheaderBox.Content = string.Format("Processed {0} files in {1:#,0.00} seconds.", filesProcessed, timeTaken);
                PhraseheaderBox.Content = string.Format("Processed {0} files in {1:#,0.00} seconds.", filesProcessed, timeTaken);
            }
            return;
        }

        private void ComboBox_Update(object sender, RoutedEventArgs e)
        {
            if (SaveTruncated == null || DirectorySaveTruncated == null || PhraseSaveTruncated == null)
                return;

            if ((sender as ComboBox).SelectedIndex == 0)
            {
                SaveTruncated.SelectedIndex = 0;
                DirectorySaveTruncated.SelectedIndex = 0;
                PhraseSaveTruncated.SelectedIndex = 0;
                return;
            }

            SaveTruncated.SelectedIndex = 1;
            DirectorySaveTruncated.SelectedIndex = 1;
            PhraseSaveTruncated.SelectedIndex = 1;
        }

        private void DatePicker_Update(object sender, RoutedEventArgs e)
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

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                srcFile = FileSource.Text;
                filesProcessed = 1;
                TransitionMenus(true);

                BackgroundWorker worker = new()
                {
                    WorkerReportsProgress = true
                };
                worker.DoWork += Worker_DoWork;
                worker.ProgressChanged += Worker_ProgressChanged;
                worker.RunWorkerCompleted += Worker_Completed;

                destFile = FileOutput.Text;
                result = 0;
                idBuffer = new byte[4];
                nextByte = 255;
                lastPosition = 0U;
                saveTruncated = SaveTruncated.SelectedIndex != 0;
                timeBegin = DateTime.Now;
                dtBefore = BeforeDate.SelectedDate;
                dtAfter = AfterDate.SelectedDate;
                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                FileProgress.Maximum = new FileInfo(srcFile).Length;
                DirectoryProgress.Maximum = new FileInfo(srcFile).Length;
                PhraseProgress.Maximum = new FileInfo(srcFile).Length;

                intactMessages = 0U;
                discardedMessages = 0U;
                intactBytes = 0U;
                discardedBytes = 0U;
                corruptTimestamps = 0U;
                truncatedMessages = 0U;
                truncatedBytes = 0U;
                emptyMessages = 0U;
                unreadBytes = 0;
                filesProcessed = 1;

                UpdateLogs();
                worker.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void DirectoryRunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] files = DirectorySource.Text.Split(';');
                filesProcessed = (uint)(files.Length);
                if (filesProcessed == 1)
                    srcFile = files[0];

                TransitionMenus(true);

                timeBegin = DateTime.Now;
                dtBefore = DirectoryBeforeDate.SelectedDate;
                dtAfter = DirectoryAfterDate.SelectedDate;
                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                intactMessages = 0U;
                discardedMessages = 0U;
                intactBytes = 0U;
                discardedBytes = 0U;
                corruptTimestamps = 0U;
                truncatedMessages = 0U;
                truncatedBytes = 0U;
                emptyMessages = 0U;
                unreadBytes = 0;
                bytesRead = 0;

                UpdateLogs();

                destDir = DirectoryOutput.Text;
                saveTruncated = DirectorySaveTruncated.SelectedIndex != 0;

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
                LogException(ex);
                return;
            }
        }

        private void PhraseRunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] files = PhraseSource.Text.Split(';');
                filesProcessed = (uint)(files.Length);
                if (filesProcessed == 1)
                    srcFile = files[0];

                TransitionMenus(true);

                timeBegin = DateTime.Now;
                dtBefore = PhraseBeforeDate.SelectedDate;
                dtAfter = PhraseAfterDate.SelectedDate;
                if (dtBefore < dtAfter)
                    (dtAfter, dtBefore) = (dtBefore, dtAfter);

                intactMessages = 0U;
                discardedMessages = 0U;
                intactBytes = 0U;
                discardedBytes = 0U;
                corruptTimestamps = 0U;
                truncatedMessages = 0U;
                truncatedBytes = 0U;
                emptyMessages = 0U;
                unreadBytes = 0;
                bytesRead = 0;

                UpdateLogs();

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
                LogException(ex);
                return;
            }
        }

        private void Worker_BatchProcess(object sender, DoWorkEventArgs e)
        {
            try
            {
                string[] files = (string[])(e.Argument);

                foreach (string logfile in files)
                {
                    srcFile = logfile;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(srcFile) + ".txt");
                    if (File.Exists(destFile) == false)
                    {
                        result = 0;
                        idBuffer = new byte[4];
                        nextByte = 255;
                        lastPosition = 0U;

                        Worker_DoWork(sender, e);
                        bytesRead += (uint)(new FileInfo(logfile).Length);
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="srcFS">A FileStream opened to the source log file.</param>
        /// <param name="dstFS">A StreamWriter opened to the destination file.</param>
        /// <returns>'true' if a message was written to file; 'false' if, for any reason, that did not occur.</returns>
        private bool Worker_TranslateMessage(FileStream srcFS, StreamWriter dstFS)
        {
            MessageType msId;
            string profileName;
            int discrepancy;
            uint messageLength = 0U;
            string messageData = "";
            uint timestamp;
            bool nextTimestamp = false;
            bool withinRange = true;
            bool matchPhrase = false;
            string messageOut = "";
            bool written = false;
            intact = true;
            idBuffer = new byte[4];
            DateTime thisDT;

            try
            {
                discrepancy = (int)srcFS.Position - (int)lastPosition; // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
                unreadBytes += discrepancy;
                lastDiscrepancy += discrepancy;
                result = srcFS.Read(idBuffer, 0, 4); // Read the timestamp.
                if (result < 4)
                    return written;
                timestamp = BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
                thisDT = DTFromStamp(timestamp);
                if (timestamp < 1                              // If it came before Jan. 1, 1970, there's probably a problem.
                    || timestamp > UNIXTimestamp()             // If it's in the future, also a problem.
                    || thisDT.ToString(dateFormat).Length == 0 // If it can't be translated to a date, also a problem.
                    || timestamp < lastTimestamp)              /* If it isn't sequential, also a problem, because F-Chat would never save it that way.
                                                                * In this case specifically, there's an extremely high chance we're about to produce garbage data in the output. */
                {
                    intact = false;
                    corruptTimestamps++;
                    messageOut = "[BAD TIMESTAMP] ";
                }
                else
                {
                    if ((dtBefore != null && thisDT.CompareTo(dtBefore) > 0) || (dtAfter != null && thisDT.CompareTo(dtAfter) < 0))
                        withinRange = false;
                    messageOut += "[" + thisDT.ToString(dateFormat) + "] ";
                    lastTimestamp = timestamp;
                }
                nextByte = srcFS.ReadByte(); // Read the delimiter.
                if (nextByte == -1)
                    return written;
                msId = (MessageType)(int)nextByte;
                if (msId != MessageType.Headless)
                {
                    nextByte = srcFS.ReadByte(); // 1-byte length of profile name.
                    if (nextByte == -1)
                        return written;
                    streamBuffer = new byte[nextByte];
                    result = srcFS.Read(streamBuffer, 0, nextByte); // Read the profile name.
                    if (result < nextByte)
                    {
                        intact = false;
                        emptyMessages++;
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
                            case MessageType.DiceRoll:
                                messageOut += "";
                                break;
                            case MessageType.Warning:
                                messageOut += " (warning): ";
                                break;
                        }
                    }
                }
                else
                    nextByte = srcFS.ReadByte();
                result = srcFS.Read(idBuffer, 0, 2);
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
                        result = srcFS.Read(streamBuffer, 0, (int)messageLength);
                    }
                }
                if (result == 0)
                {
                    intact = false;
                    emptyMessages++;
                    messageOut += "[EMPTY MESSAGE]";
                }
                else if (result < messageLength)
                {
                    intact = false;
                    truncatedMessages++;
                    truncatedBytes += (uint)result;
                    messageOut += "[TRUNCATED MESSAGE] ";
                }
                if (result > 0)
                    messageData = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                messageData = Regex.Replace(messageData, @"[^\u0009\u000A\u000D\u0020-\u007E]", ""); // Remove everything that's not a printable or newline character.
                if (phrase == null || (messageOut + messageData).Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    matchPhrase = true;
                if (intact)
                {
                    intactMessages++;
                    intactBytes += (uint)messageOut.Length + (uint)messageData.Length;
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
                    else
                    {
                        discardedMessages++;
                        discardedBytes += (uint)messageOut.Length + (uint)messageData.Length;
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
                lastPosition = (uint)(srcFS.Position);
                while (!nextTimestamp)
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
                        unreadBytes += discrepancy;
                        lastDiscrepancy += discrepancy;
                        lastPosition = (uint)srcFS.Position;
                        srcFS.ReadByte();
                        nextTimestamp = true;
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
                            unreadBytes += discrepancy;
                            lastDiscrepancy += discrepancy;
                            lastPosition = (uint)srcFS.Position;
                            nextTimestamp = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

            return written;
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            FileStream srcFS;
            try
            {
                if (File.Exists(destFile))
                    File.Delete(destFile);

                srcFS = File.OpenRead(srcFile);
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }

            using (StreamWriter dstFS = new(destFile, false))
            {
                DateTime lastUpdate = DateTime.Now;
                lastPosition = 0U;
                lastTimestamp = 0;

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

        private string ByteSizeString(double bytes)
        {
            finalBytes = bytes;
            prefixIndex = 0;
            while (finalBytes >= 921.6 && prefixIndex < 10)
            {
                finalBytes *= 0.0009765625; // 1/1024
                prefixIndex++;
            }

            if (prefixIndex == 0)
                return string.Format("{0:#,0} B", finalBytes);
            return string.Format("{0:#,0.0} {1}B", finalBytes, prefixes[prefixIndex]);
        }

        private void UpdateLogs()
        {
            int intactCount;
            int byteCount;
            string byteString;

            intactCount = (int)intactMessages - (int)discardedMessages;
            byteCount = (int)intactBytes - (int)discardedBytes;
            byteString = ByteSizeString(byteCount);
            imBox.Content = $"Intact Messages: {intactCount:N0} ({byteString})";
            ctBox.Content = $"Corrupted Timestamps: {corruptTimestamps:N0}";
            byteString = ByteSizeString(truncatedBytes);
            tmBox.Content = $"Truncated Messages: {truncatedMessages:N0} ({byteString})";
            emBox.Content = $"Empty Messages: {emptyMessages:N0}";
            byteString = ByteSizeString(unreadBytes);
            ubBox.Content = $"Unread Bytes: {unreadBytes:N0} ({byteString})";
            PhraseimBox.Content = DirectoryimBox.Content = imBox.Content;
            PhrasectBox.Content = DirectoryctBox.Content = ctBox.Content;
            PhrasetmBox.Content = DirectorytmBox.Content = tmBox.Content;
            PhraseemBox.Content = DirectoryemBox.Content = emBox.Content;
            PhraseubBox.Content = DirectoryubBox.Content = ubBox.Content;

            return;
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
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
                LogException(ex);
                return;
            }
        }

        private void Worker_Completed(object sender, EventArgs e)
        {
            try
            {
                FileProgress.Value = FileProgress.Maximum;
                DirectoryProgress.Value = DirectoryProgress.Maximum;
                PhraseProgress.Value = PhraseProgress.Maximum;

                UpdateLogs();
                TransitionMenus(false);
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void ProcessWarnings()
        {
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

        private void TextboxUpdated(object sender, EventArgs e)
        {
            try
            {
                fileReadyToRun = directoryReadyToRun = phraseReadyToRun = 0;
                WarningLabel.Content = DirectoryWarningLabel.Content = PhraseWarningLabel.Content = "";
                WarningLabel.Foreground = DirectoryWarningLabel.Foreground = PhraseWarningLabel.Foreground = (ThemeSelector.Content as string) == "Light" ? Brushes.Pink : Brushes.Red;
                RunButton.IsEnabled = DirectoryRunButton.IsEnabled = PhraseRunButton.IsEnabled = false;

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
                    WarningLabel.Foreground = (ThemeSelector.Content as string) == "Light" ? Brushes.Yellow : Brushes.DarkOrange;
                    WarningLabel.Content = "Destination file will be overwritten.";
                }

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
                            DirectoryWarningLabel.Foreground = (ThemeSelector.Content as string) == "Light" ? Brushes.Yellow : Brushes.DarkOrange;
                            DirectoryWarningLabel.Content = "One or more files will be overwritten.";
                        }
                    }
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
                            PhraseWarningLabel.Foreground = (ThemeSelector.Content as string) == "Light" ? Brushes.Yellow : Brushes.DarkOrange;
                            PhraseWarningLabel.Content = "One or more files will be overwritten.";
                        }
                    }
                }

                ProcessWarnings();
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void SrcFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new();
                if (openFileDialog.ShowDialog() == true)
                    FileSource.Text = openFileDialog.FileName;
                else
                    FileSource.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void DstFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    CheckFileExists = false
                };
                if (openFileDialog.ShowDialog() == true)
                    FileOutput.Text = openFileDialog.FileName;
                else
                    FileOutput.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void SrcDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    Multiselect = true
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    DirectorySource.Text = "";
                    foreach (string logfile in openFileDialog.FileNames)
                        if (logfile.Contains(".idx") == false)
                            DirectorySource.Text += logfile + ";";
                    DirectorySource.Text = DirectorySource.Text.Remove(DirectorySource.Text.Length - 1);
                }
                else
                    DirectorySource.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void DstDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new()
                {
                    ShowNewFolderButton = true
                };
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    DirectoryOutput.Text = folderBrowserDialog.SelectedPath;
                else
                    DirectoryOutput.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void SrcPhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new()
                {
                    Multiselect = true
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    PhraseSource.Text = "";
                    foreach (string logfile in openFileDialog.FileNames)
                        if (logfile.Contains(".idx") == false)
                            PhraseSource.Text += logfile + ";";
                    PhraseSource.Text = PhraseSource.Text.Remove(PhraseSource.Text.Length - 1);
                }
                else
                    PhraseSource.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void DstPhraseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Forms.FolderBrowserDialog folderBrowserDialog = new()
                {
                    ShowNewFolderButton = true
                };
                if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    PhraseOutput.Text = folderBrowserDialog.SelectedPath;
                else
                    PhraseOutput.Text = "";
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }
    }
}
