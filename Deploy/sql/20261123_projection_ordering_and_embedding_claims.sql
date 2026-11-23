BEGIN;

ALTER TABLE IF EXISTS "MemoryReadModels"
	ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;

UPDATE "MemoryReadModels" AS read_model
SET "Version" = COALESCE(
	(SELECT MAX(event."Version")
	 FROM "Events" AS event
	 WHERE event."AggregateId" = read_model."MemoryId"::text),
	0);

CREATE TABLE IF NOT EXISTS "EmbeddingIndexingRecords"
(
	"AggregateId" uuid NOT NULL,
	"EventVersion" integer NOT NULL,
	"StartedAtUtc" timestamp with time zone NOT NULL,
	"CompletedAtUtc" timestamp with time zone NULL,
	"Failure" character varying(4000) NULL,
	CONSTRAINT "PK_EmbeddingIndexingRecords" PRIMARY KEY ("AggregateId", "EventVersion")
);

COMMIT;
