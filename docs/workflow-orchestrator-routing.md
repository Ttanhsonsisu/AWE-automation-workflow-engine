# Workflow Orchestrator Routing (Rẽ nhánh, Join, StopAtStep)

Tài liệu này mô tả logic điều hướng trong `WorkflowOrchestrator` tại `src/Core/AWE.WorkflowEngine/Services/WorkflowOrchestrator.cs`.

## 1. Mục tiêu

`WorkflowOrchestrator` chịu trách nhiệm:

- Khởi tạo workflow instance.
- Điều hướng các bước kế tiếp sau khi step hoàn thành.
- Xử lý rẽ nhánh theo điều kiện.
- Đồng bộ tại các node `Join`.
- Hỗ trợ dừng tại step chỉ định (`StopAtStepId`) trong chế độ test.

## 2. Luồng khởi động (`StartWorkflowAsync`)

1. Đọc `WorkflowDefinition` theo `definitionId`.
2. Chuẩn hóa `stopAtStepId`.
3. Validate:
   - Nếu có `stopAtStepId` nhưng `isTest = false` -> từ chối.
   - Nếu `stopAtStepId` không tồn tại trong definition -> từ chối.
4. Khởi tạo context qua `IWorkflowContextManager.InitializeContext(...)` (lưu `Meta.StopAtStepId` nếu có).
5. Tạo `WorkflowInstance` và các start pointers.
6. Tạo dispatch command cho các start pointers.
7. Lưu DB, ghi audit log, publish command cho worker.

## 3. Luồng hoàn thành step (`HandleStepCompletionAsync`)

### 3.1 Guard conditions

- Nếu pointer đã `Routed` -> bỏ qua (idempotency).
- Merge output vào context.
- Nếu instance đang `Suspended` hoặc `Cancelled` -> dừng điều hướng.

### 3.2 Stop tại step chỉ định

Khi `Meta.StopAtStepId` trùng `pointer.StepId`:

- Suspend workflow instance.
- Xóa `Meta.StopAtStepId` để tránh suspend lặp vô hạn khi resume.
- Ghi audit log `WorkflowSuspended`.
- Trả về `Success` (không đi tiếp nhánh sau).

## 4. Logic rẽ nhánh và Join

### 4.1 Tính nhánh kế tiếp

`ITransitionEvaluator.EvaluateTransitions(...)` trả về danh sách:

- `TargetNodeId`
- `IsConditionMet`

### 4.2 Trường hợp condition = true

- Tạo pointer mới cho `TargetNodeId`.
- Nếu target là `Join` -> thêm vào `joinNodesToCheck`.
- Nếu không phải `Join` -> đưa vào `pointersToDispatch`.

### 4.3 Trường hợp condition = false (Dead-path)

Đã được vá bằng `PropagateDeadPathAsync(...)`:

- Tạo pointer `Skipped` cho node target.
- Nếu node target là `Join` -> thêm vào `joinNodesToCheck`.
- Nếu chưa phải `Join`, tiếp tục lan truyền `Skipped` theo các cạnh downstream đến khi:
  - gặp `Join`, hoặc
  - hết cạnh đi tiếp.

Mục tiêu: đảm bảo `Join` nhận đủ tín hiệu đến từ các nhánh bị tắt, tránh kẹt workflow.

### 4.4 Chống lặp vô hạn khi lan truyền dead-path

Sử dụng `visitedDeadPathEdges` với key `"{source}->{target}"` để ngăn recurse vô hạn trên graph có cycle.

## 5. Đồng bộ Join

Với mỗi node trong `joinNodesToCheck`:

1. Tính số cạnh vào bằng `GetIncomingEdgesCount(...)`.
2. Gọi `IJoinBarrierService.EvaluateBarrierAsync(...)`.
3. Nếu barrier đã vỡ và có pointer cần chạy -> tạo command và đưa vào publish queue.

## 6. Publish thực thi

Sau khi đã chuẩn bị xong toàn bộ nhánh:

- Mark pointer hiện tại là `Routed`.
- Kiểm tra lại trạng thái instance trước khi publish.
- Nếu vẫn chạy bình thường -> publish toàn bộ `ExecutePluginCommand`.

## 7. Ghi chú vận hành

- Thiết kế hiện tại theo event-driven: engine publish command/event, worker xử lý và phản hồi lại event completion/failure.
- `StopAtStepId` chỉ dành cho test run để hỗ trợ debug/kiểm thử kịch bản.
- Dead-path propagation là bắt buộc với workflow có nhánh điều kiện trước `Join`.

## 8. Các hàm chính liên quan

- `StartWorkflowAsync(...)`
- `HandleStepCompletionAsync(...)`
- `PropagateDeadPathAsync(...)`
- `GetOutgoingTargetNodeIds(...)`
- `GetStopAtStepId(...)`
- `ClearStopAtStepId(...)`
