using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Serilog;

namespace BaconTracker.Core;

public class BattlegroundsCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public int TechLevel { get; set; }

    [JsonPropertyName("hero")]
    public bool IsHero { get; set; }

    [JsonPropertyName("atk")]
    public int Attack { get; set; }

    [JsonPropertyName("hp")]
    public int Health { get; set; }

    [JsonPropertyName("tribes")]
    public List<string>? Tribes { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("active")]
    public bool IsPoolMinion { get; set; }
}

public static class CardDatabase
{
    private static readonly Dictionary<string, BattlegroundsCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    private class RawCard
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

    private static string NormalizeTribe(string tribe)
    {
        if (string.IsNullOrEmpty(tribe)) return string.Empty;
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
        string cleaned = text.Replace("<b>", "").Replace("</b>", "")
                             .Replace("<i>", "").Replace("</i>", "")
                             .Replace("$", "").Replace("[x]", "")
                             .Replace("\n", " ").Replace("\\n", " ");
        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }
        return cleaned.Trim();
    }
    
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_cards.Count > 0) return;

            string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaconTracker");
            string cachePath = Path.Combine(localDir, "bg_cards.json");

            if (!File.Exists(cachePath))
            {
                try
                {
                    Log.Information("bg_cards.json not found in cache. Downloading raw cards database from HearthstoneJSON...");
                    if (!Directory.Exists(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BaconTracker/1.0 (https://github.com/bacontracker)");
                    
                    const string hearthstoneJsonUrl = "https://api.hearthstonejson.com/v1/latest/enUS/cards.json";
                    var responseBytes = client.GetByteArrayAsync(hearthstoneJsonUrl).GetAwaiter().GetResult();
                    
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawCards = JsonSerializer.Deserialize<List<RawCard>>(responseBytes, options);

                    if (rawCards != null)
                    {
                        var bgCards = new List<BattlegroundsCard>();
                        foreach (var card in rawCards)
                        {
                            bool isBgHero = card.BattlegroundsHero == true;
                            bool isBgMinion = card.IsBattlegroundsPoolMinion == true || card.TechLevel.HasValue;
                            bool isBgCardId = card.Id.StartsWith("BG", StringComparison.OrdinalIgnoreCase) || 
                                              card.Id.StartsWith("TB_BaconShop_", StringComparison.OrdinalIgnoreCase);

                            if (isBgHero || isBgMinion || isBgCardId)
                            {
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

                                var minified = new BattlegroundsCard
                                {
                                    Id = card.Id,
                                    Name = card.Name,
                                    Type = card.Type,
                                    TechLevel = card.TechLevel ?? 0,
                                    IsHero = isBgHero,
                                    Attack = card.Attack ?? 0,
                                    Health = card.Health ?? 0,
                                    Tribes = tribesSet.Count > 0 ? new List<string>(tribesSet) : null,
                                    Text = string.IsNullOrEmpty(card.Text) ? null : CleanCardText(card.Text),
                                    IsPoolMinion = card.IsBattlegroundsPoolMinion == true
                                };

                                bgCards.Add(minified);
                            }
                        }

                        var serializeOptions = new JsonSerializerOptions
                        {
                            WriteIndented = false,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        };
                        string minifiedJson = JsonSerializer.Serialize(bgCards, serializeOptions);
                        File.WriteAllText(cachePath, minifiedJson);
                        Log.Information("Successfully compiled and saved bg_cards.json to cache ({Count} cards).", bgCards.Count);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to download or compile card database at startup");
                }
            }

            if (File.Exists(cachePath))
            {
                try
                {
                    string json = File.ReadAllText(cachePath);
                    var list = JsonSerializer.Deserialize<List<BattlegroundsCard>>(json);
                    if (list != null)
                    {
                        foreach (var card in list)
                        {
                            _cards[card.Id] = card;
                        }
                        Log.Information("Successfully initialized CardDatabase with {Count} cards from: {Path}", _cards.Count, cachePath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load card database from {Path}", cachePath);
                }
            }
            else
            {
                Log.Warning("No card database found at startup (and dynamic download failed).");
            }
        }
    }

    public static BattlegroundsCard? GetCard(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock)
        {
            _cards.TryGetValue(id, out var card);
            return card;
        }
    }

    public static IEnumerable<BattlegroundsCard> GetAllCards()
    {
        lock (_lock)
        {
            return new List<BattlegroundsCard>(_cards.Values);
        }
    }

    public static async Task UpdateDatabaseAsync(string cdnUrl)
    {
        try
        {
            using var client = new HttpClient();
            Log.Information("Fetching database updates from: {CdnUrl}", cdnUrl);
            string json = await client.GetStringAsync(cdnUrl);

            // Validate that it parses correctly before saving it
            var list = JsonSerializer.Deserialize<List<BattlegroundsCard>>(json);
            if (list == null || list.Count == 0)
            {
                throw new Exception("Downloaded database is empty or invalid.");
            }

            string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaconTracker");
            if (!Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }
            string cachePath = Path.Combine(localDir, "bg_cards.json");

            await File.WriteAllTextAsync(cachePath, json);

            lock (_lock)
            {
                _cards.Clear();
                foreach (var card in list)
                {
                    _cards[card.Id] = card;
                }
            }
            Log.Information("Database successfully updated and reloaded ({Count} cards cached).", list.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database update failed");
        }
    }
}
