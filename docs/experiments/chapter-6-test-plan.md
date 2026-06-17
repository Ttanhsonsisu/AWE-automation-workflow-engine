# Tài liệu kiểm thử và đánh giá thực nghiệm AWE

Tài liệu này mô tả kế hoạch kiểm thử, cách chạy thực nghiệm và cách ghi nhận kết quả cho chương 6 của hệ thống **AWE - Automation Workflow Engine**. Nội dung bám theo các nhóm đánh giá: môi trường thực nghiệm, unit test, load test, crash test và so sánh kết quả.

Các artifact đã chuẩn bị trong source code:

| Artifact | Đường dẫn | Mục đích |
| --- | --- | --- |
| Unit test project | `test/AWE.WorkflowEngine.Tests` | Kiểm thử thuật toán Join, Branch/Trigger, Token/Lease và Variable Resolution. |
| Workflow mẫu | `samples/experiments` | Dữ liệu workflow dùng cho smoke test, branch-join, retry và crash recovery. |
| K6 script | `experiments/k6/submit-workflow.js` | Sinh tải bằng cách submit workflow qua API Gateway. |
| Crash plugin | `test/plugin-crash-sleep` | Dynamic DLL chạy lâu để mô phỏng worker bị ngắt khi đang xử lý node. |

Hướng dẫn chạy chi tiết từng phần được đặt tại `docs/experiments/how-to-run-tests.md`.

## 1. Mục tiêu kiểm thử

Mục tiêu của bộ kiểm thử là chứng minh hệ thống workflow engine hoạt động đúng ở ba tầng:

1. **Tầng thuật toán lõi**: kiểm chứng Join, Branch, Token, Leasing và Retry bằng unit test.
2. **Tầng vận hành**: kiểm chứng API, message queue, worker và database phối hợp đúng khi có nhiều workflow chạy đồng thời.
3. **Tầng chịu lỗi**: kiểm chứng worker bị dừng đột ngột không làm workflow kẹt vĩnh viễn, nhờ cơ chế lease timeout và recovery.

Phạm vi kiểm thử tập trung vào backend/runtime. Tài liệu này không đánh giá giao diện frontend, trải nghiệm người dùng hoặc bảo mật OAuth/Keycloak ở mức penetration test.

## 2. Môi trường thực nghiệm

### 2.1. Cấu hình phần cứng và phần mềm

Môi trường ghi nhận trên máy thực nghiệm:

| Thành phần | Cấu hình |
| --- | --- |
| CPU | AMD Ryzen 7 8845HS w/ Radeon 780M Graphics |
| Số nhân | 8 cores / 16 logical processors |
| RAM | 33,585,426,432 bytes, xấp xỉ 31.3 GiB |
| OS | Windows 10.0.26200 x64 |
| .NET SDK | 10.0.300 |
| .NET runtime host | 10.0.8 |
| Docker | 29.1.3 |
| Docker Compose | v5.0.0-desktop.1 |

Lệnh kiểm tra lại môi trường:

```powershell
dotnet --info
docker --version
docker compose version
Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory
```

### 2.2. Thành phần hệ thống dùng trong thực nghiệm

Các service phụ trợ được cấu hình trong `docker-compose.yml`:

| Service | Vai trò |
| --- | --- |
| PostgreSQL | Lưu workflow definition, workflow instance, execution pointer, audit log. |
| RabbitMQ | Message broker cho lệnh submit workflow, execute plugin và event hoàn tất/thất bại. |
| Redis | Distributed lock provider cho Join barrier. |
| MinIO | Lưu plugin Dynamic DLL. |
| Keycloak | Identity provider để cấp access token gọi API. |
| API Gateway | Cung cấp REST API cho workflow, plugin, execution. |
| Worker Engine | Nhận lệnh submit workflow, điều phối pointer và xử lý event. |
| Plugin Worker | Thực thi built-in plugin hoặc Dynamic DLL plugin. |

Ghi chú vận hành: trong `docker-compose.yml` hiện tại, phần hạ tầng đang được bật, còn các service ứng dụng như `api-gateway`, `worker-engine`, `worker` đang được comment. Vì vậy có thể chạy theo một trong hai cách:

| Cách chạy | Khi nào dùng | Cách thực hiện |
| --- | --- | --- |
| Local process | Phù hợp khi debug và làm luận văn trên máy cá nhân. | Chạy hạ tầng bằng Docker, chạy API/Worker bằng `dotnet run`. |
| Container mode | Phù hợp khi đo scale nhiều worker. | Bật lại service app trong compose, bỏ `container_name` ở service cần scale, dùng `docker compose up --scale worker=N`. |

## 3. Dữ liệu và workflow mẫu

### 3.1. Danh sách workflow mẫu

Các file workflow mẫu nằm trong `samples/experiments`:

| File | Kịch bản | Mục tiêu kiểm thử |
| --- | --- | --- |
| `linear-log.workflow.json` | `ManualTrigger -> Log` | Smoke test đường chạy đơn giản nhất. |
| `branch-join.workflow.json` | `ManualTrigger -> Branch -> Join -> Log` | Kiểm chứng rẽ nhánh điều kiện, skipped branch và Join barrier. |
| `retry.workflow.json` | `ManualTrigger -> RetryTest -> Log` | Kiểm chứng retry khi plugin phát sinh lỗi tạm thời. |
| `crash-recovery.workflow.json` | `ManualTrigger -> Sleep DynamicDll -> Log` | Kiểm chứng worker crash, lease timeout và recovery. |

### 3.2. Tạo workflow definition

Ví dụ tạo workflow `branch-join` qua API:

```powershell
$base = "http://localhost:8080"
$token = "<access-token>"

Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows/definitions" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -InFile "samples/experiments/branch-join.workflow.json"
```

Nếu cần publish definition:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows/definitions/<definition-id>/publish" `
  -Headers @{ Authorization = "Bearer $token" }
```

### 3.3. Submit workflow mẫu

```powershell
$body = @{
  definitionId = "<definition-id>"
  jobName = "experiment-branch-join-001"
  inputData = @{
    message = "hello"
    score = 90
  }
  isTest = $true
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/workflows" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body $body
```

Kết quả mong đợi: API trả về `instanceId`; workflow instance sau đó chuyển dần qua các trạng thái `Running` và `Completed`.

## 4. Kiểm thử đơn vị

### 4.1. Công cụ và cách chạy

Unit test sử dụng xUnit, đặt tại `test/AWE.WorkflowEngine.Tests`.

Lệnh chạy:

```powershell
dotnet test test/AWE.WorkflowEngine.Tests/AWE.WorkflowEngine.Tests.csproj
```

### 4.2. Phạm vi unit test

Unit test tập trung vào các thuật toán có ảnh hưởng trực tiếp đến tính đúng đắn của workflow runtime:

| Nhóm | Lớp kiểm thử | Ý nghĩa |
| --- | --- | --- |
| Token và Leasing | `ExecutionPointer` | Kiểm tra vòng đời pointer, acquire lease, lease conflict, zombie takeover, retry count, suspended pointer, delay wake-up và skipped pointer. |
| Variable Resolution | `VariableResolver` | Kiểm tra nội suy biến từ workflow input, step output, metadata hệ thống, giữ đúng kiểu JSON và phát hiện biến thiếu. |
| Branch và Trigger | `TransitionEvaluator` | Kiểm tra tìm start node, lọc Manual/Webhook/Cron trigger, đánh giá điều kiện, fail-safe khi thiếu biến hoặc expression lỗi, đếm incoming edges. |
| Join | `JoinBarrierService` | Kiểm tra barrier chỉ mở khi đủ nhánh, chống dispatch trùng, xử lý mixed skipped/pending branch và dead-path. |

Các test Join dùng fake repository và fake distributed lock để giữ đúng tính chất unit test, không phụ thuộc PostgreSQL, RabbitMQ hoặc Redis.

### 4.3. Danh sách test case unit

| Mã test | Thành phần | Mục tiêu | Kết quả mong đợi |
| --- | --- | --- | --- |
| UT-EP-01 | ExecutionPointer | Acquire lease khi pointer đang `Pending`. | Pointer chuyển sang `Running`, có `LeasedBy`, `LeasedUntil`. |
| UT-EP-02 | ExecutionPointer | Worker khác chiếm lại lease khi pointer `Running` đã hết hạn. | Acquire thành công, `LeasedBy` đổi, `RetryCount` tăng. |
| UT-EP-03 | ExecutionPointer | Worker khác cố acquire pointer `Running` khi lease còn hạn. | Acquire thất bại, `LeasedBy` không đổi, `RetryCount` không tăng. |
| UT-EP-04 | ExecutionPointer | Complete pointer bằng worker không sở hữu lease. | Ném lỗi lease conflict, pointer vẫn `Running`. |
| UT-EP-05 | ExecutionPointer | Complete pointer bằng đúng worker sở hữu lease. | Pointer `Completed`, `Active=false`, lease được xóa. |
| UT-EP-06 | ExecutionPointer | Reset pointer đang chạy về `Pending`. | Pointer `Pending`, lease được xóa, `RetryCount` tăng. |
| UT-EP-07 | ExecutionPointer | Reset pointer đã `Completed`. | Ném lỗi, pointer giữ trạng thái terminal. |
| UT-EP-08 | ExecutionPointer | Resume pointer đang `Suspended` vì webhook/approval. | Pointer chuyển sang `Completed`, `ResumeAt=null`. |
| UT-EP-09 | ExecutionPointer | Wake-up pointer delay đang `Suspended`. | Pointer chuyển về `Pending`, `ResumeAt=null`. |
| UT-EP-10 | ExecutionPointer | Skip pointer thuộc dead-path. | Pointer `Skipped`, inactive, lease được xóa. |
| UT-VR-01 | VariableResolver | Resolve payload có `workflow.input`, `steps.<id>.output` và `workflow.system`. | Payload JSON hợp lệ, string/number/bool/object giữ đúng kiểu. |
| UT-VR-02 | VariableResolver | Resolve payload thiếu biến. | Trả failure, giữ payload gốc, liệt kê biến thiếu. |
| UT-VR-03 | VariableResolver | Resolve payload rỗng. | Trả thành công với `{}`. |
| UT-TE-01 | TransitionEvaluator | Tìm start node theo `ManualTrigger`. | Trả về đúng node trigger thủ công. |
| UT-TE-02 | TransitionEvaluator | Tìm webhook trigger theo `RoutePath`. | Chỉ trả webhook route khớp. |
| UT-TE-03 | TransitionEvaluator | Tìm cron trigger theo step id. | Chỉ trả cron trigger khớp id. |
| UT-TE-04 | TransitionEvaluator | Đánh giá các transition true/false/missing variable. | Điều kiện đúng trả true, sai hoặc thiếu biến trả false. |
| UT-TE-05 | TransitionEvaluator | Transition không có condition. | Mặc định trả true. |
| UT-TE-06 | TransitionEvaluator | Condition expression không hợp lệ. | Fail-safe trả false, engine không crash. |
| UT-TE-07 | TransitionEvaluator | Đếm incoming edges vào Join. | Số cạnh vào Join đúng với definition. |
| UT-TE-08 | TransitionEvaluator | Definition có nhiều start node độc lập. | Trả đủ danh sách start node. |
| UT-JB-01 | JoinBarrierService | Chưa đủ nhánh đến Join. | Barrier chưa mở, không dispatch pointer. |
| UT-JB-02 | JoinBarrierService | Đủ nhánh đến Join. | Barrier mở, chọn một pointer đại diện, pointer dư được đánh dấu `Completed`. |
| UT-JB-03 | JoinBarrierService | Join đã từng dispatch trước đó. | Không dispatch trùng. |
| UT-JB-04 | JoinBarrierService | Tất cả nhánh vào Join đều `Skipped`. | Barrier mở theo dead-path, không dispatch plugin Join. |
| UT-JB-05 | JoinBarrierService | Một nhánh `Skipped`, một nhánh `Pending`. | Dispatch pointer `Pending`, không coi là dead-path toàn phần. |
| UT-JB-06 | JoinBarrierService | Nhiều pointer `Pending` cùng đến Join. | Chỉ giữ một pointer đại diện, các pointer dư chuyển `Completed`. |

### 4.4. Tiêu chí đạt

Unit test được xem là đạt khi:

- Tất cả test case pass.
- Không cần database hoặc message broker để chạy unit test.
- Các test có thể chạy lặp lại nhiều lần và cho cùng kết quả.

Kết quả hiện tại:

```text
Passed: 27/27
```

## 5. Kiểm thử chịu tải và hiệu năng

### 5.1. Công cụ

Sử dụng K6 để gửi nhiều request `POST /api/workflows` đến API Gateway.

Script:

```text
experiments/k6/submit-workflow.js
```

Endpoint được test:

```http
POST /api/workflows
GET /api/executions/{id}
```

`GET /api/executions/{id}` chỉ được gọi khi bật `POLL_COMPLETION=true`.

### 5.2. Biến môi trường của script K6

| Biến | Bắt buộc | Giá trị mặc định | Ý nghĩa |
| --- | --- | --- | --- |
| `BASE_URL` | Không | `http://localhost:8080` | URL API Gateway. |
| `TOKEN` | Có | Không có | Bearer token để gọi API có `[Authorize]`. |
| `DEFINITION_ID` | Có | Không có | Workflow definition dùng để submit. |
| `VUS` | Không | `10` | Số virtual users. |
| `DURATION` | Không | `1m` | Thời gian chạy bài test. |
| `POLL_COMPLETION` | Không | `false` | Có poll đến trạng thái terminal hay không. |
| `POLL_TIMEOUT_SECONDS` | Không | `60` | Timeout chờ mỗi workflow hoàn tất. |
| `POLL_INTERVAL_SECONDS` | Không | `1` | Khoảng cách giữa các lần poll. |
| `THINK_TIME_SECONDS` | Không | `0` | Thời gian nghỉ giữa các vòng lặp của mỗi VU. |

### 5.3. Chạy bài test submit-only

```powershell
$env:BASE_URL = "http://localhost:8080"
$env:TOKEN = "<access-token>"
$env:DEFINITION_ID = "<definition-id>"
$env:VUS = "20"
$env:DURATION = "2m"

k6 run experiments/k6/submit-workflow.js
```

Submit-only phù hợp để đo độ trễ nhận request và khả năng tạo workflow instance.

### 5.4. Chạy bài test có poll hoàn tất

```powershell
$env:BASE_URL = "http://localhost:8080"
$env:TOKEN = "<access-token>"
$env:DEFINITION_ID = "<definition-id>"
$env:VUS = "20"
$env:DURATION = "2m"
$env:POLL_COMPLETION = "true"
$env:POLL_TIMEOUT_SECONDS = "120"

k6 run experiments/k6/submit-workflow.js
```

Chế độ này phù hợp để đo thời gian xử lý end-to-end từ lúc submit workflow đến khi workflow hoàn tất.

### 5.5. Thiết lập so sánh 1 worker và nhiều worker

Local process:

```powershell
# Terminal 1: API Gateway
dotnet run --project src/Presentation/AWE.ApiGateway/AWE.ApiGateway.csproj

# Terminal 2: Worker Engine
dotnet run --project src/Workers/AWE.Wokrer.Engine/AWE.Wokrer.Engine.csproj

# Terminal 3: Plugin Worker 1
dotnet run --project src/Workers/AWE.Worker/AWE.Worker.csproj
```

Để chạy nhiều plugin worker, mở thêm terminal và chạy lặp lại:

```powershell
dotnet run --project src/Workers/AWE.Worker/AWE.Worker.csproj
```

Container mode, sau khi bật các service ứng dụng trong `docker-compose.yml` và bỏ `container_name` ở service cần scale:

```powershell
docker compose up -d --scale worker=1
k6 run experiments/k6/submit-workflow.js

docker compose up -d --scale worker=4
k6 run experiments/k6/submit-workflow.js
```

### 5.6. Kịch bản load test

| Mã test | Workflow | Worker | VUS | Duration | Mục tiêu |
| --- | --- | ---: | ---: | --- | --- |
| LT-01 | Linear Log | 1 | 20 | 2m | Lấy baseline với một worker. |
| LT-02 | Linear Log | 4 | 20 | 2m | Đánh giá scale-out khi tăng worker. |
| LT-03 | Branch Join | 1 | 20 | 2m | Đánh giá overhead của branch/join. |
| LT-04 | Branch Join | 4 | 20 | 2m | Đánh giá Join khi nhiều worker xử lý song song. |
| LT-05 | Retry | 1 | 10 | 2m | Đánh giá ảnh hưởng của retry đến latency và throughput. |

### 5.7. Chỉ số cần ghi nhận

| Chỉ số | Nguồn | Ý nghĩa |
| --- | --- | --- |
| `workflow_submit_latency` p95 | K6 | Độ trễ API submit workflow. |
| `workflow_completion_latency` p95 | K6 | Thời gian end-to-end nếu có poll completion. |
| `http_req_failed` | K6 | Tỷ lệ HTTP request lỗi. |
| `workflow_submit_errors` | K6 | Tỷ lệ submit không trả về `instanceId`. |
| `workflow_completed` | K6/API | Số workflow hoàn tất. |
| `workflow_failed` | K6/API | Số workflow thất bại. |
| Queue depth | RabbitMQ Management UI | Độ tồn đọng message. |
| CPU/RAM worker | `docker stats` hoặc Task Manager | Tài nguyên tiêu thụ khi tăng worker. |

Tiêu chí đạt tham khảo:

- `http_req_failed < 5%`.
- `workflow_submit_errors < 5%`.
- Không có workflow bị kẹt vĩnh viễn ở `Running`.
- Khi tăng từ 1 worker lên nhiều worker, throughput tăng hoặc queue depth giảm trong cùng điều kiện tải.

## 6. Kiểm thử tự phục hồi khi worker crash

### 6.1. Mục tiêu

Crash test nhằm chứng minh hệ thống không mất trạng thái workflow khi plugin worker bị dừng đột ngột. Cơ chế cần chứng minh gồm:

- Worker chỉ xử lý pointer sau khi acquire lease.
- Worker khác không thể complete pointer nếu không sở hữu lease.
- Khi worker chết, lease không được renew.
- Recovery service phát hiện pointer `Running` quá hạn và reset về `Pending`.
- Worker còn sống nhận lại pointer và workflow tiếp tục chạy.

### 6.2. Chuẩn bị plugin chạy lâu

Build plugin:

```powershell
dotnet publish test/plugin-crash-sleep/plugin-crash-sleep.csproj -c Release -o artifacts/plugins/sleep
```

Tạo package plugin:

```powershell
$base = "http://localhost:8080"
$token = "<access-token>"

$pkg = Invoke-RestMethod `
  -Method Post `
  -Uri "$base/api/plugins/packages" `
  -Headers @{ Authorization = "Bearer $token" } `
  -ContentType "application/json" `
  -Body (@{
    uniqueName = "awe-experiments-sleep"
    displayName = "Sleep / Crash Test"
    executionMode = "DynamicDll"
    category = "Testing"
    icon = "lucide-hourglass"
    description = "Long-running plugin for crash recovery experiment"
  } | ConvertTo-Json)

$packageId = $pkg.data.id
```

Upload DLL:

```powershell
curl.exe `
  -H "Authorization: Bearer $token" `
  -F "Version=1.0.0" `
  -F "Bucket=awe-plugins" `
  -F "File=@artifacts/plugins/sleep/plugin-crash-sleep.dll" `
  "$base/api/plugins/packages/$packageId/versions"
```

Sau khi upload, lấy `ExecutionMetadata` từ response version và thay vào workflow `samples/experiments/crash-recovery.workflow.json`, sau đó tạo definition `EXP-04 Crash Recovery`.

### 6.3. Quy trình crash test

| Bước | Thao tác | Kết quả mong đợi |
| --- | --- | --- |
| 1 | Chạy API Gateway, Worker Engine và ít nhất 2 Plugin Worker. | Hệ thống sẵn sàng xử lý workflow. |
| 2 | Submit workflow `EXP-04 Crash Recovery`. | API trả về `instanceId`. |
| 3 | Đợi worker log `StepStarted` cho node `sleep`. | Pointer `sleep` chuyển sang `Running`. |
| 4 | Chờ thêm khoảng 15-20 giây rồi dừng worker đang xử lý node `sleep`. | Worker chết, heartbeat dừng. |
| 5 | Theo dõi database và log recovery. | Pointer hết lease, được reset về `Pending`. |
| 6 | Worker còn sống xử lý lại node `sleep`. | `leased_by` đổi sang worker mới. |
| 7 | Chờ workflow kết thúc. | Workflow chuyển `Completed`. |

Lý do nên chờ 15-20 giây trước khi dừng worker: khi acquire lease ban đầu, code đặt lease trong 5 phút; heartbeat sau đó renew về khoảng 30 giây. Nếu dừng worker quá sớm, có thể phải chờ tối đa gần 5 phút trước khi recovery thấy lease hết hạn.

### 6.4. Cách dừng worker

Local process: tắt đúng terminal `AWE.Worker` đang log node `sleep`.

Container mode:

```powershell
docker compose ps worker
docker stop <worker-container-name>
```

Không dùng `docker compose down` nếu chỉ muốn dừng một worker, vì `down` dừng toàn bộ project và xóa network mặc định.

### 6.5. Query kiểm chứng database

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
where instance_id = '<instance-id>'
order by created_at;
```

Mapping `ExecutionPointerStatus`:

| Giá trị | Trạng thái |
| ---: | --- |
| 0 | Pending |
| 1 | Running |
| 2 | Completed |
| 3 | Failed |
| 4 | Skipped |
| 5 | Suspended |

### 6.6. Bằng chứng cần chụp lại

| Thời điểm | Bằng chứng |
| --- | --- |
| Trước khi dừng worker | Pointer `sleep` ở `Running`, có `leased_by`, `leased_until`. |
| Sau khi dừng worker | Worker cũ không còn log xử lý node `sleep`. |
| Khi recovery chạy | Log dạng `Found {Count} zombie pointers. Resetting...`. |
| Sau recovery | Pointer quay về `Pending` hoặc được worker khác acquire lại. |
| Khi hoàn tất | Pointer `sleep` và workflow instance ở trạng thái `Completed`. |

Tiêu chí đạt:

- Workflow không bị kẹt vĩnh viễn ở `Running`.
- `RetryCount` tăng sau recovery.
- `LeasedBy` sau recovery khác worker ban đầu.
- Audit log thể hiện node được start lại và workflow hoàn tất.

## 7. Tổng hợp và so sánh kết quả

### 7.1. Bảng kết quả unit test

| Nhóm test | Số test | Passed | Failed | Ghi chú |
| --- | ---: | ---: | ---: | --- |
| ExecutionPointer | 10 | 10 | 0 | Token, leasing, suspended pointer, delay wake-up, skipped pointer. |
| VariableResolver | 3 | 3 | 0 | Resolve biến và strict missing-variable handling. |
| TransitionEvaluator | 8 | 8 | 0 | Branch, trigger routing, condition fail-safe, incoming edges. |
| JoinBarrierService | 6 | 6 | 0 | Join, duplicate dispatch, mixed branch, dead-path. |
| Tổng | 27 | 27 | 0 | Đã chạy bằng `dotnet test`. |

### 7.2. Bảng kết quả load test

| Kịch bản | Worker | VUS | Duration | Submit p95 | Completion p95 | Completed | Failed | Ghi chú |
| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |
| Linear Log | 1 | 20 | 2m |  |  |  |  | Baseline. |
| Linear Log | 4 | 20 | 2m |  |  |  |  | Đánh giá scale-out. |
| Branch Join | 1 | 20 | 2m |  |  |  |  | Có branch và Join. |
| Branch Join | 4 | 20 | 2m |  |  |  |  | Đánh giá contention. |
| Retry | 1 | 10 | 2m |  |  |  |  | Có lỗi tạm thời và retry. |

### 7.3. Bảng kết quả crash test

| Lần chạy | Worker ban đầu | Worker xử lý lại | Thời gian hết lease | Thời gian recovery | Workflow status cuối | Ghi chú |
| --- | --- | --- | ---: | ---: | --- | --- |
| 1 |  |  |  |  |  |  |
| 2 |  |  |  |  |  |  |
| 3 |  |  |  |  |  |  |

### 7.4. Nhận xét dùng trong báo cáo

Khi ghi kết luận chương 6, có thể dựa trên các luận điểm sau:

- **Branch**: engine đánh giá điều kiện bằng `TransitionEvaluator`; nhánh có điều kiện sai không chạy plugin mà được đánh dấu `Skipped`, sau đó dead-path được truyền đến Join.
- **Join**: `JoinBarrierService` chỉ mở barrier khi số pointer đến Join đủ bằng số incoming edges. Nếu Join đã có pointer `Completed`, engine không dispatch trùng.
- **Token/Lease**: `ExecutionPointer` đóng vai trò token thực thi. Pointer đang `Running` có `LeasedBy` và `LeasedUntil`, giúp tránh nhiều worker cùng complete một node.
- **Retry**: khi plugin lỗi tạm thời, pointer được reset về `Pending`, `RetryCount` tăng và engine dispatch lại nếu chưa vượt `MaxRetries`.
- **Recovery**: khi worker chết, lease hết hạn làm pointer trở thành zombie; `RecoveryBackgroundService` reset pointer về `Pending` để worker khác xử lý tiếp.
- **Scale-out**: khi tăng số plugin worker, hệ thống kỳ vọng xử lý được nhiều workflow đồng thời hơn, đặc biệt với workflow có nhiều node độc lập hoặc nhiều instance song song.

## 8. Lưu ý và giới hạn

- K6 chưa được cài sẵn trong PATH trên máy hiện tại. Cần cài K6 hoặc chạy bằng Docker image trước khi đo load test.
- Endpoint API được bảo vệ bởi `[Authorize]`, do đó mọi script gọi API cần Bearer token hợp lệ.
- `docker-compose.yml` hiện đang comment các service app. Muốn scale bằng Docker Compose cần bật lại `api-gateway`, `worker-engine`, `worker`.
- Nếu crash worker ngay sau khi node bắt đầu, initial lease có thể kéo dài 5 phút. Để demo nhanh hơn, nên dừng worker sau khi node đã chạy khoảng 15-20 giây.
- Kết quả load test phụ thuộc mạnh vào cấu hình máy, số worker, prefetch count, database và RabbitMQ. Khi đưa vào báo cáo, cần ghi rõ cấu hình chạy tương ứng với từng bảng kết quả.
