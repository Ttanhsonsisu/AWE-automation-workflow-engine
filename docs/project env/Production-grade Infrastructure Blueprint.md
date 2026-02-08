# 🏗️ INFRASTRUCTURE & CONFIGURATION BLUEPRINT

**Version:** 1.0.0 (Final)
**Scope:** Docker, Environment, RabbitMQ, .NET Aspire

### 1. Nguyên tắc Cấu hình (Configuration Principles)

Tuân thủ **12-Factor App**:

1. **Strict Separation:** Code nằm trong Git. Config nằm trong Environment.
2. **Precedence (Thứ tự ưu tiên):**
* Level 1 (Highest): Environment Variables (Production/K8s/Docker).
* Level 2: User Secrets (Local Dev).
* Level 3: `appsettings.{Env}.json`.
* Level 4 (Lowest): `appsettings.json`.


3. **No Secrets in Git:** Tuyệt đối không commit ConnectionString hay API Key vào file JSON.

---

### 2. Quy hoạch Docker (Container Strategy)

#### A. File `.dockerignore` (Root Solution)

*Mục đích: Build image nhanh, nhẹ, bảo mật.*

```text
# --- Git & IDE ---
.git
.gitignore
.vs
.vscode
.idea

# --- Build artifacts ---
**/bin
**/obj
**/TestResults

# --- User / OS ---
*.user
*.suo
*.cache
*.log
.DS_Store
Thumbs.db

# --- Env & Secrets (CRITICAL) ---
.env
.env.*
!*.example
appsettings.Development.json
appsettings.Production.json

# --- Docs & Misc ---
docs
README.md
docker-compose.yml

```

#### B. File `Dockerfile` (Security Hardened)

*Mục đích: Chạy với quyền hạn thấp nhất (Least Privilege).*

```dockerfile
# STAGE 1: BUILD
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy & Restore strategy...
COPY ["src/AWE.Worker/AWE.Worker.csproj", "src/AWE.Worker/"]
COPY ["src/AWE.Contracts/AWE.Contracts.csproj", "src/AWE.Contracts/"]
RUN dotnet restore "src/AWE.Worker/AWE.Worker.csproj"
COPY . .
WORKDIR "/src/src/AWE.Worker"
RUN dotnet build "AWE.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# STAGE 2: PUBLISH
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "AWE.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# STAGE 3: RUNTIME
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# --- SECURITY FIX: Permissions ---
# Cấp quyền cho user 'app' trước khi switch
RUN chown -R app:app /app

# Chạy với user non-root
USER app 
ENTRYPOINT ["dotnet", "AWE.Worker.dll"]

```

---

### 3. Tự động hóa RabbitMQ (VHost Automation)

*Mục đích: Tránh lỗi `NOT_ALLOWED` khi deploy mới.*

Tạo file: `src/AWE.AppHost/rabbitmq/rabbitmq_definitions.json`

```json
{
  "rabbit_version": "3.12",
  "users": [
    {
      "name": "awe-service",
      "password_hash": "", 
      "tags": "administrator"
    }
  ],
  "vhosts": [
    { "name": "/" },
    { "name": "/awe-system" }
  ],
  "permissions": [
    {
      "user": "awe-service",
      "vhost": "/awe-system",
      "configure": ".*",
      "write": ".*",
      "read": ".*"
    },
    {
      "user": "awe-service",
      "vhost": "/",
      "configure": ".*",
      "write": ".*",
      "read": ".*"
    }
  ]
}

```

---

### 4. Quy tắc AppSettings (Aspire Injection Rule)

Trong `AWE.Worker/appsettings.json` và `AWE.ApiGateway/appsettings.json`:

**❌ SAI:**

```json
"ConnectionStrings": {
  "messaging": "amqp://guest:guest@localhost..." 
}

```

**✅ ĐÚNG:**

```json
{
  "Logging": { ... },
  "AllowedHosts": "*"
}
// ConnectionStrings sẽ được Aspire tự động bơm vào Environment Variables

```