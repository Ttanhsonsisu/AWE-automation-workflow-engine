## 1) Cập nhật mới: Workflow status realtime ở trạng thái terminal + stop test

Backend đã bổ sung bắn `UiWorkflowStatusChangedEvent` cho các trạng thái kết thúc luồng:

- `Completed`
- `Failed`

Và với chế độ test có `stopAtStepId`, backend sẽ bắn thêm:

- `Suspended` (khi workflow dừng trước node được cấu hình trong `stopAtStepId`)

### FE cần xử lý
Lắng nghe event SignalR:
- `WorkflowStatusChanged(status, timestamp)`

Trong đó `status` có thể nhận thêm:
- `Completed`
- `Failed`
- `Suspended` (khi stop theo `stopAtStepId` hoặc suspend runtime)

> Bổ sung mới: trường hợp `stopAtStepId` giờ đã có tín hiệu `WorkflowStatusChanged("Suspended")` rõ ràng qua SignalR.

---

## 2) Cập nhật mới: Realtime retry cho node

Backend đã bổ sung bắn event retry khi engine quyết định retry step.

### Event được bắn
- `UiNodeStatusChangedEvent` với `Status = "Retrying"`
- `WriteAuditLogCommand` với `Event = "StepRetrying"`

### FE cần xử lý
1. Trong handler `NodeStatusChanged`, thêm mapping trạng thái:
   - `Retrying` (ví dụ màu cam/vàng + icon loading)
2. Trong luồng log realtime (`WorkflowLogReceived`), hiển thị log `StepRetrying`.

---

## 3) Cập nhật sequence cho case `stopAtStepId`

Khi chạy test với `stopAtStepId`, FE sẽ nhận lần lượt:

1. `NodeStatusChanged` -> `Running` (step hiện tại bắt đầu chạy)
2. `NodeStatusChanged` -> `Completed` (step hiện tại chạy xong)
3. `WorkflowStatusChanged` -> `Suspended` (workflow dừng trước step target)

FE có thể dùng step (2) để tô xanh node vừa chạy xong, và step (3) để khóa nút `Run`/hiện badge `Stopped for test`.

---

## 4) Gợi ý mapping UI nhanh

### Workflow badge
- `Running` -> đang chạy
- `Suspended` -> tạm dừng
- `Completed` -> hoàn thành
- `Failed` -> thất bại

### Node badge
- `Running`
- `Completed`
- `Failed`
- `Retrying` (mới)

---

## 6) Backward compatibility

Các event cũ không bị thay đổi payload. FE chỉ cần bổ sung xử lý thêm:
- workflow terminal status (`Completed`, `Failed`),
- workflow stop theo test (`Suspended` do `stopAtStepId`),
- node status mới `Retrying`.
