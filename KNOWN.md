# Known Issues
- Ads are read correctly as they are in later versions, but much older clients didn't record ads as uniquely ID'd messages - they appear to have just been plaintext, with no delimiter. Reading them causes patches of garbage.
- Subscripts are causing future tags in a message to wholly vanish. Repeat: Subscripts break all other tags. Good lord.
- HTML output doesn't retain intra-message newlines.