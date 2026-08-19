# 版本控制與發佈規範

## 版本基準

採用 Semantic Versioning 2.0.0：`MAJOR.MINOR.PATCH`。目前餵食功能位於 `agent/feeding-feature`，版本為 `v1.1.0` 候選；`v1.0.0` tag/release 是穩定回復基準。

- `v1.0.0`：目前餵食功能加入前的穩定基準。
- `MAJOR`：不相容的設定格式、資產契約或使用方式變更。
- `MINOR`：向後相容的新功能，例如本次餵食功能為 `v1.1.0`。
- `PATCH`：向後相容的修正、資源釋放、錯誤修補或文件修正。

提交訊息使用動詞開頭，例如 `feat: add feeding interaction`、`fix: clamp roaming boundary`、`docs: update handover guide`。每次提交只處理一個可說明的變更；不要提交使用者設定、建置輸出或暫存 QA 檔。

建議 `main` 保存可發佈版本，開發分支統一使用 `agent/<description>`，例如 `agent/feeding-feature`。合併前須完成建置、測試與人工驗收；公開 tag 建立後不可重寫。

## 發佈流程

1. 更新版本號、`CHANGELOG.md`、README 與必要文件。
2. 執行 Release build、93 個自動測試及 win-x64 自包含單檔 publish。
3. 依 `docs/manual-qa.md` 驗收透明、拖曳、跨螢幕、DPI、設定、圖示與關閉清理。
4. 記錄 EXE SHA-256、測試數量與已知限制。
5. 建立對應 tag（例如 `v1.1.0`），再建立 GitHub Release，附上 EXE 與變更摘要。
