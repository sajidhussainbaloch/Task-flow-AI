using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IPreviewService
{
    PreviewResult BuildPreview(Plan plan);
}
