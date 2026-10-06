# DevStudio AI Server (Ollama endpoint)

Corporate GPU/Ollama server ("servidor IA") hosted on the `DevStudio` machine, reachable from the office network at `10.97.0.19`.
Validated end-to-end on **2026-09-28** (native + OpenAI-compatible generation through the gate, TLS trust from macOS).

> ⚠️ **No credentials in this file.** The key (`SERVIDOR_IA_CLAVE`) comes from the platform team and must stay in environment variables, user-secrets or a password manager — never commit it.

## Access summary

| Item | Value |
|---|---|
| API endpoint | `https://10.97.0.19:8443` (Caddy reverse proxy, internal TLS) |
| Auth | `Authorization: Bearer $SERVIDOR_IA_CLAVE` — required on every request; anything else gets `401 "no autorizado"` |
| Backend | Ollama **0.34.3** (native API `/api/*`, OpenAI-compatible API `/v1/*`) |
| SSH | `ssh DevStudio@10.97.0.19` (password auth; the box is a Mac) |

Notes:
- Port `:80` answers `308` → redirects to `:8443`. Plain Ollama on `:11434` is **not** exposed to the network — go through the gate.
- The auth gate covers every path, including `/` and `/health`; `no autorizado` without the Bearer header is expected.

## Quick test

There's a reusable smoke test under `scripts/`: `./scripts/test-ai-gate.sh` (reads `SERVIDOR_IA_CLAVE` from the environment; see the script header for options).

```bash
export SERVIDOR_IA_CLAVE="<64-character key>"

# version
curl -sS https://10.97.0.19:8443/api/version -H "Authorization: Bearer $SERVIDOR_IA_CLAVE"
# -> {"version":"0.34.3"}

# model list (native and OpenAI-compatible)
curl -sS https://10.97.0.19:8443/api/tags   -H "Authorization: Bearer $SERVIDOR_IA_CLAVE"
curl -sS https://10.97.0.19:8443/v1/models  -H "Authorization: Bearer $SERVIDOR_IA_CLAVE"

# one-line generation (OpenAI-compatible)
curl -sS https://10.97.0.19:8443/v1/chat/completions \
  -H "Authorization: Bearer $SERVIDOR_IA_CLAVE" -H "Content-Type: application/json" \
  -d '{"model":"qwen2.5-coder:14b","messages":[{"role":"user","content":"Responde con una sola linea: PRUEBA OK"}]}'
```

## Models (2026-09-28)

| Model | Params | Notes |
|---|---|---|
| `qwen2.5-coder:32b` | 32.8B | tools |
| `qwen2.5-coder:14b` | 14.8B | tools; usually kept loaded on the GPU |
| `qwen2.5-coder:latest` | 7.6B | tools |
| `qwen2.5:7b-instruct` | 7.6B | tools |
| `gemma4:26b` | 25.2B | vision + thinking, 256k context |

## TLS certificate (Caddy internal CA)

The endpoint uses Caddy's internal CA, so clients must trust its root certificate:

- Subject: `CN = Caddy Local Authority - 2026 ECC Root`
- SHA-256: `74:B9:59:7A:2D:FB:17:3E:C0:52:91:CB:BC:F2:65:91:48:0A:D7:E1:F8:AB:92:F2:6B:AF:01:8A:A5:71:3F:72`
- Copy of the file: on the box at `~/Documents/certificados/root.crt`

Get and verify:

```bash
scp DevStudio@10.97.0.19:'~/Documents/certificados/root.crt' .
openssl x509 -in root.crt -noout -subject -issuer -dates -fingerprint -sha256
```

Install (trust):
- **macOS**: `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain root.crt`
- **Windows**: `certutil -addstore -f "Root" root.crt`
- **Linux**: `sudo cp root.crt /usr/local/share/ca-certificates/ && sudo update-ca-certificates`

After installing, tools that use the system trust store validate the endpoint normally (verified with macOS `curl` — no `-k` needed).
Tools that bundle their own CA list (some Python/Node setups) may still need to be pointed at `root.crt` explicitly.

## Gotchas

- Every request needs the Bearer header. Opening `https://10.97.0.19:8443/` in a browser only shows `no autorizado`.
- The server's leaf certificates rotate often (Caddy renews them); only the **root** needs to be installed, and only once.
- Keep `SERVIDOR_IA_CLAVE` out of git, logs and screenshots. If it leaks, ask the platform team to rotate it.
