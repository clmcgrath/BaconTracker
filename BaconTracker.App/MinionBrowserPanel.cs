using System;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using ImGuiNET;
using BaconTracker.Core;

namespace BaconTracker.App;

public class MinionBrowserPanel : OverlayPanel
{
    private int _selectedTier = 0; // 0 = All
    private string _selectedTribe = "All";
    private string _selectedPower = "All";
    private string _searchQuery = string.Empty;
    private bool _isDuos = false;

    private static readonly string[] TribesList = new[]
    {
        "All", "Beast", "Demon", "Dragon", "Elemental", "Mech", "Murloc", "Naga", "Pirate", "Quilboar", "Undead", "Neutral"
    };

    private static readonly string[] PowersList = new[]
    {
        "All", "Battlecry", "Deathrattle", "End of Turn", "Start of Combat", "Taunt", "Divine Shield", "Reborn", "Magnetic", "Spellcraft", "Avenge", "Buddies", "Trinkets", "Tavern Spells", "Spellcraft Spells", "Battlecruiser", "Timewarped"
    };

    public override bool IsInteractive => true;

    public MinionBrowserPanel() : base("Minion Browser")
    {
        Anchor = PanelAnchor.TopRight;
        AnchorOffset = new Vector2(-30, 80);
        DefaultSize = new Vector2(480, 500);
    }

    protected override void OnDraw()
    {
        // Title Header
        ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), "Battlegrounds Minion Browser");
        ImGui.Separator();

        // 1. Search Bar
        ImGui.Text("Search:");
        ImGui.SameLine();
        ImGui.InputTextWithHint("##MinionSearch", "Type minion name or text...", ref _searchQuery, 100);

        ImGui.Spacing();

        // 2. Tavern Tier Filters (Horizontal Buttons)
        ImGui.Text("Tavern Tier:");
        ImGui.SameLine();
        for (int t = 1; t <= 7; t++)
        {
            bool active = _selectedTier == t;
            if (active)
            {
                // Highlight active tier in gold style
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.9f, 0.65f, 0.0f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.75f, 0.1f, 1.0f));
            }

            if (ImGui.Button($"{t}"))
            {
                _selectedTier = active ? 0 : t; // Toggle off if clicked again
            }

            if (active)
            {
                ImGui.PopStyleColor(2);
            }
            ImGui.SameLine();
        }
        
        if (ImGui.Button("All"))
        {
            _selectedTier = 0;
        }

        ImGui.Spacing();

        // 3. Tribe & Power Filter Combo Boxes
        ImGui.Text("Tribe:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (ImGui.BeginCombo("##TribeFilterCombo", _selectedTribe))
        {
            foreach (var tribe in TribesList)
            {
                bool isSelected = _selectedTribe == tribe;
                if (ImGui.Selectable(tribe, isSelected))
                {
                    _selectedTribe = tribe;
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        ImGui.Text("Power:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("##PowerFilterCombo", _selectedPower))
        {
            foreach (var power in PowersList)
            {
                bool isSelected = _selectedPower == power;
                if (ImGui.Selectable(power, isSelected))
                {
                    _selectedPower = power;
                }
                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Duos", ref _isDuos);

        ImGui.Separator();

        // 4. Load, Filter and Sort Minions
        var allMinions = CardDatabase.GetAllCards()
            .Where(c => !c.Id.EndsWith("_G"));

        if (!_isDuos)
        {
            allMinions = allMinions.Where(c => !c.Id.StartsWith("BGDUO_", StringComparison.OrdinalIgnoreCase));
        }

        if (_selectedPower == "Buddies")
        {
            allMinions = allMinions.Where(c => c.Type.Equals("BATTLEGROUND_HERO_BUDDY", StringComparison.OrdinalIgnoreCase) || 
                                               c.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase));
        }
        else if (_selectedPower == "Trinkets")
        {
            allMinions = allMinions.Where(c => c.Type.Equals("BATTLEGROUND_TRINKET", StringComparison.OrdinalIgnoreCase));
        }
        else if (_selectedPower == "Tavern Spells")
        {
            allMinions = allMinions.Where(c => c.Type.Equals("BATTLEGROUND_SPELL", StringComparison.OrdinalIgnoreCase) && !IsBattlecruiser(c));
        }
        else if (_selectedPower == "Spellcraft Spells")
        {
            allMinions = allMinions.Where(c => IsSpellcraftSpell(c));
        }
        else if (_selectedPower == "Battlecruiser")
        {
            allMinions = allMinions.Where(c => IsBattlecruiser(c));
        }
        else if (_selectedPower == "Timewarped")
        {
            allMinions = allMinions.Where(c => c.Name.Contains("Timewarped", StringComparison.OrdinalIgnoreCase) || 
                                               c.Name.Contains("Alternate Timeline", StringComparison.OrdinalIgnoreCase) || 
                                               c.Name.Contains("Warped Conflux", StringComparison.OrdinalIgnoreCase) ||
                                               c.Name.Contains("Timeworn", StringComparison.OrdinalIgnoreCase));
        }
        else if (_selectedPower != "All")
        {
            // Keyword power filter on standard pool minions
            allMinions = allMinions.Where(c => c.Type.Equals("MINION", StringComparison.OrdinalIgnoreCase) && c.IsPoolMinion);
            if (_selectedPower == "End of Turn")
            {
                allMinions = allMinions.Where(c => c.Text != null && 
                    c.Text.Contains("End of", StringComparison.OrdinalIgnoreCase) && 
                    c.Text.Contains("turn", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                allMinions = allMinions.Where(c => c.Text != null && 
                    c.Text.Contains(_selectedPower, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            // All standard pool minions
            allMinions = allMinions.Where(c => c.Type.Equals("MINION", StringComparison.OrdinalIgnoreCase) && c.IsPoolMinion);
        }

        // Apply filters
        if (_selectedTier > 0)
        {
            allMinions = allMinions.Where(c => GetEffectiveTier(c) == _selectedTier);
        }

        if (_selectedTribe != "All")
        {
            if (_selectedTribe == "Neutral")
            {
                allMinions = allMinions.Where(c => c.Tribes == null || c.Tribes.Count == 0);
            }
            else
            {
                allMinions = allMinions.Where(c => 
                    (c.Tribes != null && c.Tribes.Contains(_selectedTribe, StringComparer.OrdinalIgnoreCase)) ||
                    ((c.Text != null && c.Text.Contains(_selectedTribe, StringComparison.OrdinalIgnoreCase)) ||
                     c.Name.Contains(_selectedTribe, StringComparison.OrdinalIgnoreCase))
                );
            }
        }

        if (!string.IsNullOrEmpty(_searchQuery))
        {
            allMinions = allMinions.Where(c => 
                c.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                (c.Text != null && c.Text.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            );
        }

        // Sort by Tier ascending, then by Name
        var filteredList = allMinions.OrderBy(c => GetEffectiveTier(c)).ThenBy(c => c.Name).ToList();

        // 5. Render Scrollable Minion List
        ImGui.TextDisabled($"Showing {filteredList.Count} minions");
        
        ImGui.BeginChild("MinionScrollArea", new Vector2(0, 0));
        ImGui.SetWindowFontScale(1.2f); // Make list items larger and easier to read

        if (ImGui.BeginTable("MinionsTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("T", ImGuiTableColumnFlags.WidthFixed, 30.0f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.5f);
            ImGui.TableSetupColumn("Tribes", ImGuiTableColumnFlags.WidthStretch, 1.5f);
            ImGui.TableHeadersRow();

            foreach (var minion in filteredList)
            {
                ImGui.TableNextRow();

                // Tier Column
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), GetEffectiveTier(minion).ToString());

                // Name Column
                ImGui.TableSetColumnIndex(1);
                ImGui.Text(minion.Name);
                
                // Show rich tooltip when hovering over the name
                if (ImGui.IsItemHovered())
                {
                    IntPtr cardArtTex = AssetManager.Instance.GetCardArt(minion.Id);
                    
                    if (cardArtTex != IntPtr.Zero)
                    {
                        // Image-based tooltip
                        ImGui.BeginTooltip();
                        
                        if (AssetManager.Instance.IsFullCardRender(minion.Id))
                        {
                            // If we have a full card render, display it directly at standard size
                            ImGui.Image(cardArtTex, new Vector2(256, 384));
                        }
                        else
                        {
                            // Fallback using raw character art: show Plain & Golden side-by-side with frames and text
                            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), minion.Name);
                            ImGui.Spacing();

                            var goldenCard = CardDatabase.GetCard(minion.Id + "_G");

                            // Plain Card with stylized border frame
                            ImGui.BeginGroup();
                            ImGui.TextColored(new Vector4(0.0f, 0.8f, 1.0f, 1.0f), "Plain");
                            
                            Vector2 startPosP = ImGui.GetCursorScreenPos();
                            Vector2 imageSizeP = new Vector2(160, 240);
                            Vector2 frameSizeP = imageSizeP + new Vector2(8, 8);
                            
                            // Reserve layout space for image frame
                            ImGui.Dummy(frameSizeP);
                            
                            var drawListP = ImGui.GetWindowDrawList();
                            uint frameColorP = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.20f, 1.0f));
                            uint borderColorP = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.30f, 0.40f, 1.0f));
                            
                            // Outer frame backing
                            drawListP.AddRectFilled(startPosP, startPosP + frameSizeP, frameColorP, 6.0f);
                            drawListP.AddRect(startPosP, startPosP + frameSizeP, borderColorP, 6.0f, ImDrawFlags.None, 2.0f);
                            
                            // Draw image inside (DrawList AddImage avoids layout side-effects)
                            drawListP.AddImage(cardArtTex, startPosP + new Vector2(4, 4), startPosP + new Vector2(4, 4) + imageSizeP);
                            
                            ImGui.Spacing();
                            if (minion.Type == "MINION" || minion.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase))
                            {
                                ImGui.Text($"Stats: {minion.Attack}/{minion.Health}");
                            }
                            else
                            {
                                ImGui.Dummy(new Vector2(0, 15));
                            }
                            ImGui.EndGroup();

                            ImGui.SameLine();
                            ImGui.Spacing();
                            ImGui.SameLine();

                            // Golden Card with stylized thick golden frame
                            ImGui.BeginGroup();
                            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), "Golden ★");
                            
                            Vector2 startPosG = ImGui.GetCursorScreenPos();
                            Vector2 imageSizeG = new Vector2(160, 240);
                            Vector2 frameSizeG = imageSizeG + new Vector2(8, 8);
                            
                            // Reserve layout space for image frame
                            ImGui.Dummy(frameSizeG);
                            
                            var drawListG = ImGui.GetWindowDrawList();
                            uint goldColorG = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.84f, 0.0f, 1.0f));
                            uint darkGoldColorG = ImGui.ColorConvertFloat4ToU32(new Vector4(0.60f, 0.45f, 0.0f, 1.0f));
                            
                            // Thick golden frame backing
                            drawListG.AddRectFilled(startPosG, startPosG + frameSizeG, darkGoldColorG, 6.0f);
                            drawListG.AddRect(startPosG, startPosG + frameSizeG, goldColorG, 6.0f, ImDrawFlags.None, 3.0f);
                            
                            // Draw image inside
                            drawListG.AddImage(cardArtTex, startPosG + new Vector2(4, 4), startPosG + new Vector2(4, 4) + imageSizeG);
                            
                            // Draw an inner golden border line on top of the image to blend it nicely
                            drawListG.AddRect(startPosG + new Vector2(4, 4), startPosG + new Vector2(4, 4) + imageSizeG, goldColorG, 0.0f, ImDrawFlags.None, 1.5f);
                            
                            ImGui.Spacing();
                            if (goldenCard != null)
                            {
                                if (goldenCard.Type == "MINION" || goldenCard.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase))
                                {
                                    ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), $"Stats: {goldenCard.Attack}/{goldenCard.Health}");
                                }
                                else
                                {
                                    ImGui.Dummy(new Vector2(0, 15));
                                }
                            }
                            else
                            {
                                if (minion.Type == "MINION" || minion.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase))
                                {
                                    ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), $"Stats: {minion.Attack * 2}/{minion.Health * 2}");
                                }
                                else
                                {
                                    ImGui.Dummy(new Vector2(0, 15));
                                }
                            }
                            ImGui.EndGroup();

                            // Append Card Descriptions below the preview images so text is never lost
                            if (!string.IsNullOrEmpty(minion.Text) || (goldenCard != null && !string.IsNullOrEmpty(goldenCard.Text)))
                            {
                                ImGui.Spacing();
                                ImGui.Separator();
                                ImGui.Spacing();
                                
                                if (!string.IsNullOrEmpty(minion.Text))
                                {
                                    ImGui.TextColored(new Vector4(0.0f, 0.8f, 1.0f, 1.0f), "Effect:");
                                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 320.0f);
                                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.7f, 1.0f), minion.Text);
                                    ImGui.PopTextWrapPos();
                                }
                                
                                if (goldenCard != null && !string.IsNullOrEmpty(goldenCard.Text) && goldenCard.Text != minion.Text)
                                {
                                    ImGui.Spacing();
                                    ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), "Golden Effect ★:");
                                    ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 320.0f);
                                    ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.5f, 1.0f), goldenCard.Text);
                                    ImGui.PopTextWrapPos();
                                }
                            }
                        }

                        ImGui.EndTooltip();
                    }
                    else
                    {
                        // Text-based fallback tooltip
                        ImGui.BeginTooltip();
                        
                        ImGui.TextColored(new Vector4(0.0f, 0.8f, 1.0f, 1.0f), minion.Name);
                        
                        string cardTypeText = minion.Type switch
                        {
                            "BATTLEGROUND_TRINKET" => "Trinket",
                            "BATTLEGROUND_SPELL" or "SPELL" => $"Tier {GetEffectiveTier(minion)} Spell",
                            _ => minion.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase) ? $"Tier {GetEffectiveTier(minion)} Buddy" : $"Tier {GetEffectiveTier(minion)} Minion"
                        };
                        ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), cardTypeText);

                        if (minion.Type == "MINION" || minion.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase))
                        {
                            ImGui.Text($"Stats: {minion.Attack} / {minion.Health}");
                        }
                        
                        string tribesText = minion.Tribes != null && minion.Tribes.Count > 0 
                            ? string.Join(", ", minion.Tribes) 
                            : "Neutral";
                        ImGui.Text($"Tribes: {tribesText}");

                        var goldenCard = CardDatabase.GetCard(minion.Id + "_G");
                        if (goldenCard != null)
                        {
                            ImGui.Separator();
                            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.0f, 1.0f), "Golden Version ★:");
                            if (goldenCard.Type == "MINION" || goldenCard.Id.Contains("_Buddy", StringComparison.OrdinalIgnoreCase))
                            {
                                ImGui.Text($"Stats: {goldenCard.Attack} / {goldenCard.Health}");
                            }
                            if (!string.IsNullOrEmpty(goldenCard.Text))
                            {
                                ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 300.0f);
                                ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.5f, 1.0f), goldenCard.Text);
                                ImGui.PopTextWrapPos();
                            }
                        }

                        if (!string.IsNullOrEmpty(minion.Text))
                        {
                            ImGui.Separator();
                            ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 300.0f);
                            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.7f, 1.0f), minion.Text);
                            ImGui.PopTextWrapPos();
                        }

                        if (AssetManager.Instance.HasCardArtFailed(minion.Id))
                        {
                            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "[Card Art Not Available]");
                        }
                        else
                        {
                            ImGui.TextDisabled("[Card Art Loading...]");
                        }

                        ImGui.EndTooltip();
                    }
                }

                // Tribes Column
                ImGui.TableSetColumnIndex(2);
                if (minion.Tribes != null && minion.Tribes.Count > 0)
                {
                    ImGui.Text(string.Join(", ", minion.Tribes));
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Neutral");
                }
            }

            ImGui.EndTable();
        }

        ImGui.SetWindowFontScale(1.0f); // Reset font scale
        ImGui.EndChild();
    }

    private int GetEffectiveTier(BattlegroundsCard c)
    {
        if (c.TechLevel > 0) return c.TechLevel;

        // Try to get tier from parent minion if c is a token/spell
        var parent = GetParentMinion(c);
        if (parent != null)
        {
            return parent.TechLevel;
        }

        // Special fallbacks for Timewarped cards with no parent (default to tier 3 or 5)
        if (c.Id.Equals("BG34_HERO_000p", StringComparison.OrdinalIgnoreCase)) return 3; // Alternate Timeline
        if (c.Id.Equals("BG34_HERO_004p", StringComparison.OrdinalIgnoreCase)) return 5; // Warped Conflux
        if (c.Id.Equals("BG34_BlackMarket_Skip", StringComparison.OrdinalIgnoreCase)) return 3; // Exit the Timewarped Tavern

        return c.TechLevel;
    }

    private BattlegroundsCard? GetParentMinion(BattlegroundsCard c)
    {
        if (c.Type.Equals("MINION", StringComparison.OrdinalIgnoreCase)) return null;

        string baseId = c.Id;
        int gtIndex = baseId.IndexOf("_Gt", StringComparison.OrdinalIgnoreCase);
        if (gtIndex >= 0)
        {
            baseId = baseId.Substring(0, gtIndex);
        }
        else
        {
            int tIndex = baseId.IndexOf('t');
            if (tIndex >= 0)
            {
                baseId = baseId.Substring(0, tIndex);
            }
        }

        if (baseId == c.Id) return null;

        var parent = CardDatabase.GetCard(baseId);
        if (parent != null && parent.Type.Equals("MINION", StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }
        return null;
    }

    private bool IsSpellcraftSpell(BattlegroundsCard c)
    {
        var parent = GetParentMinion(c);
        return parent != null && parent.Text != null && parent.Text.Contains("Spellcraft", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBattlecruiser(BattlegroundsCard c)
    {
        return c.Name.Contains("Battlecruiser", StringComparison.OrdinalIgnoreCase) || 
               (c.Text != null && c.Text.Contains("Battlecruiser", StringComparison.OrdinalIgnoreCase));
    }
}
