# Outline RAG

A retrieval-augmented generation (RAG) service for a self-hosted
[Outline](https://github.com/outline/outline) wiki, built on .NET 10.
It indexes wiki documents into PostgreSQL + pgvector and answers questions
from them, citing the source documents.

The repository also contains the [local Outline environment](#local-outline-environment-legacy)
used during the Outline 0.82.0 → 1.10.1 upgrade. The RAG service reads from
an Outline instance like that one.

## How it works

```
Outline ──(HTTP API: documents.list / documents.info)──► Worker ─┐
   │                                                             ├─► chunk → embed (Ollama) → pgvector
   └──(webhooks: documents.*)──► Api /webhooks/outline ──────────┘
                                 Api /api/ask ──► retrieve from pgvector → answer (Claude) with [n] citations
```

- **Worker:** every 15 minutes, indexes the Outline documents changed since
  the last run. The first run indexes the whole wiki.
- **Api:** serves search and question answering. It also receives Outline
  webhooks, so edits, archives and deletions reach the index within seconds.
- **Reads Outline through its HTTP API only**, never through Outline's
  database. The index lives in its own database (`outline_rag`).

## Solution layout

| Project | Role |
| --- | --- |
| `src/Outline.Rag.Domain` | Domain records: documents, chunks, answers, citations |
| `src/Outline.Rag.Application` | Use cases (ingestion, sync, retrieval, answering) and the interfaces they depend on |
| `src/Outline.Rag.Infrastructure` | Outline API client, Markdown chunker, pgvector index, AI providers |
| `src/Outline.Rag.Api` | ASP.NET Core minimal API |
| `src/Outline.Rag.Worker` | Background sync service |
| `tests/Outline.Rag.UnitTests` | xunit.v3 unit tests |

Main libraries:

- `Microsoft.Extensions.AI`: chat and embedding abstractions, so the AI
  providers can be swapped in configuration.
- `Microsoft.Extensions.VectorData` with `CommunityToolkit.VectorData.PgVector`:
  the vector store.
- The official `Anthropic` SDK for answers (Claude, `claude-opus-5` by default).
- `OllamaSharp` for embeddings (`bge-m3`, a multilingual model that runs on
  your own hardware).
- `Microsoft.ML.Tokenizers` for token-aware chunking.

Package versions are managed centrally in `Directory.Packages.props`.

## Getting started

### Prerequisites

- .NET SDK **10.0.401 recommended** (see [.NET SDK version](#net-sdk-version)). The minimum is 10.0.100, set in `global.json`
- Docker
- [Ollama](https://ollama.com/download), installed natively (see below for the container alternative)
- A running Outline instance and an Outline API key
- An Anthropic API key (or configure Ollama for chat as well, see [Configuration](#configuration))

#### .NET SDK version

Use **SDK 10.0.401** (released 2026-09-08, runtime 10.0.12). .NET 10 is an
LTS release, supported until 2028-11-14.

- **Security:** 10.0.100 ships runtime 10.0.0 (November 2025) and lacks every
  security fix since. 10.0.401 is a security release that includes them all.
- **Build workaround:** the 10.0.1xx SDKs have a C# compiler that is too old
  for OllamaSharp's source generator (error CS9057), which is why
  `Directory.Build.targets` removes it. The 4xx SDKs are expected to fix this.
- **Fallback:** 10.0.112 has the same security patches but keeps the old
  compiler. Use it only if a tool requires the 1xx SDKs.

Install it from https://dotnet.microsoft.com/download/dotnet/10.0 or with
`brew install --cask dotnet-sdk`, then check with `dotnet --list-sdks`.
`global.json` still pins 10.0.100 until the upgrade is verified in this repo.

### 1. Start the local dependencies

```bash
docker compose -f docker-compose.rag.yml up -d                  # pgvector on :5433
ollama pull bge-m3                                              # native Ollama on :11434
```

`docker-compose.rag.yml` is separate from `docker-compose.yml` (the Outline
stack), so the two can run side by side.

Ollama runs natively because Docker on macOS has no GPU access: the app uses
Metal, while the container is much slower. Where the app isn't an option
(Linux servers, CI), the compose file has the container behind a profile. It
binds the same port, so quit the app first:

```bash
docker compose -f docker-compose.rag.yml --profile ollama up -d
docker compose -f docker-compose.rag.yml exec ollama ollama pull bge-m3
```

**No Outline instance at hand?** `docker/outline-demo/seed.sh --set-user-secrets`
starts a disposable Outline 1.10.1 on :3000, fills it with the public 37signals
employee handbook (16 documents), creates an API key and a webhook
subscription to the Api, and stores both secrets for step 2 (you still set
`AI:Chat:ApiKey`). Sign in to the web UI with `admin@example.com`; the magic
link arrives in Mailpit at http://localhost:8025. Add more Markdown repos to
`SOURCES` in the script. Remove everything with
`docker compose -f docker/outline-demo/docker-compose.yml down -v` and delete
`docker/outline-demo/.env.rag`.

### 2. Configure secrets

Create an Outline API key under **Settings → API** with a dedicated account.
Every document that account can read will be searchable (see
[Limitations](#current-limitations)). Then store the secrets for local
development. The Api and Worker share one user-secrets store, so one set of
commands configures both:

```bash
dotnet user-secrets --project src/Outline.Rag.Worker set "Outline:ApiToken" "<outline api key>"
dotnet user-secrets --project src/Outline.Rag.Worker set "AI:Chat:ApiKey" "<anthropic api key>"
dotnet user-secrets --project src/Outline.Rag.Worker set "Outline:WebhookSigningSecret" "<webhook secret>"   # optional, see step 5
```

`appsettings.json` defaults to an Outline instance at `http://localhost:3000/`.
For another instance, override `Outline:BaseUrl`.

### 3. Run

```bash
dotnet run --project src/Outline.Rag.Worker    # creates the tables and indexes the wiki
dotnet run --project src/Outline.Rag.Api       # http://localhost:5157
```

In Development, the OpenAPI document is served at
`http://localhost:5157/openapi/v1.json`.

### 4. Try it

```bash
curl -s http://localhost:5157/api/ask -H 'Content-Type: application/json' \
  -d '{"question": "¿Cómo restauro un respaldo de la base de datos?"}'

curl -s http://localhost:5157/api/search -H 'Content-Type: application/json' \
  -d '{"query": "respaldo base de datos", "top": 5}'
```

Both endpoints accept an optional `collectionIds` array to restrict results
to specific Outline collections. `src/Outline.Rag.Api/Outline.Rag.Api.http`
has the same requests for Visual Studio / Rider / VS Code.

### 5. (Optional) Real-time updates through webhooks

In Outline, under **Settings → Webhooks**, create a subscription to the document events that points at
`<api base url>/webhooks/outline`, and store its signing secret as
`Outline:WebhookSigningSecret`. Requests with a missing or invalid
`Outline-Signature` header are rejected. When no secret is configured, every
webhook is rejected.

Without webhooks, changes still arrive through the Worker's periodic sync, but
**deleted and archived documents stay in the index**. The Outline list
endpoint doesn't report them.

## API

| Method & path | Body | Returns |
| --- | --- | --- |
| `POST /api/ask` | `{ "question": string, "collectionIds"?: guid[] }` | `{ answer, citations: [{ number, documentId, title, headingPath, url, score }] }` |
| `POST /api/search` | `{ "query": string, "top"?: int, "collectionIds"?: guid[] }` | Matching chunks with similarity scores |
| `POST /webhooks/outline` | Outline webhook delivery | `202 Accepted` / `401` if the signature is invalid |
| `GET /health` | | `Healthy` |

## Configuration

Settings are in `appsettings.json` and can be overridden with environment
variables (e.g. `AI__Chat__Model`) or user secrets.

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:RagDatabase` | `localhost:5433/outline_rag` | Requires the `vector` extension. The dev password is in `appsettings.Development.json` |
| `Outline:BaseUrl` | `http://localhost:3000/` | Also used to build citation links |
| `Outline:ApiToken` | *(empty)* | Required |
| `Outline:WebhookSigningSecret` | *(empty)* | Webhooks are rejected while empty |
| `AI:Chat:Provider` | `Anthropic` | `Anthropic` or `Ollama` (then set `AI:Chat:Endpoint`). With Ollama, model reasoning ("thinking") is turned off |
| `AI:Chat:Timeout` | *(provider default)* | Time allowed for one answer, e.g. `00:05:00`. Ollama defaults to 5 minutes; Anthropic uses its SDK default |
| `AI:Chat:Model` | `claude-opus-5` | |
| `AI:Chat:ApiKey` | *(empty)* | Falls back to the `ANTHROPIC_API_KEY` environment variable |
| `AI:Embeddings:Model` / `Endpoint` | `bge-m3` / `http://localhost:11434` | Ollama |
| `AI:Embeddings:Dimensions` | `1024` | Must match the embedding model |
| `VectorStore:CollectionName` | `outline_chunks` | Table holding the chunks |
| `Rag:TopK` / `Rag:MinScore` | `6` / `0.3` | Chunks retrieved per question and the minimum similarity |
| `Rag:MaxChunkTokens` / `Rag:ChunkOverlapTokens` | `512` / `64` | Chunking |
| `Sync:Interval` (Worker) | `00:15:00` | Periodic sync |

**Changing the embedding model or dimensions** needs a new
`VectorStore:CollectionName` (a new table). To re-index everything, also clear
the sync checkpoint: `DELETE FROM rag_sync_state;`

## Development

```bash
dotnet build                                            # warnings are treated as errors
dotnet test                                             # all tests
dotnet test --filter-class "*MarkdownHeadingChunkerTests"   # one test class
dotnet test --filter-method "*SkipsHeadingsWithoutBody"     # one test
```

Tests run on Microsoft.Testing.Platform (enabled in `global.json`).
`Directory.Build.targets` removes OllamaSharp's source generator, which needs a
newer C# compiler than SDK 10.0.100 includes. Delete that file after updating
the SDK.

## Current limitations

- **No authentication or per-user permissions.** Any caller can search every
  document the Outline API key can read. The chunks store their collection ID so
  results can be filtered per user. The plan is to authenticate callers with the
  same OIDC provider as Outline and derive their allowed collections (see the
  TODO in `RagEndpoints.cs`). **Don't expose the API outside a trusted network
  until then.**
- Webhook events are queued in memory and lost if the Api restarts during
  processing. The periodic sync picks up the changes, but not deletions.
- No Dockerfiles or Kubernetes manifests for the RAG services yet.

---

## Local Outline environment (legacy)

This part of the repository updates a development Outline instance from a
production database dump to the latest Outline release, using Docker. It was
used for the Outline 0.82.0 → 1.10.1 upgrade.

> **Note:** the `scripts/` folder, `sql/migration.sql`, `.env`/`.env.example`
> and the `.backup` dump files described below are not in this repository.
> Get them from wherever the upgrade material is backed up before following
> these steps.

### Versions

| Component   | Value                                                              |
| ----------- | ------------------------------------------------------------------ |
| Backup (current) | `outline_07_09_2026.backup` — dump of the production database, 2026-09-07 |
| Backup (previous) | `outline.backup` — same database, 2026-08-19                  |
| Backup schema state | 223 Sequelize migrations (≈ Jan 2025) — both dumps         |
| Source database | EDB Postgres Advanced Server 11.10.19 (dumped by pg_dump 18.3) |
| Latest Outline release | **v1.10.1** (`outlinewiki/outline:1.10.1`, released 2026-09-09) |
| Target of this repo's Docker setup | v1.10.0 (`outlinewiki/outline:1.10.0`, 2026-09-01) |
| Migrations in 1.10.0 | 302 (so 79 are applied by the update)                     |
| Migrations in 1.10.1 | 304 total (2 new: a trigger redefinition + 4 indexes)     |
| Production | **v1.10.1** — upgraded 2026-09-11                               |
| PostgreSQL  | 18 (`postgres:18`)                                                 |
| Redis       | 7 (`redis:7-alpine`)                                               |

Production was upgraded **0.82.0 → 1.10.0** (2026-09-07)
and then **1.10.0 → 1.10.1** (2026-09-11); its database is fully migrated
(304 migrations).

### The two environments

The compose file creates a single PostgreSQL container with **two databases**:

1. **`outline_dev`** — the current development environment: the backup
   restored **as-is** (old schema, untouched). Point your current/old Outline
   installation at it if you need to keep working against the old schema.
2. **`outline_latest`** — the updated environment: the backup restored and
   then migrated to the latest schema. The **latest Outline app runs against
   this database**.

### Quick start (full update)

```bash
./scripts/update.sh outline_07_09_2026.backup
```

This starts PostgreSQL + Redis, restores the backup, applies migrations and
starts Outline 1.10.0 at http://localhost:3000.

The backup argument is optional and defaults to `outline.backup`, so pass the
dump you actually want to restore.

#### Step by step

```bash
docker compose up -d redis                              # cache
./scripts/restore.sh outline_07_09_2026.backup          # -> outline_dev + outline_latest
./scripts/migrate.sh                                    # apply 1.10.0 migrations -> outline_latest
docker compose up -d outline                            # start latest Outline app
```

`restore.sh` starts PostgreSQL itself, because the dump to restore is chosen
by the `OUTLINE_BACKUP` bind mount in `docker-compose.yml` — the script exports
it and recreates the container when the mount changes. Whichever file you pass
appears as `/backup/outline.backup` inside the container.

> ⚠️ A restore **drops and recreates** both `outline_dev` and `outline_latest`.
> Anything in them is lost; only the `.backup` files on disk are the source of
> truth.

### Notes about the backup

The dump was produced by an **EnterpriseDB (EDB) Postgres Advanced Server**, so
it contains Oracle-compatibility objects (`dbms_*`, `utl_*`, `aq$_*` schemas,
roles such as `enterprisedb`) that standard PostgreSQL cannot restore.

`restore.sh` handles this by:

- restoring only the **`public` schema** (all Outline data lives there);
- pre-creating the **`uuid-ossp`** and **`unaccent`** extensions, because
  Outline's tables define `id uuid DEFAULT public.uuid_generate_v4()` columns;
- using `--no-owner --no-privileges` so the EDB roles are ignored;
- cloning `outline_dev` → `outline_latest` afterwards.

Two migrations need more than schema access:

- `20250327062414-resolve-collection-index-collisions` is a data migration that
  bootstraps the full Outline app, so migrations need the normal app
  environment variables (`SECRET_KEY`, `UTILS_SECRET`, `REDIS_URL`, `URL`);
- `20260818120000-add-deletedById-to-documents` backfills `deletedById` for
  documents already in the trash.

### Config

- `.env` — local environment (generated for development; ignored by git).
- `.env.example` — template.

### Applying the schema update directly (plain SQL)

If you cannot run the Outline Docker image against your server's database, use
the pre-generated plain-SQL migration script instead:

```bash
psql -U <user> -d <database> -v ON_ERROR_STOP=1 -f sql/migration.sql
```

`sql/migration.sql`:

- applies every schema change between the backup (223 migrations) and
  Outline v1.10.0 (302 migrations) — the 79 pending ones;
- preserves data (e.g. `backlinks` is renamed to `relationships`, not dropped);
- includes the four data `UPDATE`s shipped inside Outline's migrations;
- records all migrations in `SequelizeMeta` so Outline will not re-apply them;
- runs in a single transaction (all-or-nothing).

It was generated by diffing `outline_dev` vs `outline_latest` and has been
validated: applying it to a copy of `outline_dev` yields a schema identical to
`outline_latest` (verified with the `migra` diff tool).

> Note: the data-repair migration
> `20250327062414-resolve-collection-index-collisions` (fixes duplicate
> `collections.index` values) is a Node.js script and cannot be expressed in
> SQL — it is marked as applied in the script. See the header of
> `sql/migration.sql` for details.

### Health check

```bash
curl http://127.0.0.1:3000/_health   # -> OK
```

Sanity-check the migrated database as well:

```bash
docker compose exec -T postgres psql -U outline -d outline_latest \
  -c 'SELECT count(*) FROM "SequelizeMeta";'   # -> 302
```

### Production upgrade (Kubernetes)

The production instance (namespace `outline`) runs
Outline **`1.10.1`** (upgraded 2026-09-11). The upgrade runbooks
(0.82.0 → 1.10.0, 1.10.0 → 1.10.1) and Kubernetes manifests have been moved
out of this repository and are kept in a separate backup.
