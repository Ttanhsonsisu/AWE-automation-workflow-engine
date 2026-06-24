# Giải thích các thuật toán quan trọng trong AWE.WorkflowEngine

## 1. Mục đích của Workflow Engine

`AWE.WorkflowEngine` chịu trách nhiệm biến một workflow definition dạng JSON thành một quá trình thực thi có trạng thái.

Một workflow definition có thể xem như đồ thị có hướng:

- Mỗi `Step` là một đỉnh.
- Mỗi `Transition` là một cạnh có hướng.
- `WorkflowInstance` là một lần chạy cụ thể của đồ thị.
- `ExecutionPointer` là token biểu diễn một nhánh thực thi đang nằm tại một step.

Ví dụ:

```text
ManualTrigger -> GetOrder -> IfValid
                              | true  -> ChargeCard -> SendEmail
                              | false -> RejectOrder
```

Engine không trực tiếp chạy phần lớn plugin. Engine tạo `ExecutePluginCommand`, lưu trạng thái vào database, rồi gửi command qua MassTransit để worker thực thi.

Các file trung tâm:

- `Services/WorkflowOrchestrator.cs`: điều phối toàn bộ state machine.
- `Services/TransitionEvaluator.cs`: tìm node bắt đầu và đánh giá cạnh.
- `Services/PointerDispatcher.cs`: chuẩn bị command chạy plugin.
- `Services/JoinBarrierService.cs`: hợp nhất các nhánh song song.
- `Services/WorkflowContextManager.cs`: quản lý dữ liệu toàn workflow.
- `Services/VariableResolver.cs`: nội suy biến.
- `Services/WorkflowCompensationService.cs`: tạo lệnh rollback.

---

## 2. Mô hình token và ExecutionPointer

### 2.1 Ý tưởng

Engine sử dụng mô hình token-based execution, gần với cách Petri net hoặc BPMN token vận hành.

Thay vì chỉ lưu `CurrentStepId` trong workflow, engine tạo một `ExecutionPointer` cho mỗi nhánh. Nhờ vậy một workflow có thể có nhiều step đang chạy đồng thời.

Một pointer quan trọng ở các thuộc tính:

```text
InstanceId      Workflow mà pointer thuộc về
StepId          Node hiện tại
ParentTokenId   Pointer đã sinh ra pointer này
BranchId        Nhánh logic hiện tại
Status          Pending, Running, Completed, Failed, Suspended, Skipped
Active          Pointer còn ảnh hưởng đến việc hoàn thành workflow hay không
Routed          Engine đã tạo các pointer kế tiếp hay chưa
RetryCount      Số lần retry
LeasedUntil     Thời hạn worker được quyền xử lý pointer
```

### 2.2 Vòng đời cơ bản

```text
Pending -> Running -> Completed
                   -> Failed
                   -> Suspended -> Completed

Failed/Running -> Pending       khi retry hoặc recovery
Pending        -> Skipped       khi cạnh điều kiện không được chọn
```

`Status` mô tả trạng thái chạy plugin. `Routed` mô tả trạng thái điều hướng graph. Hai khái niệm khác nhau:

- Plugin có thể đã `Completed` nhưng engine chưa tạo node kế tiếp: `Routed = false`.
- Sau khi tạo node kế tiếp thành công: `Routed = true`.

Cờ `Routed` là idempotency guard chống một completion event bị xử lý hai lần.

---

## 3. Thuật toán khởi tạo workflow

Được thực hiện trong `WorkflowOrchestrator.StartWorkflowAsync`.

### 3.1 Các bước

1. Đọc workflow definition.
2. Kiểm tra chế độ test và `StopAtStepId`.
3. Khởi tạo context.
4. Tạo `WorkflowInstance` ở trạng thái `Running`.
5. Tìm các trigger phù hợp với nguồn kích hoạt.
6. Tạo một pointer cho mỗi trigger.
7. Resolve input và tạo command.
8. Lưu instance và pointer vào database.
9. Publish command để worker chạy.

Pseudo-code:

```text
definition = load(definitionId)
context = initialize(input, defaults, metadata)
instance = new WorkflowInstance(definition, context)

startNodes = findTriggers(definition, triggerSource, route)

for each startNode in startNodes:
    pointer = new Pointer(instance, startNode, new BranchId)
    save(pointer)
    command = createDispatchCommand(instance, pointer)
    if command exists:
        pendingCommands.add(command)

commit database

for each command in pendingCommands:
    publish(command)
```

### 3.2 Tìm start node theo trigger

`TransitionEvaluator.FindStartNodeIdsByTrigger` ánh xạ nguồn kích hoạt sang plugin type:

```text
Manual  -> ManualTrigger, ManualTriggerPlugin
Webhook -> WebhookTrigger, WebhookTriggerPlugin
Cron    -> CronTrigger, CronTriggerPlugin
Chat    -> ChatTrigger, ChatTriggerPlugin
```

Với webhook, `RoutePath` được dùng để chọn đúng trigger. Với cron, `StepId` của cron trigger được dùng làm route key.

Độ phức tạp: `O(V)`, với `V` là số step.

### 3.3 Idempotency khi start

Nếu request có `idempotencyKey`, database có unique index trên:

```text
(DefinitionId, IdempotencyKey)
```

Hai request đồng thời có thể cùng vượt qua kiểm tra ở application layer, nhưng chỉ một request insert thành công. Request còn lại nhận PostgreSQL unique violation, sau đó engine truy vấn và trả về instance đã tồn tại.

Đây là database-enforced idempotency, đáng tin cậy hơn mô hình “query trước rồi insert”.

---

## 4. Thuật toán quản lý context

Context có cấu trúc:

```json
{
  "Inputs": {},
  "Steps": {
    "StepA": {
      "Output": {}
    }
  },
  "Meta": {
    "CorrelationId": "...",
    "JobName": "...",
    "StartedAt": "..."
  }
}
```

### 4.1 Merge default input và runtime input

`WorkflowContextManager.InitializeContext` sử dụng recursive object merge:

```text
merge(default, runtime):
    nếu cả hai không phải object:
        lấy runtime nếu có, ngược lại lấy default

    result = clone(default)
    for each property in runtime:
        nếu default[property] và runtime[property] đều là object:
            result[property] = merge(default[property], runtime[property])
        ngược lại:
            result[property] = clone(runtime[property])
```

Ví dụ:

```json
Default: { "email": { "host": "smtp", "port": 25 }, "retry": 3 }
Runtime: { "email": { "port": 587 } }
Result:  { "email": { "host": "smtp", "port": 587 }, "retry": 3 }
```

Runtime input có độ ưu tiên cao hơn default input.

### 4.2 Merge output của step

Khi một step hoàn thành:

```text
Context.Steps[StepId].Output = eventOutput
```

Việc ghi đè cho phép cùng một step chạy lại trong vòng lặp. Tuy nhiên, vì toàn bộ `ContextData` được đọc và ghi như một JSON document, hai nhánh hoàn thành đồng thời có thể gây lost update. Một nhánh có thể ghi đè context vừa được nhánh kia cập nhật.

---

## 5. Thuật toán nội suy biến

Được thực hiện bởi `VariableResolver`.

### 5.1 Cú pháp

```text
{{workflow.input.customerId}}
{{steps.GetOrder.output.total}}
{{workflow.system.CorrelationId}}
```

Đường dẫn được ánh xạ sang context:

```text
workflow.input.x       -> Inputs.x
steps.A.output.x       -> Steps.A.Output.x
workflow.system.x      -> Meta.x
```

### 5.2 Quy trình resolve

```text
matches = regex tìm tất cả {{...}}

for each match:
    path = normalize(match.path)
    value = context.get(path)

    nếu value không tồn tại:
        missingVariables.add(path)
    ngược lại:
        thay placeholder bằng JSON representation của value

nếu missingVariables không rỗng:
    trả Failure và giữ nguyên payload ban đầu
ngược lại:
    trả payload đã resolve
```

Resolver chạy ở strict mode: chỉ cần thiếu một biến thì toàn bộ resolve thất bại. Điều này tránh gửi một payload nửa đúng nửa sai xuống plugin.

### 5.3 Smart unquote

Resolver phân biệt placeholder có nằm trong dấu nháy hay không:

```json
{ "count": {{workflow.input.count}} }
```

Nếu `count = 10`, kết quả phải là số:

```json
{ "count": 10 }
```

Với string:

```json
{ "name": "{{workflow.input.name}}" }
```

Giá trị được JSON-escape để tránh làm hỏng payload khi chứa dấu nháy, xuống dòng hoặc ký tự đặc biệt.

Độ phức tạp gần `O(P + K × D)`:

- `P`: độ dài payload.
- `K`: số placeholder.
- `D`: độ sâu trung bình của JSON path.

---

## 6. Thuật toán đánh giá transition

Được thực hiện trong `TransitionEvaluator.EvaluateTransitions`.

### 6.1 Quy trình

Engine duyệt các transition có `Source == currentStepId`:

```text
for each transition from current step:
    conditionMet = true

    nếu transition có Condition:
        conditionMet = evaluateCondition(condition, context)

    nếu transition có BranchType:
        actual = Context.Steps[current].Output.IsMatch
        expected = parseBoolean(BranchType)
        conditionMet = conditionMet AND actual == expected

    result.add(Target, conditionMet)
```

Điểm quan trọng: hàm trả về cả cạnh đúng và cạnh sai. Cạnh sai không bị loại khỏi danh sách vì orchestrator cần nó để tạo dead-path token.

### 6.2 Đánh giá biểu thức bằng Jint

Ví dụ condition:

```javascript
{{workflow.input.score}} >= 80
```

Nếu `score = 90`, resolver tạo:

```javascript
90 >= 80
```

Jint thực thi biểu thức và chuyển kết quả thành boolean.

Sandbox được giới hạn:

- Timeout: 2 giây.
- Memory: 4 MB.
- Thiếu biến hoặc lỗi JavaScript: trả `false`.

Đây là fail-safe evaluation: engine không vô tình chạy một nhánh khi condition không thể xác định chắc chắn.

---

## 7. Thuật toán routing và fork

Sau khi step hoàn thành, `HandleStepCompletionAsync` thực hiện graph traversal một bước.

### 7.1 Idempotency

```text
if pointer.Routed:
    ignore duplicate completion event
```

Điều này cần thiết vì message broker có semantics at-least-once: cùng một event có thể được giao nhiều lần.

### 7.2 Tạo nhánh

```text
transitions = evaluateTransitions(currentStep)

for each transition:
    branchId = transitions.count > 1 ? new Guid : parent.BranchId

    if transition.conditionMet:
        create Pending pointer at transition.Target
    else:
        propagate dead path from transition.Target
```

Nếu node chỉ có một cạnh, pointer con kế thừa `BranchId`. Nếu có nhiều cạnh, mỗi cạnh nhận branch ID riêng.

Lưu ý: code hiện tại xét `transitions.Count > 1`, không phải số cạnh có điều kiện đúng. Vì vậy cả nhánh chạy và nhánh skipped đều có identity riêng, phù hợp cho việc theo dõi fork/join.

### 7.3 Khi nào workflow hoàn thành?

Một đường đi đạt node cuối chưa có nghĩa toàn workflow hoàn thành. Engine truy vấn tất cả pointer còn `Active`:

```text
if current node has no outgoing transitions:
    mark current pointer as Routed

    if no other active pointer:
        instance.Complete()
    else:
        chờ các nhánh khác
```

Quy tắc này ngăn nhánh nhanh kết thúc workflow trong khi nhánh chậm vẫn chạy.

---

## 8. Thuật toán Dead-Path Elimination

Dead-path elimination dùng để báo cho Join biết rằng một nhánh sẽ không bao giờ tới bằng đường chạy bình thường.

Ví dụ:

```text
             -> A --\
If(condition)         Join -> D
             -> B --/
```

Nếu chỉ chạy A mà không tạo dấu vết cho B, Join sẽ chờ B vô hạn. Vì vậy engine tạo pointer `Skipped` cho B.

### 8.1 Duyệt đệ quy

`PropagateDeadPathAsync` hoạt động như DFS:

```text
propagate(source, target):
    edgeKey = source + "->" + target

    if edgeKey đã visited:
        return

    create Skipped pointer tại target

    if target là Join:
        đưa Join vào danh sách cần kiểm tra
        return

    for each outgoing edge target -> next:
        propagate(target, next)
```

`visitedDeadPathEdges` ngăn recursion vô hạn nếu workflow chứa cycle.

### 8.2 Độ phức tạp

Với một lần propagation, mỗi cạnh chỉ được thăm tối đa một lần:

```text
Time:  O(V + E)
Space: O(V + E)
```

### 8.3 Hạn chế hiện tại

Khi tất cả đường vào một Join đều skipped, `JoinBarrierService` trả `IsDeadPath = true`, nhưng orchestrator chưa tiếp tục gọi dead-path propagation cho các node phía sau Join. Nested join vì vậy có thể không nhận đủ skipped token.

---

## 9. Thuật toán Join Barrier

Join là điểm đồng bộ nhiều nhánh.

```text
A ----\
       Join -> C
B ----/
```

### 9.1 Điều kiện mở barrier

Engine tính số cạnh đi vào Join từ workflow definition:

```text
required = count(transition where transition.Target == joinId)
arrived  = count(pointer where InstanceId == instanceId and StepId == joinId)
```

Nếu:

```text
arrived < required
```

barrier vẫn đóng.

### 9.2 Chọn pointer đại diện

Khi đủ pointer:

```text
if any pointer is Completed:
    Join đã từng được dispatch, không dispatch lại

if all pointers are Skipped:
    trả về dead path

representative = first Pending pointer

for each other Pending pointer:
    mark Completed as duplicate

dispatch representative
```

Chỉ một pointer được phép chạy `JoinPlugin`; các pointer còn lại được đóng để không giữ workflow ở trạng thái active.

### 9.3 Distributed lock

Lock key:

```text
workflow:{InstanceId}:join:{JoinNodeId}
```

Mục tiêu là chỉ một host được đánh giá và chọn pointer đại diện tại một thời điểm.

Tuy nhiên, implementation hiện tại vẫn chạy `EvaluateCoreAsync` khi không lấy được lock. Fallback này làm mất tác dụng mutual exclusion và có thể tạo duplicate dispatch. Cách an toàn là:

```text
if cannot acquire lock:
    return BarrierNotEvaluated
```

Sau đó message được retry, hoặc dùng atomic claim tại database.

### 9.4 Hạn chế với loop

Barrier hiện nhóm pointer bằng `(InstanceId, JoinNodeId)`. Nếu workflow quay lại cùng Join, pointer của vòng trước vẫn được đếm. Khi thấy pointer `Completed`, engine kết luận Join đã chạy và chặn vòng mới.

Thiết kế đầy đủ cần barrier generation:

```text
(InstanceId, JoinNodeId, ForkTokenId hoặc GenerationId)
```

Khi đó mỗi lần fork tạo một generation riêng, và Join chỉ hợp nhất token cùng generation.

---

## 10. Thuật toán dispatch plugin

`PointerDispatcher.CreateDispatchCommand` biến pointer thành command có thể gửi xuống worker.

### 10.1 Pipeline

```text
load step definition
    -> validate IsConfigured
    -> resolve variables trong Inputs
    -> parse và lưu InputData vào pointer
    -> kiểm tra payload <= giới hạn
    -> xử lý đặc biệt Wait/Delay
    -> tạo ExecutePluginCommand
```

### 10.2 Payload size guard

Payload được đo theo số byte UTF-8, không phải số ký tự:

```text
payloadSize = UTF8.GetByteCount(payload)
```

Nếu vượt `MAX_PAYLOAD_SIZE_BYTES`, pointer bị fail ở engine trước khi message được gửi. Điều này bảo vệ RabbitMQ khỏi message quá lớn.

### 10.3 Wait và Delay

Hai plugin này không gửi xuống worker:

- `Wait`: pointer chuyển sang `Suspended`, chờ webhook hoặc thao tác bên ngoài.
- `Delay`: schedule một `ResumeStepCommand`, lưu `ResumeAt`, rồi chuyển sang `Suspended`.

```text
Delay(seconds):
    wakeupTime = UtcNow + seconds
    schedule ResumeStepCommand at wakeupTime
    pointer.HibernateUntil(wakeupTime)
```

---

## 11. Thuật toán suspend và resume

### 11.1 Suspend

Khi plugin yêu cầu chờ:

```text
Pointer: Running -> Suspended
Instance: Running -> Suspended
```

Engine chỉ suspend pointer đang `Running`, nhờ đó duplicate suspend event được bỏ qua.

### 11.2 Resume

```text
if pointer.Status != Suspended:
    ignore duplicate resume

if instance.Status == Suspended:
    instance.Resume()

pointer.CompleteFromWait(resumeData)
merge resumeData vào context
commit database
route pointer như một completion bình thường
```

Persist xảy ra trước routing để lần đọc trạng thái mới nhất thấy instance đã trở lại `Running`.

---

## 12. Thuật toán leasing và zombie recovery

Leasing ngăn hai worker cùng chạy một pointer.

### 12.1 Atomic lease acquisition

Worker thực hiện conditional update:

```text
UPDATE ExecutionPointer
SET Status = Running,
    LeasedBy = workerId,
    LeasedUntil = now + leaseDuration
WHERE Id = pointerId
  AND Active = true
  AND (
       Status = Pending
       OR (Status = Running AND LeasedUntil < now)
  )
```

Nếu `affectedRows == 1`, worker lấy lease thành công. Nếu bằng `0`, pointer đã được worker khác claim hoặc không còn hợp lệ.

Đây là compare-and-set tại database, không phụ thuộc lock trong process.

### 12.2 Zombie

Zombie là pointer:

```text
Status == Running AND LeasedUntil < UtcNow
```

`RecoveryBackgroundService` quét mỗi 30 giây và reset zombie về `Pending`.

Hạn chế: service hiện mới reset database, chưa publish lại `ExecutePluginCommand`. Vì worker chạy theo message thay vì polling pointer, pointer có thể nằm Pending mà không được chạy lại.

---

## 13. Thuật toán retry

Khi worker báo lỗi, `HandleStepFailureAsync` đọc `MaxRetries` từ step definition.

```text
if pointer.RetryCount < MaxRetries:
    pointer.ResetToPending()
    RetryCount++
    create ExecutePluginCommand again
    publish retry command
else:
    fail permanently
```

Nếu lỗi xảy ra ngay trong phase resolve/dispatch của retry, workflow được fail mà không gửi command xuống worker.

Engine hiện retry ngay lập tức, chưa có exponential backoff. Một phiên bản production thường dùng:

```text
delay = min(maxDelay, baseDelay * 2^RetryCount) + randomJitter
```

Jitter tránh nhiều workflow retry cùng lúc và tạo retry storm.

---

## 14. Thuật toán Saga Compensation

Compensation là rollback theo logic nghiệp vụ, không phải rollback transaction database.

Ví dụ:

```text
ReserveInventory -> ChargeCard -> CreateShipment -> lỗi
```

Rollback hợp lý:

```text
CancelShipment -> RefundCard -> ReleaseInventory
```

### 14.1 Thứ tự LIFO

Engine lấy pointer `Completed` và sắp xếp:

```text
ORDER BY EndTime DESC
```

Step hoàn thành sau được compensate trước. Đây là stack/LIFO và là quy tắc phổ biến của Saga choreography.

Pseudo-code:

```text
completed = getCompletedPointers(instance)
completed.sortByDescending(EndTime)

for each pointer in completed:
    step = findStepDefinition(pointer.StepId)
    payload = pointer.Output

    commands.add(CompensatePluginCommand(
        StepType = step.Type,
        Payload = payload,
        ExecutionMode = step.ExecutionMode
    ))
```

Output cũ được gửi cho plugin vì nó thường chứa resource ID cần rollback, ví dụ `PaymentId`, `ReservationId` hoặc `FileId`.

### 14.2 Trạng thái

```text
Running -> Compensating -> Compensated
```

Hiện tại engine mới chuyển sang `Compensating` và fire-and-forget các command. Chưa có barrier đếm kết quả compensation, nên chưa bảo đảm chuyển chính xác sang `Compensated` hoặc ghi nhận `CompensationFailed`.

Thiết kế hoàn chỉnh cần:

```text
ExpectedCompensationCount
CompletedCompensationCount
FailedCompensationCount
```

hoặc một bảng compensation task với trạng thái riêng cho từng pointer.

---

## 15. Thuật toán Cron scheduling

`WorkflowCronJob` không chạy workflow trực tiếp. Khi Quartz trigger fire, job tạo `SubmitWorkflowCommand` chứa:

- Definition ID.
- Cron trigger step ID.
- Thời điểm fire.
- Quartz fire instance ID.
- Scheduled fire time.

Sau đó command được gửi về engine qua routing key `workflow.job.submit`.

Job tính lần chạy tiếp theo theo cron expression và timezone, rồi thay thế trigger bằng một one-shot trigger mới. Bootstrap service dựng lại schedule khi host khởi động; reconciler retry các definition đồng bộ Quartz thất bại.

Distributed lock đảm bảo chỉ một host thực hiện bootstrap hoặc reconciliation tại một thời điểm.

---

## 16. Độ phức tạp tổng quát

Ký hiệu:

- `V`: số step.
- `E`: số transition.
- `P`: kích thước payload/context.
- `C`: số pointer completed.

| Thuật toán | Thời gian gần đúng | Ghi chú |
|---|---:|---|
| Tìm trigger | `O(V)` | Duyệt danh sách step |
| Đánh giá transition của node | `O(E)` | Hiện quét toàn bộ transition |
| Đếm incoming edge | `O(E)` | Có thể precompute graph index |
| Dead-path propagation | `O(V + E)` | Với visited edge set |
| Variable resolution | `O(P + K×D)` | K placeholder, D độ sâu path |
| Join query | Phụ thuộc DB | Nên index `(InstanceId, StepId)` |
| Compensation | `O(C log C)` | Do sort theo EndTime |

Với workflow lớn, có thể parse definition một lần thành adjacency maps:

```text
OutgoingEdges[sourceId]
IncomingEdgeCount[targetId]
StepById[id]
```

Sau đó lookup step và transition giảm từ `O(V)`/`O(E)` về gần `O(1)` cộng số cạnh thực sự của node.

---

## 17. Tóm tắt luồng end-to-end

```text
Trigger
  |
  v
StartWorkflowAsync
  |-- tạo Context
  |-- tạo WorkflowInstance
  |-- tạo start ExecutionPointer
  |-- resolve input
  '-- publish ExecutePluginCommand
             |
             v
          Worker
             |-- atomic lease
             |-- chạy plugin
             '-- publish completed/failed event
                         |
              +----------+----------+
              |                     |
              v                     v
       HandleCompletion       HandleFailure
              |                     |
              |                     +-- retry
              |                     '-- compensation/fail
              |
              +-- merge output
              +-- evaluate transitions
              +-- create normal/skipped pointers
              +-- evaluate Join
              +-- publish next commands
              '-- complete workflow khi không còn active pointer
```

## 18. Các nguyên tắc cốt lõi cần ghi nhớ

1. `ExecutionPointer` là đơn vị thực thi; `WorkflowInstance` là trạng thái tổng thể.
2. `Status` cho biết plugin đang ở đâu; `Routed` cho biết graph đã được mở rộng hay chưa.
3. Cạnh condition sai vẫn phải tạo dead-path token để Join không chờ vô hạn.
4. Join phải đồng bộ theo cùng một fork generation, không chỉ theo StepId.
5. Mọi completion/retry/resume event phải được xử lý idempotent vì message có thể giao lại.
6. Leasing bảo vệ việc chạy plugin, nhưng không tự giải quyết race khi merge context.
7. Compensation là reverse business action theo LIFO, không phải database transaction rollback.
8. Database state và message publication phải đi qua transactional outbox để tránh trạng thái đã lưu nhưng command bị mất.

