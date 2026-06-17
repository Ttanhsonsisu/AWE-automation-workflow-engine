# Demo Google Sheet Review Workflow

Muc tieu: seed san mot workflow manual-trigger tren UI de demo quy trinh song song kiem tra ho so tu Google Sheets cloud.

## 1. Chuan bi Google Sheet

Tao Google Sheet co cac cot sau:

```csv
applicantName,major,gpa,englishScore,experienceMonths,documentText
```

Co the import file mau:

```powershell
AWE-automation-workflow-engine\samples\demo\google-sheet-applications.csv
```

Quan trong: sheet can public view hoac published to web, vi node doc sheet dung CSV export endpoint cua Google Sheets.

## 2. Workflow duoc seed

Workflow co trigger bang tay va cac node:

- Built-in `ManualTrigger`
- Custom DLL `Read Google Sheet`
- Custom DLL `Sheet Quality Check`
- Custom DLL `Analyze Sheet Applications`
- Built-in `Delay`
- Built-in `Join`
- Custom DLL `Write Back Sheet Results`
- Built-in `If`
- Built-in `Approval`
- Built-in `Log`

Luon chay chinh:

```text
Manual -> Read Sheet -> [Quality Check | Analyze | Delay] -> Join -> Write Back/Dry Run -> If -> Approval or Log
```

## 3. Seed local

```powershell
.\AWE-automation-workflow-engine\scripts\demo\seed-google-sheet-review-workflow.ps1 `
  -Environment local `
  -SampleSheetUrl "https://docs.google.com/spreadsheets/d/<SHEET_ID>/edit#gid=0"
```

Neu muon workflow duoc publish ngay:

```powershell
.\AWE-automation-workflow-engine\scripts\demo\seed-google-sheet-review-workflow.ps1 `
  -Environment local `
  -SampleSheetUrl "https://docs.google.com/spreadsheets/d/<SHEET_ID>/edit#gid=0" `
  -Publish
```

Script se in ra URL dang:

```text
http://localhost:5173/workflows/<definition-id>/edit
```

Mo URL do tren UI la thay workflow canvas va default input data trong Run dialog.

Mac dinh moi lan chay seed, script se xoa cac workflow demo cu co cung ten
`DEMO - Parallel Google Sheet Application Review`, build lai DLL, upload plugin
version moi, activate version moi, roi tao workflow moi. Neu muon giu cac workflow
demo cu, them flag:

```powershell
-KeepExistingDemoWorkflows
```

## 4. Seed self-host

```powershell
.\AWE-automation-workflow-engine\scripts\demo\seed-google-sheet-review-workflow.ps1 `
  -Environment selfhost `
  -ApiBaseUrl "http://localhost:8080/api" `
  -KeycloakBaseUrl "http://localhost:8081" `
  -FrontendBaseUrl "http://localhost" `
  -KeycloakAdminUser "admin" `
  -KeycloakAdminPassword "change_me" `
  -SampleSheetUrl "https://docs.google.com/spreadsheets/d/<SHEET_ID>/edit#gid=0"
```

## 5. Optional write-back bang Google Apps Script

Mac dinh `dryRun = true`, nen node `Write Back Sheet Results` chi chuan bi ket qua va khong ghi vao sheet.

Neu muon ghi nguoc ket qua, tao Apps Script Web App cho Google Sheet:

```javascript
function doPost(e) {
  const payload = JSON.parse(e.postData.contents);
  const results = JSON.parse(payload.resultsJson || "[]");
  const ss = SpreadsheetApp.openByUrl(payload.sheetUrl);
  const sheet = ss.getSheetByName("AWE Results") || ss.insertSheet("AWE Results");

  sheet.clearContents();
  sheet.appendRow([
    "RowNumber",
    "ApplicantName",
    "Major",
    "Gpa",
    "EnglishScore",
    "ExperienceMonths",
    "CompositeScore",
    "Decision",
    "Reason"
  ]);

  results.forEach(function (row) {
    sheet.appendRow([
      row.RowNumber,
      row.ApplicantName,
      row.Major,
      row.Gpa,
      row.EnglishScore,
      row.ExperienceMonths,
      row.CompositeScore,
      row.Decision,
      row.Reason
    ]);
  });

  return ContentService
    .createTextOutput(JSON.stringify({ ok: true, rows: results.length }))
    .setMimeType(ContentService.MimeType.JSON);
}
```

Deploy as Web App, lay URL webhook, roi seed voi:

```powershell
.\AWE-automation-workflow-engine\scripts\demo\seed-google-sheet-review-workflow.ps1 `
  -Environment local `
  -SampleSheetUrl "https://docs.google.com/spreadsheets/d/<SHEET_ID>/edit#gid=0" `
  -AppsScriptWebhookUrl "https://script.google.com/macros/s/<DEPLOYMENT_ID>/exec" `
  -DryRun:$false
```

## 6. Luu y demo

- Neu chi can thao tac UI, khong can `-Publish`.
- Neu muon demo run ngay trong UI, co the them `-Publish`.
- Run dialog se co san cac input: `sheetUrl`, `gid`, `maxRows`, `minimumGpa`, `minimumEnglishScore`, `targetExperienceMonths`, `dryRun`, `appsScriptWebhookUrl`.
- Node read sheet se loi neu Google Sheet khong public view/published.
