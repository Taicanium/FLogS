using System;
using System.Collections;
using System.Collections.Generic;
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
        public static uint corruptTimestamps = 0U;
        public static string? destDir;
        public static string? destFile;
        public static bool divide = false;
        private static StringBuilder? dstSB;
        public static DateTime? dtAfter;
        public static DateTime? dtBefore;
        public static uint emptyMessages = 0U;
        private static List<string>? filesDone;
        private static bool headerWritten = false;
        private static readonly Dictionary<string, string> htmlEntities = new()
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
        private static readonly string htmlFooter = "</body>\n</html>";
        private static readonly string htmlHeader = @"
<!DOCTYPE html>
<html>
<head>
<meta charset=""UTF-8"" />
<base target=""_blank"">
<title>F-Chat Exported Logs</title>
<style>
body { padding: 10px; background-color: #191932; display: block; word-wrap: break-word; -ms-hyphens: auto; -moz-hyphens: auto; -webkit-hyphens: auto; hyphens: auto; max-width: 100%; position: relative; font-family: -apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica Neue,Arial,Noto Sans,Liberation Sans,sans-serif,Apple Color Emoji,Segoe UI Emoji,Segoe UI Symbol,Noto Color Emoji; font-size: 1rem; font-weight: 400; line-height: 1.5; color: #EDEDF5; text-align: left; }
script { display: block; }
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
.sp:hover { color: #FFFFFF; }
.sp:hover .ts { color: #C0C0C0; }
.sp:hover .pf { color: #6766AD; }
.sp:hover .url { color: #FFFFFF; }
.sp:hover .warn { color: #909090; }
.sp:hover .ec { filter: brightness(100%); }
</style>
</head>
<body>";
        public static ByteCount intactBytes;
        public static uint intactMessages = 0U;
        private static uint lastDate = 0U;
        private static int lastDiscrepancy;
        private static string? lastFile;
        private static uint lastMessageCount = 0U;
        private static uint lastPosition;
        private static string opposingProfile = string.Empty;
        public static string? phrase;
        public static bool regex = false;
        public static bool saveTruncated = false;
        private static bool scanIDX;
        public static string? srcFile;
        private static readonly Dictionary<string, string> tagClosings = new()
        {
            { "b", "</b>" },
            { "i", "</i>" },
            { "s", "</s>" },
            { "u", "</u>" },
            { "sub", "</sub>" },
            { "sup", "</sup>" },
            { "big", "</span>" },
            { "noparse", "</script>" },
            { "url", "</a>" },
            { "icon", ".png\" /></a>" },
            { "eicon", ".gif\" />" },
            { "user", "</span></a>" },
            { "spoiler", "</span>" },
            { "session", "</a>" },
            { "color", "</span>" },
        };
        private static readonly Dictionary<string, int> tagCounts = new()
        {
            { "b", 0 },
            { "i", 0 },
            { "s", 0 },
            { "u", 0 },
            { "sub", 0 },
            { "sup", 0 },
            { "big", 0 },
            { "noparse", 0 },
            { "url", 0 },
            { "icon", 0 },
            { "eicon", 0 },
            { "user", 0 },
            { "spoiler", 0 },
            { "session", 0 },
            { "color", 0 },
        };
        private static uint thisDate = 1U;
        public static ByteCount totalSize;
        public static ByteCount truncatedBytes;
        public static uint truncatedMessages;
        public static ByteCount unreadBytes;

        private enum MessageType
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

        private static void AdjustMessageData(ref string messageIn, string addition, int index, ref int adjustment)
        {
            messageIn = messageIn.Insert(index + adjustment, addition);
            adjustment += addition.Length;
        }

        public static void BatchProcess(object? sender, DoWorkEventArgs e)
        {
            string[]? files = (string[]?)e.Argument;
            filesDone = new();
            scanIDX = true;

            foreach (string logfile in files)
            {
                srcFile = logfile;
                string fileName = Path.GetFileNameWithoutExtension(srcFile);
                if (filesDone.Contains(fileName))
                    continue;
                filesDone.Add(fileName);

                destFile = Path.Join(destDir, fileName);
                if (!Common.plaintext)
                    destFile += ".html";
                else
                    destFile += ".txt";
                lastPosition = 0U;

                BeginRoutine(sender, e);
                bytesRead += new FileInfo(logfile).Length;

                if (!Common.lastException.Equals(string.Empty))
                    break;
            }

            return;
        }

        public static void BeginRoutine(object? sender, DoWorkEventArgs e)
        {
            if (dtBefore < dtAfter)
                (dtAfter, dtBefore) = (dtBefore, dtAfter);
            opposingProfile = string.Empty;

            try
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

                if (File.Exists(destFile))
                    File.Delete(destFile);

                FileStream srcFS = File.OpenRead(srcFile);

                using (StreamWriter dstFS = divide ? StreamWriter.Null : new(destFile, true))
                {
                    dstSB = new();
                    lastDiscrepancy = 0;
                    lastPosition = 0U;
                    Common.lastTimestamp = 0U;
                    DateTime lastUpdate = DateTime.Now;

                    while (srcFS.Position < srcFS.Length - 1)
                    {
                        TranslateMessage(srcFS, dstFS);

                        if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > 20)
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
                            if (!Common.lastException.Equals(string.Empty))
                                break;
                        }
                    }

                    if (dstSB.Length > 0)
                    {
                        dstFS.Write(dstSB.ToString());
                        if (divide)
                            File.AppendAllText(lastFile, dstSB.ToString());
                        dstSB.Clear();
                    }

                    if (!Common.plaintext)
                    {
                        dstFS.Write(htmlFooter);
                        if (divide)
                            File.AppendAllText(lastFile, htmlFooter);
                    }
                }

                srcFS.Close();
                if (lastMessageCount == 0U) // This will only happen if the source file was empty or no messages matched our search phrase.
                {
                    File.Delete(destFile);
                    if (divide)
                        File.Delete(lastFile);
                }
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
            }

            return;
        }

        public static void ResetStats()
        {
            bytesRead = new();
            corruptTimestamps = 0U;
            emptyMessages = 0U;
            intactBytes = new();
            intactMessages = 0U;
            lastDate = 0U;
            lastFile = "";
            lastMessageCount = 0U;
            lastPosition = 0U;
            scanIDX = false;
            truncatedBytes = new();
            truncatedMessages = 0U;
            unreadBytes = new();

            return;
        }

        /// <summary>
        /// Extract the name of a private channel from an IDX file, and append it to the destination file path.
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
                    || (!Path.GetFileNameWithoutExtension(srcFS.Name).Contains('#') && nameLength > 20)) // F-List profile names cannot be greater than 20 characters in length.
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
                        if (scanIDX)
                            destFile = Path.Join(Path.GetDirectoryName(destFile), "#" + nameString + Path.GetExtension(destFile)); // Preserve it as such.
                        return true;
                    }

                    opposingProfile = nameString; // Save the name so that we can later mark it as not belonging to the local user. This will later factor into highlighting messages in HTML output.
                    if (!scanIDX) // Drop from the function once we have our name string, if we aren't batch processing.
                        return true;

                    destFile = Path.Join(Path.GetDirectoryName(destFile), nameString + Path.GetExtension(destFile)); // Otherwise, it's a DM. As before, preserve the name - but this time, leave out the hashtag.
                    return true;
                }

                destFile = Path.Join(Path.GetDirectoryName(destFile), "#" + nameString + " (" + Path.GetFileNameWithoutExtension(destFile) + ")" + Path.GetExtension(destFile)); // In all other cases, it's a private channel. Format it with the channel name followed by its ID.
                return true;
            }
            catch (Exception ex)
            {
                Common.LogException(ex);
            }

            return false;
        }

        /// <summary>
        /// Read from the source log file and convert a single message to plaintext, then conditionally write it to the destination. This function seeks to the next valid message before returning.
        /// </summary>
        /// <param name="srcFS">A FileStream opened to the source log file.</param>
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
            string profileName = string.Empty;
            int result;
            byte[]? streamBuffer;
            DateTime thisDT = new();
            uint timestamp;
            bool withinRange = true;
            bool written = false;

            foreach (string key in tagCounts.Keys)
                tagCounts[key] = 0;

            discrepancy = (int)srcFS.Position - (int)lastPosition; // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
            lastDiscrepancy += discrepancy;
            unreadBytes += discrepancy;

            if (srcFS.Read(idBuffer, 0, 4) < 4) // Read the timestamp.
                return written;

            messageData.Add(string.Empty);

            timestamp = Common.BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
            if (Common.IsValidTimestamp(timestamp))
            {
                Common.lastTimestamp = timestamp;
                thisDT = Common.DTFromStamp(timestamp);

                if (!Common.plaintext)
                    messageData[^1] += "<span class=\"ts\">";
                messageData[^1] += "[" + thisDT.ToString(Common.dateFormat) + "]";
                if (!Common.plaintext)
                    messageData[^1] += "</span>";

                if (thisDT.CompareTo(dtBefore) > 0 || thisDT.CompareTo(dtAfter) < 0)
                    withinRange = false;
            }
            else
            {
                corruptTimestamps++;
                intact = false;

                if (!Common.plaintext)
                    messageData[^1] += "<span class=\"warn\">";
                messageData[^1] += "[BAD TIMESTAMP]";
                if (!Common.plaintext)
                    messageData[^1] += "</span>";

                if (timestamp > 0 && timestamp < Common.UNIXTimestamp())
                    Common.lastTimestamp = timestamp; // On the very off chance an otherwise-valid set of messages was made non-sequential, say, by F-Chat's client while trying to repair corruption.
                                                      // This should never happen, but you throw 100% of the exceptions you don't catch.
            }

            if (divide)
            {
                thisDate = timestamp - (timestamp % 86400);
                if (thisDate != lastDate)
                {
                    if (lastDate != 0U)
                    {
                        if (!Common.plaintext)
                        {
                            if (!headerWritten)
                                dstSB.Insert(0, htmlHeader);
                            dstSB.Append(htmlFooter);
                        }
                        File.AppendAllText(lastFile, dstSB.ToString());
                        dstSB.Clear();

                        if (lastMessageCount == 0U)
                            File.Delete(lastFile);
                    }

                    if (!Directory.Exists(Path.Combine(Path.GetDirectoryName(destFile) ?? "C:", Path.GetFileNameWithoutExtension(destFile) ?? "UNKNOWN")))
                        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(destFile) ?? "C:", Path.GetFileNameWithoutExtension(destFile) ?? "UNKNOWN"));

                    string newName = Path.Combine(Path.GetDirectoryName(destFile), Path.GetFileNameWithoutExtension(destFile), Path.GetFileNameWithoutExtension(destFile) + "_" + thisDT.ToString("yyyy-MM-dd") + Path.GetExtension(destFile));
                    if (File.Exists(newName))
                        File.Delete(newName);

                    headerWritten = false;
                    lastDate = thisDate;
                    lastFile = newName;
                    lastMessageCount = 0U;
                }
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

                    if (!Common.plaintext)
                        messageData[^1] += "<span class=\"warn\">";
                    messageData.Add("[TRUNCATED MESSAGE]");
                    if (!Common.plaintext)
                        messageData[^1] += "</span>";
                }

                profileName = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);

                if (!Common.plaintext)
                {
                    messageData.Add("");

                    messageData[^1] = "<a class=\"pf\" href=\"https://f-list.net/c/"
                        + profileName
                        + "\"><img class=\"av\" src=\"https://static.f-list.net/images/avatar/"
                        + profileName.ToLower()
                        + ".png\" />"
                        + profileName
                        + "</a>"
                        + messageData[^1];

                    if (!opposingProfile.Equals(string.Empty) && !profileName.ToLower().Equals(opposingProfile.ToLower())) // If this is the local user, highlight the message.
                        messageData.Insert(0, "<span class=\"us\">");
                }
                else
                    messageData.Add(profileName);

                if (msId == MessageType.Me)
                    messageData[^1] = "*" + messageData[^1];

                switch (msId)
                {
                    case MessageType.EOF:
                        return written;
                    case MessageType.Regular:
                        messageData[^1] += ":"; // This prevents us from putting a space before the colon later.
                        break;
                    case MessageType.Me:
                    case MessageType.DiceRoll: // These also include bottle spins and other 'fun' commands.

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

                if (!Common.plaintext)
                    messageData[^1] += "<span class=\"warn\">";
                messageData.Add("[EMPTY MESSAGE]");
                if (!Common.plaintext)
                    messageData[^1] += "</span>";
            }
            else
            {
                idBuffer[2] = 0;
                idBuffer[3] = 0;
                if ((messageLength = Common.BEInt(idBuffer)) < 1)
                {
                    emptyMessages++;
                    intact = false;

                    if (!Common.plaintext)
                        messageData[^1] += "<span class=\"warn\">";
                    messageData.Add("[EMPTY MESSAGE]");
                    if (!Common.plaintext)
                        messageData[^1] += "</span>";
                }
                else
                {
                    streamBuffer = new byte[messageLength];
                    if ((result = srcFS.Read(streamBuffer, 0, (int)messageLength)) < messageLength) // Read the message text.
                    {
                        intact = false;
                        truncatedBytes += result;
                        truncatedMessages++;

                        if (!Common.plaintext)
                            messageData[^1] += "<span class=\"warn\">";
                        messageData.Add("[TRUNCATED MESSAGE]");
                        if (!Common.plaintext)
                            messageData[^1] += "</span>";
                    }

                    messageOut = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);

                    if (!Common.plaintext)
                        foreach (KeyValuePair<string, string> entity in htmlEntities)
                            messageOut = Regex.Replace(messageOut, entity.Key, entity.Value);

                    if (msId == MessageType.Me || msId == MessageType.DiceRoll)
                        messageOut = messageOut.TrimStart();

                    messageData.Add(messageOut);
                }
            }

            messageOut = string.Join(' ', messageData.ToArray());
            messageOut = Regex.Replace(messageOut, @"\p{Co}+", string.Empty); // Remove everything that's not a printable, newline, or format character.

            if (phrase is null
                || (!regex && messageOut.Contains(phrase, StringComparison.OrdinalIgnoreCase)) // Either the profile name or the message body can contain our search text.
                || (regex && Regex.IsMatch(messageOut, phrase)))
                matchPhrase = true;

            if (matchPhrase && withinRange && (intact || saveTruncated))
            {
                if (!Common.plaintext && !headerWritten)
                {
                    dstSB.Insert(0, htmlHeader);
                    headerWritten = true;
                }

                if (intact)
                {
                    intactMessages++;
                    intactBytes += messageOut.Length;
                }

                if (lastDiscrepancy > 0)
                {
                    if (!Common.plaintext)
                        dstSB.Append("<span class=\"warn\">");
                    dstSB.Append(string.Format("({0:#,0} missing bytes)", lastDiscrepancy));
                    if (!Common.plaintext)
                        dstSB.Append("</span><br />");
                    dstSB.Append(dstFS.NewLine);
                }

                if (!Common.plaintext)
                    messageOut = TranslateTags(messageOut); // If we're saving to HTML, it's time to convert from BBCode to HTML-style tags.

                messageOut = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(messageOut)); // There's an odd quirk with East Asian printable characters that requires us to reformat them once.
                messageOut = Regex.Replace(messageOut, @"\p{Co}+", string.Empty); // Once more, remove everything that's not a printable, newline, or format character.

                if (!Common.plaintext && !opposingProfile.Equals(string.Empty) && !profileName.ToLower().Equals(opposingProfile.ToLower())) // If this is the local user, close the highlight tag from before.
                    messageOut += "</span>";

                dstSB.Append(messageOut);
                if (!Common.plaintext)
                    dstSB.Append("<br />");
                dstSB.Append(dstFS.NewLine);

                if (!divide)
                {
                    dstFS.Write(dstSB.ToString());
                    dstSB.Clear();
                }

                lastDiscrepancy = 0;
                lastMessageCount++;
                written = true;
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
                    srcFS.Read(idBuffer, 0, 2);
                    srcFS.Read(idBuffer, 0, 4);
                    nextByte = srcFS.ReadByte();
                    if (nextByte == -1)
                        return written;

                    srcFS.Seek(-7, SeekOrigin.Current);
                    srcFS.Read(idBuffer, 0, 2);
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

            return written;
        }

        private static string TranslateTags(string message)
        {
            int anchorIndex = 0;
            int indexAdj = 0;
            string lastTag;
            string messageOut = message;
            bool noParse = false;
            string partialParse = string.Empty;
            Stack<string> tagHistory = new();
            MatchCollection tags = Regex.Matches(messageOut, @"\[/*(\p{L}+)(?:=+([^\p{Co}\]]*))*?\]");
            string URL = "";

            for (int i = 0; i < tags.Count; i++)
            {
                string arg = "";
                string tag = tags[i].Groups[1].Value.ToLower();
                if (tag.Length < 1 || tags[i].Value.Length < 1)
                    continue;

                if (tags[i].Groups.Count > 2)
                    arg = tags[i].Groups[2].Value;
                bool isClosing = tags[i].Value.Substring(1, 1).Equals("/");
                bool validTag = true;

                if (noParse)
                {
                    if (tag.Equals("noparse"))
                    {
                        AdjustMessageData(ref messageOut, "</script>", tags[i].Index, ref indexAdj);
                        noParse = false;
                        continue;
                    }
                    AdjustMessageData(ref messageOut, "\u200B", tags[i].Index + 1, ref indexAdj); // For strict safety purposes: In addition to enclosing noparse'd tags in a plaintext script, we will also 'break' the tag by inserting a zero-width space directly after its bracket.
                    continue;
                }

                if (isClosing && tagCounts.ContainsKey(tag) && tagCounts[tag] % 2 == 0)
                    continue;

                switch (tag)
                {
                    case "b":
                    case "i":
                    case "s":
                    case "u":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</" + tag + ">", tags[i].Index, ref indexAdj);
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<" + tag + ">", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        break;
                    case "big":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</span>", tags[i].Index, ref indexAdj);
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<span style=\"font-size: 1.5rem\">", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        break;
                    case "sub":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</sub>", tags[i].Index, ref indexAdj);
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<sub>", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        break;
                    case "sup":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</sup>", tags[i].Index, ref indexAdj);
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<sup>", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        break;
                    case "noparse":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</script>", tags[i].Index, ref indexAdj);
                            noParse = false;
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<script type=\"text/plain\">", tags[i].Index, ref indexAdj);
                        noParse = true;
                        tagHistory.Push(tag);
                        break;
                    case "url":
                        if (!partialParse.Equals(string.Empty) && !partialParse.Equals(tag))
                            continue;

                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }

                            if (anchorIndex + indexAdj + URL.Length + 6 == tags[i].Index + indexAdj) // If the url tag contained a link but no label text, the client's practice is to display the URL itself.
                                                                                                     // The extra '6' here is the five '[url=' characters plus the closing bracket.
                                AdjustMessageData(ref messageOut, URL, tags[i].Index, ref indexAdj);

                            AdjustMessageData(ref messageOut, "</a>", tags[i].Index, ref indexAdj);
                            partialParse = string.Empty;
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<a class=\"url\" href=\"" + arg + "\">", tags[i].Index, ref indexAdj); // Yes, the arg can be empty. That's okay.
                        anchorIndex = tags[i].Index;
                        partialParse = tag;
                        tagHistory.Push(tag);
                        URL = arg;
                        break;
                    case "icon":
                        if (!partialParse.Equals(tag) && !partialParse.Equals(string.Empty))
                            continue;

                        if (tagCounts[tag] % 2 == 1)
                        {
                            // The img tag must be wrapped in the anchor and not the other way around.
                            // As such, indexAdj cannot be tied to the anchor, and we have to insert it manually.

                            // 62 for the img tag we inserted below, and 5 for the BBCode tag which is still there.
                            URL = messageOut[(anchorIndex + 67)..(tags[i].Index + indexAdj)];
                            messageOut = messageOut.Insert(anchorIndex, "<a class=\"pf\" href=\"https://f-list.net/c/" + URL + "\">");
                            indexAdj += URL.Length + 43;
                            AdjustMessageData(ref messageOut, ".png\" title=\"" + URL + "\" /></a>", tags[i].Index, ref indexAdj);

                            messageOut = string.Concat(messageOut.AsSpan(0, anchorIndex),
                                messageOut[anchorIndex..(tags[i].Index + indexAdj)].ToLower(),
                                messageOut.AsSpan(tags[i].Index + indexAdj, messageOut.Length - tags[i].Index - indexAdj));

                            if (tagHistory.Peek().Equals(tag))
                                tagHistory.Pop();

                            partialParse = string.Empty;
                            break;
                        }
                        anchorIndex = tags[i].Index + indexAdj;
                        AdjustMessageData(ref messageOut, "<img class=\"ec\" src=\"https://static.f-list.net/images/avatar/", tags[i].Index, ref indexAdj);
                        partialParse = tag;
                        tagHistory.Push(tag);
                        break;
                    case "eicon":
                        if (!partialParse.Equals(tag) && !partialParse.Equals(string.Empty))
                            continue;

                        if (tagCounts[tag] % 2 == 1)
                        {
                            // 61 for the img tag we inserted below, and 6 for the BBCode tag which is still there.
                            URL = messageOut[(anchorIndex + 67)..(tags[i].Index + indexAdj)];
                            AdjustMessageData(ref messageOut, ".gif\" title=\"" + URL + "\" />", tags[i].Index, ref indexAdj);

                            messageOut = string.Concat(messageOut.AsSpan(0, anchorIndex),
                                messageOut[anchorIndex..(tags[i].Index + indexAdj)].ToLower(),
                                messageOut.AsSpan(tags[i].Index + indexAdj, messageOut.Length - tags[i].Index - indexAdj));

                            if (tagHistory.Peek().Equals(tag))
                                tagHistory.Pop();

                            partialParse = string.Empty;
                            break;
                        }
                        anchorIndex = tags[i].Index + indexAdj;
                        AdjustMessageData(ref messageOut, "<img class=\"ec\" src=\"https://static.f-list.net/images/eicon/", tags[i].Index, ref indexAdj);
                        partialParse = tag;
                        tagHistory.Push(tag);
                        break;
                    case "user":
                        if (!partialParse.Equals(tag) && !partialParse.Equals(string.Empty))
                            continue;

                        if (tagCounts[tag] % 2 == 1)
                        {
                            // 42 for the anchor we inserted below, and 5 for the BBCode tag which is still there.
                            URL = messageOut[(anchorIndex + 47)..(tags[i].Index + indexAdj)];
                            AdjustMessageData(ref messageOut, "\">" + URL + "</a>", tags[i].Index, ref indexAdj);
                            partialParse = string.Empty;
                            break;
                        }
                        anchorIndex = tags[i].Index + indexAdj;
                        AdjustMessageData(ref messageOut, "<a class=\"pf\" href=\"https://f-list.net/c/", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        partialParse = tag;
                        break;
                    case "spoiler":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</span>", tags[i].Index, ref indexAdj);
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<span class=\"sp\">", tags[i].Index, ref indexAdj);
                        tagHistory.Push(tag);
                        break;
                    case "session":
                        if (!partialParse.Equals(string.Empty) && !partialParse.Equals(tag))
                            continue;

                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }

                            if (!messageOut[anchorIndex..(tags[i].Index + tags[i].Length + indexAdj)].Contains(URL)) // Session tags for public channels already contain their own name instead of a room code. Here we check against doubling them up.
                                AdjustMessageData(ref messageOut, " (" + URL + ")", tags[i].Index, ref indexAdj);

                            AdjustMessageData(ref messageOut, "</a>", tags[i].Index, ref indexAdj);
                            partialParse = string.Empty;
                            break;
                        }
                        AdjustMessageData(ref messageOut, "<a class=\"ss\" href=\"#\">", tags[i].Index, ref indexAdj); // TODO: JS-based method for copying a session invite to the user's clipboard?
                        anchorIndex = tags[i].Index + tags[i].Length + indexAdj;
                        partialParse = tag;
                        tagHistory.Push(tag);
                        URL = arg;
                        break;
                    case "color":
                        if (tagCounts[tag] % 2 == 1)
                        {
                            while (tagHistory.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
                            {
                                AdjustMessageData(ref messageOut, tagClosings[lastTag], tags[i].Index, ref indexAdj);
                                tagCounts[lastTag]++;
                            }
                            AdjustMessageData(ref messageOut, "</span>", tags[i].Index, ref indexAdj);
                            break;
                        }
                        switch (arg)
                        {
                            case "black":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #000000; text-shadow: 1px 1px 1px #8887BF, -1px 1px 1px #8887BF, -1px -1px 1px #8887BF, 1px -1px 1px #8887BF;\">", tags[i].Index, ref indexAdj);
                                break;
                            case "blue":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #3D67F7\">", tags[i].Index, ref indexAdj);
                                break;
                            case "brown":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #836E42\">", tags[i].Index, ref indexAdj);
                                break;
                            case "cyan":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #8BFCFD\">", tags[i].Index, ref indexAdj);
                                break;
                            case "gray":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #B0B0B0\">", tags[i].Index, ref indexAdj);
                                break;
                            case "green":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #87FB4A\">", tags[i].Index, ref indexAdj);
                                break;
                            case "orange":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #E46F2B\">", tags[i].Index, ref indexAdj);
                                break;
                            case "pink":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #EB9ECA\">", tags[i].Index, ref indexAdj);
                                break;
                            case "purple":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #8B3EF6\">", tags[i].Index, ref indexAdj);
                                break;
                            case "red":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #E03121\">", tags[i].Index, ref indexAdj);
                                break;
                            case "yellow":
                                AdjustMessageData(ref messageOut, "<span style=\"color: #FDFE52\">", tags[i].Index, ref indexAdj);
                                break;
                            case "white":
                            default:
                                AdjustMessageData(ref messageOut, "<span style=\"color: #FFFFFF\">", tags[i].Index, ref indexAdj);
                                break;
                        }
                        tagHistory.Push(tag);
                        break;
                    default:
                        validTag = false;
                        break;
                }
                if (validTag)
                    tagCounts[tag]++;
            }
            while (tagHistory.Count > 0)
            {
                lastTag = tagHistory.Pop();
                // No matter what, we CANnot auto-close these two specific tags at the end of a message.
                // Their compound structure just doesn't allow for it in 90% of cases.
                if (tagCounts[lastTag] % 2 == 1 && lastTag.Equals("icon") == false && lastTag.Equals("eicon") == false)
                {
                    indexAdj = 0;
                    AdjustMessageData(ref messageOut, tagClosings[lastTag], messageOut.Length, ref indexAdj);
                }

                if (partialParse.Equals(lastTag))
                    partialParse = string.Empty;

                tagCounts[lastTag]++;
            }

            // Finish things off by removing the BBCode tags, leaving only our fresh HTML behind.
            messageOut = Regex.Replace(messageOut, @"\[/*\p{L}+=+[^\p{Co}\]]*\]", "");
            messageOut = Regex.Replace(messageOut, @"\[/*\p{L}+\]", "");

            return messageOut;
        }
    }
}