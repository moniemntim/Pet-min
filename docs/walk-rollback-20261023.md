# 薯餅 1.2.0-rc.3：還原前一版走路

一般與修長模式的左右走路各八格，恢復成新增小白菜之前的步態與大小。來源為該次修改前保存的素材，沒有重新生成角色、縮放或改變花紋。只有原始腳底相差一像素的影格作整數上下平移，讓不透明主體底部仍為 y=170；可見像素完整保留。

## 保留內容

小白菜、花椰菜與種子棒餵食、其餘九列動作、共同大小設定、校色與快取、桌面／OBS 餵食同步及 Twitch 邏輯全部沿用 rc.2。

主代理比對結果：32 格走路均等於前一版來源加上 -1／0／1 像素的垂直平移，沒有縮放或 RGB／alpha 變動；其他九列 RGBA 與 30 張餵食 PNG 檔案內容完全不變。完整 Release 測試 205 通過、0 失敗、0 略過，包含三種大小、兩種身形的 546 次 WPF 繪製。

## 重建

已提交獨立來源 `pet-art/walk-rollback-20261023/source/`，包含走路兩列圖集與 16 張修長影格，不需要本機備份或個人照片／影片。來源來自既有素材的機械裁切／複製。

```powershell
python tools/restore_previous_walk.py
dotnet test ShuBingDesktopPet.sln -c Release
dotnet build ShuBingDesktopPet.sln -c Release
dotnet publish src/ShuBingDesktopPet/ShuBingDesktopPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/ShuBingDesktopPet-walk-rollback-20261023
```

恢復工具預設只讀獨立來源，只寫入目前主圖集的走路兩列與修長影格。如果先用舊版 `assembly-config.json` 重建素材，必須最後再執行上述恢復工具，才能維持 rc.3 的走路。工具拒絕超過一像素的位移或任何可見像素裁切，並記錄各格雜湊和非走路素材不變的驗證結果。

候選版附 `preview.html`、`QA` 連播與 rc.2／restored 對照，以及 `驗證紀錄.md`、`SHA256SUMS.txt`。舊版輸出與正式版保留。本次未執行 EXE 啟動、真實滑鼠操作、OBS Studio 或 Twitch 網路端到端測試；程式化 WPF 測試不代表這些人工驗收。
