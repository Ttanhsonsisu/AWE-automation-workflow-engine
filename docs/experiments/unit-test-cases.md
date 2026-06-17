# Unit Test Cases - Workflow Engine

Tài liệu này mô tả chi tiết bộ unit test của project `AWE.WorkflowEngine.Tests`. Mục tiêu là chứng minh các thuật toán lõi của workflow runtime hoạt động đúng mà không phụ thuộc PostgreSQL, RabbitMQ, Redis, MinIO hay Keycloak.

## 1. Cách chạy

```powershell
dotnet test test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj --logger "console;verbosity=normal"
```

Kết quả chạy gần nhất:

```text
Test Run Successful.
Total tests: 27
Passed: 27
Failed: 0
Total time: 0.8667 Seconds
```

## 2. Phạm vi kiểm thử

| Nhóm | File test | Thành phần được kiểm thử | Số test |
| --- | --- | --- | ---: |
| Token/Lease | `ExecutionPointerTests.cs` | `ExecutionPointer` domain state machine | 10 |
| Variable Resolution | `VariableResolverTests.cs` | `VariableResolver` | 3 |
| Branch/Trigger | `TransitionEvaluatorTests.cs` | `TransitionEvaluator` | 8 |
| Join Barrier | `JoinBarrierServiceTests.cs` | `JoinBarrierService` | 6 |
| Tổng |  |  | 27 |

## 3. Test case chi tiết

### 3.1. ExecutionPointerTests

| Mã test | Tên test trong code | Tiền điều kiện | Hành động | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| UT-EP-01 | `TryAcquireLease_WhenPending_MovesPointerToRunning` | Pointer mới ở trạng thái `Pending`. | Worker A gọi `TryAcquireLease`. | Trả `true`; pointer chuyển `Running`; có `StartTime`, `LeasedBy=worker-a`, `LeasedUntil` trong tương lai. |
| UT-EP-02 | `TryAcquireLease_WhenRunningLeaseExpired_AllowsAnotherWorkerToStealAndCountsRetry` | Worker A đã acquire pointer với lease hết hạn. | Worker B gọi `TryAcquireLease`. | Trả `true`; `LeasedBy` đổi sang worker B; `RetryCount=1`. |
| UT-EP-03 | `TryAcquireLease_WhenRunningLeaseStillValid_RejectsAnotherWorkerAndKeepsCurrentLeaseOwner` | Worker A đang giữ lease còn hạn. | Worker B gọi `TryAcquireLease`. | Trả `false`; `LeasedBy` vẫn là worker A; `RetryCount=0`. |
| UT-EP-04 | `Complete_WhenLeaseOwnerMismatch_ThrowsAndKeepsPointerRunning` | Worker A đang giữ lease. | Worker B gọi `Complete`. | Ném `InvalidOperationException` chứa `Lease conflict`; pointer vẫn `Running`. |
| UT-EP-05 | `Complete_WhenLeaseOwnerMatches_MarksPointerTerminalAndClearsLease` | Worker A đang giữ lease. | Worker A gọi `Complete`. | Pointer `Completed`, `Active=false`, `LeasedBy=null`, `LeasedUntil=null`, có `EndTime`. |
| UT-EP-06 | `ResetToPending_WhenRunning_IncrementsRetryAndClearsLease` | Pointer đang `Running`. | Gọi `ResetToPending`. | Pointer về `Pending`; `RetryCount` tăng; lease bị xóa. |
| UT-EP-07 | `ResetToPending_WhenPointerAlreadyCompleted_ThrowsAndKeepsTerminalState` | Pointer đã `Completed`. | Gọi `ResetToPending`. | Ném lỗi; pointer vẫn `Completed`, `Active=false`. |
| UT-EP-08 | `CompleteFromWait_WhenSuspended_CompletesPointerWithoutLease` | Pointer đang `Suspended` do webhook/approval. | Gọi `CompleteFromWait`. | Pointer `Completed`, `Active=false`, `ResumeAt=null`. |
| UT-EP-09 | `WakeUp_WhenSuspendedByDelay_ReturnsPointerToPendingAndClearsResumeAt` | Pointer đang `Suspended` do delay. | Gọi `WakeUp`. | Pointer về `Pending`, `ResumeAt=null`, `Active=true`. |
| UT-EP-10 | `Skip_WhenPointerIsPending_MarksPointerInactiveAndClearsLease` | Pointer mới ở `Pending`. | Gọi `Skip`. | Pointer `Skipped`, `Active=false`, lease bị xóa, có `EndTime`. |

Ý nghĩa: nhóm test này bảo vệ logic quan trọng nhất của worker concurrency. Nếu state machine pointer sai, nhiều worker có thể ghi kết quả trùng hoặc workflow bị kẹt.

### 3.2. VariableResolverTests

| Mã test | Tên test trong code | Tiền điều kiện | Hành động | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| UT-VR-01 | `Resolve_WhenPayloadUsesWorkflowInputStepOutputAndSystemVariables_ReplacesAllVariables` | Context có `Inputs`, `Steps.<id>.Output`, `Meta`. | Resolve payload chứa `{{workflow.input.*}}`, `{{steps.retry.output.*}}`, `{{workflow.system.*}}`. | Resolve thành công; JSON output parse được; string, number, bool và object giữ đúng kiểu. |
| UT-VR-02 | `Resolve_WhenVariableIsMissing_ReturnsFailureAndKeepsOriginalPayload` | Context thiếu `workflow.input.score`. | Resolve payload có biến thiếu. | `IsSuccess=false`; `ResolvedPayload` giữ payload gốc; `MissingVariables` chứa biến thiếu. |
| UT-VR-03 | `Resolve_WhenRawPayloadIsEmpty_ReturnsEmptyJsonObject` | Payload rỗng. | Gọi `Resolve`. | Trả `{}` và không có missing variable. |

Ý nghĩa: nhóm test này kiểm tra strict variable resolution. Nếu biến thiếu mà engine vẫn dispatch payload lỗi, plugin có thể chạy với dữ liệu sai và làm hỏng kết quả workflow.

### 3.3. TransitionEvaluatorTests

| Mã test | Tên test trong code | Tiền điều kiện | Hành động | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| UT-TE-01 | `FindStartNodeIdsByTrigger_WhenManualTriggerExists_ReturnsOnlyManualTriggerNodes` | Definition có ManualTrigger, WebhookTrigger và Log. | Tìm start node theo trigger Manual. | Chỉ trả node `manual`. |
| UT-TE-02 | `FindStartNodeIdsByTrigger_WhenWebhookRouteIsProvided_ReturnsOnlyMatchingWebhookTrigger` | Definition có nhiều WebhookTrigger. | Tìm start node theo route `/github`. | Chỉ trả node webhook có `RoutePath=/github`. |
| UT-TE-03 | `FindStartNodeIdsByTrigger_WhenCronStepIdIsProvided_ReturnsOnlyMatchingCronTrigger` | Definition có nhiều CronTrigger. | Tìm start node theo id `hourly`. | Chỉ trả node `hourly`. |
| UT-TE-04 | `EvaluateTransitions_EvaluatesTrueFalseAndMissingVariableAsFalse` | Context có `score=90`, thiếu `unknown`. | Evaluate transition điều kiện `>=80`, `<80`, và biến thiếu. | Nhánh high true; nhánh low false; nhánh missing false. |
| UT-TE-05 | `EvaluateTransitions_WhenTransitionHasNoCondition_DefaultsToTrue` | Transition không có `Condition`. | Evaluate transition. | Transition được xem là hợp lệ, trả true. |
| UT-TE-06 | `EvaluateTransitions_WhenConditionExpressionIsInvalid_ReturnsFalseFailSafe` | Condition expression sai cú pháp. | Evaluate transition. | Không throw; trả false để fail-safe. |
| UT-TE-07 | `GetIncomingEdgesCount_CountsAllTransitionsTargetingJoinNode` | Definition có 2 cạnh vào Join. | Đếm incoming edge và kiểm tra `IsJoinNode`. | Count bằng 2; node `join` là Join; node khác không phải Join. |
| UT-TE-08 | `FindStartNodeIds_WhenDefinitionHasMultipleIndependentStartNodes_ReturnsAllStartNodes` | Definition có ManualTrigger và WebhookTrigger cùng đi vào Log. | Tìm tất cả node không có incoming edge. | Trả cả `manual` và `webhook`. |

Ý nghĩa: nhóm test này chứng minh engine chọn đúng điểm bắt đầu theo loại trigger và đánh giá branch condition an toàn. Khi condition lỗi hoặc thiếu biến, engine không crash mà coi nhánh đó là false.

### 3.4. JoinBarrierServiceTests

| Mã test | Tên test trong code | Tiền điều kiện | Hành động | Kết quả mong đợi |
| --- | --- | --- | --- | --- |
| UT-JB-01 | `EvaluateBarrierAsync_WhenNotAllIncomingBranchesArrived_KeepsBarrierClosed` | Join có 2 incoming edge nhưng mới 1 pointer tới. | Evaluate barrier. | `IsBarrierBroken=false`, không có pointer dispatch. |
| UT-JB-02 | `EvaluateBarrierAsync_WhenAllBranchesArrived_SelectsOnePendingPointerAndCompletesRedundantPointers` | 2 pointer `Pending` tới Join. | Evaluate barrier. | Barrier mở; chọn một pointer đại diện; pointer còn lại chuyển `Completed`. |
| UT-JB-03 | `EvaluateBarrierAsync_WhenOneBranchAlreadyCompleted_DoesNotDispatchAgain` | Một pointer Join đã `Completed`, một pointer còn `Pending`. | Evaluate barrier. | Barrier mở nhưng không dispatch pointer mới, tránh duplicate dispatch. |
| UT-JB-04 | `EvaluateBarrierAsync_WhenAllIncomingBranchesAreSkipped_PropagatesDeadPath` | Tất cả pointer vào Join đều `Skipped`. | Evaluate barrier. | `IsDeadPath=true`, không dispatch plugin Join. |
| UT-JB-05 | `EvaluateBarrierAsync_WhenSkippedAndPendingBranchesArrive_DispatchesPendingBranch` | Một pointer `Skipped`, một pointer `Pending`. | Evaluate barrier. | Dispatch pointer `Pending`, không coi là dead-path toàn phần. |
| UT-JB-06 | `EvaluateBarrierAsync_WhenMultiplePendingBranchesArrive_CompletesOnlyRedundantPendingPointers` | 3 pointer `Pending` cùng tới Join. | Evaluate barrier. | Chọn pointer đầu làm đại diện; 2 pointer dư chuyển `Completed`. |

Ý nghĩa: nhóm test này bảo vệ Join barrier, nơi dễ phát sinh race condition khi nhiều nhánh song song về cùng một node. Test dùng fake repository và fake distributed lock để giữ đúng tính chất unit test.

## 4. Tiêu chí pass/fail

| Tiêu chí | Kỳ vọng |
| --- | --- |
| Tính độc lập | Test không cần Docker, PostgreSQL, RabbitMQ, Redis, MinIO hoặc Keycloak. |
| Tính lặp lại | Chạy nhiều lần cho cùng kết quả. |
| Tốc độ | Tổng thời gian test chỉ vài giây. |
| Behavior | Test tập trung vào trạng thái domain và output public của service, không phụ thuộc chi tiết triển khai nội bộ. |
| Regression | Khi sửa workflow runtime, các test này phải phát hiện thay đổi làm sai branch, join, token hoặc variable resolution. |

## 5. Lệnh dùng trong báo cáo

```powershell
dotnet test test\AWE.WorkflowEngine.Tests\AWE.WorkflowEngine.Tests.csproj --logger "console;verbosity=normal"
```

Kết quả có thể trích vào báo cáo:

```text
Test Run Successful.
Total tests: 27
Passed: 27
Failed: 0
Total time: 0.8667 Seconds
```
