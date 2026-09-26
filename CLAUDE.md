# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project goal and current state

This repo builds a **RAG (retrieval-augmented generation) system on top of a self-hosted Outline wiki** (https://github.com/outline/outline) in **.NET 10**.

It holds two unrelated halves:

- **The .NET RAG solution** (`Outline.Rag.slnx`, `src/`, `tests/`, `docker-compose.rag.yml`). This is what you are building.
- **Legacy Outline upgrade material** (0.82.0 → 1.10.0 → 1.10.1): `docker-compose.yml` (local Outline stack: PostgreSQL 18, Redis 7, `outlinewiki/outline:1.10.0`), `README.md` (restoring the dev environment from a production dump). It describes the Outline instance the RAG reads from. Don't treat it as the app. The production Kubernetes manifests and upgrade runbooks were moved out of this repo because they contained plaintext credentials; don't recreate them here.

The README mentions `scripts/` (`update.sh`, `restore.sh`, `migrate.sh`), `sql/migration.sql`, `.env`/`.env.example` and `*.backup` dumps. **None of these are in this repo.** Ask for them before relying on them.

## .NET commands

```bash
dotnet build                                   # whole solution; warnings are errors
dotnet test                                    # Microsoft.Testing.Platform runner (set in global.json)
dotnet test --filter-class "*MarkdownHeadingChunkerTests"        # one test class (xunit.v3 filters, wildcards allowed)
dotnet test --filter-method "*SkipsHeadingsWithoutBody"          # one test method
docker compose -f docker-compose.rag.yml up -d                   # pgvector on :5433
ollama pull bge-m3                                               # native Ollama app on :11434 (not the container)
docker/outline-demo/seed.sh --set-user-secrets                   # disposable Outline on :3000 with sample docs
dotnet run --project src/Outline.Rag.Worker                # periodic Outline → index sync
dotnet run --project src/Outline.Rag.Api                   # http://localhost:5157, /openapi/v1.json in Development
```

Configuration the scaffold leaves empty: `Outline:ApiToken`, `Outline:WebhookSigningSecret` and, for the Anthropic chat provider, `ANTHROPIC_API_KEY` (or `AI:Chat:ApiKey`). Keep them in user secrets or environment variables, never in `appsettings*.json`.

Ollama runs as the native app, not in Docker: Docker on macOS has no GPU, and the app already holds :11434. The `ollama` service in `docker-compose.rag.yml` sits behind the opt-in `ollama` profile for machines without the app; never run both.

Build quirks:
- **Central package management:** versions live in `Directory.Packages.props`, and `PackageReference`s carry no version. Transitive pinning is on; `Microsoft.Bcl.Memory` is pinned for a security advisory.
- **OllamaSharp source generator:** `Directory.Build.targets` strips it because it needs a newer Roslyn than SDK 10.0.100 (CS9057). Remove that target after an SDK update.
- **Test framework:** tests use xunit.v3 on Microsoft.Testing.Platform, not VSTest. Don't add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`.

## Architecture

Clean-architecture layers. The dependency direction is Api/Worker → Infrastructure → Application → Domain.

- **Domain:** records only (`SourceDocument`, `DocumentChunk`, `RetrievedChunk`, `RagAnswer`/`Citation`). Chunk keys are deterministic (`ChunkId.For(documentId, index)`), so re-indexing a document overwrites its chunks in place. `PgVectorChunkIndex.ReplaceDocumentAsync` upserts first and then deletes leftover indexes.
- **Application:** ports in `Abstractions/` (`IOutlineDocumentSource`, `IDocumentChunker`, `IChunkIndex`, `ISyncCheckpointStore`) plus the use cases:
  - `DocumentIngestionService`: chunk → embed → index.
  - `OutlineSyncService`: incremental sync from the checkpoint, and single-document resync.
  - `RetrievalService` and `AnswerService`: grounded answers with `[n]` citations.

  AI access goes only through the `Microsoft.Extensions.AI` abstractions (`IChatClient`, `IEmbeddingGenerator`).
- **Infrastructure:** the adapters.
  - `Outline/`: typed `HttpClient` over Outline's RPC-style API (`POST /api/documents.list`, `documents.info`), plus the webhook HMAC check.
  - `Chunking/`: Markdown heading → paragraph → token splitting, using `Microsoft.ML.Tokenizers` with cl100k as an approximation.
  - `VectorStore/`: pgvector through `Microsoft.Extensions.VectorData` and `CommunityToolkit.VectorData.PgVector`. The schema is defined in code (`ChunkRecordDefinition`), so the vector dimension comes from `AI:Embeddings:Dimensions`.
  - `AI/`: provider switch. Chat is Anthropic (`claude-opus-5` by default) or Ollama; embeddings are Ollama (`bge-m3`, 1024 dimensions, multilingual).
  - `DependencyInjection.AddRagInfrastructure` wires everything, including `AddApplication()`.
- **Worker:** `OutlineSyncWorker` initializes the RAG tables, then runs `SyncChangedAsync` on a `PeriodicTimer`. Its first run indexes everything.
- **Api:** minimal APIs `POST /api/search`, `POST /api/ask` and `POST /webhooks/outline`. The webhook verifies the `Outline-Signature` header, pushes the document id onto `ReindexChannel` and returns 202; `ReindexBackgroundService` drains the channel.

Design constraints to keep:
- **Read Outline through its API, never its database.** The RAG index lives in its own database (`outline_rag`), separate from Outline's.
- **Deletions only arrive via webhooks.** The list endpoint doesn't show them, so without the webhook, deleted or archived documents stay in the index until someone resyncs them.
- **Changing the embedding model or dimension requires a new `VectorStore:CollectionName`** (a new table) and a full re-sync. To trigger one, clear `rag_sync_state`.
- **Access control is not implemented yet.** Everything visible to the Outline API token is searchable by any caller. The `CollectionIds` filter is the hook for per-user permissions; see the TODO in `RagEndpoints.cs`.
- **Retrieved text is untrusted data.** The system prompt in `AnswerService` wraps excerpts as data; keep it that way when changing prompts.

## Outline data source

These facts matter for the RAG ingestion design:

- **Production:** k8s namespace `outline`, running Outline **1.10.1**. Its database is on an **EDB Postgres Advanced Server** and has all 304 Sequelize migrations applied. Auth is OIDC. `FILE_STORAGE=local`, so attachments live on the PVC `outline-pvc` at `/var/lib/outline/data`, not in S3.
- **Local dev databases** (both in the single `postgres` container, user `outline`):
  - `outline_dev`: the backup restored as-is (old schema, 223 migrations).
  - `outline_latest`: the backup migrated to 1.10.0 (302 migrations). The local Outline app runs against this one.
- All Outline data is in the `public` schema. The EDB dump also contains Oracle-compat schemas and roles (`dbms_*`, `utl_*`, `aq$_*`, `enterprisedb` and custom roles), which plain PostgreSQL can't restore. Restores use only `public`, with `--no-owner --no-privileges`, after pre-creating the `uuid-ossp` and `unaccent` extensions.
- Schema change to know about: the old `backlinks` table is now `relationships`.
- Migration state is tracked in the `"SequelizeMeta"` table.

## Common commands (local Outline)

```bash
docker compose up -d redis postgres
docker compose up -d outline                      # http://localhost:3000
curl http://127.0.0.1:3000/_health                # -> OK
docker compose exec -T postgres psql -U outline -d outline_latest \
  -c 'SELECT count(*) FROM "SequelizeMeta";'      # -> 302
```

The `outline` service needs a `.env` file with at least `SECRET_KEY`, `UTILS_SECRET`, `DATABASE_URL`, `REDIS_URL` and `URL`. The Postgres container mounts whichever dump `OUTLINE_BACKUP` points to (default `./outline.backup`) read-only at `/backup/outline.backup`. A restore **drops and recreates** both dev databases.

## Production caution

The production wiki (namespace `outline`) and its corporate database are live systems. Don't run `kubectl`, migration Jobs or SQL against production yourself. Prepare the commands and let the user execute them.
