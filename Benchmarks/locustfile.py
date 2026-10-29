import time
import json
from locust import task, between, events
from locust.contrib.fasthttp import FastHttpUser

class BfsiMemoryLoadUser(FastHttpUser):
    wait_time = between(0.001, 0.005) # Aggressive concurrency for 5,000+ req/sec
    
    def on_start(self):
        self.tenant_id = "tenant-hdfc-prod"
        self.headers = {
            "Content-Type": "application/json",
            "Authorization": "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.bfsi-test-token",
            "X-Tenant-Id": self.tenant_id
        }
        self.memory_id = "6688eeca-2e77-49da-836c-2fea01b28cc8"

    @task(4)
    def pg_read_model_lookup(self):
        """Targets PostgreSQL read-replica with tenant-scoped query"""
        start = time.perf_counter()
        with self.client.get(
            f"/api/v1/memories/{self.memory_id}",
            headers=self.headers,
            catch_response=True,
            name="PostgreSQL: GetMemoryById"
        ) as response:
            latency_ms = (time.perf_counter() - start) * 1000
            if response.status_code == 200:
                response.success()
            else:
                response.failure(f"HTTP {response.status_code}")

    @task(4)
    def qdrant_vector_similarity_search(self):
        """Targets Qdrant vector cluster for similarity retrieval"""
        payload = {
            "query": "Regulatory audit trail and encryption guidelines",
            "topK": 5,
            "minSimilarityScore": 0.70,
            "tenantId": self.tenant_id
        }
        with self.client.post(
            "/api/v1/memories/search",
            json=payload,
            headers=self.headers,
            catch_response=True,
            name="Qdrant: VectorSimilaritySearch"
        ) as response:
            if response.status_code == 200:
                response.success()
            else:
                response.failure(f"HTTP {response.status_code}")

    @task(2)
    def hsm_envelope_memory_ingestion(self):
        """Targets transactional write path with FIPS 140-2 Level 3 HSM encryption"""
        payload = {
            "rawText": "Banking Core Transfer: Authenticated transaction credit INR 25,00,000.",
            "source": "RTGS_Core_API",
            "tags": ["rtgs", "high_value", "hsm_envelope_secured"],
            "importance": "High"
        }
        with self.client.post(
            "/api/v1/memories",
            json=payload,
            headers=self.headers,
            catch_response=True,
            name="HSM-AEAD: IngestMemory"
        ) as response:
            if response.status_code in (200, 201):
                response.success()
            else:
                response.failure(f"HTTP {response.status_code}")
