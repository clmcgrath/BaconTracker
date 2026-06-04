using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using BaconTracker.Core;

namespace BaconTracker.App;

public static class Telemetry
{
    public static readonly ActivitySource Source = new("BaconTracker");
    public static readonly Meter Meter = new("BaconTracker");

    // Custom metrics
    public static readonly Counter<long> GamesStarted = Meter.CreateCounter<long>("bacontracker.games_started", description: "Number of games started");
    public static readonly Counter<long> GameEventsProcessed = Meter.CreateCounter<long>("bacontracker.events_processed", description: "Number of log/game events processed");
    public static readonly Counter<long> CardArtRequests = Meter.CreateCounter<long>("bacontracker.card_art_requests", description: "Number of card art requests");
    public static readonly Counter<long> CardArtCacheHits = Meter.CreateCounter<long>("bacontracker.card_art_cache_hits", description: "Number of card art requests served from cache");
    public static readonly Counter<long> CardArtCacheMisses = Meter.CreateCounter<long>("bacontracker.card_art_cache_misses", description: "Number of card art requests resulting in download");

    public static void Initialize()
    {
        EventBus.Subscribe("OnGameStart", args =>
        {
            GamesStarted.Add(1);
            using var activity = Source.StartActivity("GameSession");
            activity?.SetTag("game.status", "Started");
        });

        EventBus.Subscribe("OnSpellPlayed", args => GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "SpellPlayed")));
        EventBus.Subscribe("OnTurnChanged", args =>
        {
            GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "TurnChanged"));
            if (args.Length > 0 && args[0] is int turn)
            {
                using var activity = Source.StartActivity("Turn");
                activity?.SetTag("game.turn", turn);
            }
        });
        EventBus.Subscribe("OnGoldChanged", args => GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "GoldChanged")));
        EventBus.Subscribe("OnOpponentTavernTierChanged", args => GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "OpponentTavernTierChanged")));
        EventBus.Subscribe("OnOpponentTriplesChanged", args => GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "OpponentTriplesChanged")));
        EventBus.Subscribe("OnOpponentHealthChanged", args => GameEventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", "OpponentHealthChanged")));
    }
}
