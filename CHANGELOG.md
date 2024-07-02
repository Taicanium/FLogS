# 1.1.3.15 - 02/07/2024
- Fixed a crash occurring when the user mouses over a UI object during single-file processing.

# 1.1.3.14 - 20/04/2024
- We now scan pre-existing destination files for apparent source log data, and refuse to overwrite them if it exists.

# 1.1.3.13 - 17/04/2024
- File and folder selection dialogs now open by default to the standard F-Chat 3.0 log location (%appdata%/fchat/data) when selecting source logs, and to the user's desktop when selecting a destination.
- Minor fix: HTML output files containing a second, empty HTML body.

# 1.1.3.12 - 27/03/2024
- Hotfix: Recurrence of the previous headerless HTML issue.
- HTML messages containing multiple lines of subtext are no longer cut off by line height restrictions.

# 1.1.3.11 - 26/03/2024
- Hotfix: Italic HTML tags are no longer inserted into plaintext.

# 1.1.3.10 - 25/03/2024
- Fixed a misplaced function causing single-file HTML output to be written without a header or styling.
- \/me messages are now italicized according to the native client's renderer rather than left plain.
- Under-the-hood changes to error message selection which are less efficient but also much cleaner.
- Further optimizations to Regex pattern matching, including compile-time pattern functions.

# 1.1.3.9 - 25/03/2024
- Removing leftover BBCode tags after we're done translating to HTML has been massively optimized.
- Private channel logs are now formatted with their IDX name (if it exists) followed by the user's supplied file name in parentheses.

# 1.1.3.8 - 22/03/2024
- Unrecognized BBCode tags are no longer erased in HTML output. Jokes such as \[REDACTED\] can now be preserved.
- Small optimizations in repetitive code sequences that should lead to a token speedup when translating to HTML.

# 1.1.3.7 - 11/03/2024
- Messages with corrupted or non-sequential timestamps are no longer given their own files when dividing by date.
- Destination subdirectories left empty after clearing empty output files from them are now also trimmed along with their files.

# 1.1.3.6 - 10/03/2024
- Hotfix: Messages in public channels are no longer universally highlighted as if having been sent by the local user.
- Spoilers no longer fail to un-hide if they contain certain types of formatted text.

# 1.1.3.5 - 08/03/2024
- Hotfix: Last-minute calculation error causing icon tags to be mangled after HTML translation.

# 1.1.3.4 - 08/03/2024
- Fixed an issue causing the latest messages in a divided batch process to be omitted, leaving an empty HTML file.
- The local user's profile is now implicitly deduced from IDX files if they exist, enabling highlighting of their messages in DMs. This does not apply to channel logs, whose IDX files store the channel's name and not that of any one character.
- Session invite tags are now rudimentarily handled, in the form of target-less anchors displaying the channel name and code. 

# 1.1.3.3 - 05/03/2024
- HTML output now features small character avatars next to each message, as seen in the F-Chat Rising third-party client. These are especially useful for FLogS as, since we lack the capacity for gender coloring, they allow easier visual differentiation of profile names.

# 1.1.3.2 - 03/03/2024
- Fixed a niche error that occurred when 'anchor' tags, such as user and url, mistakenly contain other similar tags.

# 1.1.3.1 - 03/03/2024
- HTML styling is now handled more efficiently. Shortening some IDs and applying some base tags allows for up to 20% file size savings.
- Profile names, icons, and URLs are now properly hidden by spoilers and reveal upon hovering as ordinary text does.

# 1.1.3 - 16/02/2024
- The option is now available to divide logs up by daily messages, thereby reducing resource load when opening HTML logs.
- Reformatted certain options to use checkboxes instead of dropdown menus.

# 1.1.2.3 - 12/02/2024
- Fixed a major error whereby only the first tag in a message was handled in HTML format.
- Newlines are now handled correctly in HTML output after being broken.

# 1.1.2.2 - 09/02/2024
- Hotfix: HTML entities are no longer handled in plaintext output.

# 1.1.2.1 - 06/02/2024
- HTML entities are now securely handled. Now such things as <w< won't cause cascading lack of tag closures.
- Fixed an issue causing tags to reopen if they were manually (and mistakenly) closed twice by users.

# 1.1.2 - 27/01/2024
- We now prevent the application from reading files with the same name but different extensions, to prevent overlap in the output.
- Fixed icons and eicons not closing properly in HTML output.
- More minor HTML styling changes.

# 1.1.1 - 25/01/2024
- Unlabeled minor release.
- Day-one changes to the styling of HTML outputs.

# 1.1.0 - 25/01/2024
- HTML output! At long last. It's still experimental, though.

# 1.0.9.2 - 26/12/2023
- Empty strings are now handled in a more memory-safe way.
- Progress updates are now more efficient, leading to a minor speedup in bulk translations.

# 1.0.9.1 - 12/12/2023
- There is now a hyperlink in the About screen that leads to the official layout for .NET RegEx, for users who aren't familiar with it.
- Byte discrepancies are now cleared when moving to a new file; missing data in one file will no longer be counted when notating another. This was causing minor errors when phrase-searching.

# 1.0.9 - 03/12/2023
- Error and warning messages are now updated each time the mouse is moved; this more stringently ensures that we won't, e.g. encounter a deleted source file that could crash the program out.

# 1.0.8.7 - 10/10/2023
- Repaired the previously implemented garbage stripping method so that it no longer omits newlines.

# 1.0.8.6 - 09/10/2023
- Replaced our method for stripping garbage characters; it's now tolerant of non-ASCII Unicode characters. In other words, accented letters, em dashes, and the like are now preserved in the output rather than being removed. Further testing may be necessary.

# 1.0.8.5 - 07/10/2023
- Regex patterns can now contain leading and trailing whitespace. We'd previously trimmed such whitespace if it existed, but there are instances where it might be useful.
- Major fix: The progress bar on the File tab was broken. We've fixed the underlying routine, and also rewritten it to be more efficient.

# 1.0.8.4 - 04/10/2023
- Minor adjustments to the design, mostly margins that were on the order of a few pixels off.
- Major fix: The new IDX scanning feature was overwriting the user's desired filename when translating a single file.
- Major fix: We weren't deleting existing files before writing to them if their IDX names didn't match their filenames.
- Minor fix: Status messages going off the screen when displaying very long filenames.

# 1.0.8.3 - 04/10/2023
- We now scan for .idx files matching input source logs. If we find one, we'll extract the channel or character name from it, thus avoiding excessive "adh-#####" files in the destination directory.

# 1.0.8.2 - 01/10/2023
- The program handles exceptionally large files in a more organized manner. In particular, integer overflows should no longer occur when measuring gigabytes' worth of logs.
- Minor fix: A variable conflict caused the program to discard all messages in a batch process if a Regex search had previously been conducted during that same session. This is now remedied.

# 1.0.8.1 - 30/09/2023
- Touch-ups to the frontend design.
- Filesize measurements are handled via struct rather than naïvely. There's no measureable drop in performance, and memory should be more organized now.

# 1.0.8 - 27/09/2023
- Major refactoring of the logical backend. The program is sizable enough at this point that it was just not good practice to have so many disparate functions in the same class.
- Regular expressions (RegEx) phrase searching.
- Minor adjustments to the UI design.

# 1.0.7.3 - 25/09/2023
- Snazzy new design accents, and an overall improvement in how theme switching is handled. I've tried to emulate the old "embossed" style of Windows XP while preserving some modern touch-ups.
- Error messages are now less verbose in the log window, though they remain descriptive in the error file.
- The translation routine now aborts if a fatal error is occurred, e.g. no access to the destination file.

# 1.0.7.2 - 24/09/2023
- Streamlined error message selection. This probably wasn't impacting performance, but it was still massively inefficient.
- Major fix: Single log files were getting appended to, rather than overwritten.
- Major fix: The program now processes public announcements and staff broadcasts correctly.
- Minor fix: Run buttons weren't reverting to 'ready' status after finishing a job.
- Minor fix: We now adjust our 'timestamp count' when reading one that's out of order - so long as it's actually a valid UNIX timestamp. In practice this probably never happens without the file going right back to where it was in the next message. But it's good to proof for.

# 1.0.7.1 - 22/09/2023
- Exceptions are now reported in the header of the log window.
- Identified selection of IDX files during batch processing as the cause of the known issue regarding successful translations being overwritten with garbage.
- Minor fix: Date selection used the local time zone, even though messages are translated in UTC. Both are now UTC.

# 1.0.7 - 22/09/2023
- Phrase search function. Users can now enter one or more words to search for in the source logs, and output only messages containing that text.
- Output messages are now stripped of non-printing characters. If they exist, something's gone wrong anyways...
- Major fix: Headless messages (i.e. "(user) has logged in." and other console updates) are now processed correctly.
- Minor fix: Group separators in numbers again. Those were mistakenly removed when we moved to stream-based output.

# 1.0.6.1 - 19/09/2023
- Major fix: Swap to a different stream method for output. The one we implemented in the last version has a tendency to not flush correctly after millions of bytes written.
- Minor fix: Logged ads are now read correctly as uniquely typed messages. Previously, they hadn't been treated differently by the app, and the binary ID they are given had been assigned to bottle spins. This probably hadn't been breaking anything, but now we can label them properly.
- Known: Much older logs appear to contain non-delimited ads stored as plaintext.

# 1.0.6 - 19/09/2023
- Users can now select a range of dates to scan for messages. Messages outside this range are discarded and not counted as intact messages.
- We now stream the plaintext output instead of appending it line-by-line. The resulting speed improvement is a factor of a hundred. It was stupidly inefficient.
- Exclude IDX files if they're selected with the batch process dialog. Those are metadata files and will never contain log data. It's a matter of convenience to users who want to Ctrl+A their entire log directory.
- Major fix: Possible OutOfMemory exception when working with very large files, as a result of the one time we'd open the source file in its entirety. We now never do that. The source is exclusively streamed.
- Major fix: Refusal to overwrite files during a batch process, as a result of a misused variable.

# 1.0.5.4 - 18/09/2023
- Additional compatibility for earlier versions of Windows; previously, we had supported only the most recent version of Windows 10 and later. We now tentatively support everything back to Windows 7.
- Additional error messages and some warnings.
- The program now refuses to overwrite files it intends to read from, which for several reasons is just good practice.

# 1.0.5.3 - 17/09/2023
- Streamlined empty message handling.
- Minor fix: Cease pre-emptively ending the salvage routine if an empty message is encountered at a very specific point in the process.

# 1.0.5.2 - 17/09/2023
- Added a disclaimer to the Help tab advising the user that F-List staff cannot accept logs processed by third-party apps such as FLogS.

# 1.0.5.1 - 17/09/2023
- Minor fix: Run button now defaults to disabled until information is filled in.

# 1.0.5 - 17/09/2023
- Implemented a Help tab containing instructions for the program's use.

# 1.0.4 - 17/09/2023
- Folder destinations can now be selected via dialog; this had been intended from the beginning, but the functionality was broken until now.
- Additional checking for rare errors that may occur with older logs.
- Known: Older logs may process normally, but then be rewritten with garbage. The underlying cause is unknown. Added a stopgap that prevents opening a file twice.

# 1.0.3 - 17/09/2023
- First public release. 
- Theme selection. Users can now swap between a "light" and "dark" user interface.
- Error and warning messages next to the run button.

# 1.0.2 - 16/09/2023
- First release to identify itself with a hardcoded name and version number.
- Directory processing. Users can now provide multiple files at once to process in a batch.

# 1.0.1 - 16/09/2023
- Further refining of message translation and output formatting. Still proof-of-concept.

# 1.0.0 - 16/09/2023
- Initial private release.

