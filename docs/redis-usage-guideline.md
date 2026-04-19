# Redis Usage Guideline

## 1) Mục tiêu sử dụng Redis trong hệ thống

Trong codebase hiện tại, Redis được dùng **chủ yếu cho distributed locking** để đảm bảo an toàn khi chạy nhiều instance (scale-out), tránh race condition và xử lý trùng.

Không dùng Redis như cache chính ở thời điểm hiện tại.

## 2) Redis đang được dùng ở đâu

### 2.1 Đăng ký hạ tầng Redis
- `src/Core/AWE.Infrastructure/DependencyInjection.cs`
  - Đăng ký `IConnectionMultiplexer` (StackExchange.Redis)
  - Đăng ký `IDistributedLockProvider` (Medallion + Redis provider)

### 2.2 Các luồng đang dùng distributed lock
- `src/Core/AWE.WorkflowEngine/BackgroundServices/QuartzScheduleBootstrapService.cs`
  - Lock name: `workflow-scheduler-bootstrap`
  - Mục tiêu: chỉ một node bootstrap Quartz schedule tại một thời điểm.

- `src/Core/AWE.WorkflowEngine/BackgroundServices/WorkflowSchedulerSyncReconcilerService.cs`
  - Lock name: `workflow-scheduler-sync-reconciler`
  - Mục tiêu: chỉ một node chạy job reconcile theo tick.

- `src/Core/AWE.WorkflowEngine/Services/JoinBarrierService.cs`
  - Lock key: `workflow:{instanceId}:join:{joinNodeId}`
  - Mục tiêu: tránh race condition tại điểm Join của workflow.

## 3) Quy ước key cho lock

Đề xuất chuẩn hóa key lock theo format:

`awe:lock:{bounded-context}:{resource}:{id?}`

Ví dụ:
- `awe:lock:scheduler:bootstrap`
- `awe:lock:scheduler:reconciler`
- `awe:lock:workflow:join:{instanceId}:{joinNodeId}`

Lợi ích:
- Dễ truy vết trên Redis monitor
- Tránh đụng key giữa module
- Thuận tiện quan sát và alert

## 4) TTL / lease time cho lock

Nguyên tắc:
- Lock timeout phải **lớn hơn** thời gian xử lý trung bình một tác vụ
- Không đặt quá lớn để tránh lock treo lâu khi node lỗi
- Có log khi không acquire được lock

Khuyến nghị:
- Job tick ngắn: `5-15s`
- Bootstrap / sync đầu hệ thống: `30-60s`

## 5) Rule khi dùng Redis lock

1. **Always release lock bằng scope**
   - Dùng `await using var lockHandle = ...` như code hiện tại.

2. **Không throw chỉ vì không acquire được lock**
   - Trường hợp chạy đa node, fail acquire là hành vi bình thường.

3. **Log rõ lock name + reason**
   - Phục vụ điều tra khi thấy task bị skip.

4. **Không làm việc nặng trong lock nếu không cần**
   - Chỉ bao phần critical section.

5. **Idempotent-first**
   - Lock giảm race condition, nhưng logic nghiệp vụ vẫn cần idempotent.

## 6) Cấu hình môi trường

Redis connection string dùng key:
- `ConnectionStrings:Redis`

Hiện đã có ở:
- `src/Presentation/AWE.ApiGateway/appsettings.Development.json`

Khuyến nghị bổ sung đồng bộ cho:
- `src/Workers/AWE.Worker/appsettings.Development.json`
- `src/Workers/AWE.Wokrer.Engine/appsettings.Development.json`

Để tránh fallback không mong muốn về `localhost:6379` khi chạy môi trường khác.

## 7) Những gì chưa dùng Redis (hiện tại)

- Chưa dùng `IDistributedCache`
- Chưa dùng Redis session store
- Chưa dùng Redis SignalR backplane
- Chưa dùng Redis pub/sub cho domain events

## 8) Roadmap ứng dụng Redis tiếp theo (ưu tiên)

1. **Webhook idempotency store (ưu tiên cao)**
   - Lưu event-id/signature với TTL ngắn để chống xử lý trùng.

2. **Distributed rate limit cho API Gateway**
   - Đồng bộ quota khi scale nhiều instance.

3. **Read-through cache cho Dashboard/Dropdown**
   - Giảm tải DB cho dữ liệu đọc nhiều.

4. **SignalR backplane**
   - Cần khi scale-out realtime thông báo UI.

## 9) Checklist cho pull request có dùng Redis

- [ ] Có prefix key rõ ràng theo quy ước
- [ ] Có TTL/lease hợp lý
- [ ] Có log lock acquire fail
- [ ] Đảm bảo idempotent ở nghiệp vụ chính
- [ ] Có fallback behavior khi Redis unavailable (nếu cần)
