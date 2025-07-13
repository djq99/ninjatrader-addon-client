using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NT8Bridge.Core;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Tests
{
    [TestFixture]
    public class HistoricalDataServiceTests
    {
        private HistoricalDataService _service;
        private ILogger _logger;

        [SetUp]
        public void Setup()
        {
            _logger = new ConsoleLogger();
            _service = new HistoricalDataService(_logger);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
        }

        [Test]
        public async Task GetHistoryAsync_ValidRequest_ReturnsHistoryChunk()
        {
            // Arrange
            var command = new FetchHistoryCommand
            {
                RequestId = 1,
                Symbol = "NQ 09-25",
                Level = "Minute",
                From = DateTime.Today.AddDays(-1),
                To = DateTime.Today,
                MaxBars = 100
            };

            // Act
            var result = await _service.GetHistoryAsync(command);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.RequestId);
            Assert.AreEqual("NQ 09-25", result.Symbol);
            Assert.AreEqual("Minute", result.Level);
            Assert.IsTrue(result.Success);
        }

        [Test]
        public async Task GetHistoryAsync_InvalidSymbol_ReturnsError()
        {
            // Arrange
            var command = new FetchHistoryCommand
            {
                RequestId = 2,
                Symbol = "INVALID SYMBOL",
                Level = "Minute",
                From = DateTime.Today.AddDays(-1),
                To = DateTime.Today
            };

            // Act
            var result = await _service.GetHistoryAsync(command);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.RequestId);
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
        }

        [Test]
        public async Task GetHistoryAsync_InvalidLevel_ReturnsError()
        {
            // Arrange
            var command = new FetchHistoryCommand
            {
                RequestId = 3,
                Symbol = "NQ 09-25",
                Level = "InvalidLevel",
                From = DateTime.Today.AddDays(-1),
                To = DateTime.Today
            };

            // Act
            var result = await _service.GetHistoryAsync(command);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.RequestId);
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
        }

        [Test]
        public void CancelRequest_ValidRequestId_CancelsRequest()
        {
            // Arrange
            var requestId = 123;

            // Act
            _service.CancelRequest(requestId);

            // Assert
            // No exception should be thrown
            Assert.Pass();
        }

        [Test]
        public void ClearCache_ClearsAllCachedData()
        {
            // Act
            _service.ClearCache();

            // Assert
            // No exception should be thrown
            Assert.Pass();
        }
    }
} 