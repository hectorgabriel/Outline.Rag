# Reranker results

As of 2026-10-11. The bge-reranker-v2-m3 cross-encoder made retrieval worse on all three demo datasets and added
1.6–2.2 s per question, so it is parked, unmerged, on the `reranker` branch (`origin/reranker`, commit `a33b97c`).
Spanish questions came out worst, which matters for the production wiki: re-test it there before deciding.

## Results on the demo wiki

All runs use the 37signals handbook in the demo Outline, `bge-m3` embeddings, top 6 chunks, as `admin@example.com`.
"Off" is `main` at `Rag:MinScore` 0.5 (vector similarity). "On" reorders 30 vector candidates by reranker score.

| Dataset | Questions | Reranker | Cut-off | Hit@1 | Hit@3 | MRR | Unanswerable rejected | ms per question |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| handbook | 49 + 6 unanswerable | off | MinScore 0.50 | 98 % | 98 % | 0.980 | 50 % | 59 |
| handbook | | on | none (0.00) | 96 % | 98 % | 0.973 | 0 % | 2175 |
| handbook | | on | RerankMinScore 0.10 | 84 % | 84 % | 0.837 | 100 % | 2175 |
| benefits | 35 + 2 unanswerable | off | MinScore 0.50 | 97 % | 100 % | 0.986 | 0 % | 30 |
| benefits | | on | none (0.00) | 94 % | 100 % | 0.971 | 0 % | 1651 |
| benefits | | on | RerankMinScore 0.10 | 74 % | 77 % | 0.757 | 100 % | 1651 |
| follow-ups | 14 + 1 unanswerable | off | MinScore 0.50 | 79 % | 93 % | 0.875 | 100 % | 2881 |
| follow-ups | | on | none (0.00) | 64 % | 100 % | 0.810 | 0 % | 4093 |
| follow-ups | | on | RerankMinScore 0.10 | 50 % | 64 % | 0.571 | 100 % | 4093 |

Hit@n: answerable questions with a chunk of the expected section in the first n. Follow-up times include the
question rewrite by the chat model (qwen3.5:9b-mlx).

- **Ordering:** even with no cut-off, the reranker never beat vector order on hit@1 or MRR.
- **Cut-off:** any reranker threshold that rejects the unanswerable questions also throws away many right
  answers. At 0.01, hit@1 is already 94 % / 91 % / 50 %. No setting improves on the vector-only run.
- **Latency:** every question pays a llama.cpp call over 30 passages on the Mac (Metal, Q8_0 model).

## Why Spanish matters

The demo wiki is in English. Its Spanish questions (7 in handbook, 7 in benefits, 6 in follow-ups) are cross-lingual,
and the reranker scores them close to zero **even when it ranks the right section first**:

| Question | Right section | Reranker score |
| --- | --- | --- |
| ¿Cuándo son las evaluaciones de desempeño? | Performance Reviews, #1 | 0.034 |
| ¿Qué pasa si me despiden? ¿Hay indemnización? | Severance Packages, #1 | 0.003 |
| ¿Tenemos seguro dental? | Dental Insurance, #1 | 0.033 |
| ¿El seguro cubre lentes y exámenes de la vista? | Vision Insurance, #1 | 0.003 |
| ¿Cómo pido vacaciones? | Scheduling Time Off, #1 | 0.010 |
| ¿Cuántas semanas de licencia de maternidad me corresponden? | Family Leave, #1 | 0.013 |

So the Spanish questions fail the cut-off, not the ordering. English questions also score low when they don't
share words with the passage (for example "Can I give paid talks at conferences?" → Moonlighting > OK, 0.024).

On the production wiki, questions and documents are both in Spanish. That removes the cross-lingual handicap, so
the reranker may behave quite differently there; that is the case the demo can't test.

The scores are not a setup bug. llama.cpp's output matched the reference `transformers` implementation (fp32) on
the same pairs, for example "How many vacation days do I get?" vs the Paid Time Off passage: raw logit -0.884
(sigmoid 0.29) in both.

## Re-testing on the Spanish production wiki

1. **Build a dataset** in `tools/Outline.Rag.Eval/datasets/produccion.json`, with 30–50 real questions people ask.
   Use the exact document title, and the heading under it when the answer is in one section. Include 5–10
   questions the wiki can't answer, with `"expected": []`, and a few follow-ups with `history`:

   ```json
   {
     "name": "Wiki de producción",
     "cases": [
       { "question": "¿Cuántos días de vacaciones tengo?", "expected": ["Prestaciones"], "section": "Vacaciones" },
       { "question": "¿Cuál es la contraseña del Wi-Fi?", "expected": [] },
       {
         "history": [
           { "role": "user", "content": "¿Cómo solicito vacaciones?" },
           { "role": "assistant", "content": "Se solicitan en el portal de RH con dos semanas de anticipación [1]." }
         ],
         "question": "¿Y quién las aprueba?",
         "expected": ["Prestaciones"],
         "section": "Vacaciones"
       }
     ]
   }
   ```

   Titles and sections are matched case-insensitively. `section` matches any part of the chunk's heading path.

2. **Index the production wiki** into a local `outline_rag` database by pointing the Worker at production
   (`Outline:BaseUrl` plus an admin `Outline:ApiToken` in user secrets). This only reads Outline's API.

3. **Run the baseline** from `main`:

   ```bash
   dotnet run --project tools/Outline.Rag.Eval -- --email <a real user's email> --dataset datasets/produccion.json
   ```

4. **Run with the reranker** from the `reranker` branch:

   ```bash
   git switch reranker
   llama-server -hf gpustack/bge-reranker-v2-m3-GGUF:Q8_0 --rerank --port 8081 -c 8192 -b 8192 -ub 8192
   dotnet run --project tools/Outline.Rag.Eval -- --email <same email> --dataset datasets/produccion.json --reranker LlamaCpp
   ```

   The table covers every cut-off. Pick `Rag:RerankMinScore` from it; 0.10 is only the branch default.

5. **Decide.** Merge only if the reranker beats the baseline on hit@1 and MRR at a cut-off that still rejects the
   unanswerable questions, and the extra second or two per question is acceptable. Otherwise leave it parked.

## How the reranker is wired

- **Code** (branch `reranker` only):
  - `IReranker` in Application.
  - `LlamaCppReranker` in Infrastructure. It posts to llama-server's `/v1/rerank` and turns the raw logits into
    0–1 scores with a sigmoid.
  - `RetrievalService` fetches `Rag:RerankCandidates` (30) vector candidates, reorders them by reranker score,
    drops those under `Rag:RerankMinScore` (0.1) and keeps the top `Rag:TopK`. If the reranker fails, it falls back
    to vector order.
- **Settings:**
  - `AI:Reranker:Provider` (`None` by default, or `LlamaCpp`), `Model`, `Endpoint` (`http://localhost:8081`),
    `Timeout` (30 s).
  - `ApiKey` for a reranker behind a Bearer gate. Keep it in user secrets, not `appsettings*.json`.
- **Model:** `gpustack/bge-reranker-v2-m3-GGUF:Q8_0` (about 600 MB, multilingual), served by `llama-server`
  (`brew install llama.cpp`).
