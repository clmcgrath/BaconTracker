---@meta

---@enum PanelAnchor
PanelAnchor = {
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3,
    Center = 4
}

---@class OverlayPanel
---@field Title string The title of the panel.
---@field IsVisible boolean Whether the panel is currently visible.
---@field DefaultPosition number[] Virtual position coordinates {x, y} (scaled to 1920x1080).
---@field DefaultSize number[] Virtual dimension coordinates {width, height} (scaled to 1920x1080).
---@field Anchor PanelAnchor|nil Anchor point for screen boundaries.
---@field AnchorOffset number[] Offset coordinates {x, y} relative to the anchor.
local OverlayPanel = {}

--- Registers this panel to the tracker overlay.
function OverlayPanel:Register() end

--- Unregisters this panel from the tracker overlay.
function OverlayPanel:Unregister() end

--- Subscribes a callback to a tracker event.
---@param eventName string The name of the event (e.g. "OnSpellPlayed", "OnTurnChanged", "OnGoldChanged").
---@param callback fun(args: any[]) The function to execute when the event fires.
function OverlayPanel:Subscribe(eventName, callback) end

--- Unsubscribes a callback from a tracker event.
---@param eventName string The name of the event.
---@param callback fun(args: any[]) The function to remove.
function OverlayPanel:Unsubscribe(eventName, callback) end

--- Loads a PNG/JPG asset from disk, registers it as a texture, and returns the texture ID string.
---@param path string The path to the image file (relative to root or absolute).
---@return string textureId The texture ID string to pass to ImGui image calls.
function OverlayPanel:LoadAsset(path) end


---@class CounterPanel : OverlayPanel
---@field label string The description text displayed on the counter.
---@field value number The numeric value displayed on the counter.
---@field valueColor number[] The RGBA color values {r, g, b, a} for the value text.
---@field iconPath string|nil The path to the icon image drawn alongside the counter.
local CounterPanelClass = {}

--- Creates a new Counter Panel template.
---@param title string The window title.
---@return CounterPanel
function CounterPanel(title) end


---@class InfoPanel : OverlayPanel
local InfoPanelClass = {}

--- Adds a row of key-value pair to the info panel.
---@param key string The label/property name.
---@param value string The text value.
---@param color number[]|nil Optional RGBA color values {r, g, b, a} for the value text.
function InfoPanelClass:AddRow(key, value, color) end

--- Clears all rows in the info panel.
function InfoPanelClass:ClearRows() end

--- Creates a new Info Panel template.
---@param title string The window title.
---@return InfoPanel
function InfoPanel(title) end


---@class LuaImGui
ImGui = {}

--- Draws a simple text element.
---@param text string The text content.
function ImGui.Text(text) end

--- Draws a colored text element.
---@param r number Red channel (0.0 to 1.0)
---@param g number Green channel (0.0 to 1.0)
---@param b number Blue channel (0.0 to 1.0)
---@param a number Alpha channel (0.0 to 1.0)
---@param text string The text content.
function ImGui.TextColored(r, g, b, a, text) end

--- Draws a horizontal dividing line.
function ImGui.Separator() end

--- Adds a vertical space.
function ImGui.Spacing() end

--- Draws a bullet point text element.
---@param text string The text content.
function ImGui.BulletText(text) end

--- Draws a clickable button.
---@param label string The button text.
---@return boolean clicked True if the button was clicked on this frame.
function ImGui.Button(label) end

--- Draws a stateful checkbox.
---@param label string The checkbox text.
---@param value boolean The current boolean state.
---@return boolean newState The updated boolean state.
function ImGui.Checkbox(label, value) end

--- Draws a progress bar.
---@param fraction number The progress fraction (0.0 to 1.0).
---@param overlay string The text overlay on the bar.
function ImGui.ProgressBar(fraction, overlay) end

--- Positions the next element on the same line as the previous one.
function ImGui.SameLine() end


---@class LuaGameProxy
---@field CurrentTurn number The current turn number in the Battlegrounds match.
---@field ActiveTribes string[] The list of active minion types in the current lobby.
---@field PlayerGold number The current gold resources of the player.
Game = {}

--- Resets the local tracker state.
function Game.ResetTracker() end


---@class LuaTrackerProxy
Tracker = {}

--- Registers a custom panel to the manager.
---@param panel OverlayPanel The panel to register.
function Tracker.RegisterPanel(panel) end

--- Loads a PNG/JPG asset from disk, registers it as a texture, and returns the texture ID string.
---@param path string The path to the image file (relative to root or absolute).
---@return string textureId The texture ID string to pass to ImGui image calls.
function Tracker.LoadAsset(path) end

--- Subscribes a callback to a tracker event.
---@param eventName string The name of the event.
---@param callback fun(args: any[]) The function to execute.
function Tracker.Subscribe(eventName, callback) end
