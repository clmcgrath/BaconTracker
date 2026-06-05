using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;

namespace BaconTracker.Core;

public static class CardArtDownloader
{
    public static string? GetStrippedCardId(string cardId)
    {
        if (cardId.StartsWith("BG_", StringComparison.OrdinalIgnoreCase))
        {
            string remainder = cardId.Substring(3);
            if (remainder.Contains("_"))
            {
                return remainder;
            }
        }
        return null;
    }

    public static async Task<bool> DownloadCardRenderAsync(HttpClient client, string cardId, string cacheDir)
    {
        string renderPath = Path.Combine(cacheDir, $"{cardId}.png");
        if (File.Exists(renderPath))
        {
            return true;
        }

        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BaconTracker/1.0 (https://github.com/bacontracker)");
        }

        string? stripped = GetStrippedCardId(cardId);

        if (await TryDownloadWikiGgRender(client, cardId, renderPath)) return true;
        if (stripped != null && await TryDownloadWikiGgRender(client, stripped, renderPath)) return true;

        if (await TryDownloadHearthstoneJsonRender(client, cardId, renderPath)) return true;
        if (stripped != null && await TryDownloadHearthstoneJsonRender(client, stripped, renderPath)) return true;

        if (await TryDownloadFirestoneRender(client, cardId, renderPath)) return true;
        if (stripped != null && await TryDownloadFirestoneRender(client, stripped, renderPath)) return true;

        return false;
    }

    public static async Task<bool> DownloadMinionPortraitAsync(HttpClient client, string cardId, string cacheDir)
    {
        string rawArtPath = Path.Combine(cacheDir, $"{cardId}_art.png");
        if (File.Exists(rawArtPath))
        {
            return true;
        }

        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BaconTracker/1.0 (https://github.com/bacontracker)");
        }

        string? stripped = GetStrippedCardId(cardId);

        if (await TryDownloadHearthstoneJsonRawArt(client, cardId, rawArtPath)) return true;
        if (stripped != null && await TryDownloadHearthstoneJsonRawArt(client, stripped, rawArtPath)) return true;

        return false;
    }

    /// <summary>
    /// Downloads card art using prioritized fallback candidates.
    /// Returns true if successfully downloaded or if files already exist.
    /// </summary>
    public static async Task<bool> DownloadCardArtAsync(HttpClient client, string cardId, string cacheDir)
    {
        bool renderSuccess = await DownloadCardRenderAsync(client, cardId, cacheDir);
        bool portraitSuccess = await DownloadMinionPortraitAsync(client, cardId, cacheDir);
        return renderSuccess || portraitSuccess;
    }

    private static async Task<bool> TryDownloadWikiGgRender(HttpClient client, string id, string targetPath)
    {
        // Try Battlegrounds render suffix first (for BG cards/minions) if it starts with BG
        if (id.StartsWith("BG", StringComparison.OrdinalIgnoreCase))
        {
            string urlWikiBg = $"https://hearthstone.wiki.gg/images/{id}_Battlegrounds.png";
            if (await TryDownloadFile(client, urlWikiBg, targetPath))
            {
                string msg = $"[CardArtDownloader] Successfully downloaded Wiki.gg Battlegrounds render for {id}";
                Console.WriteLine(msg);
                Log.Debug(msg);
                return true;
            }
        }

        // Try plain render (anomalies, hero powers, standard cards, etc.)
        string urlWikiPlain = $"https://hearthstone.wiki.gg/images/{id}.png";
        if (await TryDownloadFile(client, urlWikiPlain, targetPath))
        {
            string msg = $"[CardArtDownloader] Successfully downloaded Wiki.gg plain render for {id}";
            Console.WriteLine(msg);
            Log.Debug(msg);
            return true;
        }

        return false;
    }

    private static async Task<bool> TryDownloadHearthstoneJsonRender(HttpClient client, string id, string targetPath)
    {
        string urlRender = $"https://art.hearthstonejson.com/v1/render/latest/enUS/256x/{id}.png";
        if (await TryDownloadFile(client, urlRender, targetPath))
        {
            string msg = $"[CardArtDownloader] Successfully downloaded HearthstoneJSON render for {id}";
            Console.WriteLine(msg);
            Log.Debug(msg);
            return true;
        }
        return false;
    }

    private static async Task<bool> TryDownloadFirestoneRender(HttpClient client, string id, string targetPath)
    {
        string urlFirestone = $"https://static.zerotoheroes.com/hearthstone/fullcard/en/256/{id}.png";
        if (await TryDownloadFile(client, urlFirestone, targetPath))
        {
            string msg = $"[CardArtDownloader] Successfully downloaded Firestone CDN render (almost last resort) for {id}";
            Console.WriteLine(msg);
            Log.Debug(msg);
            return true;
        }
        return false;
    }

    private static async Task<bool> TryDownloadHearthstoneJsonRawArt(HttpClient client, string id, string targetPath)
    {
        string urlArt = $"https://art.hearthstonejson.com/v1/256x/{id}.jpg";
        if (await TryDownloadFile(client, urlArt, targetPath))
        {
            string msg = $"[CardArtDownloader] Successfully downloaded raw art for {id}";
            Console.WriteLine(msg);
            Log.Debug(msg);
            return true;
        }
        return false;
    }

    private static async Task<bool> TryDownloadFile(HttpClient client, string url, string targetPath)
    {
        try
        {
            byte[] data = await client.GetByteArrayAsync(url);
            if (data == null || data.Length == 0)
            {
                return false;
            }
            await File.WriteAllBytesAsync(targetPath, data);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
