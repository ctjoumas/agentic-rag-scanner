using System.Net;
using AgenticRagScannerApi.Services;
using AgenticRagScannerApi.Workflows.Configuration;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;

namespace AgenticRagScannerApi.Tests;

/// <summary>
/// Verifies the generic <see cref="CosmosRepository{T}"/> maps each CRUD operation to the right Cosmos
/// SDK call (id + partition key), unwraps the item from the SDK response, and translates a NotFound
/// read into <see langword="null"/>. The concrete <see cref="Container"/> is mocked, so no live account
/// is required.
/// </summary>
public class CosmosRepositoryTests
{
    public sealed class TestDoc : ICosmosEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("tenantId")]
        public string TenantId { get; set; } = string.Empty;
    }

    private static (CosmosRepository<TestDoc> Repository, Mock<Container> Container) CreateRepository()
    {
        var container = new Mock<Container>();

        var client = new Mock<CosmosClient>();
        client
            .Setup(c => c.GetContainer("agentic-rag-scanner", "regdocs"))
            .Returns(container.Object);

        var options = Options.Create(new CosmosOptions
        {
            Endpoint = "https://cosmos.example.com",
            Database = "agentic-rag-scanner",
            CheckpointsContainer = "checkpoints",
            RegDocsContainer = "regdocs",
        });

        var repository = new CosmosRepository<TestDoc>(client.Object, options, NullLogger<CosmosRepository<TestDoc>>.Instance);
        return (repository, container);
    }

    private static Mock<ItemResponse<TestDoc>> ItemResponse(TestDoc doc)
    {
        var response = new Mock<ItemResponse<TestDoc>>();
        response.Setup(r => r.Resource).Returns(doc);
        return response;
    }

    [Fact]
    public async Task CreateAsync_ShouldCallCreateItemWithPartitionKey_AndReturnResource()
    {
        var (repository, container) = CreateRepository();
        var doc = new TestDoc { Id = "doc-1", TenantId = "tenant-a" };

        container
            .Setup(c => c.CreateItemAsync(doc, new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResponse(doc).Object);

        var result = await repository.CreateAsync(doc, "tenant-a");

        result.Should().BeSameAs(doc);
        container.VerifyAll();
    }

    [Fact]
    public async Task UpsertAsync_ShouldCallUpsertItemWithPartitionKey_AndReturnResource()
    {
        var (repository, container) = CreateRepository();
        var doc = new TestDoc { Id = "doc-1", TenantId = "tenant-a" };

        container
            .Setup(c => c.UpsertItemAsync(doc, new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResponse(doc).Object);

        var result = await repository.UpsertAsync(doc, "tenant-a");

        result.Should().BeSameAs(doc);
        container.VerifyAll();
    }

    [Fact]
    public async Task ReadAsync_WhenDocumentExists_ShouldReturnResource()
    {
        var (repository, container) = CreateRepository();
        var doc = new TestDoc { Id = "doc-1", TenantId = "tenant-a" };

        container
            .Setup(c => c.ReadItemAsync<TestDoc>("doc-1", new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResponse(doc).Object);

        var result = await repository.ReadAsync("doc-1", "tenant-a");

        result.Should().BeSameAs(doc);
    }

    [Fact]
    public async Task ReadAsync_WhenDocumentMissing_ShouldReturnNull()
    {
        var (repository, container) = CreateRepository();

        container
            .Setup(c => c.ReadItemAsync<TestDoc>("missing", new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("not found", HttpStatusCode.NotFound, 0, "activity", 0));

        var result = await repository.ReadAsync("missing", "tenant-a");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReplaceAsync_ShouldCallReplaceItemWithIdAndPartitionKey_AndReturnResource()
    {
        var (repository, container) = CreateRepository();
        var doc = new TestDoc { Id = "doc-1", TenantId = "tenant-a" };

        container
            .Setup(c => c.ReplaceItemAsync(doc, "doc-1", new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResponse(doc).Object);

        var result = await repository.ReplaceAsync(doc, "tenant-a");

        result.Should().BeSameAs(doc);
        container.VerifyAll();
    }

    [Fact]
    public async Task DeleteAsync_ShouldCallDeleteItemWithIdAndPartitionKey()
    {
        var (repository, container) = CreateRepository();

        container
            .Setup(c => c.DeleteItemAsync<TestDoc>("doc-1", new PartitionKey("tenant-a"), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResponse(new TestDoc { Id = "doc-1", TenantId = "tenant-a" }).Object);

        await repository.DeleteAsync("doc-1", "tenant-a");

        container.VerifyAll();
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnAllPagedResults()
    {
        var (repository, container) = CreateRepository();
        var docs = new[]
        {
            new TestDoc { Id = "doc-1", TenantId = "tenant-a" },
            new TestDoc { Id = "doc-2", TenantId = "tenant-a" },
        };

        var feedResponse = new Mock<FeedResponse<TestDoc>>();
        feedResponse.Setup(f => f.GetEnumerator()).Returns(((IEnumerable<TestDoc>)docs).GetEnumerator());

        var iterator = new Mock<FeedIterator<TestDoc>>();
        iterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        iterator
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedResponse.Object);

        container
            .Setup(c => c.GetItemQueryIterator<TestDoc>(
                It.IsAny<QueryDefinition>(),
                null,
                It.Is<QueryRequestOptions>(o => o.PartitionKey == new PartitionKey("tenant-a"))))
            .Returns(iterator.Object);

        var result = await repository.QueryAsync(new QueryDefinition("SELECT * FROM c"), "tenant-a");

        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(docs);
    }

    [Fact]
    public async Task QueryAsync_WhenNoPartitionKey_ShouldQueryCrossPartition()
    {
        var (repository, container) = CreateRepository();
        var docs = new[] { new TestDoc { Id = "doc-1", TenantId = "tenant-a" } };

        var feedResponse = new Mock<FeedResponse<TestDoc>>();
        feedResponse.Setup(f => f.GetEnumerator()).Returns(((IEnumerable<TestDoc>)docs).GetEnumerator());

        var iterator = new Mock<FeedIterator<TestDoc>>();
        iterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        iterator
            .Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feedResponse.Object);

        // No partition key => requestOptions must be null (cross-partition query).
        container
            .Setup(c => c.GetItemQueryIterator<TestDoc>(
                It.IsAny<QueryDefinition>(),
                null,
                null))
            .Returns(iterator.Object);

        var result = await repository.QueryAsync(new QueryDefinition("SELECT * FROM c"));

        result.Should().ContainSingle();
        container.VerifyAll();
    }
}
