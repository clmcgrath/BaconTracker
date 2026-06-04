using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BaconTracker.Core;

public class WinePrefixDetector
{
    private readonly ILogger<WinePrefixDetector> _logger;

    public WinePrefixDetector(ILogger<WinePrefixDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to find the Hearthstone Logs directory on Linux by combining process scanning and heuristic scans.
    /// </summary>
    public string? DetectLogsDirectory()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Windows standard logs path
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Blizzard", "Hearthstone", "Logs");
        }

        _logger.LogDebug("Scanning for active Hearthstone process...");
        string? activePrefix = GetPrefixFromActiveProcess();
        
        if (!string.IsNullOrEmpty(activePrefix))
        {
            _logger.LogDebug("Detected active prefix: {ActivePrefix}", activePrefix);
            string? logsPath = GetHearthstoneLogsPathFromPrefix(activePrefix);
            if (!string.IsNullOrEmpty(logsPath))
            {
                return logsPath;
            }
        }

        _logger.LogDebug("Hearthstone not running or environment unreadable. Running heuristic scanner...");
        string? heuristicPrefix = ScanHeuristics();
        if (!string.IsNullOrEmpty(heuristicPrefix))
        {
            _logger.LogInformation("Found heuristic prefix: {HeuristicPrefix}", heuristicPrefix);
            return GetHearthstoneLogsPathFromPrefix(heuristicPrefix);
        }

        return null;
    }

    private string? GetPrefixFromActiveProcess()
    {
        try
        {
            var hearthstoneProcess = Process.GetProcesses()
                .FirstOrDefault(p => p.ProcessName.Contains("hearthstone", StringComparison.OrdinalIgnoreCase));

            if (hearthstoneProcess == null)
            {
                // Fallback: scan for any wine process running Hearthstone
                hearthstoneProcess = Process.GetProcesses()
                    .FirstOrDefault(p => p.ProcessName.Contains("wine", StringComparison.OrdinalIgnoreCase) 
                                         && HasHearthstoneInCommandLine(p.Id));
            }

            if (hearthstoneProcess != null)
            {
                int pid = hearthstoneProcess.Id;
                string environPath = $"/proc/{pid}/environ";
                if (File.Exists(environPath))
                {
                    byte[] bytes = File.ReadAllBytes(environPath);
                    string content = Encoding.UTF8.GetString(bytes);
                    string[] envs = content.Split('\0');
                    foreach (var env in envs)
                    {
                        if (env.StartsWith("WINEPREFIX="))
                        {
                            return env.Substring("WINEPREFIX=".Length);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Process scan failed");
        }

        return null;
    }

    private bool HasHearthstoneInCommandLine(int pid)
    {
        try
        {
            string cmdLinePath = $"/proc/{pid}/cmdline";
            if (File.Exists(cmdLinePath))
            {
                string cmdLine = File.ReadAllText(cmdLinePath);
                return cmdLine.Contains("Hearthstone", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    private string? ScanHeuristics()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 1. Steam paths (standard appid 214560)
        string[] steamPaths = new[]
        {
            Path.Combine(home, ".steam/steam/steamapps/compatdata/214560/pfx"),
            Path.Combine(home, ".local/share/Steam/steamapps/compatdata/214560/pfx"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/214560/pfx") // Flatpak Steam
        };

        foreach (var path in steamPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // 2. Lutris paths
        string[] lutrisPaths = new[]
        {
            Path.Combine(home, "Games/hearthstone/pfx"),
            Path.Combine(home, "Games/battlenet/pfx"),
            Path.Combine(home, "Games/hearthstone-lutris/pfx")
        };

        foreach (var path in lutrisPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // 3. Bottles paths
        string bottlesDir = Path.Combine(home, ".var/app/com.usebottles.bottles/data/bottles/bottles");
        if (Directory.Exists(bottlesDir))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(bottlesDir))
                {
                    string pfxPath = Path.Combine(dir, "pfx");
                    if (Directory.Exists(pfxPath))
                    {
                        // Check if Hearthstone drive_c users subfolder exists
                        string appDataPath = Path.Combine(pfxPath, "drive_c", "users");
                        if (Directory.Exists(appDataPath))
                        {
                            return pfxPath;
                        }
                    }
                }
            }
            catch { }
        }

        // 4. Default Wine prefix
        string defaultWine = Path.Combine(home, ".wine");
        if (Directory.Exists(defaultWine))
        {
            return defaultWine;
        }

        return null;
    }

    public string? GetHearthstoneLogsPathFromPrefix(string prefixPath)
    {
        if (string.IsNullOrEmpty(prefixPath) || !Directory.Exists(prefixPath)) return null;

        // 1. Try registry-based detection first
        string? logsPath = DetectLogsFromRegistry(prefixPath);
        if (!string.IsNullOrEmpty(logsPath))
        {
            _logger.LogInformation("Successfully detected logs directory via registry: {LogsPath}", logsPath);
            return logsPath;
        }

        // 2. Fallback: Directory heuristic search
        _logger.LogDebug("Registry detection yielded no results. Falling back to directory scan...");
        string usersDir = Path.Combine(prefixPath, "drive_c", "users");
        if (!Directory.Exists(usersDir)) return null;

        try
        {
            foreach (var userFolder in Directory.GetDirectories(usersDir))
            {
                string userName = Path.GetFileName(userFolder);
                if (userName.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    userName.Equals("Default User", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Construct AppData Hearthstone Logs path
                string logPath = Path.Combine(userFolder, "AppData", "Local", "Blizzard", "Hearthstone", "Logs");
                if (Directory.Exists(logPath))
                {
                    return logPath;
                }
                
                // Check if Parent app data exists to auto-create Logs path later
                string appDataPath = Path.Combine(userFolder, "AppData", "Local", "Blizzard", "Hearthstone");
                if (Directory.Exists(appDataPath))
                {
                    return Path.Combine(appDataPath, "Logs");
                }
            }
        }
        catch { }

        return null;
    }

    private string? DetectLogsFromRegistry(string prefixPath)
    {
        try
        {
            string systemReg = Path.Combine(prefixPath, "system.reg");
            string userReg = Path.Combine(prefixPath, "user.reg");

            // A. Look up Hearthstone installation directory in system.reg (HKLM)
            if (File.Exists(systemReg))
            {
                // Try Wow6432Node first, then standard Uninstall key
                string? installLocation = ReadRegistryValue(systemReg, 
                    @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone", 
                    "InstallLocation");

                if (string.IsNullOrEmpty(installLocation))
                {
                    installLocation = ReadRegistryValue(systemReg, 
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone", 
                        "InstallLocation");
                }

                if (!string.IsNullOrEmpty(installLocation))
                {
                    string? installPathLinux = ConvertWindowsPathToLinux(prefixPath, installLocation);
                    if (!string.IsNullOrEmpty(installPathLinux))
                    {
                        string logsPath = Path.Combine(installPathLinux, "Logs");
                        if (Directory.Exists(logsPath))
                        {
                            return logsPath;
                        }
                    }
                }
            }

            // B. Look up Local AppData in user.reg (HKCU)
            if (File.Exists(userReg))
            {
                string? localAppData = ReadRegistryValue(userReg,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
                    "Local AppData");

                if (!string.IsNullOrEmpty(localAppData))
                {
                    string? localAppDataLinux = ConvertWindowsPathToLinux(prefixPath, localAppData);
                    if (!string.IsNullOrEmpty(localAppDataLinux))
                    {
                        string logsPath = Path.Combine(localAppDataLinux, "Blizzard", "Hearthstone", "Logs");
                        if (Directory.Exists(logsPath))
                        {
                            return logsPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in registry-based detection");
        }

        return null;
    }

    public string? ReadRegistryValue(string regFilePath, string sectionPath, string valueName)
    {
        if (!File.Exists(regFilePath)) return null;

        string targetSection = sectionPath.Replace("\\\\", "\\").Trim().ToLowerInvariant();
        string targetValueName = valueName.Trim().ToLowerInvariant();

        bool inSection = false;

        try
        {
            using var fs = new FileStream(regFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                {
                    continue;
                }

                // Check for section header
                if (trimmed.StartsWith("[") && trimmed.Contains("]"))
                {
                    int closeIndex = trimmed.IndexOf(']');
                    string sectionName = trimmed.Substring(1, closeIndex - 1);
                    sectionName = sectionName.Replace("\\\\", "\\").Trim().ToLowerInvariant();

                    inSection = (sectionName == targetSection);
                    continue;
                }

                if (inSection)
                {
                    // Check for key-value pair
                    // e.g. "InstallLocation"="C:\\Program Files (x86)\\Hearthstone"
                    int equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        string rawKey = trimmed.Substring(0, equalsIndex).Trim();
                        // Remove surrounding quotes from key
                        if (rawKey.StartsWith("\"") && rawKey.EndsWith("\"") && rawKey.Length > 1)
                        {
                            rawKey = rawKey.Substring(1, rawKey.Length - 2);
                        }

                        if (rawKey.ToLowerInvariant() == targetValueName)
                        {
                            string rawValue = trimmed.Substring(equalsIndex + 1).Trim();
                            
                            // Wine registry strings are quoted: "value"
                            if (rawValue.StartsWith("\"") && rawValue.EndsWith("\"") && rawValue.Length > 1)
                            {
                                string val = rawValue.Substring(1, rawValue.Length - 2);
                                return UnescapeRegistryString(val);
                            }
                            
                            return rawValue;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading registry file {RegFilePath}", regFilePath);
        }

        return null;
    }

    private string UnescapeRegistryString(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                char next = value[i + 1];
                if (next == '\\')
                {
                    sb.Append('\\');
                    i++;
                }
                else if (next == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (next == 'n')
                {
                    sb.Append('\n');
                    i++;
                }
                else if (next == 'r')
                {
                    sb.Append('\r');
                    i++;
                }
                else if (next == 't')
                {
                    sb.Append('\t');
                    i++;
                }
                else if (next == 'x' && i + 3 < value.Length)
                {
                    // Hex escape: \xXX
                    string hex = value.Substring(i + 2, 2);
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int hexVal))
                    {
                        sb.Append((char)hexVal);
                        i += 3;
                    }
                    else
                    {
                        sb.Append('\\');
                    }
                }
                else
                {
                    sb.Append('\\');
                }
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    private string? ConvertWindowsPathToLinux(string prefixPath, string winPath)
    {
        if (string.IsNullOrEmpty(winPath)) return null;

        winPath = winPath.Trim('"');

        if (winPath.Length >= 2 && winPath[1] == ':')
        {
            char driveLetter = char.ToLowerInvariant(winPath[0]);
            string relativePath = winPath.Substring(2).Replace('\\', '/').TrimStart('/');
            string driveDir = $"drive_{driveLetter}";
            
            // Build the absolute Linux path
            string fullPath = Path.Combine(prefixPath, driveDir, relativePath);
            return fullPath;
        }

        return null;
    }
}
