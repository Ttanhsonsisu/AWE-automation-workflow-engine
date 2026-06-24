# AWE - Automation Workflow Engine

## Documentation

- [Webhook Plugin (WebhookTrigger)](docs/webhook-plugin.md)
- [Redis Usage Guideline](docs/redis-usage-guideline.md)

# Start
docker compose up -d

# Logs
docker compose logs -f worker

# Stop
docker compose down

# Reset infra 
docker compose down -v

## Run production and development on one machine

`AWE-self-host` is the production stack. This repository uses the separate
`awe-platform-dev` Compose project, separate volumes, and dedicated host ports
from `.env`, so its jobs and data cannot mix with production.

From PowerShell, start the complete development demo (infrastructure, API,
engine worker, plugin worker, and frontend):

```powershell
.\scripts\dev\start-demo.ps1
```

Use `Ctrl+C` to stop the development applications. The infrastructure remains
available for faster restarts. To stop it as well:

```powershell
.\scripts\dev\start-demo.ps1 -StopInfrastructureOnExit
```

Default URLs:

- Production frontend: `http://localhost:7011` (or the configured domain)
- Development frontend: `http://localhost:5173`
- Development API: `http://localhost:18080`
- Development Keycloak: `http://localhost:18081`

## Docker compose usage

- local dev
```bash
cp .env.example .env
docker compose up -d
```
- staging
```bash
docker compose --env-file .env.staging up -d
```

- Production
```bash
docker compose --env-file .env.production up -d
```
