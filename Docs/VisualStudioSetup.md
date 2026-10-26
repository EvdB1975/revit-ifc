# Building the shared-family exporter in Visual Studio

For the **full Autodesk installer**, open **Revit.IFC.sln**, select **Release | x64**,
restore the managed projects' NuGet packages, and rebuild the solution. The installer
is now enabled in that configuration. WiX v3 build tools are required in addition
to the Visual Studio extension. The validated output is
`Release/IFC for Revit 2025.4.4.0.msi`.

From a Visual Studio Developer PowerShell, build the full solution with:

```powershell
MSBuild Revit.IFC.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m
```

WiX 3.14.1 was validated with normal MSI validation enabled. The installer uses
the original Release | x86 package configuration while its managed dependencies
build in x64. Building does not run the MSI.

The solution filter described below remains optional for exporter-only development;
it is not used to build the installer.

Open **Revit.IFC.Export.slnf** at the repository root. This solution filter loads
the exporter and its Common dependency without loading the WiX installer project.
Select **Release | x64** (or Debug | x64), restore NuGet packages, and build.
Use Visual Studio with .NET 8 SDK support and the desktop development workload.

The original projects expect API DLLs at `..\..\..\API\2025` relative to their
project directories. When those HintPaths are missing, the shared build targets
now look in `%ProgramW6432%\Autodesk\Revit 2025`. Existing valid HintPaths retain
precedence. Revit must be installed, or its API assemblies must be supplied in the
expected SDK location; the repository does not include Autodesk's API binaries.

For a custom Revit installation, pass `-p:RevitApiDirectory="D:\Autodesk\Revit 2025"`
to MSBuild, or set the `RevitApiDirectory` environment variable before starting
Visual Studio. The fallback also covers the Autodesk UI DLLs used by the UI project
when opening the full solution.

Command-line exporter build from the repository root:

```powershell
dotnet build Revit.IFC.Export.slnf --configuration Release -p:Platform=x64
```

The full **Revit.IFC.sln** includes `Install/RevitIFCSetupWix/RevitIFCSetupWix.wixproj`,
which imports WiX **v3.x** targets and extensions. Building that installer requires
a compatible WiX v3 toolset and Visual Studio project support. WiX is not needed
to compile or inspect the exporter through the solution filter. The installer
project remains in the legacy WiX v3 format, with its existing payload layout.

The older build command in `SharedFamilyRelationships.md` records the temporary
override used for the original implementation build. The command above uses the
repository's new automatic reference fallback and needs no temporary targets file.
