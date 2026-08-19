# 薯餅桌寵 v1.1.0-rc.5 驗證報告

驗證日期：2026-08-20

## 本版修正

- 以使用者提供的 `影/LINE_ALBUM_薯餅_260820_1.mp4` 作為走路節奏與身體動態的主要參考。
- row 1 固定為向右、row 2 固定為向左；向右影格由已核准的向左影格做精確水平鏡像，避免兩向體型、腳底與毛色漂移。
- 走路 8 格統一為每格 120 ms，取消循環末格額外停頓。
- 走路影格可見高度統一為 120 px，腳底基線固定在 y=170。
- 再次去除走路影格的綠幕邊緣殘色，alpha 保持不變。
- 餵食影格改為 WPF Resource，確保自包含單檔 EXE 仍能載入食物與餵食動作。

## 自動驗證

- Atlas：1536×2288、RGBA、8×11、spriteVersionNumber 2。
- Atlas validator：通過，0 errors、0 warnings、透明區 RGB 殘留 0。
- 方向回歸：row 1 的 8 格必須逐像素等於 row 2 的水平鏡像。
- 動畫節奏：row 1/2 均為 8 格、120 ms、無循環末格卡頓。
- 完整測試：120 passed、0 failed、0 skipped（由乾淨 Git 提交建置）。
- Release build：0 warnings、0 errors。

## 實際 EXE 驗證

- 實際啟動發佈後的單檔 EXE，100 ms 間隔連續擷取 20 秒，共 200 格。
- 視窗 X 座標範圍 879–1753 px；擷取中同時包含向右、停留、向左三種狀態。
- 所有 `dx > 0` 的走路格均朝右；所有 `dx < 0` 的走路格均朝左，未再出現倒退走路。
- 實際方向驗證圖僅保留於本機 QA，不納入儲存庫，避免桌面內容外流。
- 發佈資料夾僅含 `ShuBingDesktopPet.exe` 一個檔案。

## 發佈檔

- 路徑：`dist/ShuBingDesktopPet-win-x64-v1.1.0-rc.5-direction-fixed/ShuBingDesktopPet.exe`
- 大小：165,601,015 bytes
- SHA-256：`F3AF86B07BA3FF6EFB1AEF11ED570493EC3E2FFB7D90076DF6995D8E11DBF84E`
