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

		private static int activeMenu = 0;
		private readonly static SolidColorBrush[][] brushCombos =
		[	// 0 = Dark mode, 1 = Light mode.
			[Brushes.Black, Brushes.White], // Textboxes
			[Brushes.LightBlue, Brushes.Beige], // Buttons
			[new(new() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 }), Brushes.LightGray], // Borders
			[Brushes.Pink, Brushes.Red], // Error messages (and the ADL warning)
			[Brushes.Yellow, Brushes.DarkRed], // Warning messages
			[new(new() { A = 0xFF, R = 0x4C, G = 0x4C, B = 0x4C }), Brushes.DarkGray], // TabControl
			[Brushes.Transparent, new(new() { A = 0xFF, R = 0x33, G = 0x33, B = 0x33 })], // DatePicker borders
			[Brushes.DimGray, Brushes.Beige], // PanelGrids
			[Brushes.LightBlue, Brushes.DarkBlue], // Hyperlinks
			[Brushes.DarkGray, Brushes.Gray], // Version labels
		];
		private static (int, int) brushPalette = (1, 0);
		private static FLogS_ERROR[] localError = [FLogS_ERROR.NO_SOURCE, FLogS_ERROR.NO_SOURCES, FLogS_ERROR.NO_SOURCES];
		private static FLogS_WARNING[] localWarning = [FLogS_WARNING.None, FLogS_WARNING.None, FLogS_WARNING.None];
		private static int filesProcessed;
		private static MessagePool pool = new();
		private readonly static ContextSettings settings = new();

		public MainWindow()
		{
			InitializeComponent();
			DataContext = settings;
		}

		private void ActiveMenuChanged(object? sender, RoutedEventArgs e)
		{
			activeMenu = Math.Min(((TabControl?)sender)?.SelectedIndex ?? 0, 2);
		}

		private static void ChangeStyle(DependencyObject? sender)
		{
			if (sender is null)
				return;

			string tag = (string)sender.GetValue(TagProperty) ?? string.Empty;

			switch (sender?.DependencyObjectType.Name)
			{
				case "Button":
					sender.SetValue(BackgroundProperty, brushCombos[1][brushPalette.Item1]);
					break;
				case "CheckBox":
					sender.SetValue(BackgroundProperty, brushCombos[1][brushPalette.Item1]);
					break;
				case "DatePicker":
					sender.SetValue(BackgroundProperty, brushCombos[0][brushPalette.Item1]);
					sender.SetValue(BorderBrushProperty, brushCombos[6][brushPalette.Item1]);
					sender.SetValue(ForegroundProperty, brushCombos[0][brushPalette.Item2]);
					break;
				case "Grid":
					sender.SetValue(BackgroundProperty, tag switch
					{
						"PanelGrid" => brushCombos[7][brushPalette.Item1],
						"MainGrid" => brushCombos[5][brushPalette.Item1],
						_ => Brushes.Transparent,
					});
					break;
				case "Label":
				case "ListBoxItem":
				case "Span":
				case "TextBlock":
					sender.SetValue(ForegroundProperty, tag switch
					{
						"VersionLabel" => brushCombos[9][brushPalette.Item1],
						"WarningLabel" => brushCombos[settings.CanRun ? 4 : 3][brushPalette.Item1],
						_ => brushCombos[0][brushPalette.Item2],
					});
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
				// IDX files contain metadata relating to the corresponding (usually extension-less) log files. They will never contain actual messages, so we exclude them unconditionally.
				return string.Join(";", dialog.FileNames.Where(file => !file.Contains(".idx")));
			return string.Empty;
		}

		private static string DialogFolderSelect()
		{
			OpenFolderDialog dialog = new()
			{
				DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};

			if (dialog.ShowDialog() is true)
				return dialog.FolderName;

			return string.Empty;
		}

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
			ProcessErrors();

			RegExLink.Foreground = brushCombos[RegExLink.IsMouseOver ? 3 : 8][brushPalette.Item1];
			P_SearchLabel.Content = settings.Regex is true ? "Target Pattern" : "Target Word or Phrase";
		}

		private void MultiDest_Click(object sender, RoutedEventArgs e)
		{
			D_Output.Text = P_Output.Text = DialogFolderSelect();
		}

		private void MultiSource_Click(object sender, RoutedEventArgs e)
		{
			D_Source.Text = P_Source.Text = DialogFileSelect();
		}

		private void ProcessErrors()
		{
			var dirSources = D_Source.Text.Equals(string.Empty) ? [] : D_Source.Text.Split(';');
			var phraseSources = P_Source.Text.Equals(string.Empty) ? [] : P_Source.Text.Split(';');

			static string outputPath(string directory, string file) => Path.Join(directory, Path.GetFileNameWithoutExtension(file)) + (settings.SaveHTML ?? false ? ".html" : ".txt");

			localError = [
				new[] {
					(F_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCE),
					(!File.Exists(F_Source.Text), FLogS_ERROR.SOURCE_NOT_FOUND),
					(F_Output.Text.Length == 0, FLogS_ERROR.NO_DEST),
					(Directory.Exists(F_Output.Text), FLogS_ERROR.DEST_NOT_FILE),
					(!Directory.Exists(Path.GetDirectoryName(F_Output.Text)), FLogS_ERROR.DEST_NOT_FOUND),
					(F_Source.Text.Equals(F_Output.Text), FLogS_ERROR.SOURCE_EQUALS_DEST),
					(Common.LogTest(F_Output.Text), FLogS_ERROR.DEST_SENSITIVE),
					(true, FLogS_ERROR.None)
				}.First(condition => condition.Item1).Item2,

				new[] {
					(D_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
					(dirSources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
					(D_Output.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
					(File.Exists(D_Output.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
					(!Directory.Exists(D_Output.Text), FLogS_ERROR.DEST_NOT_FOUND),
					(dirSources.Any(file => file.Equals(outputPath(D_Output.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
					(dirSources.Any(file => Common.LogTest(outputPath(D_Output.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
					(true, FLogS_ERROR.None)
				}.First(condition => condition.Item1).Item2,

				new[] {
					(P_Source.Text.Length == 0, FLogS_ERROR.NO_SOURCES),
					(phraseSources.Any(file => !File.Exists(file)), FLogS_ERROR.SOURCES_NOT_FOUND),
					(P_Output.Text.Length == 0, FLogS_ERROR.NO_DEST_DIR),
					(File.Exists(P_Output.Text), FLogS_ERROR.DEST_NOT_DIRECTORY),
					(!Directory.Exists(P_Output.Text), FLogS_ERROR.DEST_NOT_FOUND),
					(phraseSources.Any(file => file.Equals(outputPath(P_Output.Text, file))), FLogS_ERROR.SOURCE_CONFLICT),
					(phraseSources.Any(file => Common.LogTest(outputPath(P_Output.Text, file))), FLogS_ERROR.DEST_SENSITIVE),
					(P_Search.Text.Length == 0, FLogS_ERROR.NO_REGEX),
					(settings.Regex is true && !Common.IsValidPattern(P_Search.Text), FLogS_ERROR.BAD_REGEX),
					(true, FLogS_ERROR.None)
				}.First(condition => condition.Item1).Item2
			];

			localWarning = [
				new[] {
					(File.Exists(F_Output.Text), FLogS_WARNING.SINGLE_OVERWRITE),
					(true, FLogS_WARNING.None)
				}.First(condition => condition.Item1).Item2,

				new[] {
					(dirSources.Any(file => File.Exists(outputPath(D_Output.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
					(true, FLogS_WARNING.None)
				}.First(condition => condition.Item1).Item2,

				new[] {
					(phraseSources.Any(file => File.Exists(outputPath(P_Output.Text, file))), FLogS_WARNING.MULTI_OVERWRITE),
					(true, FLogS_WARNING.None)
				}.First(condition => condition.Item1).Item2
			];

			settings.CanRun = localError[activeMenu] == FLogS_ERROR.None;
			settings.WarningText = Common.GetErrorMessage(localError[activeMenu], localWarning[activeMenu]);
		}

		private void ProcessFiles(string[] args)
		{
			settings.Exception = string.Empty;
			filesProcessed = args.Length;
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
			ProcessErrors();

			var senderButton = (Button?)sender;
			if (senderButton?.IsEnabled is false)
				return;

			string bTag = (string)(senderButton?.Tag ?? string.Empty);
			T GridObject<T>(string value) where T : DependencyObject => (T)MainGrid.FindName(bTag + value);

			try
			{
				string[] files = GridObject<TextBox>("Source").Text.Split(';');
				Common.plaintext = settings.SaveHTML == false || !GridObject<TextBox>("Output").Text.EndsWith(".html");
				pool = new()
				{
					destDir = GridObject<TextBox>("Output").Text,
					divide = settings.DivideLogs is true,
					dtAfter = GridObject<DatePicker>("AfterDate").SelectedDate ?? Common.DTFromStamp(1),
					dtBefore = GridObject<DatePicker>("BeforeDate").SelectedDate ?? DateTime.UtcNow,
					phrase = bTag.Equals("P_") ? P_Search.Text : string.Empty,
					regex = settings.Regex is true,
					saveTruncated = settings.SaveTruncated is true,
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
				ProcessErrors();
			}
			catch (Exception ex)
			{
				Common.LogException(ex);
			}
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
