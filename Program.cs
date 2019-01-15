using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using System.Threading;

namespace AppDomainExample
{
    class Program
    {
        public static byte[] DllBytes = File.ReadAllBytes(@"D:\Scripts\SharpSploit.dll");

        public static Regex GenericTypeRegex = new Regex(@"^(?<name>[\w\+]+(\.[\w|\+]+)*)(\&*)(\**)(`(?<count>\d))?(\[(?<subtypes>.*?)\])(,\s*(?<assembly>[\w\+]+(\.[\w|\+]+)*).*?)?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        public static AppDomain GetNewAppDomain(Guid Id)
        {
            AppDomainSetup appDomainSetup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory
            };
            PermissionSet appDomainPermissions = new PermissionSet(PermissionState.Unrestricted);
            return AppDomain.CreateDomain(Id.ToString(), null, appDomainSetup, appDomainPermissions, null);
        }

        public static IAssemblySandbox GetAssemeblySandbox(AppDomain appDomain)
        {
            Type assmeblySandboxType = typeof(AssemblySandbox);
            return (IAssemblySandbox)appDomain.CreateInstanceFromAndUnwrap(assmeblySandboxType.Assembly.Location, assmeblySandboxType.FullName);
        }
        
        static void Main()
        {
            Assembly[] AssembliesBeforeLoad = AppDomain.CurrentDomain.GetAssemblies();

            Assembly SharpSploit = Assembly.Load(DllBytes);

            Assembly[] AssembliesAfterLoad = AppDomain.CurrentDomain.GetAssemblies();


            Type AssemblyType = SharpSploit.GetType("SharpSploit.Enumeration.Host", false, true);
            
            MethodInfo[] MethodInfos = AssemblyType.GetMethods();
            
            Console.ReadLine();
        }
        


        public static object Deserialize(byte[] byteArray)
        {
            try
            {
                BinaryFormatter binForm = new BinaryFormatter
                {
                    Binder = new BindChanger()
                };
                using (var memoryStream = new MemoryStream())
                {
                    memoryStream.Write(byteArray, 0, byteArray.Length);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    return binForm.Deserialize(memoryStream);
                }
            }
            catch
            {
                return null;
            }
        }

        public static byte[] Serialize(object objectToSerialize)
        {
            try
            {
                BinaryFormatter serializer = new BinaryFormatter();
                using (var memoryStream = new MemoryStream())
                {
                    serializer.Serialize(memoryStream, objectToSerialize);
                    return memoryStream.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        

        public class BindChanger : System.Runtime.Serialization.SerializationBinder
        {
            public override Type BindToType(string assemblyName, string typeName)
            {
                return ReconstructType(string.Format("{0}, {1}", typeName, assemblyName), false);
            }
        }

        public static Type ReconstructType(string typeAssemblyQualifiedName, bool throwOnError = false, params Assembly[] referencedAssemblies)
        {
            Type type = null;

            // If no assemblies were provided, then there wasn't an attempt to reconstruct the type from a specific assembly.
            // Check if the current app domain can be used to resolve the requested type (this should be 99% of calls for resolution).
            if (referencedAssemblies.Count() == 0)
            {
                type = Type.GetType(typeAssemblyQualifiedName, throwOnError);
                if (type != null)
                    return type;

                // If it made it here, populate an array of assemblies in the current app domain.
                referencedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            }

            // If that failed, attempt to resolve the type from the list of supplied assemblies or those in the current app domain.
            foreach (Assembly asm in referencedAssemblies)
            {
                type = asm.GetType(typeAssemblyQualifiedName.Replace($", {asm.FullName}", ""), throwOnError);
                if (type != null)
                    return type;
            }

            // If that failed and the type looks like a generic type with assembly qualified type arguments, proceed with constructing a generic type.
            // TODO: follow the below TODO in ConstructGenericType because this if statement probably isn't accurate enough.
            Match match = GenericTypeRegex.Match(typeAssemblyQualifiedName);
            if (match.Success && !string.IsNullOrEmpty(match.Groups["count"].Value))
            {
                type = ConstructGenericType(typeAssemblyQualifiedName, throwOnError);
                if (type != null)
                    return type;
            }

            // At this point, just returns null;
            return type;
        }

        private static Type ConstructGenericType(string assemblyQualifiedName, bool throwOnError = false, params Assembly[] referencedAssemblies)
        {
            /// Modified the functionality of the regex and type resolution logic when handling cases like:
            ///     1: an assembly-qualified generic type
            ///         A: with only normal type arguments
            ///         B: with only assembly-qualified type arguments
            ///         C: with a mixture of both normal and assembly-qualified type arguments
            ///     2: a generic type
            ///         A: with only normal type arguments
            ///         B: with only assembly-qualified type arguments
            ///         C: with a mixture of both normal and assembly-qualified type arguments
            ///         
            ///     I think it's possible to have a type with normal and assembly-qualified arguments, but I'm not sure.
            ///     I'm also not skilled enough to develop test cases for each of the scenarios addressed here.
            ///     Reference: https://docs.microsoft.com/en-us/dotnet/api/system.type.gettype?view=netframework-3.5
            ///

            Match match = GenericTypeRegex.Match(assemblyQualifiedName);

            if (!match.Success)
                return null;

            string typeName = match.Groups["name"].Value.Trim();
            string typeArguments = match.Groups["subtypes"].Value.Trim();

            // If greater than 0, this is a generic type with this many type arguments.
            int numberOfTypeArguments = -1;
            if (!string.IsNullOrEmpty(match.Groups["count"].Value.Trim()))
            {
                try
                {
                    numberOfTypeArguments = int.Parse(match.Groups["count"].Value.Trim());
                }
                catch { };
            }

            // I guess this attempts to get the default type for a type of typeName for a given numberOfTypeArguments.
            // Seems to work on commonly configured.
            if (numberOfTypeArguments >= 0)
                typeName = typeName + $"`{numberOfTypeArguments}";

            Type genericType = ReconstructType(typeName, throwOnError, referencedAssemblies);
            if (genericType == null)
                return null;

            //List<string> typeNames = new List<string>();
            List<Type> TypeList = new List<Type>();

            int StartOfArgument = 0;
            int offset = 0;
            while (offset < typeArguments.Length)
            {
                // All type arguments are separated by commas.
                // Parsing would be easy, except square brackets introduce scoping.

                // If a left square bracket is encountered, start parsing until the matching right bracket is reached.
                if (typeArguments[offset] == '[')
                {
                    int end = offset;
                    int level = 0;
                    do
                    {
                        switch (typeArguments[end++])
                        {
                            // If the next character is a left square bracket, the beginning of another bracket pair was encountered.
                            case '[':
                                level++;
                                break;

                            // Else if it's a right bracket, the end of a bracket pair was encountered.
                            case ']':
                                level--;
                                break;
                        }
                    } while (level > 0 && end < typeArguments.Length);

                    // 'offset' is still the index of the encountered left square bracket.
                    // 'end' is now the index of the closing right square bracket.
                    // 'level' should be back at zero (meaning all left brackets had closing right brackets). Else there was a formatting error.
                    if (level == 0)
                    {
                        // Adding 1 to the offset and subtracting two from the substring length will get a substring without the brackets.
                        // Check that the substring length, sans the enclosing brackets, would result in a non-empty string.
                        if ((end - offset - 2) > 0)
                        {
                            // If the start of the first type argument was the left square bracket, this argument is an assembly-qualified type.
                            //  Example:    MyGenericType`1[[MyType,MyAssembly]]
                            if (StartOfArgument == offset)
                            {
                                try
                                {
                                    TypeList.Add(ReconstructType(typeArguments.Substring(offset + 1, end - offset - 2).Trim(), throwOnError, referencedAssemblies));
                                }
                                catch
                                {
                                    return null;
                                }
                            }

                            // Else a square bracket was encountered on a generic type argument.
                            //  Example:    MyGenericType`1[AnotherGenericType`2[MyType,AnotherType]]
                            else
                            {
                                try
                                {
                                    TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, end - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                                }
                                catch
                                {
                                    return null;
                                }
                            }
                        }
                    }

                    // Set the offset and StartOfArgument to the position of the discovered right square bracket (or the end of the string).
                    offset = end;
                    StartOfArgument = offset;

                    // Decrement the number of type arguments 
                    numberOfTypeArguments--;
                }

                // Else if a comma is encountered without hitting a left square bracket, a normal type argument was encountered.
                // I don't know if this will ever happen because these types should always be resolvable, I think.
                else if (typeArguments[offset] == ',')
                {
                    if ((offset - StartOfArgument) > 0)
                    {
                        try
                        {
                            TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, offset - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                        }
                        catch
                        {
                            return null;
                        }
                    }

                    offset++;
                    StartOfArgument = offset;
                }

                // Essentially adds the character at this offset to any substring produced with the StartOfArgument offset.
                else
                    offset++;
            }

            // 'offset' is out-of-bounds. 'StartOfArgument' may be out-of-bounds. 
            // 'offset-1' should be in-bounds, and if it's greater than 'StartOfArgument', there should be one last type argument to create.
            if ((offset - 1) > StartOfArgument)
            {
                try
                {
                    TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, offset - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                }
                catch
                {
                    return null;
                }
            }

            // "Should never happen" --original StackOverflow author
            // This should only happen if the number of type arguments supplied in the type string doesn't match with the number of supplied arguments.
            // If it's less than 0, 
            if (numberOfTypeArguments > 0)
                return null;

            try
            {
                return genericType.MakeGenericType(TypeList.ToArray());
            }
            catch
            {
                return null;
            }
        }
    }

    // https://docs.microsoft.com/en-us/dotnet/api/system.appdomain?view=netframework-3.5

    /// <summary>
    /// Proxy interface for AssmeblyLoader.
    /// </summary>
    public interface IAssemblySandbox
    {
        void Load(string name, byte[] bytes);
        byte[] ExecuteMethod(string assemblyName, string typeName, string methodName, string[] ConstructorTypes = null, object[] ConstructorParameters = null, string[] MethodTypes = null, object[] MethodParameters = null);
        bool ExecuteMethodAndStoreResults(string assemblyName, string assemblyQualifiedTypeName, string methodName, string variableName, string[] ConstructorTypes = null, object[] ConstructorParameters = null, string[] MethodTypes = null, object[] MethodParameters = null);
        bool ExecuteMethodOnVariable(string methodName, string targetVariableName, string returnVariableName, string[] MethodTypes = null, object[] MethodParameters = null);
        bool ConstructNewObject(string assemblyQualifiedTypeName, string variableName, string[] ConstructorTypes = null, object[] ConstructorParameters = null);
        bool SetVariable(string variableName, string assemblyQualifiedTypeName = "", byte[] serializedObject = null);
        byte[] GetVariable(string variableName);
        string GetVariableInfo(string variableName = "");
    }

    public class AssemblySandbox : MarshalByRefObject, IAssemblySandbox
    {
        /// Handles the loading and execution of in-memory Assemblies inside of a new AppDomain.
        /// 
        /// Inside this AppDomain, once the Assemblies are loaded, all Types are assumed to make sense.
        /// Any types defined within the loaded Assemblies will resolve within this AppDomain's context.
        /// In order to communicate between AppDomains, a proxy interface is required - the IAssemblySandbox.
        /// When communication happens (i.e. when calling a proxied function w/ params and receiving a return value),
        /// both parameters and return objects are serialized when passed. To prevent issues when passing
        /// objects with custom, unknown types, the return objects are serialized BEFORE returning, with
        /// the expectation that deserialization will occur somewhere where those types can be resolved.
        /// The problem with most other solutions for dynamically loading Assemblies into different AppDomains
        /// is that they all assume the loaded Assembly is a local, and therefore automatically resolvable, resource.
        /// All dependencies must be loaded into this domain - nothing is automatic.
        /// 
        /// TODO: Continue doing research on this and check out: 
        ///     https://github.com/jduv/AppDomainToolkit
        ///     
        /// Other References:
        ///     https://stackoverflow.com/questions/50127992/appdomain-assembly-not-found-when-loaded-from-byte-array

        // TODO: Figure out exactly how/where serlialization/deserialization takes place and add a custom binder.
        //          Maybe use this as a reference: https://github.com/jduv/AppDomainToolkit


        /// <summary>
        /// Keeps track of specific Assemblies we've loaded so we can find the specific methods in the specific Assemblies loaded.
        /// </summary>
        private Dictionary<string, Assembly> AssemblyMap = new Dictionary<string, Assembly>();

        /// <summary>
        /// Mapping of variable names to objects of types that only this AppDomain can describe. 
        /// </summary>
        private Dictionary<string, VariableTuple> Variables = new Dictionary<string, VariableTuple>();

        /// <summary>
        /// Regex used by ReconstructType/ConstructGenericType to recursively recognize and reconstruct generic Types.
        /// </summary>
        public static Regex GenericTypeRegex = new Regex(@"^(?<name>[\w\+]+(\.[\w|\+]+)*)(\&*)(\**)(`(?<count>\d))?(\[(?<subtypes>.*?)\])(,\s*(?<assembly>[\w\+]+(\.[\w|\+]+)*).*?)?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        /// <summary>
        /// Regex used by ReconstructType/ConstructGenericType to recursively recognize and reconstruct generic Types.
        /// </summary>
        public static Regex LocalVariableRegex = new Regex(@"^Local\.Variable\.(?<VariableName>.+)", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);



        /// <summary>
        /// <para>Checks the supplied assemblyMap dictionary for an Assembly matching the supplied assemblyName.</para>
        /// <para>If it's not there, checks all Assemblies loaded in the current AppDomain and returns the first match.</para>
        /// <para>If it's not there, reconstruct a Type based off of the object/method's Assembly qualified Type name, and then return its Assembly.</para>
        /// <para>Else, returns null. All checks are case-insensitive (all values are lowered; consider removing this in the future).</para>
        /// </summary>
        /// <param name="assemblyMap">Mapping of user-defined names to Assemblies loaded by Load method.</param>
        /// <param name="assemblyName">User-defined name to check against assemblyMap and the Assemblies loaded in the current AppDomain.</param>
        /// <param name="assemblyQualifiedTypeName">Assembly qualified Type name of a method</param>
        /// <returns>Assembly object, if a match was found for the supplied name. Else, null.</returns>
        private static Assembly GetAssembly(Dictionary<string, Assembly> assemblyMap = null, string assemblyName = "", string assemblyQualifiedTypeName = "")
        {
            try
            {
                // If a specific Assembly name was supplied and exists in the assemblyMap dict, use that specific Assembly.
                if (assemblyMap != null && !string.IsNullOrEmpty(assemblyName) && assemblyMap.ContainsKey(assemblyName.ToLower()))
                    return assemblyMap[assemblyName.ToLower()];

                // Else if the supplied Assembly name is in the AppDomain's loaded assemblies, use that one. (case-insensitive)
                else if (!string.IsNullOrEmpty(assemblyName) && AppDomain.CurrentDomain.GetAssemblies().Where(x => x.FullName.ToLower() == assemblyName.ToLower()).Count() > 0)
                    return AppDomain.CurrentDomain.GetAssemblies().First(x => x.FullName.ToLower() == assemblyName.ToLower());

                // Else if the Assembly can be found by resolving the specified type name, resolve the type name and get its Assembly.
                else if (!string.IsNullOrEmpty(assemblyQualifiedTypeName) && ReconstructType(assemblyQualifiedTypeName) != null)
                    return ReconstructType(assemblyQualifiedTypeName).Assembly;

                // Else we're out of ideas...
                else
                    return null;       
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Gets the Type object with the specified name in the Assembly instance". Wrapped method to ignore case and prevent throwing an error.
        /// </summary>
        /// <param name="assembly">Target Assembly instance to get the Type from.</param>
        /// <param name="typeName">"The full name of the type".</param>
        /// <returns>If successful, "An object that represents the specified class". Else, null.</returns>
        private static Type GetAssemblyType(Assembly assembly, string typeName)
        {
            try
            {
                return assembly.GetType(typeName, false, true);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Searches for the specified public method whose parameters match the specified argument types". Wrapped for our pleasure.
        /// </summary>
        /// <param name="assemblyType">Type from the target Assembly, where the desired method exists.</param>
        /// <param name="methodName">"The string containing the name of the public method to get."</param>
        /// <param name="methodParameterTypes">"An array of Type objects representing the number, order, and type of the parameters for the method to get."</param>
        /// <returns>"An object representing the public method whose parameters match the specified argument types, if found; otherwise, null."</returns>
        private static MethodInfo GetMethodInfo(Type assemblyType, string methodName, Type[] methodParameterTypes = null)
        {
            try
            {
                if (methodParameterTypes == null)
                    methodParameterTypes = new Type[] { };

                return assemblyType.GetMethod(methodName, methodParameterTypes);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Searches for a public instance constructor whose parameters match the types in the specified array". Wrapped for our pleasure.
        /// </summary>
        /// <param name="constructorType">Type of Constructor to get the ConstructorInfo object for.</param>
        /// <param name="constructorParameterTypes">"An array of Type objects representing the number, order, and type of the parameters for the desired constructor." Can be empty.</param>
        /// <returns>"An object representing the public instance constructor whose parameters match the types in the parameter type array, if found; otherwise, null."</returns>
        private static ConstructorInfo GetConstructorInfo(Type constructorType, Type[] constructorParameterTypes = null)
        {
            try
            {
                return constructorType.GetConstructor(constructorParameterTypes);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "Searches for a public instance constructor whose parameters match the types in the specified array", but derived from a supplied method, instead of a class.
        /// </summary>
        /// <param name="methodInfoObject">MethodInfo Object for the desired non-static method.</param>
        /// <param name="constructorTypes">Array of Types used to find the desired constructor.</param>
        /// <returns>"An object representing the public instance constructor whose parameters match the types in the parameter type array, if found; otherwise, null." Also returns null if method is static.</returns>
        private static ConstructorInfo GetConstructorInfo(MethodInfo methodInfoObject, Type[] constructorTypes = null)
        {
            try
            {
                // If the method is static, there is no constructor; return null.
                if(methodInfoObject.IsStatic)
                    return null;

                if (constructorTypes == null)
                    constructorTypes = new Type[] { };

                // DeclaringType => "Gets the type that declares the current nested type or generic type parameter."
                return methodInfoObject.DeclaringType.GetConstructor(constructorTypes);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Constructs an instance of a non-static class for the supplied methodInfoObject, given the constructor Types and parameters.
        /// </summary>
        /// <param name="methodInfoObject">MethodInfo Object of the desired method that needs instantiation.</param>
        /// <param name="constructorTypes">Type-Array defining which constructor will be used and what all of the parameters are.</param>
        /// <param name="constructorParameters">Object-Array containing parameters for the constructor.</param>
        /// <returns>An instance of the class that the method should be invoked on.</returns>
        private static object GetConstructedClassObject(MethodInfo methodInfoObject, Type[] constructorTypes = null, object[] constructorParameters = null)
        {
            try
            {
                if (methodInfoObject.IsStatic)
                    return null;

                if (constructorTypes == null)
                    constructorTypes = new Type[] { };

                if (constructorParameters == null)
                    constructorParameters = new object[] { };

                return GetConstructorInfo(methodInfoObject, constructorTypes).Invoke(constructorParameters);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Combines ordered arrays of Assembly qualified Type names and objects into a list of VariableTuple-joined values.
        /// Resolves string representations of Types into their actual Types, and converts references to locally stored variables
        /// into the stored VariableTuple objects.
        /// </summary>
        /// <param name="localVariables">Mapping of user-defined variable names to VariableTuple objects.</param>
        /// <param name="typeStrings">Array of Assembly qualified type names as strings.</param>
        /// <param name="objects">Array of objects described by and matching the order of the Types in typeStrings.</param>
        /// <param name="newParameters">Returned list of VariableTuple objects parsed from the supplied Types and objects.</param>
        /// <returns>True if the supplied Types and objects were successfully resolved and combined into VariableTuples. Else, False.</returns>
        private static bool ZipParameters(Dictionary<string, VariableTuple> localVariables, string[] typeStrings, object[] objects, out List<VariableTuple> newParameters)
        {
            newParameters = new List<VariableTuple>();

            // If both are null, no parameters were specified - there is no error; return true.
            if (typeStrings == null && objects == null)
                return true;

            // Else if only one is null, there was a msimatch in supplied parameter data; return false.
            if (typeStrings == null || objects == null)
                return false;

            // Else if they're not null and their counts aren't the same, there was a msimatch in supplied parameter data; return false.
            if (typeStrings.Count() != objects.Count())
                return false;

            try
            {    
                for(int index = 0; index < typeStrings.Count(); index++)
                {
                    // Check to see if this variable is stored locally, so we can retrieve the type from the Variables dict.
                    Match matchObject = LocalVariableRegex.Match(typeStrings[index]);
                    if (matchObject.Success && localVariables.ContainsKey(matchObject.Groups["VariableName"].Value))
                        newParameters.Add(localVariables[matchObject.Groups["VariableName"].Value]);                   
                    
                    // Else, it's not local and we'll try to resolve the string into an actual type.
                    else
                        newParameters.Add(new VariableTuple(index.ToString(), ReconstructType(typeStrings[index]), objects[index]));
                }

                // Successfully made it to the end; return true.
                return true;
            }
            catch
            {
                // Caught an unknowable exception; return false.
                return false;
            }
        }


        
        /// <summary>
        /// Invokes the supplied method.
        /// </summary>
        /// <param name="assemblyName">Name of the assembly containing the method. 
        ///     <para>This is either the user-defined name of an Assembly loaded by the Load method or the FullName of an Assembly in this AppDomain (e.g. "mscorlib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=actual_publicKeyToken_goes_here", if attempting to do System.String.Join(...)).</para>
        ///     <para>Though this parameter is mandatory, it does not need to be correct or identify a real Assembly, so long as the assemblyQualifiedTypeName does lead to the resolution of an Assembly.</para>
        /// </param>
        /// <param name="assemblyQualifiedTypeName">Assembly qualified Type name where the method exists (e.g. "System.String", if attempting to do System.String.Join(...)).</param>
        /// <param name="methodName">Name of the method to invoke (e.g. "Join", if attempting to do System.String.Join(...)).</param>
        /// <param name="constructorTypes">Ordered array of Type name strings, defining which constructor is used and what the constructorParameters are (if this is a non-static method).</param>
        /// <param name="constructorParameters">Ordered array of Objects, supplied as parameters when constructing an instance (if this is a non-static method).</param>
        /// <param name="methodTypes">Ordered array of Type name strings, defining which method is used and what the methodParameters are.</param>
        /// <param name="methodParameters">Ordered array of Objects, supplied as parameters to the method.</param>
        /// <returns>The serialized return value (if anything returned after successful execution). Else, an empty byte-array.</returns>
        public byte[] ExecuteMethod(string assemblyName, string assemblyQualifiedTypeName, string methodName, string[] constructorTypes = null, object[] constructorParameters = null, string[] methodTypes = null, object[] methodParameters = null)
        {
            List<VariableTuple> zippedConstructorParameters;
            List<VariableTuple> zippedMethodParameters;

            ZipParameters(Variables, constructorTypes, constructorParameters, out zippedConstructorParameters);
            ZipParameters(Variables, methodTypes, methodParameters, out zippedMethodParameters);

            // Both the *Types and *Parameters arrays need all types specified (even for optional parameters), using null for any non-specified *Parameters values.
            // Get the exact Assembly Load'ed in the AssemblyMap, based on the supplied name.
            Assembly CurrentAssembly = GetAssembly(AssemblyMap, assemblyName, assemblyQualifiedTypeName);
            if (CurrentAssembly == null)
                return new byte[] { };

            // Get the specified Type from the supplied Assembly.
            Type CurrentType = GetAssemblyType(CurrentAssembly, assemblyQualifiedTypeName);
            if (CurrentType == null)
                return new byte[] { };
            
            MethodInfo MethodInfoObject = GetMethodInfo(CurrentType, methodName, zippedMethodParameters.Select(x => x.Type).ToArray());
            if (MethodInfoObject == null)
                return new byte[] { };
            
            object ConstructedClassObject = GetConstructedClassObject(MethodInfoObject, zippedMethodParameters.Select(x => x.Type).ToArray(), zippedMethodParameters.Select(x => x.Instance).ToArray());
            if (MethodInfoObject.IsStatic == false && ConstructedClassObject == null)
                return new byte[] { };

            // Serialize the return value to prevent the proxy/appdomain from attempting to serialize/deserialize types it doesn't understand.
            try
            {
                return Serialize(MethodInfoObject.Invoke(ConstructedClassObject, zippedMethodParameters.Select(x => x.Instance).ToArray()));
            }
            catch
            {
                return new byte[] { };
            }
        }

        /// <summary>
        /// Invokes the supplied method and stores the results as a local variable.
        /// </summary>
        /// <param name="assemblyName">Name of the assembly containing the method. 
        ///     <para>This is either the user-defined name of an Assembly loaded by the Load method or the FullName of an Assembly in this AppDomain (e.g. "mscorlib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=actual_publicKeyToken_goes_here", if attempting to do System.String.Join(...)).</para>
        ///     <para>Though this parameter is mandatory, it does not need to be correct or identify a real Assembly, so long as the assemblyQualifiedTypeName does lead to the resolution of an Assembly.</para>
        /// </param>
        /// <param name="assemblyQualifiedTypeName">Assembly qualified Type name where the method exists (e.g. "System.String", if attempting to do System.String.Join(...)).</param>
        /// <param name="methodName">Name of the method to invoke (e.g. "Join", if attempting to do System.String.Join(...)).</param>
        /// <param name="variableName">Name to store any results under, in the local Variables dictionary.</param>
        /// <param name="constructorTypes">Ordered array of Type name strings, defining which constructor is used and what the constructorParameters are (if this is a non-static method).</param>
        /// <param name="constructorParameters">Ordered array of Objects, supplied as parameters when constructing an instance (if this is a non-static method).</param>
        /// <param name="methodTypes">Ordered array of Type name strings, defining which method is used and what the methodParameters are.</param>
        /// <param name="methodParameters">Ordered array of Objects, supplied as parameters to the method.</param>
        /// <returns>True if the method was successfully executed and the results were successfully stored. Else, False.</returns>
        public bool ExecuteMethodAndStoreResults(string assemblyName, string assemblyQualifiedTypeName, string methodName, string variableName, string[] constructorTypes = null, object[] constructorParameters = null, string[] methodTypes = null, object[] methodParameters = null)
        {
            List<VariableTuple> zippedConstructorParameters;
            List<VariableTuple> zippedMethodParameters;

            ZipParameters(Variables, constructorTypes, constructorParameters, out zippedConstructorParameters);
            ZipParameters(Variables, methodTypes, methodParameters, out zippedMethodParameters);
            
            // Both the *Types and *Parameters arrays need all types specified (even for optional parameters), using null for any non-specified *Parameters values.
            // Get the exact Assembly Load'ed in the AssemblyMap, based on the supplied name.
            Assembly CurrentAssembly = GetAssembly(AssemblyMap, assemblyName, assemblyQualifiedTypeName);
            if (CurrentAssembly == null)
                return false;

            // Get the specified Type from the supplied Assembly.
            Type CurrentType = GetAssemblyType(CurrentAssembly, assemblyQualifiedTypeName);
            if (CurrentType == null)
                return false;

            MethodInfo MethodInfoObject = GetMethodInfo(CurrentType, methodName, zippedMethodParameters.Select(x => x.Type).ToArray());
            if (MethodInfoObject == null)
                return false;

            object ConstructedClassObject = GetConstructedClassObject(MethodInfoObject, zippedMethodParameters.Select(x => x.Type).ToArray(), zippedMethodParameters.Select(x => x.Instance).ToArray());
            if (MethodInfoObject.IsStatic == false && ConstructedClassObject == null)
                return false;

            // Serialize the return value to prevent the proxy/appdomain from attempting to serialize/deserialize types it doesn't understand.
            try
            {
                Variables[variableName] = new VariableTuple(variableName, MethodInfoObject.ReturnType, MethodInfoObject.Invoke(ConstructedClassObject, zippedMethodParameters.Select(x => x.Instance).ToArray()));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a new instance of the Object specified by the Assembly qualified Type name, and stores it in the local Variables dictionary, using the supplied variable name.
        /// </summary>
        /// <param name="assemblyQualifiedTypeName">Assembly qualified Type name of the Object to create.</param>
        /// <param name="variableName">User-defined name to store/lookup the newly created object in the Variables dictionary.</param>
        /// <param name="constructorTypes">Ordered array of Type name strings, defining which constructor is used and what the constructorParameters are.</param>
        /// <param name="constructorParameters">Ordered array of Objects, supplied as parameters when constructing an instance.</param>
        /// <returns>True if a new instance was successfully created and stored in the local Variables dictionary. Else, False.</returns>
        public bool ConstructNewObject(string assemblyQualifiedTypeName, string variableName, string[] constructorTypes = null, object[] constructorParameters = null)
        {
            List<VariableTuple> zippedConstructorParameters;
            ZipParameters(Variables, constructorTypes, constructorParameters, out zippedConstructorParameters);

            // Get the specified Type from the supplied Assembly.
            Type ConstructorType = ReconstructType(assemblyQualifiedTypeName);
            if (ConstructorType == null)
                return false;

            ConstructorInfo constructorInfo = GetConstructorInfo(ConstructorType, zippedConstructorParameters.Select(x => x.Type).ToArray());
            if (constructorInfo == null)
                return false;
            
            try
            {
                Variables[variableName] = new VariableTuple(variableName, ConstructorType, constructorInfo.Invoke(zippedConstructorParameters.Select(x => x.Instance).ToArray()));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Invokes the supplied method on the specified local variable Object, and stores the results in a local variable.
        /// </summary>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="targetVariableName">Name of the variable in the Variables dictionary to execute the method on.</param>
        /// <param name="returnVariableName">Name to store any return results as in the Variables dictionary.</param>
        /// <param name="methodTypes">Ordered array of Type name strings, defining which method is used and what the methodParameters are.</param>
        /// <param name="methodParameters">Ordered array of Objects, supplied as parameters to the method.</param>
        /// <returns>True if the method was successfully executed on the variable. Else, False.</returns>
        public bool ExecuteMethodOnVariable(string methodName, string targetVariableName, string returnVariableName, string[] methodTypes = null, object[] methodParameters = null)
        {
            List<VariableTuple> zippedMethodParameters;
            ZipParameters(Variables, methodTypes, methodParameters, out zippedMethodParameters);

            if (!Variables.ContainsKey(targetVariableName))
                return false;

            if (Variables[targetVariableName] == null)
                return false;

            MethodInfo MethodInfoObject = GetMethodInfo(Variables[targetVariableName].Type, methodName, zippedMethodParameters.Select(x => x.Type).ToArray());
            if (MethodInfoObject == null)
                return false;

            // Serialize the return value to prevent the proxy/appdomain from attempting to serialize/deserialize types it doesn't understand.
            try
            {
                Variables[returnVariableName] = new VariableTuple(targetVariableName, MethodInfoObject.ReturnType, MethodInfoObject.Invoke(Variables[targetVariableName].Instance, zippedMethodParameters.Select(x => x.Instance).ToArray()));
                return true;
            }
            catch
            {
                return false;
            }
        }



        /// <summary>
        /// Loads an assembly into the current AppDomain, and stores its representation in the AssemblyMap dictionary.
        /// </summary>
        /// <param name="name">User-defined name to store/access the Assembly representation in the AssemblyMap dictionary.</param>
        /// <param name="bytes">Byte-array of the .NET Assembly to load into the current AppDomain.</param>
        public void Load(string name, byte[] bytes)
        {
            //Assembly[] AssembliesBeforeLoad = AppDomain.CurrentDomain.GetAssemblies();

            AssemblyMap[name.ToLower()] = AppDomain.CurrentDomain.Load(bytes);

            //Assembly[] AssembliesAfterLoad = AppDomain.CurrentDomain.GetAssemblies();
            return;
        }

        /// <summary>
        /// Stores an Object in the local Variables dictionary for further interaction.
        /// <para>If a serialized Object is supplied, the Type from the deserialized object is used; discarding the assemblyQualifiedTypeName.</para>
        /// </summary>
        /// <remarks>
        /// Allows other AppDomains to pass objects into this AppDomain without understanding the Types involved.
        /// What they push in is simply understood as a byte-array.
        /// </remarks>
        /// <param name="variableName">User-defined name used to access the variable.</param>
        /// <param name="assemblyQualifiedTypeName">Assembly qualified Type name to use if (de)serialized Object is null.</param>
        /// <param name="serializedObject">Serialized Object to store in the local Variables dictionary.</param>
        /// <returns>True if the Variable was successfully stored. Else, False.</returns>
        public bool SetVariable(string variableName, string assemblyQualifiedTypeName = "", byte[] serializedObject = null)
        {
            object DeserializedObject = Deserialize(serializedObject);
            if (DeserializedObject == null)
            {
                if (string.IsNullOrEmpty(assemblyQualifiedTypeName))
                    return false;

                try
                {
                    Variables[variableName] = new VariableTuple(variableName, ReconstructType(assemblyQualifiedTypeName), null);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                try
                {
                    Variables[variableName] = new VariableTuple(variableName, DeserializedObject.GetType(), DeserializedObject);   
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets a serialized value from the local Variables dictionary, using the specified variable name.
        /// </summary>
        /// <remarks>
        /// Allows other AppDomains to request objects from this AppDomain without understanding the Types involved.
        /// What they get back is simply understood as a byte-array.
        /// </remarks>
        /// <param name="variableName">Name of the variable to retrieve from the Variables dictionary</param>
        /// <returns>If Variables contains the supplied variableName, the serialized object specified. Else, null.</returns>
        public byte[] GetVariable(string variableName)
        {
            try
            {
                return Serialize(Variables[variableName].Instance);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates a list of Variables with their types and whether or not their values are null. Mainly for debugging purposes; consider removing.
        /// </summary>
        /// <param name="variableName">The user-defined name of a specific variable in the Variables dictionary.</param>
        /// <returns>A formatted string containing the variable name, Type, and whether or not the variable is null. If no variableName was supplied, information is returned for all variables.</returns>
        public string GetVariableInfo(string variableName = "")
        {
            try
            {
                if (string.IsNullOrEmpty(variableName))
                    return string.Join("\r\n", Variables.Values.Select(x => string.Format("{0} ({1}) [IsNull:{2}]", x.Name, x.Type, x.Instance == null)).ToArray());

                if (Variables.ContainsKey(variableName))
                    return string.Format("{0} ({1}) [IsNull:{2}]", variableName, Variables[variableName].Type, Variables[variableName].Instance == null);
            }
            catch { }
            return "";
        }



        /// <summary>
        /// Serializes an Object, of a Type understood by this AppDomain, into a byte-array.
        /// </summary>
        /// <param name="objectToSerialize">An Object of a Type that this AppDomain is aware of.</param>
        /// <returns>If serialization occurs successfully, a byte array representing the Object is returned. Else, null.</returns>
        private static byte[] Serialize(object objectToSerialize)
        {
            try
            {
                BinaryFormatter serializer = new BinaryFormatter();
                using (var memoryStream = new MemoryStream())
                {
                    serializer.Serialize(memoryStream, objectToSerialize);
                    return memoryStream.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deserializes a byte-array into an Object of a Type this AppDomain is aware of.
        /// </summary>
        /// <param name="byteArray">A serialized Object of a Type this APpDomain is aware of.</param>
        /// <returns>If deserialization occurs successfully, an Object is returned. Else, null.</returns>
        private static object Deserialize(byte[] byteArray)
        {
            try
            {
                BinaryFormatter binForm = new BinaryFormatter
                {
                    Binder = new BindChanger()
                };
                using (var memoryStream = new MemoryStream())
                {
                    memoryStream.Write(byteArray, 0, byteArray.Length);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    return binForm.Deserialize(memoryStream);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Custom binder that overrides BindToType, for custom Type resolution functions.
        /// </summary>
        private class BindChanger : System.Runtime.Serialization.SerializationBinder
        {
            /// <summary>
            /// "Controls the binding of a serialized object to a type." Uses custom Type resolution functions.
            /// </summary>
            /// <param name="assemblyName"> "Specifies the Assembly name of the serialized object."</param>
            /// <param name="typeName">"Specifies the Type name of the serialized object."</param>
            /// <returns>"The type of the object the formatter creates a new instance of." If Type is successfully resolved from the supplied strings, the resolved Type is returned; Else, null.</returns>
            public override Type BindToType(string assemblyName, string typeName)
            {
                return ReconstructType(string.Format("{0}, {1}", typeName, assemblyName), false);
            }
        }

        /// <summary>
        /// Resolves Type names into the actual types based on the Assemblies supplied or loaded into this AppDomain - Turns strings into Types.
        /// </summary>
        /// <param name="assemblyQualifiedTypeName">The Assembly qualified Type name (prefferred) to get a Type for. Can also be a non-Assembly-qualified-Type-name, though resolution accuracy may be affected.</param>
        /// <param name="throwOnError">Whether or not an Exception should be thrown on error. Defaults to False.</param>
        /// <param name="referencedAssemblies">Specific array of Assemblies that should be used to resolve Types.</param>
        /// <returns>If successful, a resolved Type; Else, null.</returns>
        private static Type ReconstructType(string assemblyQualifiedTypeName, bool throwOnError = false, params Assembly[] referencedAssemblies)
        {
            // TODO: Consider not passing referencedAssemblies or only passing it through one layer of resolution.

            Type type = null;
            
            // If no assemblies were provided, then there wasn't an attempt to reconstruct the type from a specific assembly.
            // Check if the current app domain can be used to resolve the requested type (this should be 99% of calls for resolution).
            if (referencedAssemblies.Count() == 0)
            {
                type = Type.GetType(assemblyQualifiedTypeName, throwOnError);
                if (type != null)
                    return type;

                // If it made it here, populate an array of assemblies in the current app domain.
                referencedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            }

            // If that failed, attempt to resolve the type from the list of supplied assemblies or those in the current app domain.
            foreach (Assembly asm in referencedAssemblies)
            {
                type = asm.GetType(assemblyQualifiedTypeName.Replace($", {asm.FullName}", ""), throwOnError);
                if (type != null)
                    return type;
            }

            // If that failed and the type looks like a generic type with assembly qualified type arguments, proceed with constructing a generic type.
            Match match = GenericTypeRegex.Match(assemblyQualifiedTypeName);
            if (match.Success && !string.IsNullOrEmpty(match.Groups["count"].Value))
            {
                type = ConstructGenericType(assemblyQualifiedTypeName, throwOnError);
                if (type != null)
                    return type;
            }

            // At this point, just returns null;
            return type;
        }

        /// <summary>
        /// Recursively parses and resolves generic Types based on the Assemblies supplied or loaded into this AppDomain - Turns strings into Types.
        /// </summary>
        /// <param name="assemblyQualifiedName">The Assembly qualified Type name (prefferred) to get a Type for. Can also be a non-Assembly-qualified-Type-name, though resolution accuracy may be affected.</param>
        /// <param name="throwOnError">Whether or not an Exception should be thrown on error. Defaults to False.</param>
        /// <param name="referencedAssemblies">Specific array of Assemblies that should be used to resolve Types.</param>
        /// <returns>If successful, a resolved Type; Else, null.</returns>
        private static Type ConstructGenericType(string assemblyQualifiedName, bool throwOnError = false, params Assembly[] referencedAssemblies)
        {
            /// Modified the functionality of the regex and type resolution logic when handling cases like:
            ///     1: an assembly-qualified generic type
            ///         A: with only normal type arguments
            ///         B: with only assembly-qualified type arguments
            ///         C: with a mixture of both normal and assembly-qualified type arguments
            ///     2: a generic type
            ///         A: with only normal type arguments
            ///         B: with only assembly-qualified type arguments
            ///         C: with a mixture of both normal and assembly-qualified type arguments
            ///         
            ///     I think it's possible to have a type with normal and assembly-qualified arguments, but I'm not sure.
            ///     I'm also not skilled enough to develop test cases for each of the scenarios addressed here.
            ///     Reference: https://docs.microsoft.com/en-us/dotnet/api/system.type.gettype?view=netframework-3.5
            ///

            // TODO: Spend more time cleaning this up.

            Match match = GenericTypeRegex.Match(assemblyQualifiedName);

            if (!match.Success)
                return null;

            string typeName = match.Groups["name"].Value.Trim();
            string typeArguments = match.Groups["subtypes"].Value.Trim();

            // If greater than 0, this is a generic type with this many type arguments.
            int numberOfTypeArguments = -1;
            if (!string.IsNullOrEmpty(match.Groups["count"].Value.Trim()))
            {
                try
                {
                    numberOfTypeArguments = int.Parse(match.Groups["count"].Value.Trim());
                }
                catch { };
            }

            // I guess this attempts to get the default type for a type of typeName for a given numberOfTypeArguments.
            // Seems to work on commonly configured.
            if (numberOfTypeArguments >= 0)
                typeName = typeName + $"`{numberOfTypeArguments}";

            Type genericType = ReconstructType(typeName, throwOnError, referencedAssemblies);
            if (genericType == null)
                return null;

            //List<string> typeNames = new List<string>();
            List<Type> TypeList = new List<Type>();

            int StartOfArgument = 0;
            int offset = 0;
            while (offset < typeArguments.Length)
            {
                // All type arguments are separated by commas.
                // Parsing would be easy, except square brackets introduce scoping.

                // If a left square bracket is encountered, start parsing until the matching right bracket is reached.
                if (typeArguments[offset] == '[')
                {
                    int end = offset;
                    int level = 0;
                    do
                    {
                        switch (typeArguments[end++])
                        {
                            // If the next character is a left square bracket, the beginning of another bracket pair was encountered.
                            case '[':
                                level++;
                                break;

                            // Else if it's a right bracket, the end of a bracket pair was encountered.
                            case ']':
                                level--;
                                break;
                        }
                    } while (level > 0 && end < typeArguments.Length);

                    // 'offset' is still the index of the encountered left square bracket.
                    // 'end' is now the index of the closing right square bracket.
                    // 'level' should be back at zero (meaning all left brackets had closing right brackets). Else there was a formatting error.
                    if (level == 0)
                    {
                        // Adding 1 to the offset and subtracting two from the substring length will get a substring without the brackets.
                        // Check that the substring length, sans the enclosing brackets, would result in a non-empty string.
                        if ((end - offset - 2) > 0)
                        {
                            // If the start of the first type argument was the left square bracket, this argument is an assembly-qualified type.
                            //  Example:    MyGenericType`1[[MyType,MyAssembly]]
                            if (StartOfArgument == offset)
                            {
                                try
                                {
                                    TypeList.Add(ReconstructType(typeArguments.Substring(offset + 1, end - offset - 2).Trim(), throwOnError, referencedAssemblies));
                                }
                                catch
                                {
                                    return null;
                                }
                            }

                            // Else a square bracket was encountered on a generic type argument.
                            //  Example:    MyGenericType`1[AnotherGenericType`2[MyType,AnotherType]]
                            else
                            {
                                try
                                {
                                    TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, end - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                                }
                                catch
                                {
                                    return null;
                                }
                            }
                        }
                    }

                    // Set the offset and StartOfArgument to the position of the discovered right square bracket (or the end of the string).
                    offset = end;
                    StartOfArgument = offset;

                    // Decrement the number of type arguments 
                    numberOfTypeArguments--;
                }

                // Else if a comma is encountered without hitting a left square bracket, a normal type argument was encountered.
                // I don't know if this will ever happen because these types should always be resolvable, I think.
                else if (typeArguments[offset] == ',')
                {
                    if ((offset - StartOfArgument) > 0)
                    {
                        try
                        {
                            TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, offset - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                        }
                        catch
                        {
                            return null;
                        }
                    }

                    offset++;
                    StartOfArgument = offset;
                }

                // Essentially adds the character at this offset to any substring produced with the StartOfArgument offset.
                else
                    offset++;
            }

            // 'offset' is out-of-bounds. 'StartOfArgument' may be out-of-bounds. 
            // 'offset-1' should be in-bounds, and if it's greater than 'StartOfArgument', there should be one last type argument to create.
            if ((offset - 1) > StartOfArgument)
            {
                try
                {
                    TypeList.Add(ReconstructType(typeArguments.Substring(StartOfArgument, offset - StartOfArgument).Trim(), throwOnError, referencedAssemblies));
                }
                catch
                {
                    return null;
                }
            }

            // "Should never happen" --original StackOverflow author
            // This should only happen if the number of type arguments supplied in the type string doesn't match with the number of supplied arguments.
            // If it's less than 0, 
            if (numberOfTypeArguments > 0)
                return null;

            try
            {
                return genericType.MakeGenericType(TypeList.ToArray());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A tuple defining a named variable and its properties.
        /// </summary>
        private class VariableTuple
        {
            /// <summary>
            /// Name of the variable.
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// Resolved Type of the variable.
            /// </summary>
            public Type Type { get; private set; }

            /// <summary>
            /// Actual Object stored in this variable.
            /// </summary>
            public object Instance { get; private set; }

            /// <summary>
            /// Constructs a new variable based on the supplied values.
            /// </summary>
            /// <param name="name">Name of the variable.</param>
            /// <param name="type">Resolved Type of the variable.</param>
            /// <param name="instance">Actual Object stored in this variable.</param>
            internal VariableTuple(string name, Type type, object instance)
            {
                Name = name;
                Type = type;
                Instance = instance;
            }
        }

        /// <summary>
        /// Currently unused function for compressing byte-array's into smaller byte-array's. 
        /// </summary>
        /// <param name="data">Byte-array to compress.</param>
        /// <returns>If successful, a compressed byte-array; Else, null.</returns>
        public static byte[] Compress(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                using (MemoryStream output = new MemoryStream())
                {
                    using (DeflateStream compressionStream = new DeflateStream(output, CompressionMode.Compress))
                    {
                        compressionStream.Write(data, 0, data.Length);
                    }
                    return output.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
