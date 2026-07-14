using AgenticRagScannerApi.Services;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Story 8.6 - <see cref="CosmosVocabularyProvider"/> reads the impact-area and tag vocabularies from
/// the RegDocs container at runtime, scoping each query to its <c>/doc_type</c> partition, projecting
/// the <c>name</c>, and caching the result so a second read does not re-query Cosmos. The
/// <see cref="ICosmosRepository{T}"/> is mocked, so no live account is required.
/// </summary>
public class CosmosVocabularyProviderTests
{
    private static VocabularyDocument Doc(string docType, string name) =>
        new() { Id = Guid.NewGuid().ToString(), DocType = docType, Name = name };

    private static CosmosVocabularyProvider CreateProvider(Mock<ICosmosRepository<VocabularyDocument>> repository) =>
        new(repository.Object, NullLogger<CosmosVocabularyProvider>.Instance);

    [Fact]
    public async Task GetImpactAreasAsync_QueriesImpactAreasPartition_AndProjectsNames()
    {
        var repository = new Mock<ICosmosRepository<VocabularyDocument>>();
        repository
            .Setup(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "ImpactAreas", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Doc("ImpactAreas", "Employer tax reporting/filing requirements"),
                Doc("ImpactAreas", "Taxation of equity & incentives"),
            });

        var provider = CreateProvider(repository);

        var result = await provider.GetImpactAreasAsync();

        result.Should().BeEquivalentTo(new[]
        {
            "Employer tax reporting/filing requirements",
            "Taxation of equity & incentives",
        });
        repository.Verify(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "ImpactAreas", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTagsAsync_QueriesTagsPartition()
    {
        var repository = new Mock<ICosmosRepository<VocabularyDocument>>();
        repository
            .Setup(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "tags", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Doc("tags", "IR35"), Doc("tags", "Pensions") });

        var provider = CreateProvider(repository);

        var result = await provider.GetTagsAsync();

        result.Should().Contain(new[] { "IR35", "Pensions" });
        repository.Verify(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "tags", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetImpactAreasAsync_CachesResult_AndDoesNotRequery()
    {
        var repository = new Mock<ICosmosRepository<VocabularyDocument>>();
        repository
            .Setup(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "ImpactAreas", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Doc("ImpactAreas", "Employment taxes rates & thresholds") });

        var provider = CreateProvider(repository);

        var first = await provider.GetImpactAreasAsync();
        var second = await provider.GetImpactAreasAsync();

        first.Should().BeSameAs(second);
        repository.Verify(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "ImpactAreas", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetImpactAreasAsync_TrimsBlanksAndDeduplicates()
    {
        var repository = new Mock<ICosmosRepository<VocabularyDocument>>();
        repository
            .Setup(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "ImpactAreas", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Doc("ImpactAreas", "  Taxation of equity & incentives  "),
                Doc("ImpactAreas", "Taxation of equity & incentives"),
                Doc("ImpactAreas", "   "),
            });

        var provider = CreateProvider(repository);

        var result = await provider.GetImpactAreasAsync();

        result.Should().ContainSingle().Which.Should().Be("Taxation of equity & incentives");
    }

    [Fact]
    public async Task GetTagsAsync_ReturnsEmpty_WhenVocabularyNotSeeded()
    {
        var repository = new Mock<ICosmosRepository<VocabularyDocument>>();
        repository
            .Setup(r => r.QueryAsync(It.IsAny<QueryDefinition>(), "tags", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VocabularyDocument>());

        var provider = CreateProvider(repository);

        var result = await provider.GetTagsAsync();

        result.Should().BeEmpty();
    }
}
