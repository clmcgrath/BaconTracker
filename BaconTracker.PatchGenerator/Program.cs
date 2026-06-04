using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Linq;

namespace BaconTracker.PatchGenerator;

public class RawCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("techLevel")]
    public int? TechLevel { get; set; }

    [JsonPropertyName("battlegroundsHero")]
    public bool? BattlegroundsHero { get; set; }

    [JsonPropertyName("isBattlegroundsPoolMinion")]
    public bool? IsBattlegroundsPoolMinion { get; set; }

    [JsonPropertyName("attack")]
    public int? Attack { get; set; }

    [JsonPropertyName("health")]
    public int? Health { get; set; }

    [JsonPropertyName("race")]
    public string? Race { get; set; }

    [JsonPropertyName("races")]
    public List<string>? Races { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

public class MinifiedCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TechLevel { get; set; }

    [JsonPropertyName("hero")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsHero { get; set; }

    [JsonPropertyName("atk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Attack { get; set; }

    [JsonPropertyName("hp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Health { get; set; }

    [JsonPropertyName("tribes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tribes { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPoolMinion { get; set; }
}

class Program
{
    private const string HearthstoneJsonUrl = "https://api.hearthstonejson.com/v1/latest/enUS/cards.json";

    static async Task Main(string[] args)
    {
        Console.WriteLine("[PatchGenerator] Starting Battlegrounds cards metadata scraping...");

        string outputPath = "bg_cards.json";
        bool syncArt = false;
        string artDir = "art_cache";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--sync-art", StringComparison.OrdinalIgnoreCase) || 
                args[i].Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                syncArt = true;
            }
            else if (args[i].Equals("--art-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                artDir = args[++i];
            }
            else if (!args[i].StartsWith("-"))
            {
                outputPath = args[i];
            }
        }
        
        try
        {
            using var httpClient = new HttpClient();
            Console.WriteLine($"[PatchGenerator] Downloading raw cards registry from: {HearthstoneJsonUrl}");
            
            var responseBytes = await httpClient.GetByteArrayAsync(HearthstoneJsonUrl);
            Console.WriteLine($"[PatchGenerator] Downloaded {responseBytes.Length / 1024 / 1024.0:F2} MB. Parsing database...");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var rawCards = JsonSerializer.Deserialize<List<RawCard>>(responseBytes, options);
            if (rawCards == null)
            {
                Console.WriteLine("[PatchGenerator] Error: Deserialized raw cards database was null.");
                return;
            }

            Console.WriteLine($"[PatchGenerator] Loaded {rawCards.Count} raw cards. Running filters...");

            var bgCards = new List<MinifiedCard>();

            foreach (var card in rawCards)
            {
                // Filter: must be explicitly a Battlegrounds Hero or a Battlegrounds minion/card
                bool isBgHero = card.BattlegroundsHero == true;
                bool isBgMinion = card.IsBattlegroundsPoolMinion == true || card.TechLevel.HasValue;
                bool isBgCardId = card.Id.StartsWith("BG", StringComparison.OrdinalIgnoreCase) || 
                                  card.Id.StartsWith("TB_BaconShop_", StringComparison.OrdinalIgnoreCase);

                if (isBgHero || isBgMinion || isBgCardId)
                {
                    // Unify races/tribes
                    var tribesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(card.Race))
                    {
                        tribesSet.Add(NormalizeTribe(card.Race));
                    }
                    if (card.Races != null)
                    {
                        foreach (var race in card.Races)
                        {
                            tribesSet.Add(NormalizeTribe(race));
                        }
                    }

                    var minified = new MinifiedCard
                    {
                        Id = card.Id,
                        Name = card.Name,
                        Type = card.Type,
                        TechLevel = card.TechLevel ?? 0,
                        IsHero = isBgHero,
                        Attack = card.Attack ?? 0,
                        Health = card.Health ?? 0,
                        Tribes = tribesSet.Count > 0 ? tribesSet.OrderBy(t => t).ToList() : null,
                        Text = string.IsNullOrEmpty(card.Text) ? null : CleanCardText(card.Text),
                        IsPoolMinion = card.IsBattlegroundsPoolMinion == true
                    };

                    bgCards.Add(minified);
                }
            }

            Console.WriteLine($"[PatchGenerator] Minified dataset contains {bgCards.Count} cards. Serializing and saving to: {outputPath}");

            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save minified JSON (compressed formatting)
            var serializeOptions = new JsonSerializerOptions
            {
                WriteIndented = false, // Minified to save CDN bandwidth
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var minifiedJson = JsonSerializer.Serialize(bgCards, serializeOptions);
            await File.WriteAllTextAsync(outputPath, minifiedJson);

            Console.WriteLine($"[PatchGenerator] Success! Generated {outputPath} ({minifiedJson.Length / 1024.0:F2} KB).");

            if (syncArt)
            {
                Console.WriteLine($"[PatchGenerator] Starting card art sync to directory: {artDir}");
                Directory.CreateDirectory(artDir);
                
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BaconTracker-PatchGenerator/1.0 (https://github.com/bacontracker)");

                int count = 0;
                int total = bgCards.Count;
                int downloaded = 0;
                int failed = 0;

                foreach (var card in bgCards)
                {
                    count++;
                    string cardId = card.Id;

                    if (card.Type.Equals("ENCHANTMENT", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool success = await BaconTracker.Core.CardArtDownloader.DownloadCardArtAsync(client, cardId, artDir);
                    if (success) downloaded++;
                    else failed++;

                    if (count % 20 == 0 || count == total)
                    {
                        Console.WriteLine($"[PatchGenerator] Art Sync Progress: {count}/{total} cards processed. Success: {downloaded}, Failed: {failed}");
                    }
                }
                Console.WriteLine($"[PatchGenerator] Art Sync Completed! Total processed: {total}, Downloaded/Verified: {downloaded}, Failed: {failed}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PatchGenerator] Error generating patches: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static string NormalizeTribe(string tribe)
    {
        if (string.IsNullOrEmpty(tribe)) return string.Empty;
        
        // Convert HEARTHSTONE tribes to readable case
        string formatted = tribe.Trim().ToUpperInvariant();
        return formatted switch
        {
            "PET" => "Beast",
            "BEAST" => "Beast",
            "DEMON" => "Demon",
            "DRAGON" => "Dragon",
            "ELEMENTAL" => "Elemental",
            "MECHANICAL" => "Mech",
            "MECH" => "Mech",
            "MURLOC" => "Murloc",
            "PIRATE" => "Pirate",
            "QUILBOAR" => "Quilboar",
            "NAGA" => "Naga",
            "UNDEAD" => "Undead",
            "ALL" => "All",
            _ => char.ToUpperInvariant(tribe[0]) + tribe.Substring(1).ToLowerInvariant()
        };
    }

    private static string CleanCardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        // Remove XML markup formatting commonly found in Hearthstone card descriptions
        string cleaned = text.Replace("<b>", "").Replace("</b>", "")
                             .Replace("<i>", "").Replace("</i>", "")
                             .Replace("$", "").Replace("[x]", "")
                             .Replace("\n", " ").Replace("\\n", " ");
                             
        // Compact duplicate spaces
        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }
        
        return cleaned.Trim();
    }


}
