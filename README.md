# AWE - Automation Workflow Engine

# Start
docker compose up -d

# Logs
docker compose logs -f worker

# Stop
docker compose down

# Reset infra 
docker compose down -v

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
