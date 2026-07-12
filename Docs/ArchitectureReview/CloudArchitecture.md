# Cloud Architecture

## Azure Deployment Architecture

If deployed to Azure, the recommended enterprise topology is:

### Compute
- **AKS (Azure Kubernetes Service):** Hosts the API, Workers, and Projectors as scalable pods.
  - *API Pods:* HPA (Horizontal Pod Autoscaler) based on CPU/HTTP traffic.
  - *Worker Pods:* KEDA (Kubernetes Event-driven Autoscaling) based on Service Bus queue depth.

### Data
- **Azure Cosmos DB (MongoDB API):** Enterprise event store with multi-region replication.
- **Azure SQL Database:** Hosts the EF Core Read Models.
- **Azure AI Search:** Replaces Qdrant for enterprise-grade vector search and hybrid retrieval.

### Messaging
- **Azure Service Bus:** Replaces `InMemoryEventBus`. Configured with Topics and Subscriptions for Pub/Sub.

### Security & AI
- **Azure Key Vault:** Stores DB connection strings, AI keys.
- **Azure OpenAI:** Provides the `IEmbeddingService` and Compression LLM capabilities.
- **Azure AD (Entra ID):** Identity Provider for API authentication.

## AWS Alternative Deployment

- **Compute:** Amazon EKS or ECS Fargate.
- **Data:** Amazon DocumentDB (Event Store), Amazon RDS for PostgreSQL (Read Models), Amazon OpenSearch Serverless (Vector Store).
- **Messaging:** Amazon SNS + SQS.
- **Security & AI:** AWS Secrets Manager, Amazon Bedrock (Anthropic Claude for compression/embeddings), Amazon Cognito (Identity).
