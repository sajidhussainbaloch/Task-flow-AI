namespace ZayFlow.Backend.DTOs;

public sealed record TokenStatusDTO(
    int Used,
    int Remaining,
    int DailyLimit,
    DateOnly WindowDate,
    string Tier);
