using System;
using System.IO;
using System.Text.RegularExpressions;

namespace FLogS
{
    struct ByteCount
    {
        public ByteCount() { bytes = 0.0; prefix = -1; }
        public ByteCount(double b, short p) { bytes = b; prefix = p; }

        public double bytes;
        public short prefix;

        public void Adjust(int factor, bool absolute = true)
        {
            if (!absolute)
                factor = (short)(prefix - factor);
            while (prefix < factor)
                Magnitude(-1);
            while (prefix > factor)
                Magnitude(1);
            return;
        }

        public void Magnitude(int factor)
        {
            bytes *= Math.Pow(1024, factor);
            prefix -= (short)factor;
        }

        public void Simplify()
        {
            while (bytes >= 921.6 && prefix < Common.prefixes.Length)
                Magnitude(-1);
            while (bytes < 0.9 && prefix > -1)
                Magnitude(1);
            return;
        }

        public static ByteCount operator -(ByteCount a, ByteCount b)
        {
            ByteCount o;
            ByteCount o2;

            if (Math.Abs(a.prefix - b.prefix) > 3) // Special accommodations must be made if the disparity in magnitude would normally result in an int overflow.
            {
                o = new(a.bytes, a.prefix);
                o2 = new(b.bytes, b.prefix);
                o.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
                o2.Adjust(o.prefix);
                o.bytes -= o2.bytes;
                o.Simplify();
                return o;
            }

            o = new(a.bytes, a.prefix);
            o.Adjust(b.prefix);
            o.bytes -= b.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, ByteCount b)
        {
            ByteCount o;
            ByteCount o2;

            if (Math.Abs(a.prefix - b.prefix) > 3)
            {
                o = new(a.bytes, a.prefix);
                o2 = new(b.bytes, b.prefix);
                o.Adjust(Math.Abs(a.prefix - b.prefix) / 2);
                o2.Adjust(o.prefix);
                o.bytes += o2.bytes;
                o.Simplify();
                return o;
            }

            o = new(a.bytes, a.prefix);
            o.Adjust(b.prefix);
            o.bytes += b.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, int b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, uint b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        public static ByteCount operator +(ByteCount a, long b)
        {
            ByteCount o = new(a.bytes, a.prefix);
            ByteCount o2 = new(b, -1);
            o.Adjust((o.prefix + 1) / 2);
            o2.Adjust(o.prefix);
            o.bytes += o2.bytes;
            o.Simplify();
            return o;
        }

        /// <summary>
        /// Return the size of this byte counter, formatted with the most appropriate metric prefix. This function does not preserve manual adjustments to the counter's magnitude.
        /// </summary>
        /// <returns>The byte counter size, in the format "0.0 xB", where 'x' is a metric prefix.</returns>
        public override string ToString()
        {
            Simplify();
            if (prefix == -1)
                return $"{bytes:N0} B";
            return $"{bytes:N1} {Common.prefixes[prefix]}B";
        }
    }

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
