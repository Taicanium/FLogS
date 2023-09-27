using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FLogS
{
    enum MessageType
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

    /// <summary>
    /// Static helper functions serving purely logical purposes in either the front- or backend.
    /// </summary>
    internal class Common
    {
        private readonly static string dateFormat = "yyyy-MM-dd HH:mm:ss"; // ISO 8601.
        private readonly static DateTime epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public readonly static string errorFile = "FLogS_ERROR.txt";
        public static string lastException = "";
        public static uint lastTimestamp;
        private readonly static string[] prefixes = { "k", "M", "G", "T", "P", "E", "Z", "Y", "R", "Q" }; // We count bytes, so in practice this app will overflow upon reaching 2 GB.
        public static DateTime timeBegin;

        public static uint BEInt(byte[] buffer)
        {
            return buffer[0]
                + buffer[1] * 256U
                + buffer[2] * 65536U
                + buffer[3] * 16777216U;
        }

        public static string ByteSizeString(double bytes)
        {
            double finalBytes = bytes;
            int prefixIndex = -1;

            while (finalBytes >= 921.6 && prefixIndex < 9)
            {
                finalBytes *= 0.0009765625; // 1/1024
                prefixIndex++;
            }

            if (prefixIndex == -1)
                return $"{finalBytes:N0} B";
            return $"{finalBytes:N1} {prefixes[prefixIndex]}B";
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
            if (pattern.Trim().Equals(""))
                return false;

            try
            {
                Regex.IsMatch("", pattern);
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
            if ((DTFromStamp(timestamp).ToString(dateFormat) ?? "").Equals("")) // If it can't be translated to a date, also a problem.
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
            File.AppendAllText(errorFile, e.TargetSite.DeclaringType.FullName + "." + e.TargetSite.Name + "\n");
            return;
        }

        public static uint UNIXTimestamp()
        {
            return (uint)Math.Floor(DateTime.UtcNow.Subtract(epoch).TotalSeconds);
        }
    }
}
