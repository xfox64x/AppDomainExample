# AppDomainExample
Using AppDomain's to escape detection and enable dynamic execution.

This is a work-in-progress - a collection of my thoughts and half-baked ideas when I threw too much time into the thrash.

## Prologue
I've been working on a new .NET RAT to both escape security product detection and explore some .NET concepts I've been meaning to check out, in support of my engagements. Meterpreter is too well-known/signatured to work during an engagement, in the presence of an advanced capability (like Crowdstrike). Combining advanced capabilities with AMSI has pretty much ruined PowerShell as a reliable and stealthy vector. The move to .NET was the rage over the last couple years (and well before), resulting in (and probably better described by) [things like SpecterOps' SharpSploit](https://posts.specterops.io/introducing-sharpsploit-a-c-post-exploitation-library-5c7be5f16c51) and [HarmJ0y's](https://github.com/HarmJ0y) [GhostPack](https://github.com/GhostPack). 

These tools provide most of the functionality I like in a RAT and they are familiar, so my goal became figuring out how I could remotely load and execute these capabilities. Well... my goal was really hijacked by a couple of things, as I went along wantonly developing, that I wanted to understand and try:
- How do I .NET Reflection? (load and execute the goods)
- Is there anything I can do to hide my activity from security products? (short-term hide the goods)
- Can I/How do I unload modules when not in use? (long-term protect the goods)

.NET Reflection is well understood and documented. Loading additional .NET Assemblies from a file or byte-array and calling the methods within is easy. 

Unloading .NET Assemblies after you're done with them? That's a bit more tricky. In general, once a DLL is loaded, it cannot be unloaded without destroying the current application... or what .NET knows as an [AppDomain](https://docs.microsoft.com/en-us/dotnet/api/system.appdomain?redirectedfrom=MSDN&view=netframework-4.7.2). Fortunately for us, we can create more AppDomains within our main AppDomain, which we can load Assemblies into, execute methods from said Assemblies, and then promptly unload the AppDomain and all Assemblies loaded in it. Unfortunately for us (me), doing this creates a whole track and field of hurdles and hoops that we must contort around.
