using AgenticRagScannerApi.Models;

namespace AgenticRagScannerApi.Mappers;

public interface IScanMapper
{
    ScanResponse ToResponse(ScanRequest request, string runId, DateTimeOffset acceptedAtUtc);
}