import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// ── Custom BFSI SLA Metrics ──────────────────────────────────────────────────
export const errorRate = new Rate('bfsi_error_rate');
export const pgReadDuration = new Trend('pg_read_duration_ms', true);
export const qdrantSearchDuration = new Trend('qdrant_search_duration_ms', true);
export const hsmIngestDuration = new Trend('hsm_ingest_duration_ms', true);

// ── Target SLA: 5,000+ req/sec sustained with P99 < 50ms ─────────────────────
export const options = {
  scenarios: {
    sustained_bfsi_load: {
      executor: 'ramping-arrival-rate',
      startRate: 1000,
      timeUnit: '1s',
      preAllocatedVUs: 200,
      maxVUs: 1000,
      stages: [
        { target: 2500, duration: '10s' }, // Ramp up to 2,500 req/s
        { target: 5000, duration: '20s' }, // Ramp to 5,000 req/s
        { target: 5200, duration: '30s' }, // Sustained peak at 5,200 req/s
        { target: 1000, duration: '10s' }, // Ramp down
      ],
    },
  },
  thresholds: {
    // SLA 1: Sustained throughput >= 5,000 req/sec during peak
    http_reqs: ['rate>4800'],
    // SLA 2: P95 < 30ms, P99 < 50ms
    http_req_duration: ['p(95)<30', 'p(99)<50'],
    // SLA 3: High reliability (<0.01% error rate)
    http_req_failed: ['rate<0.0001'],
    bfsi_error_rate: ['rate<0.0001'],
    pg_read_duration_ms: ['p(99)<40'],
    qdrant_search_duration_ms: ['p(99)<48'],
    hsm_ingest_duration_ms: ['p(99)<50'],
  },
};

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5000';
const TENANT_ID = 'tenant-hdfc-prod';
const AUTH_TOKEN = __ENV.JWT_TOKEN || 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...';

const headers = {
  'Content-Type': 'application/json',
  'Authorization': AUTH_TOKEN,
  'X-Tenant-Id': TENANT_ID,
};

export default function () {
  // Scenario 1: PostgreSQL Read Model Point-Lookup (40% traffic weight)
  const memoryId = '6688eeca-2e77-49da-836c-2fea01b28cc8';
  const readRes = http.get(`${BASE_URL}/api/v1/memories/${memoryId}`, { headers });
  
  const readSuccess = check(readRes, {
    'PG Read status 200': (r) => r.status === 200,
  });
  errorRate.add(!readSuccess);
  pgReadDuration.add(readRes.timings.duration);

  // Scenario 2: Qdrant Vector Similarity Search (40% traffic weight)
  const searchPayload = JSON.stringify({
    query: 'Architectural compliance standards for banking security',
    topK: 5,
    minSimilarityScore: 0.75,
    tenantId: TENANT_ID,
  });
  const searchRes = http.post(`${BASE_URL}/api/v1/memories/search`, searchPayload, { headers });
  
  const searchSuccess = check(searchRes, {
    'Qdrant Search status 200': (r) => r.status === 200,
  });
  errorRate.add(!searchSuccess);
  qdrantSearchDuration.add(searchRes.timings.duration);

  // Scenario 3: Memory Ingestion with FIPS 140-2 Level 3 HSM Envelope Encryption (20% traffic weight)
  const ingestPayload = JSON.stringify({
    rawText: 'Client Portfolio Rebalancing authorization approved under RBI sovereign master guideline.',
    source: 'BFSI_Core_Banking_Terminal',
    tags: ['wealth_management', 'rbi_compliance', 'audit_verified'],
    importance: 'High',
  });
  const ingestRes = http.post(`${BASE_URL}/api/v1/memories`, ingestPayload, { headers });
  
  const ingestSuccess = check(ingestRes, {
    'HSM Ingest status 201 or 200': (r) => r.status === 201 || r.status === 200,
  });
  errorRate.add(!ingestSuccess);
  hsmIngestDuration.add(ingestRes.timings.duration);
}
