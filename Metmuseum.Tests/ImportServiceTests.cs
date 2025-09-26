using Metmuseum.Data;
using Metmuseum.Models;
using Metmuseum.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Newtonsoft.Json.Linq;
using System.Net;

namespace Metmuseum.Tests
{
    public class ImportServiceTests
    {
        private readonly DbContextOptions<MetMuseumContext> _dbOptions;

        public ImportServiceTests()
        {
            _dbOptions = new DbContextOptionsBuilder<MetMuseumContext>()
                         .UseInMemoryDatabase(Guid.NewGuid().ToString())
                         .Options;
        }

        private IMetMuseumService MetMuseumMockService(HttpResponseMessage? httpResponseMessage = null)
        {
            httpResponseMessage ??= new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""objectIDs"":[123]}")
            };

            var messageHandler = new Mock<HttpMessageHandler>();
            messageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponseMessage);

            var httpClient = new HttpClient(messageHandler.Object)
            {
                BaseAddress = new Uri("https://collectionapi.metmuseum.org/public/collection/v1/")
            };

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient("metMuseum")).Returns(httpClient);

            return new MetMuseumService(httpClientFactory.Object, Mock.Of<ILogger<MetMuseumService>>());
        }

        private TestImportService CreateImportService(IMetMuseumService svc)
        {
            return new TestImportService(svc, Mock.Of<ILogger<ImportService>>(), _dbOptions);
        }

        [Fact]
        public async Task FlushBufferAsync_WritesBatch()
        {
            var service = MetMuseumMockService();
            var importService = CreateImportService(service);

            var metMuseumObject = new MetMuseumObject
            {
                ObjectID = 99,
                Title = "BatchTitle",
                JsonData = "{}",
                RetrievedAt = DateTimeOffset.UtcNow
            };

            var bufferField = typeof(ImportService)
                .GetField("_buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var buffer = (System.Collections.Concurrent.ConcurrentBag<MetMuseumObject>)bufferField!.GetValue(importService)!;
            buffer.Add(metMuseumObject);

            var flushMethod = typeof(ImportService)
                .GetMethod("FlushBufferAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            await (Task)flushMethod.Invoke(importService, new object[] { CancellationToken.None })!;

            using var ctx = new MetMuseumContext(_dbOptions);
            var saved = await ctx.MetObjects.FindAsync(99);
            Assert.NotNull(saved);
            Assert.Equal("BatchTitle", saved!.Title);
        }

        [Fact]
        public async Task RunAsync_UpdatesExistingObjects()
        {

            using (var ctx = new MetMuseumContext(_dbOptions))
            {
                ctx.MetObjects.Add(new MetMuseumObject
                {
                    ObjectID = 123,
                    Title = "OldTitle",
                    JsonData = "{}",
                    RetrievedAt = DateTimeOffset.UtcNow
                });
                await ctx.SaveChangesAsync();
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{""objectIDs"":[123]}")
            };
            var service = MetMuseumMockService(response);

            var mockMetMuseumService = new Mock<IMetMuseumService>();
            mockMetMuseumService.Setup(s => s.GetAllIdsAsync(It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new List<int> { 123 });
            mockMetMuseumService.Setup(s => s.GetObjectAsync(123, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(JObject.FromObject(new { title = "NewTitle" }));

            var importService = CreateImportService(mockMetMuseumService.Object);

            await importService.RunAsync(parallelism: 1);

            using var verifyCtx = new MetMuseumContext(_dbOptions);
            var metMuseum = await verifyCtx.MetObjects.FindAsync(123);
            Assert.Equal("NewTitle", metMuseum!.Title);
        }

        [Fact]
        public void DrawProgressBar_DoesNotThrow()
        {
            var methodInfo = typeof(ImportService)
                .GetMethod("DrawProgressBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            methodInfo.Invoke(null, new object[] { 5, 10, 20 });
        }

        [Fact]
        public async Task GetObjectAsync_RetriesOn429And403()
        {
            int callCount = 0;

            var messageHandler = new Mock<HttpMessageHandler>();
            messageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return callCount < 3
                        ? new HttpResponseMessage((HttpStatusCode)429)
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"objectID\":1,\"title\":\"RetrySuccess\"}")
                        };
                });

            var httpClient = new HttpClient(messageHandler.Object)
            {
                BaseAddress = new Uri("https://collectionapi.metmuseum.org/public/collection/v1/")
            };

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient("metMuseum")).Returns(httpClient);

            var service = new MetMuseumService(httpClientFactory.Object, Mock.Of<ILogger<MetMuseumService>>());

            var result = await service.GetObjectAsync(1);

            Assert.NotNull(result);
            Assert.Equal("RetrySuccess", result["title"]?.ToString());
            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task GetObjectAsync_ReturnsNullOnForbidden()
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden));

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("https://collectionapi.metmuseum.org/public/collection/v1/")
            };

            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient("metMuseum")).Returns(httpClient);

            var loggerMock = new Mock<ILogger<MetMuseumService>>();
            var service = new MetMuseumService(httpClientFactory.Object, loggerMock.Object);

            var result = await service.GetObjectAsync(1);

            Assert.Null(result);
            loggerMock.Verify(
                logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to fetch object")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

    }
}