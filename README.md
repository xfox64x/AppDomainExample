# AppDomainExample
A .NET tool that uses AppDomain's to enable dynamic execution and escape detection.

This is a work-in-progress - a collection of my thoughts and half-baked ideas when I threw too much time into the thrash. I probably don't know what I'm doing, have probably done things in obscure/dumb ways, and may inaccurately describe most things. All criticisms and help welcome.

## Overall Goals
1. Write something dank to help with demonstrating risk during engagements.
2. Defeat current protections and detection mechanisms.
3. Find ways to detect and prevent #2.

Until this project is finished into an actual tool, [the Wiki](https://github.com/xfox64x/AppDomainExample/wiki) will serve as my documentation of understanding and progress, to avoid cluttering up this README.

## Description
The current state of Program.cs offers three demonstrations of what I was trying to accomplish in creating a generalized way to load .NET assemblies and execute methods from said assemblies, inside of a child AppDomain, from an external AppDomain, with little to no knowledge of the Types available inside of the executing AppDomain. Without having the same Types loaded in the calling AppDomain, passing parameters and return values becomes a difficult, uncertain task. The AssemblySandbox class (and interface) ease Type resolution, manage Object creation and storage, and handle method execution through the boundaries of AppDomains. This creates an interactive environment that interprets code/script-like strings into invocations and stored variables. [The Wiki](https://github.com/xfox64x/AppDomainExample/wiki) reveals my true intentions behind this, though it may become obvious that I'm headed in the direction of a PowerShell-like environment, without PowerShell.

The included example in Program.cs creates a new AppDomain Object and uses that to create an interface to an instance of AssemblySandbox. The different demonstrations show how to call static/non-static methods from another .NET DLL/Assembly, pass commonly-typed function parameters through the serialized AppDomain barrier, and how variable storage works. Images and better descriptions will follow. Compiling your own [SharpSploit DLL](https://github.com/cobbr/SharpSploit) is required, and make sure you use the same .NET versions between the two (I used 3.5 as my target).

**The latest update:** Program_with_Command_Line_Demo.cs

This monstrosity contains my progress towards an interactive PowerShell-like environment. The shell parses PowerShell-like commands, does basic input validation, and handles the communications between the AppDomain AssemblySandbox'es and terminal. It handles variable assignment operations and supports calling basically any method the AppDomain has access to. A simple example of the language's syntax looks like this:
* Store string "asdfasdf" in variable $x:
    * $x = "asdfasdf"
* Get the value stored in variable $x:
    * $x
* Replace the "a" in the $x string with "z", and get the results back:
    * $x.Replace("a", "z")
* Replace the "a" in the $x string with "z", and store the results in $x:
    $x = $x.Replace("a", "z")

This works probably how you imagine it does: the variable $x was initially set to "asdfasdf", which is assumed to be of type System.String; so $x is assumed to be of type System.String. Calling the non-static method "Replace" on the variable $x will resolve the method based on the type of the variable it was called on (i.e. System.String.Replace).

Static methods are invoked by calling the full Assembly Qualified Type Name of your desired method:
* A basic format-string example that returns "zxcv - asdf":
    * System.String.Format("{0} - {1}", "zxcv", "asdf")

Currently, only simple command resolution is supported. I haven't worked out more complex things like instantiating a temporary string variable that would allow you to immediately call something on it (like "asdf".ToUpper() and longer things like: "asdf".ToUpper().ToLower().ToUpper()). I also haven't added any operators or if/while/for logic; nor do I think I will. One could write their own logic into a .NET assembly that one then loads and executes. Also, I didn't implement logic for getters/setters; so accessing those won't work.

There are also several "Local" methods for extending capabilities and debugging. Their abilities and syntax follow:
* Load a DLL into the active AppDomain:
    * $Local.Load("DllName", "C:\Path\To\Dll\DllName.dll")
* Get information on all variables stored in the active AppDomain:
    * $Local.GetVariableInfo()
* Get information on one named variable stored in the active AppDomain:
    * $Local.GetVariableInfo("VariableName")
* Get a list of all Assemblies loaded in the active AppDomain:
    * $Local.CheckLoadedAssemblies()
* Get an indexed list of all AssemblySandboxes:
    * $Local.ListSandboxes()
* Create a new AssemblySandbox and make it the active AppDomain:
    * $Local.NewSandbox()
* Set the active AssemblySandbox and AppDomain to the one at index 0:
    * $Local.SetSandbox(0)
* Unload AppDomain/AssemblySandbox 1, unloading all loaded DLL's:
    * $Local.DeleteSandbox(1)
* Disable debug messages:
    * $Local.DisableDebug()
* Enable debug messages:
    * $Local.EnableDebug()    

Using these Local methods, we can accomplish a lot more. The following script-like list of commands demonstrates the full set of capabilities; from managing numerous sandboxes, to loading a SharpSploit DLL and calling both static and non-static methods from it:
```powershell
$Local.NewSandbox()
$Local.ListSandboxes()
$Local.Load("SharpSploit", "C:\SharpSploit.dll")
$Local.CheckLoadedAssemblies()
SharpSploit.Enumeration.Host.GetHostname()
SharpSploit.Enumeration.Host.GetCurrentDirectory()
$Local.DisableDebug()
$Token = new SharpSploit.Credentials.Tokens()
$Local.GetVariableInfo()
$Token.WhoAmI()
$Local.SetSandbox(0)
$Local.ListSandboxes()
$Local.DeleteSandbox(1)
$Local.ListSandboxes()
$Local.CheckLoadedAssemblies()
exit
```

Slight modifications could be made to run a list of commands from a file (like a script).


A lot of things still need cleaning. Not everything serves a purpose. The thought-process is still: you don't do much of anything in a PowerShell prompt that isn't strings-into-interpreter and strings-out-terminal; everything else is complicated data and data accessories.
