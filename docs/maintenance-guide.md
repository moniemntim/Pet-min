# 薯餅桌寵維護手冊

## 專案架構

專案是 .NET 8 WPF Windows 桌面程式：`src/ShuBingDesktopPet/` 放主程式與視窗，`Services/` 放螢幕工作區、漫遊、拖曳、動畫、設定、系統匣、啟動捷徑與原生視窗 API；`Models/` 放設定模型；`tests/ShuBingDesktopPet.Tests/` 放 xUnit 測試；`pet-art/` 保存 81 張照片的目錄、參考圖板、contact sheet 與角色 QA；`tools/` 放可重現資產工具。

主視窗每約 16 ms 執行一次 tick：先更新游標與漫遊，再決定動畫列，最後從啟動時建立的 Frozen 影格快取取出影格。拖曳與反應優先於漫遊，關閉漫遊後仍保留互動。

## 動畫資產

角色圖集採 sprite v2：8×11、共 88 格，單格 192×208 px，總尺寸 1536×2288、RGBA。`pet.json` 的 `spriteVersionNumber` 必須維持為 2。各動畫列由 `AnimationPlanFactory` 定義，包含待機、左右行走、拖曳、反應、整理毛髮與 16 方向追視。

替換圖集後須檢查透明邊緣、腳底基準、坐姿／站姿比例、摸頭與舔毛倍率，以及 16 方向。圖集不完整時 `SpriteFrameCache` 應拒絕建立半套快取。

## 資源生命週期與記憶體

動畫 88 格在啟動時切割為 `CroppedBitmap` 並 `Freeze()`，每次 tick 重用相同物件。關閉時需停止兩個 DispatcherTimer、解除顯示器事件、隱藏並 Dispose `NotifyIcon`、釋放 tray icon 與選單，並關閉設定視窗。開機捷徑使用的 WScript.Shell 與 shortcut COM 物件在 `finally` 釋放。

目前基準取樣：冷啟動 Private Memory 約 130–137 MB；建立設定／右鍵選單後連續兩分鐘約 182–184 MB；GDI 約 57、USER 約 49–50、Handle 約 552–561，沒有持續成長。若改動畫渲染或系統匣資源，必須重做長時間取樣並記錄結果。

## 建置、測試與發佈

需要 Windows 與 .NET 8 SDK：

```powershell
dotnet restore .\ShuBingDesktopPet.sln
dotnet build .\ShuBingDesktopPet.sln -c Release
dotnet test .\ShuBingDesktopPet.sln -c Release
dotnet publish .\src\ShuBingDesktopPet\ShuBingDesktopPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\ShuBingDesktopPet-win-x64
```

發佈前確認建置 0 錯誤／警告、測試通過、EXE 可啟動、圖示為薯餅、透明置頂與拖曳正常、設定可保存、跨螢幕與縮放驗收完成。發佈資料夾不應納入版本庫。

## 餵食功能

`FeedingController` 管理 `PetFoodType`（Apple、Carrot、Pellet）的啟動、完成、冷卻與完成訊息；`MainWindow` 只在 Feed one-shot 完成時增加一次統計並保存。右鍵桌寵與系統匣共用「餵食薯餅」子選單。餵食序列使用 row7（與 Groom 共用），拿食物到嘴邊後完成，顯示「吃飽了！」並播放 `HappyJump`，完成後 3 秒內拒絕新餵食。

餵食序列是最高優先狀態：暫停漫遊、拖曳、點擊、摸頭與其他左鍵互動；完成後才恢復原本狀態。overlay 的 apple/carrot/pellet shape 在 XAML 固定建立一次，每 tick 只更新可見性、位置與文字，不得在 render loop 建立新 shape。餵食仍可由 tray 觸發，即使 click-through 開啟。

## 設定格式

設定服務目前 schema version 為 3，新增 `TotalFeedCount`、`LastFedUtc`、`LastFoodType`；schema 2 讀取時餵食欄位使用預設值，保持向後相容。使用 JSON 字串列舉；新增欄位應提供預設值並在 `Normalize` 補正非法值。
