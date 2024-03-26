using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FLogS
{
    /// <summary>
    /// Static helper functions serving purely logical purposes in either the front- or backend.
    /// </summary>
    internal static class Common
    {
        public readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
        private readonly static DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public readonly static string errorFile = "FLogS_ERROR.txt";
        public static bool plaintext = true;
        public static string lastException = string.Empty;
        public static uint lastTimestamp;
        public readonly static string[] prefixes = { "k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q" }; // Always futureproof...
        public static DateTime timeBegin;

        public static uint BEInt(byte[] buffer)
        {
            return buffer[0]
                + buffer[1] * 256U
                + buffer[2] * 65536U
                + buffer[3] * 16777216U;
        }

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

        public static bool IsValidTimestamp(uint timestamp)
        {
            if (timestamp < 1) // If it came before Jan. 1, 1970, there's a problem.
                return false;
            if (timestamp > UNIXTimestamp()) // If it's in the future, also a problem.
                return false;
            if ((DTFromStamp(timestamp).ToString(dateFormat) ?? string.Empty).Equals(string.Empty)) // If it can't be translated to a date, also a problem.
                return false;
            if (timestamp < lastTimestamp)  // If it isn't sequential, also a problem, because F-Chat would never save it that way.
                                            // In this case specifically, there's an extremely high chance we're about to produce garbage data in the output.
                return false;
            return true;
        }

        public static void LogException(Exception e)
        {
            lastException = e.Message;
            File.AppendAllText(errorFile, DateTime.Now.ToString(dateFormat) + " - " + lastException + "\n");
            File.AppendAllText(errorFile, e?.TargetSite?.DeclaringType?.FullName + "." + e?.TargetSite?.Name + "\n\n");
            return;
        }

        public static uint UNIXTimestamp()
        {
            return (uint)Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds);
        }
    }
}
