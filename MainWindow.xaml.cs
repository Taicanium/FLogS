using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace FLogS
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("UXTheme.dll", SetLastError = true, EntryPoint = "#138")]
        public static extern bool ShouldSystemUseDarkMode();

        public MainWindow()
        {
            InitializeComponent();
        }

        private uint bytesRead;
        private uint filesProcessed;
        DateTime timeBegin;
        private string? srcFile;
        private string? destFile;
        private string? destDir;
        private int result;
        private byte[]? idBuffer;
        private byte[]? streamBuffer;
        private int nextByte;
        private uint lastPosition;
        private bool intact;
        private bool saveTruncated;
        private bool destAlreadyExists;

        private uint intactMessages;
        private uint intactBytes;
        private uint corruptTimestamps;
        private uint truncatedMessages;
        private uint truncatedBytes;
        private uint emptyMessages;
        private int unreadBytes;

        enum MessageType
        {
            EOF = -1,
            Regular = 0,
            Me = 1,
            BottleSpin = 2,
            DiceRoll = 3,
            Warning = 4,
        }

        private static uint BEInt(byte[] buffer)
        {
            return (uint)(buffer[0] + buffer[1] * 256U + buffer[2] * 65536U + buffer[3] * 16777216U);
        }

        private static uint UNIXTimestamp()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0);
            return (uint)(Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds));
        }

        private static DateTime DTFromStamp(uint stamp)
        {
            DateTime dtout = new DateTime(1970, 1, 1, 0, 0, 0);
            return dtout.AddSeconds(stamp);
        }

        private void LogException(Exception e)
        {
            File.AppendAllText("FLogS_ERROR.txt", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " - " + e.Message + "\n");
            if (e.StackTrace != null) File.AppendAllText("FLogS_ERROR.txt", e.StackTrace + "\n");
            return;
        }

        private void MainGrid_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ShouldSystemUseDarkMode())
                    ThemeSelector_Click(sender, e);
            }
            catch (Exception ex)
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
            };

                int brushPalette = 1;
                int reversePalette = 0;

                if (ThemeSelector.Content.ToString().Equals("Dark"))
                {
                    brushPalette = 0;
                    reversePalette = 1;
                    ThemeSelector.Content = "Light";
                    DirectoryThemeSelector.Content = "Light";
                }
                else
                {
                    ThemeSelector.Content = "Dark";
                    DirectoryThemeSelector.Content = "Dark";
                }

                // Grids
                FileGrid.Background = brushCombos[3][brushPalette];
                DirectoryGrid.Background = brushCombos[3][brushPalette];
                HelpGrid.Background = brushCombos[3][brushPalette];

                // TabControl
                TabMenu.Background = brushCombos[3][brushPalette];

                // Textboxes
                LogWindow.Background = brushCombos[0][brushPalette];
                DirectoryLogWindow.Background = brushCombos[0][brushPalette];
                FileSource.Background = brushCombos[0][brushPalette];
                FileOutput.Background = brushCombos[0][brushPalette];
                DirectorySource.Background = brushCombos[0][brushPalette];
                DirectoryOutput.Background = brushCombos[0][brushPalette];
                LogWindow.Foreground = brushCombos[0][reversePalette];
                DirectoryLogWindow.Foreground = brushCombos[0][reversePalette];
                FileSource.Foreground = brushCombos[0][reversePalette];
                FileOutput.Foreground = brushCombos[0][reversePalette];
                DirectorySource.Foreground = brushCombos[0][reversePalette];
                DirectoryOutput.Foreground = brushCombos[0][reversePalette];

                //ListBoxItems
                headerBox.Foreground = brushCombos[0][reversePalette];
                imBox.Foreground = brushCombos[0][reversePalette];
                ctBox.Foreground = brushCombos[0][reversePalette];
                tmBox.Foreground = brushCombos[0][reversePalette];
                emBox.Foreground = brushCombos[0][reversePalette];
                ubBox.Foreground = brushCombos[0][reversePalette];
                DirectoryheaderBox.Foreground = brushCombos[0][reversePalette];
                DirectoryimBox.Foreground = brushCombos[0][reversePalette];
                DirectoryctBox.Foreground = brushCombos[0][reversePalette];
                DirectorytmBox.Foreground = brushCombos[0][reversePalette];
                DirectoryemBox.Foreground = brushCombos[0][reversePalette];
                DirectoryubBox.Foreground = brushCombos[0][reversePalette];

                // Buttons
                ThemeSelector.Background = brushCombos[2][brushPalette];
                SrcFileButton.Background = brushCombos[2][brushPalette];
                DstFileButton.Background = brushCombos[2][brushPalette];
                RunButton.Background = brushCombos[2][brushPalette];
                DirectoryThemeSelector.Background = brushCombos[2][brushPalette];
                SrcDirectoryButton.Background = brushCombos[2][brushPalette];
                DstDirectoryButton.Background = brushCombos[2][brushPalette];
                DirectoryRunButton.Background = brushCombos[2][brushPalette];

                // Labels
                FileSourceLabel.Foreground = brushCombos[0][reversePalette];
                FileOutputLabel.Foreground = brushCombos[0][reversePalette];
                FileVersionNumber.Foreground = brushCombos[0][reversePalette];
                DirectorySourceLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryOutputLabel.Foreground = brushCombos[0][reversePalette];
                DirectoryVersionNumber.Foreground = brushCombos[0][reversePalette];
                HelpVersionNumber.Foreground = brushCombos[0][reversePalette];
                HelpHeader1.Foreground = brushCombos[0][reversePalette];
                HelpHeader2.Foreground = brushCombos[0][reversePalette];
                HelpHeader3.Foreground = brushCombos[0][reversePalette];
                HelpText1.Foreground = brushCombos[0][reversePalette];
                HelpText2.Foreground = brushCombos[0][reversePalette];
                HelpText3.Foreground = brushCombos[0][reversePalette];
                WarningLabel.Foreground = brushCombos[4][brushPalette];
                DirectoryWarningLabel.Foreground = brushCombos[4][brushPalette];
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RunButton.IsEnabled = false;
                TabMenu.IsEnabled = false;
                LogWindow.IsEnabled = true;
                RunButton.Content = "Scanning...";
                headerBox.Content = "Scanning " + Path.GetFileName(FileSource.Text) + "...";

                BackgroundWorker worker = new BackgroundWorker();
                worker.WorkerReportsProgress = true;
                worker.DoWork += worker_DoWork;
                worker.ProgressChanged += worker_ProgressChanged;
                worker.RunWorkerCompleted += worker_Completed;

                srcFile = FileSource.Text;
                destFile = FileOutput.Text;
                result = 0;
                idBuffer = new byte[4];
                nextByte = 255;
                lastPosition = 0U;
                saveTruncated = SaveTruncated.SelectedIndex != 0;
                timeBegin = DateTime.Now;

                string data = File.ReadAllText(srcFile);
                FileProgress.Maximum = data.Length;

                intactMessages = 0U;
                intactBytes = 0U;
                corruptTimestamps = 0U;
                truncatedMessages = 0U;
                truncatedBytes = 0U;
                emptyMessages = 0U;
                unreadBytes = 0;
                filesProcessed = 1;

                imBox.Content = "Intact Messages: 0";
                ctBox.Content = "Corrupted Timestamps: 0";
                tmBox.Content = "Truncated Messages: 0";
                emBox.Content = "Empty Messages: 0";
                ubBox.Content = "Unread Bytes: 0";

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

                DirectoryRunButton.IsEnabled = false;
                TabMenu.IsEnabled = false;
                DirectoryLogWindow.IsEnabled = true;
                DirectoryRunButton.Content = "Scanning...";
                DirectoryheaderBox.Content = "Batch processing " + filesProcessed + " files...";
                timeBegin = DateTime.Now;

                intactMessages = 0U;
                intactBytes = 0U;
                corruptTimestamps = 0U;
                truncatedMessages = 0U;
                truncatedBytes = 0U;
                emptyMessages = 0U;
                unreadBytes = 0;
                bytesRead = 0;

                DirectoryimBox.Content = "Intact Messages: 0";
                DirectoryctBox.Content = "Corrupted Timestamps: 0";
                DirectorytmBox.Content = "Truncated Messages: 0";
                DirectoryemBox.Content = "Empty Messages: 0";
                DirectoryubBox.Content = "Unread Bytes: 0";

                destDir = DirectoryOutput.Text;
                saveTruncated = DirectorySaveTruncated.SelectedIndex != 0;

                long totalSize = 0;
                foreach (string logfile in files)
                {
                    totalSize += new FileInfo(logfile).Length;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(srcFile) + ".txt");
                    if (File.Exists(destFile))
                        File.Delete(destFile);
                }
                DirectoryProgress.Maximum = totalSize;

                BackgroundWorker worker = new BackgroundWorker();
                worker.WorkerReportsProgress = true;
                worker.DoWork += worker_BatchProcess;
                worker.ProgressChanged += worker_ProgressChanged;
                worker.RunWorkerCompleted += worker_Completed;

                worker.RunWorkerAsync(files);
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        void worker_BatchProcess(object sender, DoWorkEventArgs e)
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

                        worker_DoWork(sender, e);
                        bytesRead += (uint)(new FileInfo(logfile).Length);
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        void worker_DoWork(object sender, DoWorkEventArgs e)
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

            uint lastTimestamp = 0;

            while (srcFS.Position < srcFS.Length - 1)
            {
                MessageType msId;
                string profileName;
                int discrepancy;
                uint messageLength;
                string messageData;
                uint messageFooter;
                uint timestamp;
                bool nextTimestamp = false;
                string messageOut = "";
                intact = true;

                try
                {
                    discrepancy = (int)(srcFS.Position - lastPosition); // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
                    unreadBytes += discrepancy;
                    if (discrepancy > 0)
                        File.AppendAllText(destFile, string.Format("({0:#,0} bytes missing here)\n", discrepancy));
                    result = srcFS.Read(idBuffer, 0, 4); // Read the timestamp.
                    if (result < 4)
                    {
                        intact = false;
                        emptyMessages++;
                        messageOut += "[EMPTY MESSAGE]";
                        return;
                    }
                    timestamp = BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
                    if (timestamp < 1) // If it came before Jan. 1, 1970, there's probably a problem.
                    {
                        intact = false;
                        corruptTimestamps++;
                        messageOut = "[BAD TIMESTAMP] ";
                    }
                    else if (timestamp > UNIXTimestamp()) // If it's in the future, also a problem.
                    {
                        intact = false;
                        corruptTimestamps++;
                        messageOut = "[BAD TIMESTAMP] ";
                    }
                    else if (DTFromStamp(timestamp).ToString("dd/MM/yyyy HH:mm:ss").Length == 0) // If it can't be translated to a date, also a problem.
                    {
                        intact = false;
                        corruptTimestamps++;
                        messageOut = "[BAD TIMESTAMP] ";
                    }
                    else if (timestamp < lastTimestamp) // If it isn't sequential, also a problem, because F-Chat would never save it that way.
                                                        // In this case specifically, there's an extremely high chance we're about to produce garbage data in the output.
                    {
                        intact = false;
                        corruptTimestamps++;
                        messageOut = "[BAD TIMESTAMP] ";
                    }
                    else
                    {
                        messageOut += "[" + DTFromStamp(timestamp).ToString("dd/MM/yyyy HH:mm:ss") + "] ";
                        lastTimestamp = timestamp;
                    }
                    nextByte = srcFS.ReadByte(); // Read the delimiter.
                    if (nextByte == -1)
                    {
                        intact = false;
                        emptyMessages++;
                        messageOut += "[EMPTY MESSAGE]";
                        return;
                    }
                    msId = (MessageType)(int)nextByte;
                    nextByte = srcFS.ReadByte(); // 1-byte length of profile name.
                    if (nextByte == -1)
                    {
                        intact = false;
                        emptyMessages++;
                        messageOut += "[EMPTY MESSAGE]";
                        return;
                    }
                    streamBuffer = new byte[nextByte];
                    result = srcFS.Read(streamBuffer, 0, nextByte); // Read the profile name.
                    if (result < nextByte)
                    {
                        intact = false;
                        emptyMessages++;
                        messageOut += "[EMPTY MESSAGE]";
                    }
                    profileName = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                    messageOut += profileName;
                    switch (msId)
                    {
                        case MessageType.EOF:
                            return;
                            break;
                        case MessageType.Regular:
                            messageOut += ": ";
                            break;
                        case MessageType.Me:
                            messageOut += " ";
                            break;
                        case MessageType.BottleSpin:
                            messageOut += " ";
                            break;
                        case MessageType.DiceRoll:
                            messageOut += " ";
                            break;
                        case MessageType.Warning:
                            messageOut += " (warning): ";
                            break;
                    }
                    result = srcFS.Read(idBuffer, 0, 2);
                    if (result < 2)
                    {
                        intact = false;
                        emptyMessages++;
                        messageOut += "[EMPTY MESSAGE]";
                    }
                    idBuffer[2] = 0;
                    idBuffer[3] = 0;
                    messageLength = BEInt(idBuffer);
                    streamBuffer = new byte[messageLength];
                    result = srcFS.Read(streamBuffer, 0, (int)(messageLength));
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
                    messageData = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                    if (intact)
                    {
                        messageOut += messageData;
                        intactMessages++;
                        intactBytes += (uint)messageOut.Length;
                        File.AppendAllText(destFile, messageOut + "\n");
                    }
                    else if (saveTruncated)
                    {
                        messageOut += messageData;
                        File.AppendAllText(destFile, messageOut + "\n");
                    }
                    lastPosition = (uint)(srcFS.Position);
                    while (!nextTimestamp)
                    {
                        srcFS.ReadByte();
                        srcFS.Read(idBuffer, 0, 4);
                        nextByte = srcFS.ReadByte();
                        if (nextByte == -1)
                            return;
                        srcFS.Seek(-6, SeekOrigin.Current);
                        if (nextByte < 5)
                        {
                            discrepancy = (int)(srcFS.Position - lastPosition);
                            unreadBytes += discrepancy;
                            if (discrepancy > 0)
                                File.AppendAllText(destFile, string.Format("({0:#,0} bytes missing here)\n", discrepancy));
                            lastPosition = (uint)(srcFS.Position);
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
                                return;
                            srcFS.Seek(-7, SeekOrigin.Current);
                            srcFS.ReadByte();
                            srcFS.ReadByte();
                            if (nextByte < 5)
                            {
                                discrepancy = (int)(srcFS.Position - lastPosition) - 2;
                                unreadBytes += discrepancy;
                                if (discrepancy > 0)
                                    File.AppendAllText(destFile, string.Format("({0:#,0} bytes missing here)\n", discrepancy));
                                lastPosition = (uint)(srcFS.Position);
                                nextTimestamp = true;
                            }
                        }
                    }
                    (sender as BackgroundWorker).ReportProgress((int)(bytesRead + srcFS.Position));
                }
                catch (Exception ex)
                {
                    LogException(ex);
                    return;
                }
            }
            srcFS.Close();
        }

        void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            try
            {
                FileProgress.Value = e.ProgressPercentage;
                DirectoryProgress.Value = e.ProgressPercentage;

                imBox.Content = string.Format("Intact Messages: {0:#,0} ({1:#,0.0 kB})", intactMessages, (double)(intactBytes) / 1000.0);
                ctBox.Content = string.Format("Corrupted Timestamps: {0:#,0}", corruptTimestamps);
                tmBox.Content = string.Format("Truncated Messages: {0:#,0} ({1:#,0.0 kB})", truncatedMessages, (double)(truncatedBytes) / 1000.0);
                emBox.Content = string.Format("Empty Messages: {0:#,0}", emptyMessages);
                ubBox.Content = string.Format("Unread Bytes: {0:#,0} ({1:#,0.0 kB})", unreadBytes, (double)(unreadBytes) / 1000.0);
                DirectoryimBox.Content = imBox.Content;
                DirectoryctBox.Content = ctBox.Content;
                DirectorytmBox.Content = tmBox.Content;
                DirectoryemBox.Content = emBox.Content;
                DirectoryubBox.Content = ubBox.Content;
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        void worker_Completed(object sender, EventArgs e)
        {
            try
            {
                FileProgress.Value = FileProgress.Maximum;
                DirectoryProgress.Value = DirectoryProgress.Maximum;

                imBox.Content = string.Format("Intact Messages: {0:#,0} ({1:#,0.0 kB})", intactMessages, (double)(intactBytes) / 1000.0);
                ctBox.Content = string.Format("Corrupted Timestamps: {0:#,0}", corruptTimestamps);
                tmBox.Content = string.Format("Truncated Messages: {0:#,0} ({1:#,0.0 kB})", truncatedMessages, (double)(truncatedBytes) / 1000.0);
                emBox.Content = string.Format("Empty Messages: {0:#,0}", emptyMessages);
                ubBox.Content = string.Format("Unread Bytes: {0:#,0} ({1:#,0.0 kB})", unreadBytes, (double)(unreadBytes) / 1000.0);
                DirectoryimBox.Content = imBox.Content;
                DirectoryctBox.Content = ctBox.Content;
                DirectorytmBox.Content = tmBox.Content;
                DirectoryemBox.Content = emBox.Content;
                DirectoryubBox.Content = ubBox.Content;

                RunButton.Content = "Run";
                RunButton.IsEnabled = true;
                DirectoryRunButton.Content = "Run";
                DirectoryRunButton.IsEnabled = true;
                TabMenu.IsEnabled = true;

                double timeTaken = DateTime.Now.Subtract(timeBegin).TotalSeconds;
                if (filesProcessed == 1)
                {
                    headerBox.Content = string.Format("Processed {0} in {1:#,0.0} seconds.", Path.GetFileName(srcFile), timeTaken);
                    DirectoryheaderBox.Content = string.Format("Processed {0} in {1:#,0.0} seconds.", Path.GetFileName(srcFile), timeTaken);
                }
                else
                {
                    headerBox.Content = string.Format("Processed {0} files in {1:#,0.0} seconds.", filesProcessed, timeTaken);
                    DirectoryheaderBox.Content = string.Format("Processed {0} files in {1:#,0.0} seconds.", filesProcessed, timeTaken);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                return;
            }
        }

        private void TextboxUpdated(object sender, EventArgs e)
        {
            try
            {
                bool fileReadyToRun = true;
                bool directoryReadyToRun = true;
                WarningLabel.Content = "";
                DirectoryWarningLabel.Content = "";

                if (FileSource.Text.Length == 0)
                {
                    WarningLabel.Content = "No source log file selected.";
                    fileReadyToRun = false;
                }
                else if (File.Exists(FileSource.Text) == false)
                {
                    WarningLabel.Content = "Source log file does not exist.";
                    fileReadyToRun = false;
                }
                else if (FileOutput.Text.Length == 0)
                {
                    WarningLabel.Content = "No destination file selected.";
                    fileReadyToRun = false;
                }
                else if (Directory.Exists(Path.GetDirectoryName(FileOutput.Text)) == false)
                {
                    WarningLabel.Content = "Destination directory does not exist.";
                    fileReadyToRun = false;
                }

                if (DirectorySource.Text.Length == 0)
                {
                    DirectoryWarningLabel.Content = "No source log files selected.";
                    directoryReadyToRun = false;
                }
                else if (DirectoryOutput.Text.Length == 0)
                {
                    DirectoryWarningLabel.Content = "No destination directory selected.";
                    directoryReadyToRun = false;
                }
                else if (Directory.Exists(DirectoryOutput.Text) == false)
                {
                    DirectoryWarningLabel.Content = "Destination directory does not exist.";
                    directoryReadyToRun = false;
                }

                if (fileReadyToRun)
                    RunButton.IsEnabled = true;
                else
                    RunButton.IsEnabled = false;

                if (directoryReadyToRun)
                    DirectoryRunButton.IsEnabled = true;
                else
                    DirectoryRunButton.IsEnabled = false;
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
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
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
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.CheckFileExists = false;
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
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Multiselect = true;
                if (openFileDialog.ShowDialog() == true)
                {
                    DirectorySource.Text = "";
                    foreach (string logfile in openFileDialog.FileNames)
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
                FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
                folderBrowserDialog.ShowNewFolderButton = true;
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
    }
}
