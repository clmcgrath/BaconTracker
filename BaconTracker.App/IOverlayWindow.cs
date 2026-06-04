using System;

namespace BaconTracker.App;

public interface IOverlayWindow : IDisposable
{
    void Initialize();
    void Run();
    void SetClickThrough(bool clickThrough);
    void Close();
}
