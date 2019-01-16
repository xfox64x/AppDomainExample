# AppDomainExample
Using AppDomain's to escape detection and enable dynamic execution.
This is a work-in-progress - a collection of my thoughts and half-baked ideas when I threw too much time into the thrash.

Warning: these are the ramblings of a professional amateur. I probably don't know what I'm doing, have probably done things in an obscure/dumb way, and may inaccurately describe most things. All criticisms and help welcome.

## Overall Goals
1. Write something dank to help with demonstrating risk during engagements.
2. Defeat current protections and detection mechanisms.
3. Find ways to detect and prevent #2.

## Prologue
I've been working on a new .NET RAT to both escape security product detection and explore some .NET concepts I've been meaning to check out, in support of engagements. Meterpreter is too well-known/signatured to work during an engagement, in the presence of an advanced capability (like Crowdstrike). Combining advanced capabilities with AMSI has pretty much ruined PowerShell as a reliable and stealthy vector. The move to .NET was the rage over the last couple years (and well before), resulting in (and probably better described by) [things like SpecterOps' SharpSploit](https://posts.specterops.io/introducing-sharpsploit-a-c-post-exploitation-library-5c7be5f16c51) and [HarmJ0y's](https://github.com/HarmJ0y) [GhostPack](https://github.com/GhostPack). 

These tools provide most of the functionality I would like in a RAT and they are familiar, so my goal became figuring out how I could remotely load and execute these capabilities. Well... my goal was really hijacked by a couple of things, as I wantonly developed, that I wanted to understand and try:
- How do I .NET Reflection? (load and execute the goods)
- Is there anything I can do to hide my activity from security products? (short-term hide the goods)
- Can I/How do I unload modules when not in use? (long-term protect the goods)

.NET Reflection is well understood and documented. Loading additional .NET Assemblies from a file or byte-array and calling the methods within is easy. 

Unloading .NET Assemblies after you're done with them? That's a bit more tricky. In general, once a DLL is loaded, it cannot be unloaded without destroying the current application... or what .NET knows as an [AppDomain](https://docs.microsoft.com/en-us/dotnet/api/system.appdomain?redirectedfrom=MSDN&view=netframework-4.7.2). Fortunately for us, we can create more AppDomains within our main AppDomain, which we can load Assemblies into, execute methods from said Assemblies, and then promptly unload the AppDomain and all Assemblies loaded in it. Unfortunately for us (me), doing this creates a whole track and field of Type hurdles and Serialization hoops that we must contort around. AppDomains bless us with many things: protection, obscurity, boundaries, the ability to unload, and paperclip-string solutions that I probably need to re-think.

Oh, and did I mention my target was .NET 3.5/3.0?

-- Will be updated as I find time --
 
## Hiding from Crowdstrike
Forgive me pappa Ionescu, for I have sinned (I'm sure he understands this far better than I). This doesn't truly and fully escape anything; we're just taking advantage of how something automated, like Crowdstrike's agent, peeks into applications. Even in the following example, I don't have full confidence that Crowdstrike isn't capable of getting our sketchy payload ([SharpSploit](https://github.com/cobbr/SharpSploit)) or plumbing the depths of the "sandboxed" AppDomain, though it also isn't logging anything suspicious and doesn't seem to be catching new threads or any odd calls (save for opening handles to protected processes from under-protected places). This certainly warrants more of my time, to look into.

To start off this under-explained example (that I will surely enrich in the future), here are the Assemblies loaded in the main AppDomain, before I load something sketchy like SharpSploit:
![Main AppDomain Before Loading SharpSploit](https://github.com/xfox64x/AppDomainExample/raw/master/MainAppDomain_BeforeLoading.png)

Nothing out of the ordinary; no Crowdstrike sensor or, at least, not yet. I'm not sure if there's some sort of determined delay, if the sensor Assembly is always loaded, or if the actions of loading another Assembly, sketchy or not, triggers it -- Add it to the TODO list.

Anyways, nothing crazy here; just showing that both the SharpSploit and Crowdstrike Assemblies are now loaded:
![Main AppDomain After Loading SharpSploit](https://raw.githubusercontent.com/xfox64x/AppDomainExample/master/MainAppDomain_AfterLoading.png)

Now, this is the point that I realized that I had removed the example code that creates the new AppDomain and loads the SharpSploit Assembly into it... Add that to the TODO pile. Here's a listing of the Assemblies loaded in this new "sandboxed" AppDomain, before I load SharpSploit into it:
![Sandbox AppDomain Before Loading SharpSploit](https://raw.githubusercontent.com/xfox64x/AppDomainExample/master/SandboxAppDomain_BeforeLoading.png)

And then here we are after loading our sketchy SharpSploit payload:
![Sandbox AppDomain After Loading SharpSploit](https://raw.githubusercontent.com/xfox64x/AppDomainExample/master/SandboxAppDomain_AfterLoading.png)

Neat. Of course, the lack of a Crowdstrike sensor Assembly doesn't mean I've done anything of value here. Even though it would appear my activity inside this AppDomain is no longer being tracked, according to the Crowdstrike Falcon console, I'm a strong believer in "If you can't fully explain how it works, it's probably not actually working (and you're dumb)". So brb; need to work. Also, if Pappa Ionescu or someone from Crowdstrike sees this: feel free to reach out to me, but this is the extent of my knowledge (stupidity), thus far.
