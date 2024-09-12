using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FLogS
{
	public class ContextSettings : INotifyPropertyChanged
	{
		private DateTime? _afterDate;
		private DateTime? _beforeDate;
		private bool _canRun = false;
		private string _corruptedTimestamps = "Corrupted Timestamps:";
		private bool? _divideLogs = false;
		private string _emptyMessages = "Empty Messages:";
		private string _exception = "";
		private string _intactMessages = "Intact Messages:";
		private string _logHeader = "Ready to receive data.";
		private double _progress = 0.0;
		private double _progressMax = 100.0;
		private bool? _regex = false;
		private string _runLabel = "Run";
		private bool? _saveHTML = false;
		private bool? _saveTruncated = false;
		private string _themeLabel = "Dark";
		private string _truncatedMessages = "Truncated Messages:";
		private string _unreadData = "Unread Data:";
		private readonly string _versionString = "FLogS — Version " + Assembly.GetExecutingAssembly().GetName().Version + " © Taica, " + GetBuildYear(Assembly.GetExecutingAssembly());
		private string _warningText = Common.GetErrorMessage(FLogS_ERROR.NO_SOURCE, FLogS_WARNING.None);

		public DateTime? AfterDate { get => _afterDate; set { _afterDate = value; OnPropertyChanged(); } }
		public DateTime? BeforeDate { get => _beforeDate; set { _beforeDate = value; OnPropertyChanged(); } }
		public bool CanRun { get => _canRun; set { _canRun = value; OnPropertyChanged(); } }
		public string CorruptedTimestamps { get => _corruptedTimestamps; set { _corruptedTimestamps = value; OnPropertyChanged(); } }
		public bool? DivideLogs { get => _divideLogs; set { _divideLogs = value; OnPropertyChanged(); } }
		public string EmptyMessages { get => _emptyMessages; set { _emptyMessages = value; OnPropertyChanged(); } }
		public string Exception { get => _exception; set { _exception = value; OnPropertyChanged(); } }
		public string IntactMessages { get => _intactMessages; set { _intactMessages = value; OnPropertyChanged(); } }
		public string LogHeader { get => _logHeader; set { _logHeader = value; OnPropertyChanged(); } }
		public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
		public double ProgressMax { get => _progressMax; set { _progressMax = value; OnPropertyChanged(); } }
		public event PropertyChangedEventHandler? PropertyChanged;
		public string RunLabel { get => _runLabel; set { _runLabel = value; OnPropertyChanged(); } }
		public bool? Regex { get => _regex; set { _regex = value; OnPropertyChanged(); } }
		public bool? SaveHTML { get => _saveHTML; set { _saveHTML = value; OnPropertyChanged(); } }
		public bool? SaveTruncated { get => _saveTruncated; set { _saveTruncated = value; OnPropertyChanged(); } }
		public string ThemeLabel { get => _themeLabel; set { _themeLabel = value; OnPropertyChanged(); } }
		public string TruncatedMessages { get => _truncatedMessages; set { _truncatedMessages = value; OnPropertyChanged(); } }
		public string UnreadData { get => _unreadData; set { _unreadData = value; OnPropertyChanged(); } }
		public string VersionString => _versionString;
		public string WarningText { get => _warningText; set { _warningText = value; OnPropertyChanged(); } }

		private static int GetBuildYear(Assembly assembly)
		{
			const string prefix = "+build";
			var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

			string? value = attr?.InformationalVersion;
			int index = value?.IndexOf(prefix) ?? 0;
			if (index > 0 && DateTime.TryParseExact(value?[(index + prefix.Length)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
				return result.Year;

			return default;
		}

		protected void OnPropertyChanged([CallerMemberName] string? name = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
	}
}
