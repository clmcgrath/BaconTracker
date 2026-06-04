using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

using Path = System.IO.Path;

namespace BaconTracker.Build;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public string Target { get; }
    public string BuildConfig { get; }
    public string OutDir { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        Target = context.Argument("target", "Default");
        BuildConfig = context.Argument("configuration", "Release");
        OutDir = context.Argument("outdir", "./artifacts");
    }
}

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("Cleaning workspace directories...");
        
        string[] projectDirs = {
            "./BaconTracker.App",
            "./BaconTracker.Core",
            "./BaconTracker.PatchGenerator"
        };

        foreach (var proj in projectDirs)
        {
            var binPath = Path.Combine(proj, "bin");
            var objPath = Path.Combine(proj, "obj");

            if (Directory.Exists(binPath))
            {
                try
                {
                    Directory.Delete(binPath, true);
                    context.Information($"Deleted: {binPath}");
                }
                catch (Exception ex)
                {
                    context.Warning($"Could not delete {binPath}: {ex.Message}");
                }
            }

            if (Directory.Exists(objPath))
            {
                try
                {
                    Directory.Delete(objPath, true);
                    context.Information($"Deleted: {objPath}");
                }
                catch (Exception ex)
                {
                    context.Warning($"Could not delete {objPath}: {ex.Message}");
                }
            }
        }

        if (Directory.Exists(context.OutDir))
        {
            try
            {
                Directory.Delete(context.OutDir, true);
            }
            catch {}
        }
        Directory.CreateDirectory(context.OutDir);
    }
}

[TaskName("Publish-Windows")]
[IsDependentOn(typeof(CleanTask))]
public sealed class PublishWindowsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("Publishing application for Windows (win-x64)...");
        var publishDir = Path.Combine(context.OutDir, "win-x64");
        
        context.DotNetPublish("./BaconTracker.App/BaconTracker.App.csproj", new DotNetPublishSettings
        {
            Configuration = context.BuildConfig,
            Runtime = "win-x64",
            SelfContained = true,
            OutputDirectory = publishDir,
            MSBuildSettings = new DotNetMSBuildSettings()
                .WithProperty("PublishSingleFile", "true")
                .WithProperty("PublishTrimmed", "false")
        });

        // Zip output using .NET ZipFile API
        var zipFile = Path.Combine(context.OutDir, "BaconTracker-Windows-x64.zip");
        if (File.Exists(zipFile)) File.Delete(zipFile);
        
        ZipFile.CreateFromDirectory(publishDir, zipFile);
        context.Information($"Successfully packaged Windows build to: {zipFile}");
    }
}

[TaskName("Publish-Linux")]
[IsDependentOn(typeof(CleanTask))]
public sealed class PublishLinuxTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("Publishing application for Linux (linux-x64)...");
        var publishDir = Path.Combine(context.OutDir, "linux-x64");
        
        context.DotNetPublish("./BaconTracker.App/BaconTracker.App.csproj", new DotNetPublishSettings
        {
            Configuration = context.BuildConfig,
            Runtime = "linux-x64",
            SelfContained = true,
            OutputDirectory = publishDir,
            MSBuildSettings = new DotNetMSBuildSettings()
                .WithProperty("PublishSingleFile", "true")
                .WithProperty("PublishTrimmed", "false")
        });

        // Pack Linux tarball
        var tarFile = Path.Combine(context.OutDir, "BaconTracker-Linux-x64.tar.gz");
        if (File.Exists(tarFile)) File.Delete(tarFile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            context.StartProcess("tar", new ProcessSettings
            {
                Arguments = $"-czf {tarFile} -C {publishDir} ."
            });
            context.Information($"Successfully packaged Linux tarball to: {tarFile}");
        }
        else
        {
            // Windows fallback using zip
            var zipFile = Path.Combine(context.OutDir, "BaconTracker-Linux-x64.zip");
            ZipFile.CreateFromDirectory(publishDir, zipFile);
            context.Information($"Not on Unix. Packaged Linux binary as zip to: {zipFile}");
        }

        // Build Debian Package (.deb) if running on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            BuildDebianPackage(context, publishDir);
        }
    }

    private void BuildDebianPackage(BuildContext context, string publishDir)
    {
        context.Information("Structuring deb-root filesystem for Debian packaging...");
        var debRoot = Path.Combine(context.OutDir, "deb-root");
        
        try
        {
            if (Directory.Exists(debRoot)) Directory.Delete(debRoot, true);

            var debianDir = Path.Combine(debRoot, "DEBIAN");
            var usrBinDir = Path.Combine(debRoot, "usr", "bin");
            var shareAppsDir = Path.Combine(debRoot, "usr", "share", "applications");

            Directory.CreateDirectory(debianDir);
            Directory.CreateDirectory(usrBinDir);
            Directory.CreateDirectory(shareAppsDir);

            // Write Debian package configuration file
            string controlContent = @"Package: bacontracker
Version: 1.0.0
Section: utils
Priority: optional
Architecture: amd64
Maintainer: BaconTracker Team <support@bacontracker.com>
Description: Zero-cost C# Hearthstone Battlegrounds Tracker.
 ImGui-based visual overlay tracker for active lobby card tech levels, triples, and opponent telemetry.
";
            File.WriteAllText(Path.Combine(debianDir, "control"), controlContent);

            // Write desktop launcher shortcut
            string desktopEntry = @"[Desktop Entry]
Name=BaconTracker
Comment=Hearthstone Battlegrounds Tracker
Exec=/usr/bin/bacontracker
Icon=bacontracker
Type=Application
Terminal=false
Categories=Game;Utility;
";
            File.WriteAllText(Path.Combine(shareAppsDir, "bacontracker.desktop"), desktopEntry);

            // Copy binary
            string srcBinary = Path.Combine(publishDir, "bacontracker");
            if (!File.Exists(srcBinary))
            {
                // Fallback check if compiled project binary matches workspace project name casing
                srcBinary = Path.Combine(publishDir, "BaconTracker.App");
            }

            if (File.Exists(srcBinary))
            {
                string destBinary = Path.Combine(usrBinDir, "bacontracker");
                File.Copy(srcBinary, destBinary, true);
                
                // Ensure executable permission is toggled
                context.StartProcess("chmod", new ProcessSettings { Arguments = $"+x {destBinary}" });
            }
            else
            {
                context.Warning($"Error: Could not find output binary in {publishDir} to package.");
                return;
            }

            var finalDebFile = Path.Combine(context.OutDir, "BaconTracker-Linux-x64.deb");
            if (File.Exists(finalDebFile)) File.Delete(finalDebFile);

            var result = context.StartProcess("dpkg-deb", new ProcessSettings
            {
                Arguments = $"--build {debRoot} {finalDebFile}"
            });
            
            if (result == 0)
            {
                context.Information($"Successfully created Debian installer package at: {finalDebFile}");
            }
            else
            {
                context.Warning("dpkg-deb failed to compile Debian package.");
            }
        }
        catch (Exception ex)
        {
            context.Warning($"Failed to build Debian package: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(debRoot)) Directory.Delete(debRoot, true);
            }
            catch {}
        }
    }
}

[TaskName("Publish-Mac")]
[IsDependentOn(typeof(CleanTask))]
public sealed class PublishMacTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Information("Publishing application for macOS (osx-x64)...");
        var publishDir = Path.Combine(context.OutDir, "osx-x64");
        
        context.DotNetPublish("./BaconTracker.App/BaconTracker.App.csproj", new DotNetPublishSettings
        {
            Configuration = context.BuildConfig,
            Runtime = "osx-x64",
            SelfContained = true,
            OutputDirectory = publishDir,
            MSBuildSettings = new DotNetMSBuildSettings()
                .WithProperty("PublishSingleFile", "true")
                .WithProperty("PublishTrimmed", "false")
        });

        // Pack macOS tarball
        var tarFile = Path.Combine(context.OutDir, "BaconTracker-Mac-x64.tar.gz");
        if (File.Exists(tarFile)) File.Delete(tarFile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            context.StartProcess("tar", new ProcessSettings
            {
                Arguments = $"-czf {tarFile} -C {publishDir} ."
            });
            context.Information($"Successfully packaged macOS tarball to: {tarFile}");
        }
        else
        {
            var zipFile = Path.Combine(context.OutDir, "BaconTracker-Mac-x64.zip");
            ZipFile.CreateFromDirectory(publishDir, zipFile);
            context.Information($"Not on Unix. Packaged macOS binary as zip to: {zipFile}");
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PublishWindowsTask))]
[IsDependentOn(typeof(PublishLinuxTask))]
[IsDependentOn(typeof(PublishMacTask))]
public class DefaultTask : FrostingTask
{
}
