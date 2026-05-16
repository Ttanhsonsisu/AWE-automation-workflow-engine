# Hướng dẫn chạy các bài test thực nghiệm

Tài liệu này hướng dẫn từng bước để chạy các phần test đã chuẩn bị cho chương 6:

- Unit test bằng xUnit.
- Workflow smoke/integration test qua API.
- Load test bằng K6.
- Crash recovery test.

Các lệnh bên dưới chạy từ thư mục root của repo:

```powershell
cd "c:\Users\ttanh\OneDrive\Máy tính\DATN\soucre-code\AWE-automation-workflow-engine"
```

## 1. Chuẩn bị chung

### 1.1. Kiểm tra công cụ cần có

```powershell
dotnet --info
docker --version
docker compose version
```

K6 chỉ cần cho load test:

```powershell
k6 version
```

Nếu `k6` chưa có trong PATH, có thể bỏ qua unit test và integration test; chỉ load test mới cần K6.

### 1.2. Khởi động hạ tầng Docker

Nếu chưa có file `.env`, tạo từ file mẫu:

```powershell
Copy-Item .env.example .env
```

Sau đó chạy hạ tầng:

```powershell
docker compose up -d postgres rabbitmq rabbitmq-setup redis minio keycloak redisinsight
```

Kiểm tra container:

```powershell
docker compose ps
```

Các port mặc định cần lưu ý:

| Thành phần | Port local thường dùng |
| --- | ---: |
| API Gateway | 8080 |
| PostgreSQL | 15432 |
| RabbitMQ AMQP | 5673 |
| RabbitMQ Management | 15673 |
| Keycloak | 8081 |
| MinIO API | 9000 hoặc port trong `.env` |
| Redis | Theo `REDIS_PORT` trong `.env`; nếu không có thì mặc định 6379 |

Nếu app không kết nối được Redis, kiểm tra port trong `.env` và appsettings. Với local process, có thể set tạm:

```powershell
$env:ConnectionStrings__Redis = "localhost:6389,password=change_me,abortConnect=false"
```

Đổi `6389` theo port Redis thật trong `.env`.

## 2. Chạy unit test

Unit test không cần Docker, database, RabbitMQ hay Keycloak. Đây là phần dễ chạy nhất.

### 2.1. Chạy toàn bộ unit test workflow engine

```powershell
dotnet test test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj
```

Kết quả đúng sẽ có dạng:

```text
Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13
```

### 2.2. Unit test đang kiểm tra gì?

| File test | Thành phần kiểm thử |
| --- | --- |
| `ExecutionPointerTests.cs` | Token, acquire lease, complete lease, retry count, suspended pointer. |
| `TransitionEvaluatorTests.cs` | Branch condition, start node, incoming edge count. |
| `JoinBarrierServiceTests.cs` | Join barrier, duplicate dispatch, dead-path. |

### 2.3. Khi test fail thì xem gì?

Chạy lại với log chi tiết hơn:

```powershell
dotnet test test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj --logger "console;verbosity=detailed"
```

Nếu lỗi restore package:

```powershell
dotnet restore test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj
```

## 3. Chạy API và Worker để test workflow thật

Phần này cần Docker hạ tầng đã chạy ở bước 1.2.

Mở 3 terminal riêng.

### 3.1. Terminal 1 - chạy API Gateway

```powershell
dotnet run --project src\Presentation\AWE.ApiGateway\AWE.ApiGateway.csproj --launch-profile http
```

API sẽ chạy ở:

```text
http://localhost:8080
```

### 3.2. Terminal 2 - chạy Worker Engine

```powershell
dotnet run --project src\Workers\AWE.Wokrer.Engine\AWE.Wokrer.Engine.csproj
```

Worker Engine nhận lệnh submit workflow và điều phối pointer.

### 3.3. Terminal 3 - chạy Plugin Worker

```powershell
dotnet run --project src\Workers\AWE.Worker\AWE.Worker.csproj
```

Plugin Worker thực thi node/plugin.

Muốn test nhiều worker, mở thêm terminal 4, 5... và chạy lại cùng lệnh:

```powershell
dotnet run --project src\Workers\AWE.Worker\AWE.Worker.csproj
```

## 4. Lấy access token để gọi API

Các API workflow/plugin/execution đang có `[Authorize]`, nên request cần Bearer token.

Mở Keycloak:

```text
http://localhost:8081
```

Realm trong project:

```text
awe-auth
```

Client frontend có trong realm export:

```text
awe-fe
```

Cách lấy token thực tế phụ thuộc user/role đã import trong Keycloak. Có 2 cách thường dùng:

### 4.1. Lấy token từ frontend hoặc Swagger

Nếu frontend hoặc Swagger đã cấu hình login Keycloak:

1. Login bằng tài khoản có quyền `Operator` hoặc `Editor`.
2. Mở Developer Tools.
3. Copy access token từ request `Authorization: Bearer ...`.
4. Gán vào biến PowerShell:

```powershell
$token = "<access-token>"
```

### 4.2. Lấy token bằng password grant nếu client cho phép

Chỉ dùng cách này nếu Keycloak client đang bật direct access grant:

```powershell
$tokenResponse = Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:8081/realms/awe-auth/protocol/openid-connect/token" `
  -ContentType "application/x-www-form-urlencoded" `
  -Body @{
    grant_type = "password"
    client_id = "test"
    client_secret = "yYOp0Gdz13j0qX3p072zB16E8oClvyJ0"
    username = "admin1"
    password = "1234$"
  }

$token = $tokenResponse.access_token
```

Kiểm tra token đã có:

```powershell
$token.Substring(0, 30)
```

## 5. Tạo workflow definition mẫu

Chọn một file trong `samples/experiments`.

Ví dụ tạo workflow tuyến tính:

```powershell
$base = "http://localhost:8080"

$created = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows/definitions" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -InFile "samples/experiments/linear-log.workflow.json"

$definitionId = $created.data.id
$definitionId
```

Các file có thể tạo:

```powershell
samples/experiments/linear-log.workflow.json
samples/experiments/branch-join.workflow.json
samples/experiments/retry.workflow.json
samples/experiments/crash-recovery.workflow.json
```

Nếu cần publish:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows/definitions/$definitionId/publish" `
  -Headers @{ Authorization = "Bearer $token" }
```

## 6. Submit workflow và xem kết quả

### 6.1. Submit workflow

```powershell
$submitBody = @{
  definitionId = $definitionId
  jobName = "manual-test-001"
  inputData = @{
    message = "hello from test"
    score = 90
  }
  isTest = $true
} | ConvertTo-Json -Depth 10

$submitted = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $submitBody

$instanceId = $submitted.data.instanceId
$instanceId
```

### 6.2. Xem trạng thái execution

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/executions/$instanceId" `
  -Headers @{ Authorization = "Bearer $token" }
```

### 6.3. Xem log execution

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/executions/$instanceId/logs" `
  -Headers @{ Authorization = "Bearer $token" }
```

### 6.4. Xem context workflow

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/workflows/$instanceId/context" `
  -Headers @{ Authorization = "Bearer $token" }
```

Kết quả đúng:

- `linear-log`: workflow chuyển `Completed`.
- `branch-join` với `score=90`: nhánh pass chạy, nhánh fail bị `Skipped`, Join xử lý tiếp.
- `branch-join` với `score=50`: nhánh fail chạy, nhánh pass bị `Skipped`, Join xử lý tiếp.
- `retry`: node `RetryTest` fail tạm thời vài lần rồi hoàn tất nếu chưa vượt `MaxRetries`.

## 7. Chạy load test bằng K6

### 7.1. Cài K6 nếu chưa có

Nếu `k6 version` báo không tìm thấy lệnh, cài K6 trước. Trên Windows có thể dùng:

```powershell
winget install k6.k6
```

Mở terminal mới và kiểm tra:

```powershell
k6 version
```

### 7.2. Chuẩn bị biến môi trường

Dùng workflow nhẹ như `linear-log` hoặc `branch-join`.

```powershell
$env:BASE_URL = "http://localhost:8080"
$env:TOKEN = $token
$env:DEFINITION_ID = $definitionId
$env:VUS = "20"
$env:DURATION = "2m"
```

### 7.3. Chạy submit-only load test

```powershell
k6 run experiments/k6/submit-workflow.js
```

Chế độ này đo tốc độ API nhận request submit workflow.

### 7.4. Chạy end-to-end load test

```powershell
$env:POLL_COMPLETION = "true"
$env:POLL_TIMEOUT_SECONDS = "120"

k6 run experiments/k6/submit-workflow.js
```

Chế độ này submit workflow rồi poll đến khi workflow `Completed` hoặc `Failed`.

### 7.5. So sánh 1 worker và nhiều worker

Lần 1: chỉ chạy 1 terminal Plugin Worker.

```powershell
$env:VUS = "20"
$env:DURATION = "2m"
k6 run experiments/k6/submit-workflow.js
```

Lần 2: mở thêm 3 terminal Plugin Worker nữa, tổng cộng 4 worker, rồi chạy lại:

```powershell
$env:VUS = "20"
$env:DURATION = "2m"
k6 run experiments/k6/submit-workflow.js
```

Ghi lại các chỉ số:

| Chỉ số | Xem ở đâu |
| --- | --- |
| `workflow_submit_latency` | Output K6 |
| `workflow_completion_latency` | Output K6 khi bật `POLL_COMPLETION=true` |
| `workflow_completed` | Output K6 |
| `workflow_failed` | Output K6 |
| `http_req_failed` | Output K6 |
| Queue depth | RabbitMQ Management UI |
| CPU/RAM | Task Manager hoặc `docker stats` |

RabbitMQ Management UI:

```text
http://localhost:15673
```

## 8. Chạy crash recovery test

Crash test cần workflow `crash-recovery.workflow.json`, workflow này dùng Dynamic DLL `SleepPlugin`.

### 8.1. Build plugin Sleep

```powershell
dotnet publish test/plugin-crash-sleep/plugin-crash-sleep.csproj -c Release -o artifacts/plugins/sleep
```

File DLL sẽ nằm tại:

```text
artifacts/plugins/sleep/plugin-crash-sleep.dll
```

### 8.2. Tạo package plugin

```powershell
$pkgBody = @{
  uniqueName = "awe-experiments-sleep"
  displayName = "Sleep / Crash Test"
  executionMode = "DynamicDll"
  category = "Testing"
  icon = "lucide-hourglass"
  description = "Long-running plugin for crash recovery experiment"
} | ConvertTo-Json

$pkg = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/plugins/packages" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $pkgBody

$packageId = $pkg.data.id
$packageId
```

### 8.3. Upload plugin version

```powershell
curl.exe `
  -H "Authorization: Bearer $token" `
  -F "Version=1.0.0" `
  -F "Bucket=awe-plugins" `
  -F "File=@artifacts/plugins/sleep/plugin-crash-sleep.dll" `
  "$base/api/plugins/packages/$packageId/versions"
```

Lưu lại phần `executionMetadata` trong response. Metadata này có `Bucket`, `ObjectKey`, `Sha256`, `PluginType`.

### 8.4. Cập nhật workflow crash-recovery

Mở file:

```text
samples/experiments/crash-recovery.workflow.json
```

Thay phần `ExecutionMetadata` của node `sleep` bằng metadata vừa upload.

Sau đó tạo workflow definition:

```powershell
$createdCrash = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows/definitions" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -InFile "samples/experiments/crash-recovery.workflow.json"

$crashDefinitionId = $createdCrash.data.id
```

### 8.5. Chạy ít nhất 2 Plugin Worker

Mở 2 terminal khác nhau, mỗi terminal chạy:

```powershell
dotnet run --project src\Workers\AWE.Worker\AWE.Worker.csproj
```

Vẫn cần API Gateway và Worker Engine đang chạy.

### 8.6. Submit workflow crash test

```powershell
$crashBody = @{
  definitionId = $crashDefinitionId
  jobName = "crash-test-001"
  inputData = @{
    message = "crash recovery test"
    score = 90
  }
  isTest = $true
} | ConvertTo-Json -Depth 10

$crashRun = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $crashBody

$crashInstanceId = $crashRun.data.instanceId
$crashInstanceId
```

### 8.7. Dừng worker đang xử lý node sleep

Trong log Plugin Worker, tìm dòng có node `sleep` hoặc plugin `AWE.Experiments.Sleep`.

Chờ khoảng 15-20 giây rồi tắt đúng terminal worker đó.

Lý do chờ 15-20 giây: lease ban đầu là 5 phút, heartbeat sau đó renew về khoảng 30 giây. Nếu tắt quá sớm, bạn có thể phải chờ lâu hơn để lease hết hạn.

### 8.8. Quan sát recovery

Worker Engine có `RecoveryBackgroundService`, quét zombie pointer mỗi 30 giây. Theo dõi log Worker Engine, kỳ vọng thấy log tương tự:

```text
Found {Count} zombie pointers. Resetting...
```

Sau đó worker còn sống sẽ xử lý lại node.

Kiểm tra execution:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/executions/$crashInstanceId" `
  -Headers @{ Authorization = "Bearer $token" }
```

Kiểm tra log:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri "$base/api/executions/$crashInstanceId/logs" `
  -Headers @{ Authorization = "Bearer $token" }
```

### 8.9. Query database để chứng minh lease/retry

Dùng PostgreSQL client bất kỳ, kết nối:

```text
Host: localhost
Port: 15432
Database: awe_db
Username: awe_user
Password: change_me
```

Query:

```sql
select id,
       step_id,
       status,
       active,
       retry_count,
       leased_by,
       leased_until,
       start_time,
       end_time
from execution_pointers
where instance_id = '<crashInstanceId>'
order by created_at;
```

Kết quả cần chụp:

- Trước crash: node `sleep` có `status=1` hoặc `Running`, có `leased_by`.
- Sau lease timeout: `retry_count` tăng.
- Sau recovery: `leased_by` đổi sang worker khác.
- Cuối cùng: workflow `Completed`.

## 9. Checklist chạy nhanh

Nếu chỉ muốn kiểm tra nhanh:

```powershell
# 1. Unit test
dotnet test test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj

# 2. Hạ tầng
docker compose up -d postgres rabbitmq rabbitmq-setup redis minio keycloak redisinsight

# 3. API
dotnet run --project src\Presentation\AWE.ApiGateway\AWE.ApiGateway.csproj --launch-profile http

# 4. Worker Engine
dotnet run --project src\Workers\AWE.Wokrer.Engine\AWE.Wokrer.Engine.csproj

# 5. Plugin Worker
dotnet run --project src\Workers\AWE.Worker\AWE.Worker.csproj
```

Sau đó lấy token, tạo definition, submit workflow theo các bước ở trên.

## 10. Lỗi thường gặp

### API trả 401 Unauthorized

Nguyên nhân: thiếu token hoặc token không hợp lệ.

Cách xử lý:

- Lấy lại token Keycloak.
- Kiểm tra header có dạng `Authorization: Bearer <token>`.
- Kiểm tra user có role phù hợp với policy `Operator` hoặc `Editor`.

### Không kết nối được PostgreSQL

Kiểm tra container:

```powershell
docker compose ps postgres
```

Kiểm tra connection string trong appsettings:

```text
Host=localhost;Port=15432;Database=awe_db;Username=awe_user;Password=change_me
```

### Không kết nối được RabbitMQ

Kiểm tra container:

```powershell
docker compose ps rabbitmq
```

Mở UI:

```text
http://localhost:15673
```

Kiểm tra cấu hình:

```text
Host=localhost
Port=5673
VirtualHost=awe-system
Username=awe-service
Password=change_me
```

### Redis connection refused hoặc NOAUTH

Kiểm tra port Redis thật trong `.env`:

```powershell
Select-String -Path .env -Pattern "REDIS_PORT|REDIS_PASSWORD"
```

Nếu appsettings không khớp, set tạm biến môi trường trước khi chạy process:

```powershell
$env:ConnectionStrings__Redis = "localhost:<redis-port>,password=<redis-password>,abortConnect=false"
```

### K6 không chạy

Kiểm tra:

```powershell
k6 version
```

Nếu chưa cài:

```powershell
winget install k6.k6
```

### Crash test chờ quá lâu

Nguyên nhân thường gặp: worker bị tắt trước lần heartbeat đầu tiên, nên lease ban đầu vẫn còn dài.

Cách xử lý:

- Chạy lại crash test.
- Sau khi node `sleep` bắt đầu, chờ 15-20 giây rồi mới tắt worker.
- Kiểm tra `leased_until` trong bảng `execution_pointers`.
