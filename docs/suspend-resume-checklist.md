# Suspend/Resume - Các điểm cần lưu ý

## 1) Vì sao có trạng thái `Completed` nhưng `Routed = false`
- Đây là trạng thái trung gian hợp lệ trong kiến trúc event-driven.
- Worker hoàn thành step trước, orchestrator route step tiếp theo ở pha sau.
- Với `Join`, pointer có thể phải chờ đủ nhánh hoặc chờ lock.

## 2) Race condition quan trọng khi bấm `Suspend`
- Có thể xảy ra lúc orchestrator đang xử lý completion/routing.
- Nếu chỉ đọc trạng thái từ entity đã track trong scope hiện tại, có thể bị stale state.
- Cần kiểm tra trạng thái mới nhất từ DB (`AsNoTracking`) trước khi publish lệnh chạy node tiếp theo.

## 3) Quy ước xử lý trong `ResumeExecutionUseCase`
- `Resume` nên trả về nhanh (non-blocking), không nên chờ hội tụ toàn bộ pointer.
- Chấp nhận còn pointer kẹt tạm thời, nhưng phải có cơ chế đo `progress` thực sự.
- Nếu không có tiến triển nào sau vài vòng kick-off ngắn, trả lỗi rõ ràng (`Execution.ResumeNoProgress`).

## 4) Những điểm đã gia cố
- Thêm đọc trạng thái nhẹ từ DB: `GetInstanceStatusAsync(...)`.
- Orchestrator re-check trạng thái workflow trước khi dispatch/publish command tiếp theo.
- `Resume` tránh trả `409` giả khi luồng đã chạy lại nhưng chưa hội tụ xong.
- Nếu `Resume` không tạo được tiến triển thật -> trả lỗi conflict có ý nghĩa.

## 5) Delay + Suspend
- `Delay` là bước dễ gặp race nhất vì thời điểm completion và lệnh suspend có thể giao nhau.
- Khi test, luôn kiểm tra:
  - `WorkflowInstance.Status`
  - số lượng pointer `Completed && !Routed`
  - log lock/join/routing trong orchestrator

## 6) Lưu ý khi debug tại Visual Studio
- Một số thay đổi interface/base class không áp dụng bằng Hot Reload.
- Nếu thấy hành vi không khớp code mới, restart process để nạp lại toàn bộ DI và contracts.

## 7) Checklist vận hành nhanh
- Suspend trả về 200 -> kiểm tra status DB đã là `Suspended`.
- Sau khi step hiện tại hoàn tất, orchestrator không được publish lệnh mới nếu status đang `Suspended`.
- Resume trả về nhanh (thường `Success`), workflow tiếp tục dần theo eventual consistency.
- Nếu vẫn đứng: kiểm tra lỗi `Execution.ResumeNoProgress` + logs pointer routing.
