# Tài liệu tích hợp Realtime (SignalR) cho Frontend

## 1. Mục tiêu
Tài liệu này mô tả phần realtime hiện tại đã tích hợp trong hệ thống để Frontend (FE) có thể:
- nhận cập nhật trạng thái từng node/step theo thời gian thực,
- nhận trạng thái tổng thể workflow,
- nhận log runtime dạng stream (terminal-like).

Realtime được thiết kế theo hướng event-driven:
`Worker/Engine -> Message Broker (MassTransit) -> API Gateway Consumer -> SignalR Hub -> FE`.

---

## 2. Endpoint SignalR
API Gateway expose Hub tại:
- `"/hubs/workflow"`

FE cần kết nối vào endpoint này, sau đó join đúng workflow instance để chỉ nhận dữ liệu liên quan.

---

## 3. Cơ chế group theo workflow instance
Hub hỗ trợ 2 method client gọi lên server:

### `JoinWorkflowGroup(instanceId: string)`
- Thêm connection hiện tại vào group có tên chính là `instanceId`.
- Sau khi join, FE sẽ nhận các event realtime của instance đó.

### `LeaveWorkflowGroup(instanceId: string)`
- Rời group khi không còn theo dõi instance.

> Lưu ý: `instanceId` truyền dạng string (GUID string), và backend cũng broadcast theo key này.

---

## 4. Các event FE sẽ nhận (server -> client)
Tên event đúng theo method trong hub client contract:

## 4.1 `NodeStatusChanged`
Dùng để cập nhật trạng thái step/node.

Payload:
```json
{
  "stepId": "Step_1_Start",
  "status": "Running",
  "timestamp": "2026-04-01T11:30:00Z"
}
```

Model:
- `stepId: string`
- `status: string`
- `timestamp: DateTime`

Nguồn phát hiện tại:
- `UiNodeStatusChangedEvent` (engine/worker publish)
- `StepStartedEvent` (được map cứng sang trạng thái `Running`)

## 4.2 `WorkflowStatusChanged`
Dùng để cập nhật trạng thái tổng thể workflow instance.

Payload logic:
- arg1: `status: string`
- arg2: `timestamp: DateTime`

Ví dụ:
```ts
(status, timestamp) => {
  // status: "Running" | "Completed" | "Failed" | ...
}
```

Nguồn phát hiện tại:
- `UiWorkflowStatusChangedEvent`

## 4.3 `WorkflowLogReceived`
Dùng để stream log runtime lên UI.

Payload:
```json
{
  "stepId": "Activity_Validate",
  "level": "Information",
  "message": "Validate input success",
  "timestamp": "2026-04-01T11:31:10Z"
}
```

Model:
- `stepId: string`
- `level: string` (`Information` | `Warning` | `Error`)
- `message: string`
- `timestamp: DateTime`

Nguồn phát hiện tại:
- `WriteAuditLogCommand`

Filter hiện tại ở gateway consumer:
- nếu `NodeId` rỗng hoặc `"System"` thì **bỏ qua**, không push về FE.

---

## 5. Mapping event bus -> event SignalR (hợp đồng realtime hiện tại)
- `UiNodeStatusChangedEvent` -> `NodeStatusChanged(NodeStatusUpdateMessage)`
- `StepStartedEvent` -> `NodeStatusChanged({ status: "Running" })`
- `UiWorkflowStatusChangedEvent` -> `WorkflowStatusChanged(status, timestamp)`
- `WriteAuditLogCommand` (có `NodeId`) -> `WorkflowLogReceived(LogUpdateMessage)`

Queue consumer tại API Gateway đang dùng:
- `q.workflow.gateway.ui`

---

## 6. FE integration quick-start (TypeScript)
```ts
import * as signalR from "@microsoft/signalr";

const instanceId = "<workflow-instance-guid>";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://<api-gateway-host>/hubs/workflow")
  .withAutomaticReconnect()
  .build();

connection.on("NodeStatusChanged", (payload) => {
  // payload: { stepId, status, timestamp }
  console.log("NodeStatusChanged", payload);
});

connection.on("WorkflowStatusChanged", (status, timestamp) => {
  console.log("WorkflowStatusChanged", { status, timestamp });
});

connection.on("WorkflowLogReceived", (log) => {
  // log: { stepId, level, message, timestamp }
  console.log("WorkflowLogReceived", log);
});

await connection.start();
await connection.invoke("JoinWorkflowGroup", instanceId);

// Khi rời màn hình
// await connection.invoke("LeaveWorkflowGroup", instanceId);
// await connection.stop();
```

---

## 7. Gợi ý tích hợp UI
- Khi mở trang execution detail:
  1) gọi API lấy snapshot ban đầu (status hiện tại, danh sách step, logs đã có),
  2) sau đó mới mở SignalR và join group để nhận delta realtime.
- Luôn bật `withAutomaticReconnect()` để giảm ảnh hưởng khi mạng chập chờn.
- Nên de-duplicate theo `(stepId, status, timestamp)` hoặc bằng cơ chế id riêng ở FE nếu cần.
- Realtime là eventual consistency; thứ tự event có thể lệch nhẹ trong môi trường phân tán.

---

## 8. Checklist FE trước khi go-live
- Kết nối thành công đến `/hubs/workflow`.
- Join đúng `instanceId` ngay sau `start`.
- Bind đủ 3 event: `NodeStatusChanged`, `WorkflowStatusChanged`, `WorkflowLogReceived`.
- Có xử lý reconnect + re-join group sau reconnect.
- Có fallback gọi API để đồng bộ lại nếu bỏ lỡ event realtime.
