using System;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using BaconTracker.Core;

namespace BaconTracker.App;

public class LobbyAnalystPanel : OverlayPanel
{
    private bool _analyzeOpponents = true;

    public LobbyAnalystPanel() : base("Lobby Analyst Panel")
    {
        DefaultPosition = new Vector2(30, 80);
        DefaultSize = new Vector2(450, 400);
    }

    public override bool ShouldDisplay(GameState state)
    {
        _lastState = state;
        // Only show during active game sessions or hero selection
        return state.CurrentMode == GameMode.InGame || state.CurrentMode == GameMode.HeroSelection;
    }

    protected override void OnDraw()
    {
        var state = PanelRegistry.ActivePanels.Count > 0 ? GetCurrentGameState() : null;
        if (state == null)
        {
            ImGui.Text("No active game state data.");
            return;
        }

        // Header info
        ImGui.TextColored(new Vector4(0.0f, 0.75f, 1.0f, 1.0f), $"Bacon Tracker - Turn {state.CurrentTurn}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 100);
        ImGui.Text($"Gold: {state.PlayerGold}/10");
        
        ImGui.Separator();

        // Local Player Hero Details
        ImGui.Text($"Local Hero: {state.HeroName}");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.2f, 1.0f), $" HP: {state.Health} (+{state.Armor} Armor)");

        ImGui.Spacing();

        ImGui.Checkbox("Show Opponent Insights", ref _analyzeOpponents);
        ImGui.Separator();

        if (!_analyzeOpponents)
        {
            ImGui.TextDisabled("Insights deactivated by user.");
            return;
        }

        var opponents = state.Opponents.Values.ToList();
        if (opponents.Count == 0)
        {
            ImGui.TextDisabled("Waiting for lobby logs...");
            ImGui.BulletText("Tavern tiers and triples will appear");
            ImGui.BulletText("as opponents trigger actions.");
            return;
        }

        // Render Lobby Table
        if (ImGui.BeginTable("OpponentsTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            // Table Headers
            ImGui.TableSetupColumn("Opponent / Hero", ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGui.TableSetupColumn("HP (+Arm)", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, 55.0f);
            ImGui.TableSetupColumn("Triples", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableHeadersRow();

            foreach (var opponent in opponents)
            {
                ImGui.TableNextRow();

                // Col 1: Opponent Name / Hero
                ImGui.TableSetColumnIndex(0);
                string displayLabel = opponent.HeroName != "Unknown Hero" ? opponent.HeroName : opponent.Name;
                ImGui.Text(displayLabel);
                if (ImGui.IsItemHovered() && opponent.HeroName != "Unknown Hero")
                {
                    ImGui.SetTooltip($"Player: {opponent.Name}\nHero Code: {opponent.HeroCardId}");
                }

                // Col 2: Health + Armor
                ImGui.TableSetColumnIndex(1);
                Vector4 hpColor = opponent.IsDead ? new Vector4(0.5f, 0.5f, 0.5f, 1.0f) :
                                  opponent.Health > 15 ? new Vector4(0.2f, 0.9f, 0.2f, 1.0f) :
                                  new Vector4(0.9f, 0.2f, 0.2f, 1.0f);

                if (opponent.IsDead)
                {
                    ImGui.TextColored(hpColor, "ELIMINATED");
                }
                else
                {
                    string hpText = opponent.Armor > 0 ? $"{opponent.Health} (+{opponent.Armor})" : $"{opponent.Health}";
                    ImGui.TextColored(hpColor, hpText);
                }

                // Col 3: Tavern Tier
                ImGui.TableSetColumnIndex(2);
                string romanTier = ConvertToRoman(opponent.TavernTier);
                ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.0f, 1.0f), $" {romanTier}");
                if (ImGui.IsItemHovered())
                {
                    if (opponent.TierUpgradeTurns.Count > 0)
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Tavern Upgrade History:");
                        foreach (var upgrade in opponent.TierUpgradeTurns.OrderBy(k => k.Key))
                        {
                            ImGui.BulletText($"Turn {upgrade.Key}: Reached Tier {ConvertToRoman(upgrade.Value)}");
                        }
                        ImGui.EndTooltip();
                    }
                    else
                    {
                        ImGui.SetTooltip("No recorded tier changes.");
                    }
                }

                // Col 4: Triples Count
                ImGui.TableSetColumnIndex(3);
                if (opponent.TriplesCount > 0)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.0f, 1.0f), $"  ★ {opponent.TriplesCount}");
                }
                else
                {
                    ImGui.Text("   -");
                }
            }

            ImGui.EndTable();
        }
    }

    private GameState GetCurrentGameState()
    {
        // Reflection or central retrieval: since Gtk4Window holds the GameState instance, 
        // we can fetch it. To be clean, let's fetch it from the window or define a singleton/registry helper.
        // Wait! In BaconTracker, how do panels query GameState?
        // Let's check WaylandGtk4Window.cs lines 298-300:
        //   if (panel.IsVisible && panel.ShouldDisplay(_gameState))
        // So the GameState is passed to panel.ShouldDisplay(GameState state).
        // Let's modify OverlayPanel.cs to cache the last-received GameState or let OnDraw receive GameState, 
        // or we can pass it down!
        // Wait, let's check OverlayPanel.cs to see the signature of Draw!
        return _lastState;
    }

    private GameState _lastState = new();



    private static string ConvertToRoman(int number)
    {
        return number switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            _ => number.ToString()
        };
    }
}
