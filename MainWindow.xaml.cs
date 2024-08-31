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
		private static (int, int) brushPalette = (1, 0);
		private static FLogS_ERROR directoryError = FLogS_ERROR.NO_SOURCES;
		private static FLogS_WARNING directoryWarning = FLogS_WARNING.None;
		private static FLogS_ERROR fileError = FLogS_ERROR.NO_SOURCE;
		private static FLogS_WARNING fileWarning = FLogS_WARNING.None;
		private static int filesProcessed;
		private static bool overrideFormat = false;
		private static FLogS_ERROR phraseError = FLogS_ERROR.NO_SOURCES;
		private static FLogS_WARNING phraseWarning = FLogS_WARNING.None;
		private static MessagePool pool = new();
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
			DataContext = settings;
		}

		private static void ChangeStyle(DependencyObject? sender)
		{
			if (sender is null)
				return;

			switch (sender?.DependencyObjectType.Name)
			{
				case "Button":
					sender.SetValue(BackgroundProperty, brushCombos[1][brushPalette.Item1]);
					break;
				case "DatePicker":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette.Item1]);
					sender.SetValue(BorderBrushProperty, brushCombos[6][brushPalette.Item1]);
					sender.SetValue(ForegroundProperty, brushCombos[0][brushPalette.Item2]);
					break;
				case "Grid":
					if ((sender.GetValue(TagProperty) ?? string.Empty).Equals("PanelGrid"))
						sender.SetValue(BackgroundProperty, brushCombos[7][brushPalette.Item1]);
					break;
				case "Label":
				case "TextBlock":
					sender.SetValue(ForegroundProperty, brushCombos[0][brushPalette.Item2]);
					break;
				case "ListBox":
				case "TextBox":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette.Item1]);
					sender.SetValue(BorderBrushProperty, brushCombos[2][brushPalette.Item2]);
					sender.SetValue(ForegroundProperty, brushCombos[0][brushPalette.Item2]);
					break;
				case "ProgressBar":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette.Item1]);
					break;
				case "StackPanel":
					sender.SetValue(BackgroundProperty, brushCombos[2][brushPalette.Item1]);
					break;
				case "TabControl":
					sender.SetValue(BackgroundProperty, brushCombos[5][brushPalette.Item1]);
					break;
			}

			foreach (object dp in LogicalTreeHelper.GetChildren(sender))
				ChangeStyle(dp as DependencyObject);
		}

		private static string DialogFileSelect(bool outputSelect = false, bool checkExists = false, bool multi = true)
		{
			OpenFileDialog dialog = new()
			{
				CheckFileExists = checkExists,
				Multiselect = multi,
				InitialDirectory = outputSelect ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
				: Path.Exists(Common.defaultLogDir) ? Common.defaultLogDir
				: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
			};

			if (dialog.ShowDialog() == true)
				return string.Join(";", dialog.FileNames.Where(file => !file.Contains(".idx"))); // IDX files contain metadata relating to the corresponding (usually extension-less) log files.
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

		private void FormatOverride(object? sender, RoutedEventArgs e)
		{
			if (sender is null)
				return;

			var senderBox = (CheckBox)sender;

			if (senderBox.Name.Contains("DivideLogs"))
			{
				F_DivideLogsCheckbox.IsChecked = D_DivideLogsCheckbox.IsChecked = P_DivideLogsCheckbox.IsChecked = senderBox.IsChecked;
				return;
			}

			if (senderBox.Name.Contains("SaveTruncated"))
			{
				F_SaveTruncatedCheckbox.IsChecked = D_SaveTruncatedCheckbox.IsChecked = P_SaveTruncatedCheckbox.IsChecked = senderBox.IsChecked;
				return;
			}

			if (senderBox.Name.Contains("SaveHTML"))
			{
				F_SaveHTMLCheckbox.IsChecked = D_SaveHTMLCheckbox.IsChecked = P_SaveHTMLCheckbox.IsChecked = overrideFormat = (senderBox.IsChecked ?? false);
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
			(FLogS_ERROR.None, _) => string.Empty,

			(_, _) => "An unknown error has occurred.",
		};

		private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
			e.Handled = true;
		}

		private void MainGrid_Loaded(object? sender, RoutedEventArgs e)
		{
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

			RegExLink.Foreground = brushCombos[RegExLink.IsMouseOver ? 3 : 8][brushPalette.Item1];

			if (!overrideFormat)
			{
				F_SaveHTMLCheckbox.IsChecked = F_Output.Text.EndsWith(".html");
				D_SaveHTMLCheckbox.IsChecked = D_Output.Text.EndsWith(".html");
				P_SaveHTMLCheckbox.IsChecked = P_Output.Text.EndsWith(".html");
			}
		}

		private void MultiDest_Click(object sender, RoutedEventArgs e)
		{
			D_Output.Text = P_Output.Text = DialogFolderSelect(true);
		}

		private void MultiSource_Click(object sender, RoutedEventArgs e)
		{
			D_Source.Text = P_Source.Text = DialogFileSelect();
		}

		private void ProcessErrors()
		{
			var dirSources = D_Source.Text.Equals(string.Empty) ? [] : D_Source.Text.Split(';');
			var phraseSources = P_Source.Text.Equals(string.Empty) ? [] : P_Source.Text.Split(';');

			static string outputPath(string directory, string file) => Path.Join(directory, Path.GetFileNameWithoutExtension(file)) + (Common.plaintext ? ".txt" : ".html");

			directoryError = new[] {
				(D_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
				(dirSources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
				(D_Output.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
				(File.Exists(D_Output.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
				(!Directory.Exists(D_Output.Text), FLogS_ERROR.DEST_NOT_FOUND),
				(dirSources.Any(file => file.Equals(outputPath(D_Output.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
				(dirSources.Any(file => Common.LogTest(outputPath(D_Output.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			fileError = new[] {
				(F_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCE),
				(!File.Exists(F_Source.Text), FLogS_ERROR.SOURCE_NOT_FOUND),
				(F_Output.Text.Length == 0, FLogS_ERROR.NO_DEST),
				(Directory.Exists(F_Output.Text), FLogS_ERROR.DEST_NOT_FILE),
				(!Directory.Exists(Path.GetDirectoryName(F_Output.Text)), FLogS_ERROR.DEST_NOT_FOUND),
				(F_Source.Text.Equals(F_Output.Text), FLogS_ERROR.SOURCE_EQUALS_DEST),
				(Common.LogTest(F_Output.Text), FLogS_ERROR.DEST_SENSITIVE),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			phraseError = new[] {
				(P_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
				(phraseSources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
				(P_Output.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
				(File.Exists(P_Output.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
				(!Directory.Exists(P_Output.Text), FLogS_ERROR.DEST_NOT_FOUND),
				(phraseSources.Any(file => file.Equals(outputPath(P_Output.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
				(phraseSources.Any(file => Common.LogTest(outputPath(P_Output.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
				(P_Search.Text.Length == 0, FLogS_ERROR.NO_REGEX),
				(RegexCheckBox?.IsChecked == true && !Common.IsValidPattern(P_Search.Text), FLogS_ERROR.BAD_REGEX),
				(true, FLogS_ERROR.None)
			}.First(condition => condition.Item1).Item2;

			directoryWarning = new[]
			{
				(dirSources.Any(file => File.Exists(outputPath(D_Output.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			fileWarning = new[]
			{
				(File.Exists(F_Output.Text), FLogS_WARNING.SINGLE_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			phraseWarning = new[]
			{
				(phraseSources.Any(file => File.Exists(outputPath(P_Output.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
				(true, FLogS_WARNING.None)
			}.First(condition => condition.Item1).Item2;

			F_RunButton.IsEnabled = fileError == FLogS_ERROR.None;
			D_RunButton.IsEnabled = directoryError == FLogS_ERROR.None;
			P_RunButton.IsEnabled = phraseError == FLogS_ERROR.None;

			F_WarningLabel.Content = GetErrorMessage(fileError, fileWarning);
			D_WarningLabel.Content = GetErrorMessage(directoryError, directoryWarning);
			P_WarningLabel.Content = GetErrorMessage(phraseError, phraseWarning);

			F_WarningLabel.Foreground = brushCombos[fileError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
			D_WarningLabel.Foreground = brushCombos[directoryError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
			P_WarningLabel.Foreground = brushCombos[phraseError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
		}

		private void ProcessFiles(string[] args)
		{
			settings.Exception = string.Empty;
			filesProcessed = args.Length;
			overrideFormat = false;
			Common.processing = true;

			pool.totalSize.Simplify();
			pool.totalSize.Magnitude(1);
			settings.ProgressMax = pool.totalSize.bytes;

			pool.ResetStats();
			TransitionMenus(false);
			UpdateLogs();

			BackgroundWorker worker = new()
			{
				WorkerReportsProgress = true,
				WorkerSupportsCancellation = true
			};
			worker.DoWork += pool.BatchProcess;
			worker.ProgressChanged += Worker_ProgressChanged;
			worker.RunWorkerCompleted += Worker_Completed;

			worker.RunWorkerAsync(args);
		}

		private void RunButton_Click(object? sender, RoutedEventArgs e)
		{
			TextboxUpdated(sender, e);

			var senderButton = (Button?)sender;
			if (senderButton?.IsEnabled is false)
				return;

			string bTag = (string)(senderButton?.Tag ?? string.Empty);
			T GridObject<T>(string value) where T : DependencyObject => (T)MainGrid.FindName(bTag + value);

			try
			{
				string[] files = GridObject<TextBox>("Source").Text.Split(';');
				Common.plaintext = GridObject<CheckBox>("SaveHTMLCheckbox").IsChecked == false;
				pool = new()
				{
					destDir = GridObject<TextBox>("Output").Text,
					divide = GridObject<CheckBox>("DivideLogsCheckbox").IsChecked is true,
					dtAfter = GridObject<DatePicker>("AfterDate").SelectedDate ?? Common.DTFromStamp(1),
					dtBefore = GridObject<DatePicker>("BeforeDate").SelectedDate ?? DateTime.UtcNow,
					phrase = bTag.Equals("Phrase") ? P_Search.Text : string.Empty,
					saveTruncated = GridObject<CheckBox>("SaveTruncatedCheckbox").IsChecked is true,
					srcFile = GridObject<TextBox>("Source").Text,
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
			}
		}

		private void SingleDest_Click(object sender, RoutedEventArgs e)
		{
			F_Output.Text = DialogFileSelect(outputSelect: true, multi: false);
		}

		private void SingleSource_Click(object sender, RoutedEventArgs e)
		{
			F_Source.Text = DialogFileSelect(checkExists: true, multi: false);
		}

		private void ThemeSelector_Click(object? sender, RoutedEventArgs e)
		{
			try
			{
				Common.Swap(ref brushPalette);
				settings.ThemeLabel = brushPalette.Item1 == 0 ? "Light" : "Dark";

				ChangeStyle(MainGrid);

				ADLWarning.Foreground = brushCombos[3][brushPalette.Item1];
				MainGrid.Background = brushCombos[5][brushPalette.Item1];
				RegexCheckBox.Background = brushCombos[1][brushPalette.Item1];

				F_WarningLabel.Foreground = brushCombos[fileError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
				D_WarningLabel.Foreground = brushCombos[directoryError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
				P_WarningLabel.Foreground = brushCombos[phraseError == FLogS_ERROR.None ? 4 : 3][brushPalette.Item1];
			}
			catch (Exception ex)
			{
				Common.LogException(ex);
			}
		}

		private void TextboxUpdated(object? sender, EventArgs e)
		{
			if (Common.processing)
				return;

			pool.regex = (RegexCheckBox?.IsVisible ?? false) && (RegexCheckBox?.IsChecked ?? false);
			P_SearchLabel.Content = pool.regex ? "Target Pattern" : "Target Word or Phrase";

			ProcessErrors();
		}

		private static void TransitionEnableables(DependencyObject? sender, bool enabled)
		{
			if (sender is null)
				return;

			if ((sender.GetValue(TagProperty) ?? string.Empty).Equals("Enableable") || !Common.lastException.Equals(string.Empty))
				sender.SetValue(IsEnabledProperty, enabled);

			foreach (object dp in LogicalTreeHelper.GetChildren(sender))
				TransitionEnableables(dp as DependencyObject, enabled);
		}

		private void TransitionMenus(bool enabled)
		{
			TransitionEnableables(MainGrid, enabled);

			if (!enabled)
			{
				settings.LogHeader = "Scanning " + (filesProcessed == 1 ? $"{Path.GetFileName(pool?.srcFile)}..." : $"{filesProcessed:N0} files...");
				settings.RunLabel = "Scanning...";

				Common.lastException = string.Empty;
				Common.timeBegin = DateTime.Now;

				return;
			}

			settings.RunLabel = "Run";

			if (Common.lastException.Equals(string.Empty))
			{
				string? formattedName = Path.GetFileName(pool?.srcFile);
				if (formattedName?.Length > 16)
					formattedName = formattedName[..14] + "...";

				double timeTaken = DateTime.Now.Subtract(Common.timeBegin).TotalSeconds;
				settings.LogHeader = "Processed " + (filesProcessed == 1 ? $"{formattedName} in {timeTaken:N2} seconds." : $"{filesProcessed:N0} files in {timeTaken:N2} seconds.");
			}
		}

		private static void UpdateLogs(object? sender = null)
		{
			settings.IntactMessages = $"Intact Messages: {pool?.intactMessages:N0} ({pool?.intactBytes:S})";
			settings.CorruptedTimestamps = $"Corrupted Timestamps: {pool?.corruptTimestamps:N0}";
			settings.TruncatedMessages = $"Truncated Messages: {pool?.truncatedMessages:N0} ({pool?.truncatedBytes:S})";
			settings.EmptyMessages = $"Empty Messages: {pool?.emptyMessages:N0}";
			settings.UnreadData = $"Unread Data: {pool?.unreadBytes:S}";

			if (!Common.lastException.Equals(string.Empty))
			{
				settings.LogHeader = "A critical error has occurred.";
				settings.Exception = Common.lastException;
				(sender as BackgroundWorker)?.CancelAsync();
			}
		}

		private void Worker_Completed(object? sender, EventArgs e)
		{
			try
			{
				settings.Progress = settings.ProgressMax;

				UpdateLogs(sender);
				TransitionMenus(true);
				Common.processing = false;
			}
			catch (Exception ex)
			{
				Common.LogException(ex);
			}
		}

		private void Worker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
		{
			try
			{
				settings.Progress = e.ProgressPercentage;

				UpdateLogs(sender);
			}
			catch (Exception ex)
			{
				Common.LogException(ex);
			}
		}
	}
}
