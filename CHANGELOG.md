# 1.0.0 - 16/09/2023
- Initial release.

# 1.0.3 - 17/09/2023
- First public version.

# 1.0.4 - 17/09/2023
- Folder destinations can now be selected via dialog; this had been intended from the beginning, but the functionality was broken until now.
- Additional checking for rare errors that may occur with older logs.
- Known: Older logs may process normally, but then be rewritten with garbage. Added a stopgap that prevents opening a file twice.

# 1.0.5 - 17/09/2023
- Implemented a Help tab containing instructions for the program's use.

# 1.0.5.1 - 17/09/2023
- Minor fix: Run button defaults to disabled until information is filled in.

# 1.0.5.2 - 17/09/2023
- Added a disclaimer to the Help tab advising the user that F-List staff cannot accept logs processed by third-party apps such as FLogS.

# 1.0.5.3 - 17/09/2023
- Streamlined empty message handling.
- Minor fix: Cease pre-emptively ending the salvage routine if an empty message is encountered at a very specific point in the process.
