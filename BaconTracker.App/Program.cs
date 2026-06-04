using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using BaconTracker.Core;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BaconTracker.App;

public class Program
{
    [System.Runtime.InteropServices.DllImport("libgtk-4.so.1", EntryPoint = "gtk_init")]
    private static extern void GtkInit();

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static void Main(string[] args)
    {
        // Setup Serilog
        string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaconTracker");
        string logPath = Path.Combine(localDir, "Logs", "log.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = "http://127.0.0.1:4317";
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "BaconTracker",
                    ["service.version"] = "1.0.0"
                };
            })
            .CreateLogger();

        Log.Information("=== BaconTracker Starting ===");

        // Setup Dependency Injection container
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // Initialize custom telemetry and event listeners
        Telemetry.Initialize();

        // Instantiate singletons to trigger ambient context setter and start OTel tracking
        _ = ServiceProvider.GetRequiredService<AssetManager>();
        _ = ServiceProvider.GetRequiredService<LogWatcher>();
        _ = ServiceProvider.GetRequiredService<PluginManager>();
        _ = ServiceProvider.GetService<TracerProvider>();
        _ = ServiceProvider.GetService<MeterProvider>();

        if (HandleArgs(args))
        {
            return;
        }

        // Setup Crash Handlers
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled AppDomain Exception");
            }
            else
            {
                Log.Fatal("Unhandled AppDomain Exception (non-exception object: {ExceptionObject})", e.ExceptionObject);
            }
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            Log.Fatal(e.Exception, "Unobserved Task Exception");
            e.SetObserved();
            Log.CloseAndFlush();
        };

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var window = scope.ServiceProvider.GetRequiredService<IOverlayWindow>();
            window.Initialize();
            
            Log.Information("[Program] Running main overlay loop...");
            window.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal application error during main loop execution");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static bool HandleArgs(string[] args)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        foreach (var arg in args)
        {
            // Bypass relaunch if the user explicitly requested native Wayland/Linux backend
            if (arg.Equals("--wayland", StringComparison.OrdinalIgnoreCase) || 
                arg.Equals("-wayland", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--native", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[Program] Explicit native backend requested. Bypassing XWayland relaunch and forcing GDK_BACKEND=wayland.");
                Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
                return false;
            }
        }

        string? sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        string? gdkBackend = Environment.GetEnvironmentVariable("GDK_BACKEND");
        
        if (sessionType == "wayland" && gdkBackend != "x11")
        {
            bool layerShellSupported = false;
            try
            {
                if (System.Runtime.InteropServices.NativeLibrary.TryLoad("libgtk4-layer-shell.so.0", out IntPtr handle))
                {
                    System.Runtime.InteropServices.NativeLibrary.Free(handle);
                    
                    // Call native gtk_init to fully open the display connection before checking layer shell support
                    GtkInit();
                    if (Gtk4LayerShell.IsSupported())
                    {
                        layerShellSupported = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load layer shell or check support");
            }

            if (!layerShellSupported)
            {
                Log.Information("[Program] Wayland Layer Shell is not supported or not installed. Relaunching with GDK_BACKEND=x11 to enable XWayland stay-above support...");
                try
                {
                    var mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                    if (mainModule != null)
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = mainModule.FileName,
                            UseShellExecute = false
                        };
                        foreach (var arg in args)
                        {
                            psi.ArgumentList.Add(arg);
                        }
                        psi.EnvironmentVariables["GDK_BACKEND"] = "x11";
                        System.Diagnostics.Process.Start(psi);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to relaunch with GDK_BACKEND=x11");
                }
            }
        }
        return false;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService("BaconTracker", serviceVersion: "1.0.0");

        services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
        });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resourceBuilder)
                       .AddHttpClientInstrumentation()
                       .AddSource("BaconTracker")
                       .AddOtlpExporter(otlp =>
                       {
                           otlp.Endpoint = new Uri("http://127.0.0.1:4317");
                       });
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resourceBuilder)
                       .AddHttpClientInstrumentation()
                       .AddMeter("BaconTracker")
                       .AddOtlpExporter(otlp =>
                       {
                           otlp.Endpoint = new Uri("http://127.0.0.1:4317");
                       });
            });

        services.AddSingleton<WinePrefixDetector>();
        services.AddSingleton<LogWatcher>();
        services.AddSingleton<AssetManager>();
        services.AddSingleton<PluginManager>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<Win32Window>();
            services.AddSingleton<IOverlayWindow>(sp => sp.GetRequiredService<Win32Window>());
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<WaylandGtk4Window>();
            services.AddSingleton<IOverlayWindow>(sp => sp.GetRequiredService<WaylandGtk4Window>());
        }
        else
        {
            throw new PlatformNotSupportedException("This operating system is not supported by BaconTracker.");
        }
    }
}
