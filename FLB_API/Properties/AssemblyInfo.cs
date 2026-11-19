using System.Reflection;
using System.Runtime.InteropServices;

using FLB_API;

[assembly: AssemblyTitle("Fusion Lobby Browser")]
[assembly: AssemblyDescription("The backend responsible for providing the website with lobbies and other data.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
[assembly: AssemblyCompany("Catboys")]
[assembly: AssemblyProduct("Fusion Lobby Browser Backend")]
[assembly: AssemblyCopyright("Copyright © HAHOOS 2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion(Program.Version)]
[assembly: AssemblyFileVersion(Program.Version)]
[assembly: AssemblyInformationalVersion(Program.Version)]