using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FLogS;

enum ErrorCode
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

enum WarningCode
{
	None,
	MULTI_OVERWRITE,
	SINGLE_OVERWRITE,
}

/// <summary>
/// Static helper functions and variables serving purely logical purposes in either the front- or backend.
/// </summary>
static class Common
{
	public readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
	public readonly static string defaultLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fchat", "data");
	private readonly static DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	public readonly static string errorFile = "FLogS_ERROR.txt";
	public static Dictionary<string, FileInfo> fileListing = [];
	public readonly static Dictionary<string, string> htmlEntities = new()
	{
		{ "&", "&amp;" },
		{ "\"", "&quot;" },
		{ "\'", "&apos;" },
		{ "<", "&lt;" },
		{ ">", "&gt;" },
		{ "¢", "&cent;" },
		{ "£", "&pound;" },
		{ "¥", "&yen;" },
		{ "€", "&euro;" },
		{ "©", "&copy;" },
		{ "®", "&reg;" },
		{ "\n", "<br />" },
	};
	public readonly static string htmlFooter = "</body>\n</html>";
	public readonly static string htmlHeader = @"
<!DOCTYPE html>
<html>
<head>
<meta charset=""UTF-8"" />
<base target=""_blank"">
<title>F-Chat Exported Logs</title>
<style>
body { padding: 10px; background-color: #191932; display: block; word-wrap: break-word; -ms-hyphens: auto; -moz-hyphens: auto; -webkit-hyphens: auto; hyphens: auto; max-width: 100%; position: relative; font-family: -apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica Neue,Arial,Noto Sans,Liberation Sans,sans-serif,Apple Color Emoji,Segoe UI Emoji,Segoe UI Symbol,Noto Color Emoji; font-size: 1rem; font-weight: 400; line-height: 1.5; color: #EDEDF5; text-align: left; }
script { display: block; }
span { position: relative; }
.pf { color: #6766AD; text-decoration: none; font-weight: bold; }
.url { color: #FFFFFF; text-decoration: underline; }
.warn { color: #909090; }
.us { background-color: #2A2A54; padding-top: 3px; padding-bottom: 3px; padding-right: 3px; margin-top: 3px; margin-bottom: 3px; margin-right: 3px; }
.ss { color: #D6D6FF; text-decoration: underline; }
.ts { color: #C0C0C0; }
.ec { width: 50px; height: 50px; vertical-align: middle; display: inline; }
.av { width: 15px; height: 15px; vertical-align: middle; display: inline; margin-left: 2px; margin-right: 2px; }
.sp { background-color: #0D0D0F; color: #0D0D0F; }
.sp * { background-color: #0D0D0F; color: #0D0D0F; }
.sp .ec { filter: brightness(0%); }
.sp .sc { opacity: 0; }
.sp:hover { color: #FFFFFF; }
.sp:hover * { color: #FFFFFF; }
.sp:hover .ts { color: #C0C0C0; }
.sp:hover .ss { color: #D6D6FF; }
.sp:hover .pf { color: #6766AD; }
.sp:hover .warn { color: #909090; }
.sp:hover .ec { filter: brightness(100%); }
.sp:hover .sc { opacity: 100%; }
</style>
</head>
<body>";
	public static string lastException = string.Empty;
	public static uint lastTimestamp;
	public static bool plaintext = true;
	public static bool processing;
	public readonly static Dictionary<string, string> tagClosings = new()
	{
		{ "b", "</b>" },
		{ "big", "</span>" },
		{ "color", "</span>" },
		{ "eicon", ".gif\" />" },
		{ "i", "</i>" },
		{ "icon", ".png\" /></a>" },
		{ "noparse", "</script>" },
		{ "s", "</s>" },
		{ "session", "</a>" },
		{ "spoiler", "</span>" },
		{ "sub", "</sub>" },
		{ "sup", "</sup>" },
		{ "u", "</u>" },
		{ "url", "</a>" },
		{ "user", "</span></a>" },
	};
	public static DateTime timeBegin;

	public static uint BEInt(byte[] buffer) => buffer[0]
			+ buffer[1] * 256U
			+ buffer[2] * 65536U
			+ buffer[3] * 16777216U;

	public static DateTime DTFromStamp(uint stamp)
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

	public static string GetErrorMessage(ErrorCode eCode, WarningCode wCode) => (eCode, wCode) switch
	{
		(ErrorCode.BAD_REGEX, _) => "Search text contains an invalid RegEx pattern.",
		(ErrorCode.DEST_NOT_DIRECTORY, _) => "Destination is not a directory.",
		(ErrorCode.DEST_NOT_FILE, _) => "Destination is not a file.",
		(ErrorCode.DEST_NOT_FOUND, _) => "Destination directory does not exist.",
		(ErrorCode.DEST_SENSITIVE, _) => "Destination appears to contain source log data.",
		(ErrorCode.NO_DEST, _) => "No destination file selected.",
		(ErrorCode.NO_DEST_DIR, _) => "No destination directory selected.",
		(ErrorCode.NO_REGEX, _) => "No search text entered.",
		(ErrorCode.NO_SOURCE, _) => "No source log file selected.",
		(ErrorCode.NO_SOURCES, _) => "No source log files selected.",
		(ErrorCode.SOURCE_CONFLICT, _) => "One or more source files exist in the destination.",
		(ErrorCode.SOURCE_EQUALS_DEST, _) => "Source and destination files are identical.",
		(ErrorCode.SOURCE_NOT_FOUND, _) => "Source log file does not exist.",
		(ErrorCode.SOURCES_NOT_FOUND, _) => "One or more source files do not exist.",

		(ErrorCode.None, WarningCode.MULTI_OVERWRITE) => "One or more files will be overwritten.",
		(ErrorCode.None, WarningCode.SINGLE_OVERWRITE) => "Destination file will be overwritten.",
		(ErrorCode.None, _) => string.Empty,

		(_, _) => "An unknown error has occurred.",
	};

	public static bool IsValidPattern(string? pattern = null)
	{
		if (pattern is null)
			return false;

		try
		{
			Regex.IsMatch(string.Empty, pattern);
		}
		catch (Exception)
		{
			return false;
		}

		return true;
	}

	public static bool IsValidTimestamp(uint timestamp, bool LogTestOverride = false) =>
		timestamp >= 1 // If it came before Jan. 1, 1970, there's a problem.
		&& timestamp <= UNIXTimestamp() // If it's in the future, also a problem.
		&& !string.Empty.Equals(DTFromStamp(timestamp).ToString(dateFormat) ?? string.Empty) // If it can't be translated to a date, also a problem.
		&& (timestamp >= lastTimestamp || LogTestOverride); // If it isn't sequential, also a problem, because F-Chat would never save it that way.

	public static void LogException(Exception e)
	{
		lastException = e.Message;
		File.AppendAllText(errorFile, DateTime.Now.ToString(dateFormat) + " - " + lastException + "\n");
		File.AppendAllText(errorFile, e?.TargetSite?.DeclaringType?.FullName + "." + e?.TargetSite?.Name + "\n\n");
	}

	public static bool LogTest(string targetFile)
	{
		if (!File.Exists(targetFile))
			return false;

		byte[] idBuffer = new byte[4];
		byte[] srcBuffer;
		using FileStream srcFS = new FileInfo(targetFile).OpenRead();

		if (srcFS.Read(idBuffer, 0, 4) < 4)
			return false;
		if (!IsValidTimestamp(BEInt(idBuffer), true))
			return false;

		if (srcFS.ReadByte() > 6)
			return false;

		int profLen = srcFS.ReadByte();
		if (profLen < 1)
			return false;

		srcBuffer = new byte[profLen];
		if (srcFS.Read(srcBuffer, 0, profLen) < profLen)
			return false;

		if (srcFS.Read(idBuffer, 0, 2) < 2)
			return false;

		// We assume a valid log file starting from here, as the header format of the first message is now confirmed.
		// TODO: Consider testing for two or even three messages to increase certainty. What if the first message in a log file happens to be corrupted?

		return true;
	}

	public static (T, T) Swap<T>(ref (T, T) tuple) => tuple = (tuple.Item2, tuple.Item1);

	public static uint UNIXTimestamp() => (uint)Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds);
}
