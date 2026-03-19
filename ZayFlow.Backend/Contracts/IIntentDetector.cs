using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface IIntentDetector
{
    IntentResult DetectIntent(string userInput);
}
