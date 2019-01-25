# AppDomainExample
A .NET tool that uses AppDomain's to enable dynamic execution and escape detection.

This is a work-in-progress - a collection of my thoughts and half-baked ideas when I threw too much time into the thrash. I probably don't know what I'm doing, have probably done things in obscure/dumb ways, and may inaccurately describe most things. All criticisms and help welcome.

## Overall Goals
1. Write something dank to help with demonstrating risk during engagements.
2. Defeat current protections and detection mechanisms.
3. Find ways to detect and prevent #2.

Until this project is finished into an actual tool, [the Wiki](https://github.com/xfox64x/AppDomainExample/wiki) will serve as my documentation of understanding and progress, to avoid cluttering up this README.

## Description
The current state of the code offers three demonstrations of what I was trying to accomplish in creating a generalized way to load .NET assemblies and execute methods from said assemblies, inside of a child AppDomain, from an external AppDomain, with little to no knowledge of the Types available inside of the executing AppDomain. Without having the same Types loaded in the calling AppDomain, passing parameters and return values becomes a difficult, uncertain task. The AssemblySandbox class (and interface) ease Type resolution, manage Object creation and storage, and handle method execution through the boundaries of AppDomains. This creates an interactive environment that interprets code/script-like strings into invocations and stored variables. [The Wiki](https://github.com/xfox64x/AppDomainExample/wiki) reveals my true intentions behind this, though it may become obvious that I'm headed in the direction of a PowerShell-like environment, without PowerShell.

The included example creates a new AppDomain Object and uses that to create an interface to an instance of AssemblySandbox. The different demonstrations show how to call static/non-static methods from another .NET DLL/Assembly, pass commonly-typed function parameters through the serialized AppDomain barrier, and how variable storage works. Images and better descriptions will follow. Compiling your own [SharpSploit DLL](https://github.com/cobbr/SharpSploit) is required, and make sure you use the same .NET versions between the two (I used 3.5 as my target).

A lot of things need cleaning. Not everything serves a purpose. Variable resolution isn't fully functional. I'm currently working on the interpreter portion of this mess, with the goals of passing PowerShell-like commands (as strings) into the AssemblySandbox and passing out string-based interpretations of data. The thought-process being: you don't do much of anything in a PowerShell prompt that isn't strings-into-interpreter and strings-out-terminal; everything else is complicated data and data accessories.
