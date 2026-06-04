using System;
using System.Collections.Generic;
using System.IO;
using Silk.NET.OpenGL;
using StbImageSharp;
using Microsoft.Extensions.Logging;

namespace BaconTracker.App;

public class AssetManager : IDisposable
{
    private static AssetManager? _instance;
    public static AssetManager Instance
    {
        get => _instance ?? throw new InvalidOperationException("AssetManager not initialized via dependency injection.");
        internal set => _instance = value;
    }

    private GL? _gl;
    private readonly Dictionary<string, IntPtr> _textureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AssetManager> _logger;

    public AssetManager(ILogger<AssetManager> logger)
    {
        _logger = logger;
        _instance = this;
    }

    /// <summary>
    /// Initializes the asset manager with the active OpenGL context.
    /// </summary>
    public void Initialize(GL gl)
    {
        _gl = gl;
        CleanupCorruptedCacheFiles();
    }

    private void CleanupCorruptedCacheFiles()
    {
        if (!Directory.Exists(_cacheDir)) return;
        try
        {
            int deletedCount = 0;
            foreach (var file in Directory.GetFiles(_cacheDir, "*.png"))
            {
                if (!file.EndsWith("_art.png", StringComparison.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(file);
                    // Full card renders (PNG) are always >= 35KB. Small files (usually <20KB) are either raw art JPEGs
                    // named with .png or 404 error files from prior runs.
                    if (info.Length < 35000)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }
            }
            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {DeletedCount} corrupted/small cached card renders to trigger clean re-downloads.", deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    /// <summary>
    /// Loads a texture from disk and caches it. Returns an IntPtr representing the OpenGL texture ID.
    /// If the path is relative, it resolves it against the app's plugins/assets directory.
    /// </summary>
    public unsafe IntPtr LoadTexture(string path)
    {
        if (_gl == null)
        {
            _logger.LogWarning("Attempted to load texture before initialization.");
            return IntPtr.Zero;
        }

        // Standardize path
        string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

        if (_textureCache.TryGetValue(fullPath, out IntPtr cachedId))
        {
            return cachedId;
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogError("Texture file not found at: {FullPath}", fullPath);
            return IntPtr.Zero;
        }

        try
        {
            // Load file using StbImageSharp (pure C# image loading)
            using var stream = File.OpenRead(fullPath);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            // Generate texture in OpenGL
            uint textureId = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, textureId);

            // Set filter parameters safely (avoiding ref readonly warnings)
            int minFilter = (int)TextureMinFilter.Linear;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in minFilter);
            
            int magFilter = (int)TextureMagFilter.Linear;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in magFilter);
            
            int wrapS = (int)TextureWrapMode.ClampToEdge;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in wrapS);
            
            int wrapT = (int)TextureWrapMode.ClampToEdge;
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in wrapT);

            // Upload pixel buffer to GPU
            fixed (byte* ptr = image.Data)
            {
                _gl.TexImage2D(
                    target: TextureTarget.Texture2D,
                    level: 0,
                    internalformat: InternalFormat.Rgba8,
                    width: (uint)image.Width,
                    height: (uint)image.Height,
                    border: 0,
                    format: PixelFormat.Rgba,
                    type: PixelType.UnsignedByte,
                    pixels: ptr
                );
            }

            _gl.BindTexture(TextureTarget.Texture2D, 0);

            IntPtr imguiTextureId = (IntPtr)textureId;
            _textureCache[fullPath] = imguiTextureId;
            
            _logger.LogDebug("Loaded and cached texture: {FileName} (ID: {TextureId})", Path.GetFileName(fullPath), textureId);
            return imguiTextureId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading texture {Path}", path);
            return IntPtr.Zero;
        }
    }

    private readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BaconTracker",
        "Cache",
        "CardArt"
    );

    private readonly HashSet<string> _downloadingCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedDownloads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if downloading the card art failed previously.
    /// </summary>
    public bool HasCardArtFailed(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return false;
        lock (_failedDownloads)
        {
            if (cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
            {
                string plainId = cardId.Substring(0, cardId.Length - 2);
                return _failedDownloads.Contains(cardId) && _failedDownloads.Contains(plainId);
            }
            return _failedDownloads.Contains(cardId);
        }
    }

    /// <summary>
    /// Checks if a full card render is cached on disk.
    /// </summary>
    public bool IsFullCardRender(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return false;
        
        string renderPath = Path.Combine(_cacheDir, $"{cardId}.png");
        if (File.Exists(renderPath)) return true;

        if (cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
        {
            string plainId = cardId.Substring(0, cardId.Length - 2);
            string plainRenderPath = Path.Combine(_cacheDir, $"{plainId}.png");
            if (File.Exists(plainRenderPath)) return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the OpenGL texture pointer for the given card ID, downloading it from the CDN asynchronously if cached file does not exist.
    /// </summary>
    public IntPtr GetCardArt(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return IntPtr.Zero;

        Telemetry.CardArtRequests.Add(1);

        // Try getting the golden or plain render directly
        IntPtr tex = GetCardArtInternal(cardId);
        if (tex != IntPtr.Zero)
        {
            Telemetry.CardArtCacheHits.Add(1);
            return tex;
        }

        // If it's a golden card and the golden render is not cached/failed to download,
        // fall back to loading the plain render.
        if (cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
        {
            string plainId = cardId.Substring(0, cardId.Length - 2);
            IntPtr plainTex = GetCardArtInternal(plainId);
            if (plainTex != IntPtr.Zero)
            {
                Telemetry.CardArtCacheHits.Add(1);
                return plainTex;
            }
        }

        return IntPtr.Zero;
    }

    private IntPtr GetCardArtInternal(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return IntPtr.Zero;

        lock (_failedDownloads)
        {
            if (_failedDownloads.Contains(cardId))
            {
                return IntPtr.Zero;
            }
        }

        // Ensure directory exists
        if (!Directory.Exists(_cacheDir))
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create cache directory");
                return IntPtr.Zero;
            }
        }

        string renderPath = Path.Combine(_cacheDir, $"{cardId}.png");
        string rawArtPath = Path.Combine(_cacheDir, $"{cardId}_art.png");

        if (File.Exists(renderPath))
        {
            return LoadTexture(renderPath);
        }
        if (File.Exists(rawArtPath))
        {
            return LoadTexture(rawArtPath);
        }

        // Not in cache, check if already downloading
        lock (_downloadingCards)
        {
            if (_downloadingCards.Contains(cardId))
            {
                return IntPtr.Zero; // Return zero indicating loading
            }
            _downloadingCards.Add(cardId);
        }

        // Start background download task
        System.Threading.Tasks.Task.Run(async () =>
        {
            using var activity = Telemetry.Source.StartActivity("DownloadCardArt");
            activity?.SetTag("card.id", cardId);
            Telemetry.CardArtCacheMisses.Add(1);

            using var client = new System.Net.Http.HttpClient();
            bool success = await BaconTracker.Core.CardArtDownloader.DownloadCardArtAsync(client, cardId, _cacheDir);

            lock (_downloadingCards)
            {
                _downloadingCards.Remove(cardId);
            }

            if (!success)
            {
                lock (_failedDownloads)
                {
                    _failedDownloads.Add(cardId);
                }
                _logger.LogError("Failed to download any card art/render for {CardId} after trying fallbacks.", cardId);
                activity?.SetTag("download.success", false);
            }
            else
            {
                activity?.SetTag("download.success", true);
            }
        });

        return IntPtr.Zero;
    }

    /// <summary>
    /// Disposes all GPU texture allocations.
    /// </summary>
    public void Dispose()
    {
        if (_gl == null) return;

        _logger.LogDebug("Cleaning up GPU texture allocations...");
        foreach (var pair in _textureCache)
        {
            uint id = (uint)pair.Value;
            _gl.DeleteTexture(id);
        }
        _textureCache.Clear();
        _gl = null;
    }
}
