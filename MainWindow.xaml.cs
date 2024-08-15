﻿using Microsoft.Win32;
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
		[   // 0 = Dark mode, 1 = Light mode.
			[Brushes.Black, Brushes.White], // Textboxes
			[Brushes.LightBlue, Brushes.Beige], // Buttons
			[new(new() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray], // Borders
			[Brushes.Pink, Brushes.Red], // Error messages (and the ADL warning)
			[Brushes.Yellow, Brushes.DarkRed], // Warning messages
			[new(new() { A = 0xFF, R = 0x4C, G = 0x4C, B = 0x4C }), Brushes.DarkGray], // TabControl
			[Brushes.Transparent, new(new() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 })], // DatePicker borders
			[Brushes.DimGray, Brushes.Beige], // PanelGrids
			[Brushes.LightBlue, Brushes.DarkBlue], // Hyperlinks
		];
		private static int brushPalette = 1;
		private static FLogS_ERROR directoryError = FLogS_ERROR.NO_SOURCES;
		private static FLogS_WARNING directoryWarning = FLogS_WARNING.None;
		private static FLogS_ERROR fileError = FLogS_ERROR.NO_SOURCE;
		private static FLogS_WARNING fileWarning = FLogS_WARNING.None;
		private static int filesProcessed;
		private static bool overrideFormat = false;
		private static FLogS_ERROR phraseError = FLogS_ERROR.NO_SOURCES;
		private static FLogS_WARNING phraseWarning = FLogS_WARNING.None;
		private static MessagePool? pool;
		private static int reversePalette = 0;
		private readonly static ContextSettings settings = new();

		private enum FLogS_ERROR
		{
			None,
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
			None,
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
					sender.SetValue(BackgroundProperty, brushCombos[1][brushPalette]);
					break;
				case "DatePicker":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette]);
					sender.SetValue(BorderBrushProperty, brushCombos[6][brushPalette]);
					sender.SetValue(ForegroundProperty, brushCombos[0][reversePalette]);
					break;
				case "Grid":
					if ((sender.GetValue(TagProperty) ?? "").Equals("PanelGrid"))
						sender.SetValue(BackgroundProperty, brushCombos[7][brushPalette]);
					break;
				case "Label":
					sender.SetValue(ForegroundProperty, brushCombos[0][reversePalette]);
					break;
				case "ListBox":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette]);
					sender.SetValue(BorderBrushProperty, brushCombos[2][reversePalette]);
					sender.SetValue(ForegroundProperty, brushCombos[0][reversePalette]);
					break;
				case "ProgressBar":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette]);
					break;
				case "StackPanel":
					sender.SetValue(BackgroundProperty, brushCombos[2][brushPalette]);
					break;
				case "TabControl":
					sender.SetValue(BackgroundProperty, brushCombos[5][brushPalette]);
					break;
				case "TextBlock":
					sender.SetValue(ForegroundProperty, brushCombos[0][reversePalette]);
					break;
				case "TextBox":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette]);
					sender.SetValue(BorderBrushProperty, brushCombos[2][reversePalette]);
					sender.SetValue(ForegroundProperty, brushCombos[0][reversePalette]);
					break;
			}

			foreach (object dp in LogicalTreeHelper.GetChildren(sender))
				ChangeStyle(dp as DependencyObject);

			return;
		}

		private void DatePicker_Update(object? sender, RoutedEventArgs e)
		{
			var senderDate = (DatePicker?)sender;

			if (senderDate.Name.Contains("BeforeDate"))
			{
				BeforeDate.SelectedDate = DirectoryBeforeDate.SelectedDate = PhraseBeforeDate.SelectedDate = senderDate.SelectedDate;
				return;
			}

			AfterDate.SelectedDate = DirectoryAfterDate.SelectedDate = PhraseAfterDate.SelectedDate = senderDate.SelectedDate;
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
				InitialDirectory = outputSelect ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
					: Path.Exists(Common.defaultLogDir) ? Common.defaultLogDir
					: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
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
					Common.fileListing[logfile] ??= new(logfile);
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

			var senderBox = (CheckBox)sender;

			if (senderBox.Name.Contains("DivideLogs"))
			{
				DivideLogsCheckbox.IsChecked = DirectoryDivideLogsCheckbox.IsChecked = PhraseDivideLogsCheckbox.IsChecked = senderBox.IsChecked;
				return;
			}

			if (senderBox.Name.Contains("SaveTruncated"))
			{
				SaveTruncatedCheckbox.IsChecked = DirectorySaveTruncatedCheckbox.IsChecked = PhraseSaveTruncatedCheckbox.IsChecked = senderBox.IsChecked;
				return;
			}

			if (senderBox.Name.Contains("SaveHTML"))
			{
				SaveHTMLCheckbox.IsChecked = DirectorySaveHTMLCheckbox.IsChecked = PhraseSaveHTMLCheckbox.IsChecked = senderBox.IsChecked;
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

			(FLogS_ERROR.None, FLogS_WARNING.MULTI_OVERWRITE) => "One or more files will be overwritten.",
			(FLogS_ERROR.None, FLogS_WARNING.SINGLE_OVERWRITE) => "Destination file will be overwritten.",
			(FLogS_ERROR.None, _) => "",

			(_, FLogS_WARNING.None) => "An unknown error has occurred.",
			(_, _) => "An unknown error has occurred.",
		};

		private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
			e.Handled = true;
		}

		private void MainGrid_Loaded(object? sender, RoutedEventArgs e)
		{
			Common.fileListing = [];
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

			RegExLink.Foreground = brushCombos[RegExLink.IsMouseOver ? 3 : 8][brushPalette];

			if (!overrideFormat)
			{
				DirectorySaveHTMLCheckbox.IsChecked = DirectoryOutput.Text.EndsWith(".html");
				PhraseSaveHTMLCheckbox.IsChecked = PhraseOutput.Text.EndsWith(".html");
				SaveHTMLCheckbox.IsChecked = FileOutput.Text.EndsWith(".html");
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

		private void ProcessErrors()
		{
			var directorySources = DirectorySource.Text.Equals(string.Empty) ? [] : DirectorySource.Text.Split(';');
			var phraseSources = PhraseSource.Text.Equals(string.Empty) ? [] : PhraseSource.Text.Split(';');
			static string outputPath(string directory, string file) => Path.Join(directory, Path.GetFileNameWithoutExtension(file)) + (Common.plaintext ? ".txt" : ".html");

			directoryError = new[] {
				(DirectorySource.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
				(directorySources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
				(DirectoryOutput.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
				(File.Exists(DirectoryOutput.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
				(!Directory.Exists(DirectoryOutput.Text), FLogS_ERROR.DEST_NOT_FOUND),
				(directorySources.Any(file => file.Equals(outputPath(DirectoryOutput.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
				(directorySources.Any(file => Common.LogTest(outputPath(DirectoryOutput.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			fileError = new[] {
				(FileSource.Text.Length == 0, FLogS_ERROR.NO_SOURCE),
				(!File.Exists(FileSource.Text), FLogS_ERROR.SOURCE_NOT_FOUND),
				(FileOutput.Text.Length == 0, FLogS_ERROR.NO_DEST),
				(Directory.Exists(FileOutput.Text), FLogS_ERROR.DEST_NOT_FILE),
				(!Directory.Exists(Path.GetDirectoryName(FileOutput.Text)), FLogS_ERROR.DEST_NOT_FOUND),
				(FileSource.Text.Equals(FileOutput.Text), FLogS_ERROR.SOURCE_EQUALS_DEST),
				(Common.LogTest(FileOutput.Text), FLogS_ERROR.DEST_SENSITIVE),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			phraseError = new[] {
				(PhraseSource.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
				(phraseSources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
				(PhraseOutput.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
				(File.Exists(PhraseOutput.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
				(!Directory.Exists(PhraseOutput.Text), FLogS_ERROR.DEST_NOT_FOUND),
				(phraseSources.Any(file => file.Equals(outputPath(PhraseOutput.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
				(phraseSources.Any(file => Common.LogTest(outputPath(PhraseOutput.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
				(PhraseSearch.Text.Length == 0, FLogS_ERROR.NO_REGEX),
				(RegexCheckBox?.IsChecked == true && !Common.IsValidPattern(PhraseSearch.Text), FLogS_ERROR.BAD_REGEX),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			directoryWarning = new[]
			{
				(directorySources.Any(file => File.Exists(outputPath(DirectoryOutput.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			fileWarning = new[]
			{
				(File.Exists(FileOutput.Text), FLogS_WARNING.SINGLE_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			phraseWarning = new[]
			{
				(phraseSources.Any(file => File.Exists(outputPath(PhraseOutput.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			DirectoryWarningLabel.Content = GetErrorMessage(directoryError, directoryWarning);
			PhraseWarningLabel.Content = GetErrorMessage(phraseError, phraseWarning);
			WarningLabel.Content = GetErrorMessage(fileError, fileWarning);
			DirectoryRunButton.IsEnabled = directoryError == FLogS_ERROR.None;
			PhraseRunButton.IsEnabled = phraseError == FLogS_ERROR.None;
			RunButton.IsEnabled = fileError == FLogS_ERROR.None;

			DirectoryWarningLabel.Foreground = brushCombos[directoryError == FLogS_ERROR.None ? 4 : 3][brushPalette];
			PhraseWarningLabel.Foreground = brushCombos[phraseError == FLogS_ERROR.None ? 4 : 3][brushPalette];
			WarningLabel.Foreground = brushCombos[fileError == FLogS_ERROR.None ? 4 : 3][brushPalette];

			return;
		}

		private void ProcessFiles(string[]? args = null, bool batch = true)
		{
			PhraseEXBox.Content = DirectoryEXBox.Content = EXBox.Content = string.Empty;
			filesProcessed = args?.Length ?? 1;
			overrideFormat = false;
			Common.processing = true;

			pool?.totalSize.Simplify();
			pool?.totalSize.Magnitude(1);
			DirectoryProgress.Maximum = FileProgress.Maximum = PhraseProgress.Maximum = pool?.totalSize.bytes ?? 100.0;

			pool?.ResetStats();
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
				DirectoryWarningLabel.Foreground = brushCombos[directoryError == FLogS_ERROR.None ? 4 : 3][brushPalette];
				MainGrid.Background = brushCombos[5][brushPalette];
				PhraseWarningLabel.Foreground = brushCombos[phraseError == FLogS_ERROR.None ? 4 : 3][brushPalette];
				RegexCheckBox.Background = brushCombos[1][brushPalette];
				WarningLabel.Foreground = brushCombos[fileError == FLogS_ERROR.None ? 4 : 3][brushPalette];
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

			pool.regex = (RegexCheckBox?.IsVisible ?? false) && (RegexCheckBox?.IsChecked ?? false);
			PhraseSearchLabel.Content = pool.regex ? "Target Pattern" : "Target Word or Phrase";

			ProcessErrors();
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
				HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = "Scanning " + (filesProcessed == 1 ? $"{Path.GetFileName(pool?.srcFile)}..." : $"{filesProcessed:N0} files...");
				DirectoryRunButton.Content = PhraseRunButton.Content = RunButton.Content = "Scanning...";

				Common.lastException = string.Empty;
				Common.timeBegin = DateTime.Now;

				return;
			}

			DirectoryRunButton.Content = PhraseRunButton.Content = RunButton.Content = "Run";

			if (Common.lastException.Equals(string.Empty))
			{
				double timeTaken = DateTime.Now.Subtract(Common.timeBegin).TotalSeconds;
				string? formattedName = Path.GetFileName(pool?.srcFile);
				if (formattedName?.Length > 16)
					formattedName = formattedName[..14] + "...";

				HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = "Processed " + (filesProcessed == 1 ? $"{formattedName} in {timeTaken:N2} seconds." : $"{filesProcessed:N0} files in {timeTaken:N2} seconds.");
			}

			return;
		}

		private void UpdateLogs(object? sender = null)
		{
			PhraseIMBox.Content = DirectoryIMBox.Content = IMBox.Content = $"Intact Messages: {pool?.intactMessages:N0} ({pool?.intactBytes:S})";
			PhraseCTBox.Content = DirectoryCTBox.Content = CTBox.Content = $"Corrupted Timestamps: {pool?.corruptTimestamps:N0}";
			PhraseTMBox.Content = DirectoryTMBox.Content = TMBox.Content = $"Truncated Messages: {pool?.truncatedMessages:N0} ({pool?.truncatedBytes:S})";
			PhraseEMBox.Content = DirectoryEMBox.Content = EMBox.Content = $"Empty Messages: {pool?.emptyMessages:N0}";
			PhraseUBBox.Content = DirectoryUBBox.Content = UBBox.Content = $"Unread Data: {pool?.unreadBytes:S}";

			if (!Common.lastException.Equals(string.Empty))
			{
				HeaderBox.Content = DirectoryHeaderBox.Content = PhraseHeaderBox.Content = "A critical error has occurred.";
				PhraseEXBox.Content = DirectoryEXBox.Content = EXBox.Content = Common.lastException;
				(sender as BackgroundWorker)?.CancelAsync();
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
