using System;

namespace BaconTracker.App;

public class LuaGameProxy
{
    public int CurrentTurn => 4; // Stub turn number for testing
    
    public string[] ActiveTribes => new[] { "Beasts", "Demons", "Elementals", "Pirates", "Undead" };

    public int PlayerGold => 7; // Stub gold amount
    
    public void ResetTracker()
    {
        Console.WriteLine("[LuaGameProxy] ResetTracker called from Lua script!");
    }
}
