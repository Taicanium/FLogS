using System;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FLogS
{
    /// <summary>
    /// Static functions serving most of the needs of the actual translation routine. Everything not strictly WPF but still integral to the BackgroundWorker threads goes here.
    /// </summary>
    internal class MessagePool
    {
        public static ByteCount bytesRead;
        public static uint corruptTimestamps;
        public static string? destDir;
        public static string? destFile;
        public static ByteCount discardedBytes;
        public static uint discardedMessages;
        public static DateTime? dtAfter;
        public static DateTime? dtBefore;
        public static uint emptyMessages;
        public static ByteCount intactBytes;
        public static uint intactMessages;
        private static int lastDiscrepancy;
        private static uint lastPosition;
        public static string? phrase;
        public static bool regex;
        public static bool saveTruncated;
        private static bool scanIDX;
        public static string? srcFile;
        public static ByteCount totalSize;
        public static ByteCount truncatedBytes;
        public static uint truncatedMessages;
        public static ByteCount unreadBytes;

        public static void BatchProcess(object? sender, DoWorkEventArgs e)
        {
            try
            {
                string[]? files = (string[]?)e.Argument;
                scanIDX = true;

                foreach (string logfile in files)
                {
                    srcFile = logfile;
                    destFile = Path.Join(destDir, Path.GetFileNameWithoutExtension(srcFile) + ".txt");
                    lastPosition = 0U;

                    DoWork(sender, e);
                    bytesRead += new FileInfo(logfile).Length;

                    if (!Common.lastException.Equals(""))
                        break;
                }
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
            }

            return;
        }

        public static void DoWork(object? sender, DoWorkEventArgs e)
        {
            if (dtBefore < dtAfter)
                (dtAfter, dtBefore) = (dtBefore, dtAfter);

            if (scanIDX) // We only want to scan for an IDX channel name during a batch process.
                         // The user supplies their own filename during single-file translation, so it's moot in those cases.
            {
                string[] idxOptions = {
                    Path.Join(Path.GetDirectoryName(srcFile), Path.GetFileNameWithoutExtension(srcFile)) + ".idx", // Search first for an IDX file matching just the log file's name. e.g. "pokefurs.idx".
                    srcFile + ".idx", // As a fallback, also search for an IDX that matches the log's name and extension. e.g. "pokefurs.log.idx".
                };
                bool idxFound = false;

                foreach (string idx in idxOptions)
                {
                    if (!idxFound && File.Exists(idx))
                    {
                        FileStream srcIDX = File.OpenRead(idx);
                        idxFound = TranslateIDX(srcIDX);
                        srcIDX.Close();
                    }
                }
            }

            if (File.Exists(destFile))
                File.Delete(destFile);

            FileStream srcFS = File.OpenRead(srcFile);

            using (StreamWriter dstFS = new(destFile, true))
            {
                lastDiscrepancy = 0;
                lastPosition = 0U;
                Common.lastTimestamp = 0;
                DateTime lastUpdate = DateTime.Now;

                while (srcFS.Position < srcFS.Length - 1)
                {
                    TranslateMessage(srcFS, dstFS);

                    if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > 10)
                    {
                        ByteCount progress = bytesRead + srcFS.Position;
                        progress.Simplify();
                        totalSize.Simplify();
                        if (progress.prefix > totalSize.prefix - 2)
                        {
                            totalSize.Magnitude(1); // We'll look at the progress values with more precision to keep the bar from "jerking".
                            progress.Adjust(totalSize.prefix);
                            (sender as BackgroundWorker).ReportProgress((int)progress.bytes);
                        }
                        else
                            (sender as BackgroundWorker).ReportProgress(0);
                        lastUpdate = DateTime.Now;
                        if (!Common.lastException.Equals(""))
                            break;
                    }
                }
            }

            srcFS.Close();
            if (new FileInfo(destFile).Length == 0) // This will only happen if the source file was empty or no messages matched our search phrase.
                File.Delete(destFile);

            return;
        }

        public static void ResetStats()
        {
            bytesRead = new();
            corruptTimestamps = 0U;
            discardedBytes = new();
            discardedMessages = 0U;
            emptyMessages = 0U;
            intactBytes = new();
            intactMessages = 0U;
            lastPosition = 0U;
            scanIDX = false;
            truncatedBytes = new();
            truncatedMessages = 0U;
            unreadBytes = new();

            return;
        }

        /// <summary>
        /// Extracts the name of a private channel from an IDX file, and appends it to the destination file path.
        /// </summary>
        /// <param name="srcFS">>A FileStream opened to the IDX file matching MessagePool.srcFile.</param>
        /// <returns>'true' if a channel name was successfully extracted from the filestream and appended to MessagePool.destFile; 'false' if, for any reason, that did not occur.</returns>
        private static bool TranslateIDX(FileStream srcFS)
        {
            string? fileName = Path.GetFileNameWithoutExtension(srcFile);
            int nameLength;
            string nameString;
            int result;
            byte[]? streamBuffer;

            try
            {
                nameLength = srcFS.ReadByte();
                if (nameLength < 1
                    || (!Path.GetFileNameWithoutExtension(srcFS.Name).Contains('#') && nameLength > 20)) // F-List character profiles cannot be greater than 20 characters in length.
                    return false;

                streamBuffer = new byte[nameLength];
                if ((result = srcFS.Read(streamBuffer, 0, (int)nameLength)) < nameLength)
                    return false;

                nameString = new string(Encoding.UTF8.GetString(streamBuffer).Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).ToLower();

                if (("#" + nameString).Equals(fileName?.ToLower())
                    || ("#" + nameString).Equals("#" + fileName?.ToLower())) // If the IDX encoded name matches the log file name, we're either working with a public channel or a DM.
                                                                             // It bears mentioning that the IDX name will never contain a hashtag, hence why we append it here.
                {
                    if (fileName.Contains('#')) // If the log filename contains a hashtag, it's a public channel.
                    {
                        destFile = new string(Path.Join(Path.GetDirectoryName(destFile), "#" + nameString + Path.GetExtension(destFile))); // Preserve it as such.
                        return true;
                    }

                    destFile = new string(Path.Join(Path.GetDirectoryName(destFile), nameString + Path.GetExtension(destFile))); // Otherwise, it's a DM. As before, preserve the name - but this time, leave out the hashtag.
                    return true;
                }

                destFile = new string(Path.Join(Path.GetDirectoryName(destFile), "#" + nameString + " (" + Path.GetFileNameWithoutExtension(destFile) + ")" + Path.GetExtension(destFile))); // In all other cases, it's a private channel. Format it with the channel name followed by its ID.

                return true;
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="srcFS">A FileStream opened to the source log file.</param>
        /// <param name="dstFS">A StreamWriter opened to the destination file.</param>
        /// <returns>'true' if a message was written to file; 'false' if, for any reason, that did not occur.</returns>
        private static bool TranslateMessage(FileStream srcFS, StreamWriter dstFS)
        {
            int discrepancy;
            byte[]? idBuffer = new byte[4];
            bool intact = true;
            bool matchPhrase = false;
            ArrayList messageData = new();
            uint messageLength;
            string messageOut;
            MessageType msId;
            int nextByte;
            bool nextTimestamp = false;
            string profileName;
            int result;
            byte[]? streamBuffer;
            DateTime thisDT;
            uint timestamp;
            bool withinRange = true;
            bool written = false;

            try
            {
                discrepancy = (int)srcFS.Position - (int)lastPosition; // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
                lastDiscrepancy += discrepancy;
                unreadBytes += discrepancy;

                if (srcFS.Read(idBuffer, 0, 4) < 4) // Read the timestamp.
                    return written;

                timestamp = Common.BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
                if (Common.IsValidTimestamp(timestamp))
                {
                    Common.lastTimestamp = timestamp;
                    thisDT = Common.DTFromStamp(timestamp);
                    messageData.Add("[" + thisDT.ToString(Common.dateFormat) + "]");
                    if (thisDT.CompareTo(dtBefore) > 0 || thisDT.CompareTo(dtAfter) < 0)
                        withinRange = false;
                }
                else
                {
                    corruptTimestamps++;
                    intact = false;
                    messageData.Add("[BAD TIMESTAMP]");
                    if (timestamp > 0 && timestamp < Common.UNIXTimestamp())
                        Common.lastTimestamp = timestamp; // On the very off chance an otherwise-valid set of messages was made non-sequential, say, by F-Chat's client while trying to repair corruption.
                                                          // This should never happen, but you throw 100% of the exceptions you don't catch.
                }

                msId = (MessageType)srcFS.ReadByte(); // Message delimiter.
                nextByte = srcFS.ReadByte(); // 1-byte length of profile name. Headless messages have a null terminator here.

                if (msId == MessageType.EOF || nextByte == -1)
                    return written;

                if (msId != MessageType.Headless)
                {
                    streamBuffer = new byte[nextByte];

                    if ((result = srcFS.Read(streamBuffer, 0, nextByte)) < nextByte) // Read the profile name.
                    {
                        intact = false;
                        truncatedBytes += result;
                        truncatedMessages++;
                        messageData.Add("[TRUNCATED MESSAGE]");
                    }

                    profileName = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);
                    messageData.Add(profileName);
                    switch (msId)
                    {
                        case MessageType.EOF:
                            return written;
                        case MessageType.Regular:
                            messageData[^1] += ":"; // This prevents us from putting a space before the colon later.
                            break;
                        case MessageType.Me:
                        case MessageType.DiceRoll: // These also include bottle spins and other 'fun' commands.
                            // messageData.Add("");
                            break;
                        case MessageType.Ad:
                            messageData.Add("(ad):");
                            break;
                        case MessageType.Warning:
                            messageData.Add("(warning):");
                            break;
                        case MessageType.Announcement:
                            messageData.Add("(announcement):");
                            break;
                    }
                }

                if (srcFS.Read(idBuffer, 0, 2) < 2) // 2-byte length of message.
                {
                    emptyMessages++;
                    intact = false;
                    messageData.Add("[EMPTY MESSAGE]");
                }
                else
                {
                    idBuffer[2] = 0;
                    idBuffer[3] = 0;
                    if ((messageLength = Common.BEInt(idBuffer)) < 1)
                    {
                        emptyMessages++;
                        intact = false;
                        messageData.Add("[EMPTY MESSAGE]");
                    }
                    else
                    {
                        streamBuffer = new byte[messageLength];
                        if ((result = srcFS.Read(streamBuffer, 0, (int)messageLength)) < messageLength) // Read the message text.
                        {
                            intact = false;
                            truncatedBytes += result;
                            truncatedMessages++;
                            messageData.Add("[TRUNCATED MESSAGE]");
                        }
                        messageData.Add(Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length));
                    }
                }

                messageOut = string.Join(' ', messageData.ToArray());
                messageOut = Regex.Replace(messageOut, @"\p{Co}+", string.Empty); // Remove everything that's not a printable, newline, or format character.

                if (phrase is null
                    || (!regex && messageOut.Contains(phrase, StringComparison.OrdinalIgnoreCase)) // Either the profile name or the message body can contain our search text.
                    || (regex && Regex.IsMatch(messageOut, phrase)))
                    matchPhrase = true;

                if (intact)
                {
                    intactBytes += messageOut.Length;
                    intactMessages++;
                    if (withinRange && matchPhrase)
                    {
                        if (lastDiscrepancy > 0)
                        {
                            dstFS.Write(string.Format("({0:#,0} missing bytes)", lastDiscrepancy));
                            dstFS.Write(dstFS.NewLine);
                        }
                        dstFS.Write(messageOut);
                        dstFS.Write(dstFS.NewLine);
                        lastDiscrepancy = 0;
                        written = true;
                    }
                    else // If the message doesn't match our criteria, we won't count it.
                    {
                        discardedBytes += messageOut.Length;
                        discardedMessages++;
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
                        dstFS.Write(dstFS.NewLine);
                        lastDiscrepancy = 0;
                        written = true;
                    }
                }
                lastPosition = (uint)srcFS.Position;
                while (!nextTimestamp) // Search for the next message by locating its timestamp and delimiter. It's the latter we're *really* looking for; the timestamp just helps us identify it.
                {
                    srcFS.ReadByte();
                    srcFS.Read(idBuffer, 0, 4);
                    nextByte = srcFS.ReadByte();
                    if (nextByte == -1)
                        return written;
                    srcFS.Seek(-6, SeekOrigin.Current);
                    if (nextByte < 7)
                    {
                        discrepancy = (int)srcFS.Position - (int)lastPosition;
                        lastDiscrepancy += discrepancy;
                        lastPosition = (uint)srcFS.Position;
                        nextTimestamp = true;
                        unreadBytes += discrepancy;
                        srcFS.ReadByte();
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
                        if (nextByte < 7)
                        {
                            discrepancy = (int)srcFS.Position - (int)lastPosition - 2;
                            lastDiscrepancy += discrepancy;
                            lastPosition = (uint)srcFS.Position;
                            nextTimestamp = true;
                            unreadBytes += discrepancy;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
            }

            return written;
        }
    }
}
