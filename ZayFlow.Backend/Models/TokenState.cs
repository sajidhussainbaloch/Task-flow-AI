namespace ZayFlow.Backend.Models;

public sealed class TokenState
{
    public DateOnly WindowDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public int Used { get; set; }
    public int DailyLimit { get; set; } = 100;
    public string Tier { get; set; } = "Free";
}
