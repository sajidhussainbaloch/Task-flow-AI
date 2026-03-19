namespace ZayFlow.Backend.Contracts;

public interface IPathSafetyValidator
{
    bool IsPathAllowed(string path);
    void EnsurePathAllowed(string path);
}
