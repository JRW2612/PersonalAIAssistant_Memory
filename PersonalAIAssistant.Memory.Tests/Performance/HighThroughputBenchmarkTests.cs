using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models.Security;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Performance
{
    public class HighThroughputBenchmarkTests
    {
        private readonly HsmEnvelopeEncryptionService _envelopeService;

        public HighThroughputBenchmarkTests()
        {
            var hsmOpts = new HsmOptions
            {
                Provider = "SimulatedHsm",
                FipsComplianceLevel = "FIPS 140-2 Level 3",
                MasterKeyName = "BenchmarkMasterKek"
            };

            var hsmService = new SimulatedFipsHsmKeyVaultService(
                Options.Create(hsmOpts),
                NullLogger<SimulatedFipsHsmKeyVaultService>.Instance);

            var legacyCbc = new AesEncryptionService();
            var legacyGcm = new AesGcmEncryptionService(NullLogger<AesGcmEncryptionService>.Instance, legacyCbc);

            _envelopeService = new HsmEnvelopeEncryptionService(
                hsmService,
                legacyGcm,
                Options.Create(hsmOpts),
                NullLogger<HsmEnvelopeEncryptionService>.Instance);
        }

        [Fact]
        public async Task HighThroughput_HsmEnvelopeEncryption_Validates_P99_Under_50ms()
        {
            // Simulate 5,000+ operations benchmark with 50 concurrent virtual workers
            const int totalOperations = 5000;
            var latencies = new ConcurrentBag<double>();
            var errorCount = 0;

            var rawPayload = "Account 501004829104: NEFT Transfer INR 12,50,000 authorized under RBI settlement rules.";
            var tenantKey = "tenant-hdfc-benchmark";

            // Warm up
            for (int i = 0; i < 50; i++)
            {
                var enc = _envelopeService.Encrypt(rawPayload, tenantKey);
                _ = _envelopeService.Decrypt(enc, tenantKey);
            }

            var overallStopwatch = Stopwatch.StartNew();

            // Execute 5,000 operations across 50 concurrent virtual workers (equivalent to K6 / Locust concurrent runner)
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(16, Environment.ProcessorCount * 4)
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, totalOperations), parallelOptions, async (i, ct) =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var encrypted = _envelopeService.Encrypt(rawPayload, tenantKey);
                    var decrypted = _envelopeService.Decrypt(encrypted, tenantKey);
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);

                    if (decrypted != rawPayload)
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            });

            overallStopwatch.Stop();

            var latencyList = latencies.OrderBy(x => x).ToList();
            Assert.NotEmpty(latencyList);
            Assert.Equal(0, errorCount);

            // Calculate percentiles
            var p50 = latencyList[(int)(latencyList.Count * 0.50)];
            var p90 = latencyList[(int)(latencyList.Count * 0.90)];
            var p95 = latencyList[(int)(latencyList.Count * 0.95)];
            var p99 = latencyList[(int)(latencyList.Count * 0.99)];
            var throughput = totalOperations / overallStopwatch.Elapsed.TotalSeconds;

            // Validate BFSI SLA: P99 < 50ms, P95 < 30ms, and error rate == 0
            Assert.True(p99 < 50.0, $"SLA Violation: P99 latency was {p99:F2}ms (expected < 50ms)");
            Assert.True(p95 < 30.0, $"SLA Violation: P95 latency was {p95:F2}ms (expected < 30ms)");
            Assert.True(errorCount == 0, $"SLA Violation: {errorCount} errors occurred under load");
        }
    }
}
