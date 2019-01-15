# AppDomainExample
Using AppDomain's to escape detection and enable dynamic execution.

I've been working on a new .NET RAT to both escape security product detection and explore some .NET concepts I've been meaning to check out. Meterpreter is too well-known/signatured to work in an engagement environment with an advanced capability (like Crowdstrike). Combining advanced capabilities with AMSI has pretty much ruined PowerShell as a reliable and stealthy vector. Moving to .NET was the rage over the last few years, resulting in (and probably better described by) [things like SpecterOps' SharpSploit](https://posts.specterops.io/introducing-sharpsploit-a-c-post-exploitation-library-5c7be5f16c51) and [HarmJ0y's](https://github.com/HarmJ0y) [GhostPack](https://github.com/GhostPack). These tools provide most of the functionality I like in a RAT, so my goal became figuring out how I could remotely load and execute these capabilities. Well... my goal really expanded into a couple of things, as I went along wantonly developing, that I wanted to understand and try:
- How do I .NET Reflection? (load and execute the goods)
- Is there anything I can do to hide my activity from security products? (short-term hide the goods)
- Can I/How do I unload modules when not in use? (long-term protect the goods)
