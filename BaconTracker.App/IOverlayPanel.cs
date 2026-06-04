using System.Numerics;
using BaconTracker.Core;

namespace BaconTracker.App;

public interface IOverlayPanel
{
    string Title { get; }
    bool IsVisible { get; set; }
    bool IsInteractive { get; }
    Vector2 DefaultPosition { get; set; }
    Vector2 DefaultSize { get; set; }
    Vector2 ScreenPosition { get; }
    Vector2 ScreenSize { get; }
    bool ShouldDisplay(GameState state);
    void Draw(bool isOverlayLocked);
    void Register();
    void Unregister();
}
