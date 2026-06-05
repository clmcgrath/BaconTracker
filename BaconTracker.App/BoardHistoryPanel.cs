using System;
using System.Numerics;
using ImGuiNET;
using BaconTracker.Core;

namespace BaconTracker.App;

public class BoardHistoryPanel : OverlayPanel
{
    private static BoardHistoryPanel? _instance;
    public static BoardHistoryPanel Instance => _instance ?? throw new InvalidOperationException("BoardHistoryPanel not registered.");

    public string? SelectedOpponentName { get; set; }
    private readonly GameState _gameState;

    public override bool IsInteractive => true; // Allows the user to move and close the window

    public BoardHistoryPanel(GameState gameState) : base("Opponent Board History")
    {
        _gameState = gameState;
        _instance = this;
        IsVisible = false; // Hidden by default, opens when user clicks a Board button
        DefaultPosition = new Vector2(500, 300);
        DefaultSize = new Vector2(600, 180);
    }

    public override bool ShouldDisplay(GameState state)
    {
        // Only show if a game is active
        return state.CurrentMode == GameMode.InGame || state.CurrentMode == GameMode.HeroSelection;
    }

    protected override void OnDraw()
    {
        if (string.IsNullOrEmpty(SelectedOpponentName))
        {
            ImGui.Text("Select an opponent in the Lobby Analyst to view their last known board.");
            return;
        }

        if (!_gameState.Opponents.TryGetValue(SelectedOpponentName, out var opponent))
        {
            ImGui.Text($"Opponent '{SelectedOpponentName}' not found in current game session.");
            return;
        }

        // Draw header info
        ImGui.TextColored(new Vector4(0f, 0.75f, 1f, 1f), $"{opponent.Name} ({opponent.HeroName}) - Last Seen Board");
        ImGui.Separator();

        var board = opponent.KnownBoardMinions;
        if (board.Count == 0)
        {
            ImGui.Text("No board history recorded for this opponent yet.");
            return;
        }

        float cardWidth = 72f;
        float cardHeight = 96f;
        var drawList = ImGui.GetWindowDrawList();

        for (int i = 0; i < board.Count; i++)
        {
            string cardId = board[i];
            var card = CardDatabase.GetCard(cardId);
            if (card == null) continue;

            // Get texture pointer from AssetManager
            IntPtr texId = AssetManager.Instance.GetCardArt(cardId);
            Vector2 cursor = ImGui.GetCursorScreenPos();
            Vector2 size = new Vector2(cardWidth, cardHeight);

            // Draw image or a loading placeholder
            if (texId != IntPtr.Zero)
            {
                ImGui.Image(texId, size);
            }
            else
            {
                // Placeholder border when texture is downloading
                ImGui.Button("Loading...", size);
            }

            // Capture bounding rect coordinates for drawings
            Vector2 min = cursor;
            Vector2 max = cursor + size;

            // Draw tooltip on card hover
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), card.Name);
                if (cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), "(Golden)");
                }
                ImGui.Text($"Tier {card.TechLevel}");
                if (card.Tribes != null && card.Tribes.Count > 0)
                {
                    ImGui.Text($"Tribe: {string.Join(", ", card.Tribes)}");
                }
                ImGui.Separator();
                if (!string.IsNullOrEmpty(card.Text))
                {
                    ImGui.PushTextWrapPos(250f);
                    ImGui.TextUnformatted(card.Text);
                    ImGui.PopTextWrapPos();
                }
                ImGui.EndTooltip();
            }

            // Draw golden border outline if the card is golden
            bool isGolden = cardId.EndsWith("_G", StringComparison.OrdinalIgnoreCase);
            if (isGolden)
            {
                uint goldColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.84f, 0.0f, 0.9f));
                drawList.AddRect(min, max, goldColor, 4.0f, ImDrawFlags.None, 2.5f);
            }
            else
            {
                uint darkBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 0.6f));
                drawList.AddRect(min, max, darkBorder, 4.0f, ImDrawFlags.None, 1.0f);
            }

            // Overlay stats bubbles on the bottom corners of the card art
            int attack = card.Attack;
            int health = card.Health;

            // Draw Attack Bubble (Bottom-Left)
            Vector2 atkCenter = new Vector2(min.X + 12f, max.Y - 12f);
            float bubbleRadius = 9f;
            uint bubbleBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.75f));
            drawList.AddCircleFilled(atkCenter, bubbleRadius, bubbleBg);
            
            string atkStr = attack.ToString();
            Vector2 atkTextSize = ImGui.CalcTextSize(atkStr);
            Vector2 atkTextPos = atkCenter - (atkTextSize / 2f);
            uint atkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0.2f, 1f)); // Yellow
            drawList.AddText(atkTextPos, atkColor, atkStr);

            // Draw Health Bubble (Bottom-Right)
            Vector2 hpCenter = new Vector2(max.X - 12f, max.Y - 12f);
            drawList.AddCircleFilled(hpCenter, bubbleRadius, bubbleBg);

            string hpStr = health.ToString();
            Vector2 hpTextSize = ImGui.CalcTextSize(hpStr);
            Vector2 hpTextPos = hpCenter - (hpTextSize / 2f);
            uint hpColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.2f, 0.2f, 1f)); // Red
            drawList.AddText(hpTextPos, hpColor, hpStr);

            // Set up next card horizontally
            if (i < board.Count - 1)
            {
                ImGui.SameLine(0f, 6f);
            }
        }
    }
}
