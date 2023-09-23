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

