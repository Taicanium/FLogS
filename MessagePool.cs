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
	/// Non-static functions serving most of the needs of the actual translation routine. Everything not strictly WPF but still integral to the BackgroundWorker threads goes here.
	/// </summary>
	internal partial class MessagePool
	{
		private ByteCount bytesRead;
		public uint corruptTimestamps = 0U;
		public string destDir = string.Empty;
		public string destFile = string.Empty;
		public bool divide = false;
		private StringBuilder? dstSB;
		public DateTime? dtAfter;
		public DateTime? dtBefore;
		public uint emptyMessages = 0U;
		private List<string>? filesDone;
		private bool headerWritten = false;
		public ByteCount intactBytes;
		public uint intactMessages = 0U;
		private uint lastDate = 0U;
		private int lastDiscrepancy;
		private string lastFile = string.Empty;
		private uint lastMessageCount = 0U;
		private uint lastPosition;
		private string opposingProfile = string.Empty;
		public string phrase = string.Empty;
		public bool regex = false;
		public bool saveTruncated = false;
		private bool scanIDX;
		public string srcFile = string.Empty;
		private readonly Dictionary<string, int> tagCounts = new()
		{
			{ "b", 0 },
			{ "big", 0 },
			{ "color", 0 },
			{ "eicon", 0 },
			{ "i", 0 },
			{ "icon", 0 },
			{ "noparse", 0 },
			{ "s", 0 },
			{ "session", 0 },
			{ "spoiler", 0 },
			{ "sub", 0 },
			{ "sup", 0 },
			{ "u", 0 },
			{ "url", 0 },
			{ "user", 0 },
		};
		private Stack<string>? tagHistory;
		private uint thisDate = 1U;
		public ByteCount totalSize;
		public ByteCount truncatedBytes;
		public uint truncatedMessages;
		public ByteCount unreadBytes;
		private List<string>? writtenDirectories;

		private enum ErrorType
		{
			Truncated,
			Empty,
			Corrupted,
		}

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

		private static void AppendError(ref ArrayList messageData, ErrorType format = ErrorType.Truncated)
		{
			if (!Common.plaintext)
				messageData[^1] += "<span class=\"warn\">";

			switch (format)
			{
				case ErrorType.Truncated:
					messageData.Add("[TRUNCATED MESSAGE]");
					break;
				case ErrorType.Empty:
					messageData.Add("[EMPTY MESSAGE]");
					break;
				case ErrorType.Corrupted:
					messageData[^1] += "[BAD TIMESTAMP]";
					break;
			}

			if (!Common.plaintext)
				messageData[^1] += "</span>";
		}

		public void BatchProcess(object? sender, DoWorkEventArgs e)
		{
			string[] files = (string[]?)e.Argument ?? [];
			filesDone = [];
			scanIDX = true;
			writtenDirectories = [];

			foreach (string? logfile in files)
			{
				string fileName = Path.GetFileNameWithoutExtension(logfile);
				if (filesDone.Contains(fileName))
					continue;

				srcFile = logfile;
				destFile = files.Length == 1 ? destDir : Path.Join(destDir, fileName) + (Common.plaintext ? ".txt" : ".html");
				lastPosition = 0U;

				BeginRoutine(sender, e);

				bytesRead += Common.fileListing?[logfile].Length ?? 0;
				filesDone.Add(fileName);

				if (!Common.lastException.Equals(string.Empty))
					break;
			}

			if (divide)
				foreach (string dir in writtenDirectories)
					if (Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
						Directory.Delete(dir, true);
		}

		public void BeginRoutine(object? sender, DoWorkEventArgs e)
		{
			opposingProfile = string.Empty;

			try
			{
				if (dtBefore < dtAfter)
					(dtAfter, dtBefore) = (dtBefore, dtAfter);

				string[] idxOptions = [
					Path.Join(Path.GetDirectoryName(srcFile), Path.GetFileNameWithoutExtension(srcFile)) + ".idx", // Search first for an IDX file matching just the log file's name. e.g. "pokefurs.idx".
					srcFile + ".idx", // As a fallback, also search for an IDX that matches the log's name and extension. e.g. "pokefurs.log.idx".
				];
				bool idxFound = false;

				foreach (string idx in idxOptions)
				{
					if (!idxFound && File.Exists(idx))
					{
						using FileStream srcIDX = File.OpenRead(idx);
						idxFound = TranslateIDX(srcIDX);
					}
				}

				if (File.Exists(destFile))
					File.Delete(destFile);

				using FileStream? srcFS = Common.fileListing?[srcFile].OpenRead();

				using (StreamWriter dstFS = divide ? StreamWriter.Null : new(destFile, true))
				{
					dstSB = new();
					headerWritten = false;
					lastDate = 0U;
					lastDiscrepancy = 0;
					lastPosition = 0U;
					Common.lastTimestamp = 0U;
					DateTime lastUpdate = DateTime.Now;

					while (srcFS?.Position < srcFS?.Length - 1)
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
								(sender as BackgroundWorker)?.ReportProgress((int)progress.bytes);
							}
							else
								(sender as BackgroundWorker)?.ReportProgress(0);

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
						dstFS.Write(Common.htmlFooter);
						if (divide)
							File.AppendAllText(lastFile, Common.htmlFooter);
					}
				}

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
				(sender as BackgroundWorker)?.CancelAsync();
			}
		}

		public void ResetStats()
		{
			bytesRead = new();
			corruptTimestamps = 0U;
			emptyMessages = 0U;
			intactBytes = new();
			intactMessages = 0U;
			lastDate = 0U;
			lastFile = string.Empty;
			lastMessageCount = 0U;
			lastPosition = 0U;
			scanIDX = false;
			truncatedBytes = new();
			truncatedMessages = 0U;
			unreadBytes = new();
		}

		/// <summary>
		/// Extract the name of a private channel from an IDX file, and append it to the destination file path.
		/// </summary>
		/// <param name="srcFS">>A FileStream opened to the IDX file matching MessagePool.srcFile.</param>
		/// <returns>'true' if a channel name was successfully extracted from the filestream and appended to MessagePool.destFile; 'false' if, for any reason, that did not occur.</returns>
		private bool TranslateIDX(FileStream srcFS)
		{
			/*
			 * I have not reverse-engineered the IDX format beyond reading channel/profile names from it.
			 * It appears to contain 8-byte blocks of numerical data in ascending value; there are more such blocks in IDX files paired with older logs.
			 * I don't know what the blocks represent, but they are almost certainly not timestamps - they're several powers of ten too large.
			 */

			string? fileName = Path.GetFileNameWithoutExtension(srcFile);

			int nameLength = srcFS.ReadByte();
			if (nameLength < 1
				|| !Path.GetFileNameWithoutExtension(srcFS.Name).Contains('#') && nameLength > 20) // F-List profile names cannot be greater than 20 characters in length.
				return false;

			byte[] streamBuffer = new byte[nameLength];
			if (srcFS.Read(streamBuffer, 0, nameLength) < nameLength)
				return false;

			string nameString = new string(Encoding.UTF8.GetString(streamBuffer).Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).ToLower();

			if (("#" + nameString).Equals(fileName?.ToLower())
				|| ("#" + nameString).Equals("#" + fileName?.ToLower())) // If the IDX encoded name matches the log file name, we're either working with a public channel or a DM.
																			// It bears mentioning that the IDX name will never contain a hashtag, hence why we append it here.
			{
				if (fileName?.Contains('#') is true) // If the log filename contains a hashtag, it's a public channel.
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

		/// <summary>
		/// Read from the source log file and convert a single message to plaintext, then conditionally write it to the destination. This function seeks to the next valid message before returning.
		/// </summary>
		/// <param name="srcFS">A FileStream opened to the source log file.</param>
		/// <returns>'true' if a message was written to file; 'false' if, for any reason, that did not occur.</returns>
		private bool TranslateMessage(FileStream srcFS, StreamWriter dstFS)
		{
			/*
			 * Log files come in a "plaintext-plus" format consisting of sequential blocks in the form:
			 ** 4-byte UNIX timestamp;
			 ** 1-byte message format ID/delimiter (bottle spin, /me message, etc.);
			 ** 1+N-byte profile name (or a null terminator if the message format is headless);
			 ** 2+N-byte message data;
			 ** 1-byte null terminator.
			 */

			byte[]? idBuffer = new byte[4];
			bool intact = true;
			bool matchPhrase = false;
			ArrayList messageData = [];
			uint messageLength;
			string profileName = string.Empty;
			int result;
			tagHistory = new();
			DateTime thisDT = new();
			bool withinRange = true;

			foreach (string key in tagCounts.Keys)
				tagCounts[key] = 0;

			int discrepancy = (int)srcFS.Position - (int)lastPosition; // If there's data inbetween the last successfully read message and this one...well, there's corrupted data there.
			lastDiscrepancy += discrepancy;
			unreadBytes += discrepancy;

			if (srcFS.Read(idBuffer, 0, 4) < 4) // Read the timestamp.
				return false;

			messageData.Add(string.Empty);

			uint timestamp = Common.BEInt(idBuffer); // The timestamp is Big-endian. Fix that.
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
				AppendError(ref messageData, ErrorType.Corrupted);

				// On the very off chance an otherwise-valid set of messages was made non-sequential, say, by F-Chat's client while trying to repair corruption.
				// This should never happen, but you throw 100% of the exceptions you don't catch.
				if (timestamp > 0 && timestamp < Common.UNIXTimestamp())
					Common.lastTimestamp = timestamp;
			}

			if (divide)
			{
				thisDate = timestamp - timestamp % 86400;
				if (thisDate != lastDate && intact)
				{
					if (lastDate != 0U)
					{
						if (!Common.plaintext)
						{
							if (!headerWritten)
							{
								dstSB?.Insert(0, Common.htmlHeader);
								headerWritten = true;
							}
							dstSB?.Append(Common.htmlFooter);
						}

						File.AppendAllText(lastFile, dstSB?.ToString());
						dstSB?.Clear();

						if (lastMessageCount == 0U)
							File.Delete(lastFile);
					}

					string destName = Path.GetFileNameWithoutExtension(destFile) ?? "UNKNOWN";
					string newDir = Path.Combine(Path.GetDirectoryName(destFile) ?? "C:", destName);

					writtenDirectories?.Add(newDir);

					if (!Directory.Exists(newDir))
						Directory.CreateDirectory(newDir);

					string newName = Path.Combine(
						Path.GetDirectoryName(destFile) ?? "C:",
						destName,
						destName + "_" + thisDT.ToString("yyyy-MM-dd") + Path.GetExtension(destFile) ?? ".txt");

					if (File.Exists(newName))
						File.Delete(newName);

					headerWritten = false;
					lastDate = thisDate;
					lastFile = newName;
					lastMessageCount = 0U;
				}
			}

			MessageType msId = (MessageType)srcFS.ReadByte(); // Message delimiter.
			int nextByte = srcFS.ReadByte(); // 1-byte length of profile name. Headless messages have a null terminator here.

			if (msId == MessageType.EOF || nextByte == -1)
				return false;

			if (msId != MessageType.Headless)
			{
				byte[] streamBuffer = new byte[nextByte];

				if ((result = srcFS.Read(streamBuffer, 0, nextByte)) < nextByte) // Read the profile name.
				{
					intact = false;
					truncatedBytes += result;
					truncatedMessages++;
					AppendError(ref messageData);
				}

				profileName = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);

				if (!Common.plaintext)
				{
					messageData.Add("<a class=\"pf\" href=\"https://f-list.net/c/"
						+ profileName
						+ "\"><img class=\"av\" src=\"https://static.f-list.net/images/avatar/"
						+ profileName.ToLower()
						+ ".png\" />"
						+ profileName
						+ "</a>");

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
						return false;
					case MessageType.Regular:
						messageData[^1] += ":"; // This prevents us from putting a space before the colon later.
						break;
					case MessageType.Me:
					case MessageType.DiceRoll: // These also include bottle spins and other 'fun' commands.
						// Non est.
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
				AppendError(ref messageData, ErrorType.Empty);
			}
			else
			{
				idBuffer[2] = 0;
				idBuffer[3] = 0;
				if ((messageLength = Common.BEInt(idBuffer)) < 1)
				{
					emptyMessages++;
					intact = false;
					AppendError(ref messageData, ErrorType.Empty);
				}
				else
				{
					byte[] streamBuffer = new byte[messageLength];
					if ((result = srcFS.Read(streamBuffer, 0, (int)messageLength)) < messageLength) // Read the message text.
					{
						intact = false;
						truncatedBytes += result;
						truncatedMessages++;
						AppendError(ref messageData);
					}

					string coreMessage = Encoding.UTF8.GetString(streamBuffer, 0, streamBuffer.Length);

					if (!Common.plaintext)
						foreach (KeyValuePair<string, string> entity in Common.htmlEntities)
							coreMessage = Regex.Replace(coreMessage, entity.Key, entity.Value);

					if (!Common.plaintext && (msId == MessageType.Me || msId == MessageType.DiceRoll))
					{
						coreMessage = "<i>" + coreMessage;
						tagHistory?.Push("i");
						tagCounts["i"] += 1;
						coreMessage = coreMessage.TrimStart();
					}

					messageData.Add(coreMessage);
				}
			}

			string messageOut = string.Join(' ', messageData.ToArray());
			messageOut = ControlCharacters().Replace(messageOut, string.Empty); // Remove everything that's not a printable, newline, or format character.

			if (phrase is null
				|| !regex && messageOut.Contains(phrase, StringComparison.OrdinalIgnoreCase) // Either the profile name or the message body can contain our search text.
				|| regex && Regex.IsMatch(messageOut, phrase, RegexOptions.IgnoreCase))
				matchPhrase = true;

			if (matchPhrase && withinRange && (intact || saveTruncated))
			{
				if (intact)
				{
					intactMessages++;
					intactBytes += messageOut.Length;
				}

				if (lastDiscrepancy > 0)
				{
					if (!Common.plaintext)
						dstSB?.Append("<span class=\"warn\">");
					dstSB?.Append(string.Format("({0:#,0} missing bytes)", lastDiscrepancy));
					if (!Common.plaintext)
						dstSB?.Append("</span><br />");
					dstSB?.Append(dstFS.NewLine);
				}

				if (!Common.plaintext)
					messageOut = TranslateTags(messageOut); // If we're saving to HTML, it's time to convert from BBCode to HTML-style tags.

				messageOut = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(messageOut)); // There's an odd quirk with East Asian printable characters that requires us to reformat them once.
				messageOut = ControlCharacters().Replace(messageOut, string.Empty); // Once more, remove everything that's not a printable, newline, or format character.

				if (!Common.plaintext && !opposingProfile.Equals(string.Empty) && !profileName.ToLower().Equals(opposingProfile.ToLower())) // If this is the local user, close the highlight tag from before.
					messageOut += "</span>";

				dstSB?.Append(messageOut);
				if (!Common.plaintext)
					dstSB?.Append("<br />");
				dstSB?.Append(dstFS.NewLine);

				if (!Common.plaintext && !headerWritten)
				{
					dstSB?.Insert(0, Common.htmlHeader);
					headerWritten = true;
				}

				if (!divide)
				{
					dstFS.Write(dstSB?.ToString());
					dstSB?.Clear();
				}

				lastDiscrepancy = 0;
				lastMessageCount++;
			}

			lastPosition = (uint)srcFS.Position;
			bool nextID = false;
			while (!nextID) // Search for the next message by locating its delimiter.
			{
				srcFS.ReadByte();
				srcFS.Read(idBuffer, 0, 4);
				nextByte = srcFS.ReadByte();
				if (nextByte == -1)
					return true;

				srcFS.Seek(-6, SeekOrigin.Current);

				// Our verification of the next message is rudimentary. We only validate whether the byte occupying the space where a delimiter SHOULD be COULD in fact be a delimiter.
				// Tying the message to the presence of a timestamp would be less reliable, since we can't assume that any such timestamp is intact and well-ordered.
				// In practice, I validated 2.2 million messages across ~250 channel logs, and the occurrence of failed delimiter checks, or issues arising thereof, was 0.
				// That's unfair, however, because I also know *most* of my logs to be wholly uncorrupted; further testing is necessary with logs that are less intact.
				if (nextByte < 7)
				{
					discrepancy = (int)srcFS.Position - (int)lastPosition;
					lastDiscrepancy += discrepancy;
					lastPosition = (uint)srcFS.Position;
					nextID = true;
					unreadBytes += discrepancy;
					srcFS.ReadByte();
				}
				else
				{
					srcFS.Read(idBuffer, 0, 2);
					srcFS.Read(idBuffer, 0, 4);
					nextByte = srcFS.ReadByte();
					if (nextByte == -1)
						return true;

					srcFS.Seek(-7, SeekOrigin.Current);
					srcFS.Read(idBuffer, 0, 2);
					if (nextByte < 7)
					{
						discrepancy = (int)srcFS.Position - (int)lastPosition - 2;
						lastDiscrepancy += discrepancy;
						lastPosition = (uint)srcFS.Position;
						nextID = true;
						unreadBytes += discrepancy;
					}
				}
			}

			return true;
		}

		private string TranslateTags(string message)
		{
			int anchorIndex = 0;
			int indexAdj = 0;
			string lastTag;
			string messageOut = message;
			bool noParse = false;
			string partialParse = string.Empty;
			string tag;
			MatchCollection tags = BBCodeTags().Matches(messageOut);
			string URL = string.Empty;

			// The best practice is to avoid sub-routines like these where possible.
			// But with the number of times this code snippet is later called, it's virtually unthinkable not to better organize.
			bool AdjustHistory(int index)
			{
				while (tagHistory?.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
				{
					AdjustMessageData(ref messageOut, Common.tagClosings[lastTag], index, ref indexAdj);
					tagCounts[lastTag]++;
				}
				AdjustMessageData(ref messageOut, Common.tagClosings[tag], index, ref indexAdj);
				return true;
			}

			for (int i = 0; i < tags.Count; i++)
			{
				string arg = string.Empty;
				tag = tags[i].Groups[1].Value.ToLower();
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
					AdjustMessageData(ref messageOut, "\u200B", tags[i].Index + 1, ref indexAdj); // In addition to enclosing noparse'd tags in a plaintext script, we will also 'break' the tag by inserting a zero-width space directly after its opening bracket.
					continue;
				}

				if (isClosing && tagCounts.TryGetValue(tag, out int tagCount) && tagCount % 2 == 0)
					continue;

				switch (tag)
				{
					case "b":
					case "i":
					case "s":
					case "sub":
					case "sup":
					case "u":
						if (tagCounts[tag] % 2 == 1)
						{
							AdjustHistory(tags[i].Index);
							break;
						}
						AdjustMessageData(ref messageOut, "<" + tag + ">", tags[i].Index, ref indexAdj);
						tagHistory?.Push(tag);
						break;
					case "big":
						if (tagCounts[tag] % 2 == 1)
						{
							AdjustHistory(tags[i].Index);
							break;
						}
						AdjustMessageData(ref messageOut, "<span style=\"font-size: 1.5rem\">", tags[i].Index, ref indexAdj);
						tagHistory?.Push(tag);
						break;
					case "color":
						if (tagCounts[tag] % 2 == 1)
						{
							AdjustHistory(tags[i].Index);
							break;
						}
						string colorData = arg switch
						{
							"black" => "#000000; text-shadow: 1px 1px 1px #8887BF, -1px 1px 1px #8887BF, -1px -1px 1px #8887BF, 1px -1px 1px #8887BF;",
							"blue" => "#3D67F7",
							"brown" => "#836E42",
							"cyan" => "#8BFCFD",
							"gray" => "#B0B0B0",
							"green" => "#87FB4A",
							"orange" => "#E46F2B",
							"pink" => "#EB9ECA",
							"purple" => "#8B3EF6",
							"red" => "#E03121",
							"yellow" => "#FDFE52",
							"white" => "#FFFFFF",
							_ => "#F0F0F0",
						};
						AdjustMessageData(ref messageOut, "<span style=\"color: " + colorData + "\">", tags[i].Index, ref indexAdj);
						tagHistory?.Push(tag);
						break;
					case "eicon":
						if (!partialParse.Equals(tag))
						{
							if (!partialParse.Equals(string.Empty))
								continue;

							if (!messageOut.Contains("[/eicon]"))
							{
								// If this eicon tag isn't closed later, then noparse it. Same with icons below.
								AdjustMessageData(ref messageOut, "\u200B", tags[i].Index + 1, ref indexAdj);
								continue;
							}
						}

						if (tagCounts[tag] % 2 == 1)
						{
							// 61 for the img tag we inserted below, and 6 for the BBCode tag which is still there.
							URL = messageOut[(anchorIndex + 67)..(tags[i].Index + indexAdj)];
							AdjustMessageData(ref messageOut, ".gif\" title=\"" + URL + "\" />", tags[i].Index, ref indexAdj);

							messageOut = string.Concat(messageOut.AsSpan(0, anchorIndex),
								messageOut[anchorIndex..(tags[i].Index + indexAdj)].ToLower(),
								messageOut.AsSpan(tags[i].Index + indexAdj, messageOut.Length - tags[i].Index - indexAdj));

							if (tagHistory?.Peek().Equals(tag) is true)
								tagHistory.Pop();

							partialParse = string.Empty;
							break;
						}
						anchorIndex = tags[i].Index + indexAdj;
						AdjustMessageData(ref messageOut, "<img class=\"ec\" src=\"https://static.f-list.net/images/eicon/", tags[i].Index, ref indexAdj);
						partialParse = tag;
						tagHistory?.Push(tag);
						break;
					case "icon":
						if (!partialParse.Equals(tag))
						{
							if (!partialParse.Equals(string.Empty))
								continue;

							if (!messageOut.Contains("[/icon]"))
							{
								AdjustMessageData(ref messageOut, "\u200B", tags[i].Index + 1, ref indexAdj);
								continue;
							}
						}

						if (tagCounts[tag] % 2 == 1)
						{
							// The img tag must be enclosed in the anchor and not the other way around.
							// As such, indexAdj cannot be tied to the anchor, and we have to insert it manually.

							// 62 for the img tag we inserted below, and 5 for the BBCode tag which is still there.
							URL = messageOut[(anchorIndex + 67)..(tags[i].Index + indexAdj)];
							messageOut = messageOut.Insert(anchorIndex, "<a class=\"pf\" href=\"https://f-list.net/c/" + URL + "\">");
							indexAdj += URL.Length + 43;
							AdjustMessageData(ref messageOut, ".png\" title=\"" + URL + "\" /></a>", tags[i].Index, ref indexAdj);

							messageOut = string.Concat(messageOut.AsSpan(0, anchorIndex),
								messageOut[anchorIndex..(tags[i].Index + indexAdj)].ToLower(),
								messageOut.AsSpan(tags[i].Index + indexAdj, messageOut.Length - tags[i].Index - indexAdj));

							if (tagHistory?.Peek().Equals(tag) is true)
								tagHistory.Pop();

							partialParse = string.Empty;
							break;
						}
						anchorIndex = tags[i].Index + indexAdj;
						AdjustMessageData(ref messageOut, "<img class=\"ec\" src=\"https://static.f-list.net/images/avatar/", tags[i].Index, ref indexAdj);
						partialParse = tag;
						tagHistory?.Push(tag);
						break;
					case "noparse":
						if (tagCounts[tag] % 2 == 1)
						{
							AdjustHistory(tags[i].Index);
							noParse = false;
							break;
						}
						AdjustMessageData(ref messageOut, "<script type=\"text/plain\">", tags[i].Index, ref indexAdj);
						noParse = true;
						tagHistory?.Push(tag);
						break;
					case "session":
						if (!partialParse.Equals(string.Empty) && !partialParse.Equals(tag))
							continue;

						if (tagCounts[tag] % 2 == 1)
						{
							while (tagHistory?.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
							{
								AdjustMessageData(ref messageOut, Common.tagClosings[lastTag], tags[i].Index, ref indexAdj);
								tagCounts[lastTag]++;
							}

							if (!messageOut[anchorIndex..(tags[i].Index + tags[i].Length + indexAdj)].Contains(URL)) // Session tags for public channels already contain their own name instead of a room code. Here we check against doubling them up.
								AdjustMessageData(ref messageOut, " (" + URL + ")", tags[i].Index, ref indexAdj);

							AdjustMessageData(ref messageOut, Common.tagClosings[tag], tags[i].Index, ref indexAdj);
							partialParse = string.Empty;
							break;
						}
						AdjustMessageData(ref messageOut, "<a class=\"ss\" href=\"#\">", tags[i].Index, ref indexAdj); // TODO: JS-based method for copying a session invite to the user's clipboard?
						anchorIndex = tags[i].Index + tags[i].Length + indexAdj;
						partialParse = tag;
						tagHistory?.Push(tag);
						URL = arg;
						break;
					case "spoiler":
						if (tagCounts[tag] % 2 == 1)
						{
							AdjustHistory(tags[i].Index);
							break;
						}
						AdjustMessageData(ref messageOut, "<span class=\"sp\">", tags[i].Index, ref indexAdj);
						tagHistory?.Push(tag);
						break;
					case "url":
						if (!partialParse.Equals(string.Empty) && !partialParse.Equals(tag))
							continue;

						if (tagCounts[tag] % 2 == 1)
						{
							while (tagHistory?.Count > 0 && (lastTag = tagHistory.Pop()).Equals(tag) == false)
							{
								AdjustMessageData(ref messageOut, Common.tagClosings[lastTag], tags[i].Index, ref indexAdj);
								tagCounts[lastTag]++;
							}

							if (anchorIndex + indexAdj + URL.Length + 6 == tags[i].Index + indexAdj) // If the url tag contained a link but no label text, we follow the client's practice of displaying the URL itself.
																									 // The extra '6' here is the five '[url=' characters plus the closing bracket.
								AdjustMessageData(ref messageOut, URL, tags[i].Index, ref indexAdj);
							AdjustMessageData(ref messageOut, Common.tagClosings[tag], tags[i].Index, ref indexAdj);
							partialParse = string.Empty;
							break;
						}
						AdjustMessageData(ref messageOut, "<a class=\"url\" href=\"" + arg + "\">", tags[i].Index, ref indexAdj); // Yes, the arg can be empty. That's okay.
						anchorIndex = tags[i].Index;
						partialParse = tag;
						tagHistory?.Push(tag);
						URL = arg;
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
						tagHistory?.Push(tag);
						partialParse = tag;
						break;
					default:
						validTag = false;
						AdjustMessageData(ref messageOut, "\u200B", tags[i].Index + 1, ref indexAdj); // As before when processing noparse'd tags, we'll break any unrecognized tags so that they won't be deleted at the end of this routine.
						break;
				}
				if (validTag)
					tagCounts[tag]++;
			}
			while (tagHistory?.Count > 0)
			{
				lastTag = tagHistory.Pop();
				// No matter what, we CANnot auto-close these two specific tags at the end of a message.
				// Their compound structure just doesn't allow for it in 90% of cases.
				if (tagCounts[lastTag] % 2 == 1 && !lastTag.Equals("icon") && !lastTag.Equals("eicon"))
				{
					indexAdj = 0;
					AdjustMessageData(ref messageOut, Common.tagClosings[lastTag], messageOut.Length, ref indexAdj);
				}

				if (partialParse.Equals(lastTag))
					partialParse = string.Empty;

				tagCounts[lastTag]++;
			}

			// Finish things off by removing the BBCode tags, leaving our fresh HTML behind.
			messageOut = BBCodeTags().Replace(messageOut, string.Empty);

			return messageOut;
		}

		[GeneratedRegex(@"\[/*(\p{L}+)(?:=+([^\p{Co}\]]*))*?\]")]
		private static partial Regex BBCodeTags();

		[GeneratedRegex(@"\p{Co}+")]
		private static partial Regex ControlCharacters();
	}
}