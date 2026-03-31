# Retry / Failure / Idempotency – Tài liệu chi tiết

> Tài liệu này mô tả **chi tiết triển khai hiện tại** của hệ thống AWE (Worker + Engine collaboration), không phải lý tưởng thuần lý thuyết.

---

## 1. Mục tiêu của cơ chế retry

Cơ chế retry được thiết kế để:

1. Chịu lỗi tạm thời (transient fault) khi gọi plugin/external service.
2. Không chạy trùng step khi có duplicate message.
3. Không làm hỏng state khi worker crash giữa chừng.
4. Cho phép Engine quyết định retry cấp workflow bằng `MaxRetries` theo từng step.

---

## 2. Thành phần tham gia

### Worker (`PluginConsumer`)

- Acquire lease cho pointer (`TryAcquireLeaseAsync`) theo cách atomic.
- Thực thi plugin.
- Gia hạn lease bằng heartbeat (`RenewLeaseAsync`).
- Khi lỗi:
  - ghi audit,
  - mark pointer failed,
  - publish `StepFailedEvent`,
  - throw exception taxonomy (`RetryableException` / `NonRetryableException`) để MassTransit xử lý.

### Engine (`WorkflowEventConsumer` + `WorkflowOrchestrator`)

- Nhận `StepFailedEvent`.
- Đọc `MaxRetries` từ step definition.
- Nếu còn retry:
  - reset pointer về `Pending`,
  - tạo lại `ExecutePluginCommand` và publish.
- Nếu hết retry:
  - chuyển workflow sang `Compensating` (logic hiện tại),
  - khởi động compensation plan.

### Persistence (`ExecutionPointerRepository`)

- `TryAcquireLeaseAsync(pointerId, workerId, leaseDuration)`:
  - atomic update với điều kiện:
    - `Status == Pending`
    - hoặc `Status == Running && LeasedUntil < now`
- `RenewLeaseAsync(...)` để heartbeat gia hạn lease.
- `ResetRawPointersAsync(...)` cho recovery zombie.

---

## 3. Exception taxonomy (đang dùng)

```csharp
public class RetryableException : Exception {}
public class NonRetryableException : Exception {}
```

### Mapping hiện tại trong Worker

- Retryable (thường là transient):
  - `TimeoutException`
  - `HttpRequestException`
  - `TaskCanceledException`
- Non-retryable:
  - lỗi cấu hình/metadata plugin,
  - lỗi nghiệp vụ plugin trả về fail không retry,
  - các lỗi không thuộc nhóm transient.

### Retry policy MassTransit

- `Handle<RetryableException>()`
- `Ignore<NonRetryableException>()`
- Backoff: exponential (theo `PluginConsumerDefinition`).

---

## 4. State machine liên quan retry

### Pointer

- `Pending` -> `Running`: acquire lease thành công.
- `Running` -> `Completed`: plugin success.
- `Running` -> `Failed`: worker bắt lỗi.
- `Failed` -> `Pending`: engine cho retry (`ResetToPending`) nếu còn lượt.
- `Running` -> `Pending`: recovery reset khi lease expired (zombie).

### WorkflowInstance

- Bình thường giữ `Running`.
- Nếu lỗi dispatch/resolve ở Engine (không tạo được command), hệ thống hiện tại chuyển `Fail()`.
- Nếu retry exhausted từ `HandleStepFailureAsync`, hiện tại đi nhánh `Compensating`.

---

## 5. Luồng retry chuẩn (thành công ở lần N)

1. Worker nhận `ExecutePluginCommand`.
2. Worker gọi `TryAcquireLeaseAsync`.
3. Plugin chạy và throw transient exception.
4. Worker publish `StepFailedEvent`, throw `RetryableException`.
5. Engine nhận event, vào `HandleStepFailureAsync`.
6. Engine kiểm tra `MaxRetries`:
   - nếu còn lượt: `pointer.ResetToPending()` + publish lại `ExecutePluginCommand`.
7. Worker nhận lại message, acquire lease lại, thực thi lại.
8. Khi thành công: publish `StepCompletedEvent`.

---

## 6. Luồng không retry (non-retryable)

1. Worker bắt lỗi non-retryable.
2. Worker publish `StepFailedEvent` + throw `NonRetryableException`.
3. MassTransit không retry message.
4. Engine xử lý fail theo `HandleStepFailureAsync`:
   - còn retry theo `MaxRetries` thì vẫn có thể retry cấp Engine,
   - hết retry thì vào compensation.

> Lưu ý: quyết định retry ở đây là phối hợp **cả bus-level** (taxonomy) và **engine-level** (`MaxRetries`).

---

## 7. Retry KHÔNG kích hoạt trong trường hợp nào?

### 7.1 Lỗi resolve biến ở `PointerDispatcher`

Nếu `CreateDispatchCommand` fail vì thiếu biến (`VARIABLE_RESOLUTION_FAILED`), command không được tạo/publish.

Ví dụ sai biến:

- Input dùng `{{steps.Step_2_Rabbit.output.msg}}`
- Nhưng `LogPlugin` thực tế trả `LogStatus`

=> Đây là **engine-dispatch failure**, không phải worker execution failure, nên retry worker không chạy.

### 7.2 Lỗi payload quá lớn

`PAYLOAD_TOO_LARGE` được fail ngay ở engine dispatch.

### 7.3 Pointer đã bị lock bởi worker khác

`TryAcquireLeaseAsync` trả false, worker skip/ack.

---

## 8. Idempotency guarantees

1. **Duplicate message**: check bằng status + lease acquire atomic.
2. **Parallel workers**: chỉ 1 worker acquire lease thành công.
3. **Crash khi đang chạy**: lease timeout + recovery reset về `Pending`.
4. **Retry sau khi success**: pointer `Completed` sẽ bị skip.
5. **Resume double click**: guard theo trạng thái pointer `Suspended`.

---

## 9. Các tham số cần quan tâm

- Lease acquire duration: `5 phút` (worker).
- Heartbeat interval: `10 giây`.
- Heartbeat extension: `30 giây`.
- Retry bus-level: trong `PluginConsumerDefinition` (exponential).
- Retry engine-level: `MaxRetries` trên từng step.

---

## 10. Kịch bản test khuyến nghị

### Kịch bản A: Retry thành công

- Step dùng `RetryTest` built-in.
- `FailTimes = 1`, `MaxRetries = 3`.
- Kỳ vọng:
  - attempt 1 fail,
  - attempt 2 success,
  - workflow complete.

### Kịch bản B: Retry exhausted

- `FailTimes = 5`, `MaxRetries = 3`.
- Kỳ vọng:
  - sau lượt retry cho phép, workflow vào nhánh compensation.

### Kịch bản C: Engine dispatch fail

- Cố tình dùng biến không tồn tại trong `Inputs`.
- Kỳ vọng:
  - pointer bị fail bởi engine,
  - workflow không còn `Running` (chuyển `Failed` theo logic hiện tại).

---

## 11. Troubleshooting nhanh

### Triệu chứng: `Lease conflict: Held by , request by Worker-...`

Nguyên nhân: stale tracked entity sau `ExecuteUpdate`.

Khắc phục: không dùng pointer entity đã load trước acquire; reload pointer sau khi `TryAcquireLeaseAsync` thành công.

### Triệu chứng: debugger dừng ở `RetryableException`

Đây thường là expected trong retry test. Continue chạy để message được redeliver.

### Triệu chứng: workflow vẫn `Running` dù step fail ở dispatcher

Kiểm tra nhánh `engine dispatch failed` trong `WorkflowOrchestrator` đã set `instance.Fail()` và save chưa.

---

## 12. Ghi chú thiết kế

Hiện tại hệ thống dùng mô hình phối hợp:

- Worker chịu trách nhiệm execution attempt và phát event lỗi.
- Engine chịu trách nhiệm business retry (`MaxRetries`) và orchestration tiếp theo.

Mô hình này phù hợp khi cần vừa có retry kỹ thuật (bus) vừa có retry nghiệp vụ (workflow).
