using AgenticRagScannerApi.Models;
using Riok.Mapperly.Abstractions;

namespace AgenticRagScannerApi.Mappers;

[Mapper]
public partial class ScanMapper : IScanMapper
{
    [MapperIgnoreSource(nameof(ScanRequest.AsOfDate))]
    [MapperIgnoreSource(nameof(ScanRequest.Jurisdiction))]
    [MapperIgnoreTarget(nameof(ScanResponse.Status))]
    public partial ScanResponse ToResponse(ScanRequest request, string runId, DateTimeOffset acceptedAtUtc);
}