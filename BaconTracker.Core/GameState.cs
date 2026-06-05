using System;
using System.Collections.Generic;

namespace BaconTracker.Core;

public enum GameMode
{
    Menu,
    HeroSelection,
    InGame,
    Spectating
}

public class OpponentInfo
{
    public string Name { get; set; } = "Unknown";
    public string HeroName { get; set; } = "Unknown Hero";
    public string HeroCardId { get; set; } = string.Empty;
    public int TavernTier { get; set; } = 1;
    public int TriplesCount { get; set; } = 0;
    public int Health { get; set; } = 30;
    public int Armor { get; set; } = 0;
    public bool IsDead => Health <= 0;
    public List<string> KnownBoardMinions { get; } = new();
    public Dictionary<int, int> TierUpgradeTurns { get; } = new(); // Turn -> Tier
}

public class GameState
{
    public GameMode CurrentMode { get; set; } = GameMode.Menu;
    public int CurrentTurn { get; set; } = 1;
    public int PlayerGold { get; set; } = 3;
    public List<string> ActiveTribes { get; } = new();
    public List<string> BannedTribes { get; } = new();
    public int Health { get; set; } = 40;
    public int Armor { get; set; } = 0;
    public string HeroName { get; set; } = "Unknown Hero";
    public string HeroCardId { get; set; } = string.Empty;
    public string LocalPlayerName { get; set; } = string.Empty;
    
    // Stats trackers for current game session
    public int BloodGemsPlayed { get; set; } = 0;
    public int SpellsPlayed { get; set; } = 0;
    public int TriplesCreated { get; set; } = 0;

    // Track opponents in the current lobby
    public Dictionary<string, OpponentInfo> Opponents { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastBoardUpdateTurns = new(StringComparer.OrdinalIgnoreCase);

    public GameState()
    {
        // 1. Reset game state on game start
        EventBus.Subscribe("OnGameStart", args => Reset());

        // 2. Set local player name
        EventBus.Subscribe("OnLocalPlayerNameDetected", args => {
            if (args.Length > 0 && args[0] is string name)
            {
                LocalPlayerName = name;
                CurrentMode = GameMode.InGame;
            }
        });

        // 3. Update Turn
        EventBus.Subscribe("OnTurnChanged", args => {
            if (args.Length > 0 && args[0] is int turn)
            {
                CurrentTurn = turn;
                CurrentMode = GameMode.InGame;
            }
        });

        // 4. Update Gold
        EventBus.Subscribe("OnGoldChanged", args => {
            if (args.Length > 0 && args[0] is int gold)
            {
                PlayerGold = gold;
            }
        });

        // 5. Update Spell casts
        EventBus.Subscribe("OnSpellPlayed", args => {
            SpellsPlayed++;
        });

        // 6. Associate Hero choices/assignments
        EventBus.Subscribe("OnPlayerHeroSelected", args => {
            if (args.Length > 1 && args[0] is string name && args[1] is string cardId)
            {
                if (!string.IsNullOrEmpty(LocalPlayerName) && name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    HeroCardId = cardId;
                    HeroName = CardIdToName(cardId);
                }
                else
                {
                    var opponent = GetOrCreateOpponent(name);
                    opponent.HeroCardId = cardId;
                    opponent.HeroName = CardIdToName(cardId);
                }
            }
        });

        // 7. Update opponent Tavern Tier
        EventBus.Subscribe("OnOpponentTavernTierChanged", args => {
            if (args.Length > 1 && args[0] is string name && args[1] is int tier)
            {
                if (!string.IsNullOrEmpty(LocalPlayerName) && name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    // Track local player tech if needed
                }
                else if (name != "GameEntity" && !name.Contains("Player") && name != "1")
                {
                    var opponent = GetOrCreateOpponent(name);
                    opponent.TavernTier = tier;
                    opponent.TierUpgradeTurns[CurrentTurn] = tier;
                }
            }
        });

        // 8. Update opponent Triples count
        EventBus.Subscribe("OnOpponentTriplesChanged", args => {
            if (args.Length > 1 && args[0] is string name && args[1] is int triples)
            {
                if (!string.IsNullOrEmpty(LocalPlayerName) && name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    TriplesCreated = triples;
                }
                else if (name != "GameEntity" && !name.Contains("Player") && name != "1")
                {
                    var opponent = GetOrCreateOpponent(name);
                    opponent.TriplesCount = triples;
                }
            }
        });

        // 9. Update opponent Health/Armor
        EventBus.Subscribe("OnOpponentHealthChanged", args => {
            if (args.Length > 2 && args[0] is string name && args[1] is int health && args[2] is int armor)
            {
                if (!string.IsNullOrEmpty(LocalPlayerName) && name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    if (health != -1) Health = health;
                    if (armor != -1) Armor = armor;
                }
                else if (name != "GameEntity" && !name.Contains("Player") && name != "1")
                {
                    var opponent = GetOrCreateOpponent(name);
                    if (health != -1) opponent.Health = health;
                    if (armor != -1) opponent.Armor = armor;
                }
            }
        });

        // 10. Update opponent board minions
        EventBus.Subscribe("OnMinionBoardSummon", args => {
            if (args.Length > 1 && args[0] is string name && args[1] is string cardId)
            {
                int turn = CurrentTurn;
                
                // We only track opponent boards for sidebar hover display
                if (name != "GameEntity" && !name.Contains("Player") && name != "1" &&
                    (string.IsNullOrEmpty(LocalPlayerName) || !name.Equals(LocalPlayerName, StringComparison.OrdinalIgnoreCase)))
                {
                    var opponent = GetOrCreateOpponent(name);
                    var board = opponent.KnownBoardMinions;
                    
                    // If this is the first minion seen for this opponent in this turn, clear their board list
                    string key = opponent.Name;
                    if (!_lastBoardUpdateTurns.TryGetValue(key, out int lastTurn) || lastTurn < turn)
                    {
                        board.Clear();
                        _lastBoardUpdateTurns[key] = turn;
                    }
                    
                    // Add minion if not already present
                    if (!board.Contains(cardId))
                    {
                        board.Add(cardId);
                    }
                }
            }
        });
    }

    private OpponentInfo GetOrCreateOpponent(string name)
    {
        if (!Opponents.TryGetValue(name, out var opponent))
        {
            opponent = new OpponentInfo { Name = name };
            Opponents[name] = opponent;
        }
        return opponent;
    }

    private static string CardIdToName(string cardId)
    {
        if (string.IsNullOrEmpty(cardId)) return "Unknown Hero";
        
        var card = CardDatabase.GetCard(cardId);
        if (card != null && !string.IsNullOrEmpty(card.Name))
        {
            return card.Name;
        }
        
        return cardId switch
        {
            "BG20_HERO_301" => "Sire Denathrius",
            "BG20_HERO_101" => "Sylvanas Windrunner",
            "BG20_HERO_201" => "The Jailer",
            "BG20_HERO_242" => "Murloc Holmes",
            "TB_BaconShop_HERO_01" => "Edwin VanCleef",
            "TB_BaconShop_HERO_02" => "The Rat King",
            "TB_BaconShop_HERO_08" => "Ragnaros the Firelord",
            "TB_BaconShop_HERO_10" => "Patchwerk",
            "TB_BaconShop_HERO_11" => "The Great Akazamzarak",
            "TB_BaconShop_HERO_12" => "Dancin' Deryl",
            "TB_BaconShop_HERO_14" => "Lord Jaraxxus",
            "TB_BaconShop_HERO_16" => "King Mukla",
            "TB_BaconShop_HERO_18" => "Pyramad",
            "TB_BaconShop_HERO_21" => "Sir Finley Mrrgglton",
            "TB_BaconShop_HERO_22" => "Elise Starseeker",
            "TB_BaconShop_HERO_23" => "Sindragosa",
            "TB_BaconShop_HERO_25" => "Brann Bronzebeard",
            "TB_BaconShop_HERO_27" => "Sneed",
            "TB_BaconShop_HERO_28" => "Millificent Manastorm",
            "TB_BaconShop_HERO_29" => "Ysera",
            "TB_BaconShop_HERO_30" => "Fungalmancer Flurgl",
            "TB_BaconShop_HERO_31" => "Alexstrasza",
            "TB_BaconShop_HERO_33" => "Arch-Villain Rafaam",
            "TB_BaconShop_HERO_34" => "Millhouse Manastorm",
            "TB_BaconShop_HERO_35" => "Tess Greymane",
            "TB_BaconShop_HERO_36" => "Deathwing",
            "TB_BaconShop_HERO_37" => "Yogg-Saron, Hope's End",
            "TB_BaconShop_HERO_38" => "Kael'thas Sunstrider",
            "TB_BaconShop_HERO_39" => "Nozdormu",
            "TB_BaconShop_HERO_41" => "Aranna Starseeker",
            "TB_BaconShop_HERO_42" => "The Curator",
            "TB_BaconShop_HERO_43" => "Illidan Stormrage",
            "TB_BaconShop_HERO_44" => "Maiev Shadowsong",
            "TB_BaconShop_HERO_45" => "Captain Eudora",
            "TB_BaconShop_HERO_47" => "Captain Hooktusk",
            "TB_BaconShop_HERO_49" => "Skycap'n Kragg",
            "TB_BaconShop_HERO_50" => "Mr. Bigglesworth",
            "TB_BaconShop_HERO_52" => "Jandice Barov",
            "TB_BaconShop_HERO_53" => "Lord Barov",
            "TB_BaconShop_HERO_55" => "Forest Warden Omu",
            "TB_BaconShop_HERO_56" => "Chenvaala",
            "TB_BaconShop_HERO_57" => "Al'Akir",
            "TB_BaconShop_HERO_58" => "Ragnaros",
            "TB_BaconShop_HERO_59" => "Zephrys, the Great",
            "TB_BaconShop_HERO_60" => "Silas Darkmoon",
            "TB_BaconShop_HERO_62" => "Y'Shaarj",
            "TB_BaconShop_HERO_64" => "Tickatus",
            "TB_BaconShop_HERO_65" => "Greybough",
            "TB_BaconShop_HERO_67" => "Overlord Saurfang",
            "TB_BaconShop_HERO_68" => "Death Speaker Blackthorn",
            "TB_BaconShop_HERO_70" => "Vol'jin",
            "TB_BaconShop_HERO_71" => "Xyrella",
            "TB_BaconShop_HERO_72" => "Mutanus the Devourer",
            "TB_BaconShop_HERO_74" => "Guff Runetotem",
            "TB_BaconShop_HERO_76" => "Kurtrus Ashfallen",
            "TB_BaconShop_HERO_78" => "Trade Prince Gallywix",
            "TB_BaconShop_HERO_91" => "Cariel Roame",
            "TB_BaconShop_HERO_93" => "Sneed (Alternative)",
            "TB_BaconShop_HERO_94" => "Cookie the Cook",
            "TB_BaconShop_HERO_95" => "Tamsin Roame",
            "TB_BaconShop_HERO_98" => "Scabbs Cutterbutter",
            "TB_BaconShop_HERO_101" => "Sylvanas Windrunner (Shop)",
            _ => cardId
        };
    }

    public bool HasTribe(string tribeName)
    {
        return ActiveTribes.Contains(tribeName);
    }

    public void Reset()
    {
        CurrentTurn = 1;
        PlayerGold = 3;
        ActiveTribes.Clear();
        BannedTribes.Clear();
        Health = 40;
        Armor = 0;
        HeroName = "Unknown Hero";
        HeroCardId = string.Empty;
        LocalPlayerName = string.Empty;
        BloodGemsPlayed = 0;
        SpellsPlayed = 0;
        TriplesCreated = 0;
        Opponents.Clear();
        CurrentMode = GameMode.Menu;
    }
}

