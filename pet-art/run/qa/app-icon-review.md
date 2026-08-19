# 應用程式圖示製作紀錄

製作日期：2026-08-19

- 圖示直接取自最終核准圖集 `spritesheet.png` 的 row 6 / column 0 正面坐姿，沒有重新生成或改變薯餅身分。
- 原始透明邊界為 `[14, 20, 178, 185]`，裁切後 164×165；在 256×256 透明畫布上顯示為 231×232，四周保留安全空間，沒有裁切耳朵、腳掌或毛髮。
- ICO 內含 16、24、32、48、64、128、256 七種 Windows 尺寸，兼顧系統匣、檔案總管與高 DPI 顯示。
- 同一個 ICO 嵌入 Release EXE，並供主視窗、設定視窗與系統匣使用。
- 可重現產生工具：`tools/build_app_icon.py`；視覺預覽：`pet-art/run/qa/app-icon-preview.png`；量測：`pet-art/run/qa/app-icon-metrics.json`。
