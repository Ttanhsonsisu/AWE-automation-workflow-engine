### 1. Cấu trúc tin nhắn (The Format)

Mỗi lần commit phải tuân theo định dạng sau:

```text
<type>(<scope>): <subject>

[Optional Body]
[Optional Footer]

```

**Ví dụ:** `feat(worker): implement rabbitmq consumer for node execution`

---

### 2. Các từ khóa quan trọng (Types)

Bạn chỉ được phép bắt đầu câu bằng các từ khóa sau:

| Type | Ý nghĩa | Khi nào dùng? |
| --- | --- | --- |
| **feat** | Feature | Khi bạn thêm một tính năng mới (Ví dụ: Thêm API tạo Workflow). |
| **fix** | Bug Fix | Khi bạn sửa một lỗi (Ví dụ: Sửa lỗi NullReferenceException). |
| **chore** | Chore | Các việc vặt không đụng vào code logic (Cập nhật NuGet, sửa .editorconfig, cấu hình Aspire). |
| **refactor** | Refactor | Sửa code cho sạch/gọn hơn nhưng không thay đổi tính năng (Ví dụ: Tách hàm, đổi tên biến). |
| **docs** | Documentation | Chỉ sửa tài liệu (README, Swagger XML comments). |
| **style** | Style | Sửa format, khoảng trắng, dấu chấm phẩy (Code chạy y hệt cũ). |
| **test** | Test | Thêm hoặc sửa Unit Test/Integration Test. |
| **perf** | Performance | Code cải thiện hiệu năng (Ví dụ: Thêm Redis Cache). |
| **ci** | CI/CD | Sửa file GitHub Actions, Dockerfile. |

---

### 3. Phạm vi (Scope) - Tùy chọn

Phần nằm trong ngoặc đơn `()` để chỉ rõ bạn đang sửa ở module nào trong dự án AWE.

* `api`: Code trong `AWE.Api`
* `worker`: Code trong `AWE.Worker`
* `domain`: Code trong `AWE.Domain`
* `ui`: Code Frontend React
* `infra`: Code Database, MassTransit
* `aspire`: Code cấu hình AppHost

---

### 4. Quy tắc viết nội dung (Subject Rules)

1. **Sử dụng Tiếng Anh:** Code là ngôn ngữ quốc tế, hãy tập thói quen viết commit bằng tiếng Anh.
2. **Động từ nguyên thể (Imperative mood):**
* ✅ Đúng: `add`, `fix`, `change`, `update`
* ❌ Sai: `added`, `fixes`, `changing`, `updated`
* *Mẹo:* Câu commit phải hoàn thành được câu: "If applied, this commit will..." (Nếu apply commit này, nó sẽ...) -> "Add new API" (đúng).


3. **Viết thường toàn bộ:** Không viết hoa chữ cái đầu (trừ tên riêng).
4. **Không có dấu chấm:** Không chấm câu ở cuối.

---

### 5. Ví dụ thực tế cho dự án AWE

Dưới đây là các ví dụ mẫu bạn có thể copy theo:

**Khi thêm tính năng mới:**

```text
feat(api): add endpoint for creating new workflow
feat(ui): implement drag-and-drop for react-flow nodes
feat(domain): add workflow aggregate root and value objects

```

**Khi sửa lỗi:**

```text
fix(worker): resolve null reference when graph data is empty
fix(infra): correct rabbitmq connection string in docker-compose
fix(ui): fix node connection line alignment issue

```

**Khi cấu hình hệ thống (Chore/CI):**

```text
chore(aspire): add redis container to apphost configuration
chore(deps): update masstransit to version 8.1.0
ci: add github actions workflow for dotnet build

```

**Khi viết Test:**

```text
test(domain): add unit tests for workflow validation rules
test(infra): add integration test for postgres repository using testcontainers

```

**Khi Refactor code:**

```text
refactor(api): extract validation logic to mediatr pipeline behavior
perf(worker): implement caching for fetching workflow definitions

```