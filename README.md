# 薯餅桌寵

目前版本：`v1.1.0-rc.10` 候選（Twitch Bits／頻道點數／OBS 擷取整合）；`v1.0.0` 為餵食功能前穩定基準。

以專案內 81 張薯餅實物照片共同還原的 Windows 10/11 寫實桌面寵物。

## 功能

- 透明、無邊框、永遠置頂，不顯示在工作列。
- 可透過系統匣直接開啟「薯餅 OBS 擷取」獨立透明捕捉視窗，設定固定尺寸（1280x720、1920x1080、2560x1440、3840x2160）後，OBS 以 `Window Capture` 可直接抓到薯餅在畫布內行走；餵食／反應與修長身形會同步，宿主視窗固定在虛擬桌面外以免桌面出現第二隻。
- 按住寵物移動超過 6 DIP 後拖曳；點擊抬爪、雙擊跳躍、摸頭與游標追視。
- 可開關自主漫遊，支援目前螢幕／所有螢幕、三段速度與 75%–150% 大小。
- 自動避開工作列並保持完整可見；支援負座標、多螢幕與 per-monitor DPI。
- 系統匣可顯示／隱藏、切換漫遊與滑鼠穿透、開啟設定、回主螢幕及退出；在寵物本體按右鍵會顯示同一份選單。
- 右鍵寵物或系統匣選「餵食薯餅」→「種子棒（薯餅實拍）」；動畫直接使用薯餅啃種子棒的原照連拍，餵食時暫停漫遊與其他左鍵互動，完成後顯示「吃飽了！」並開心跳躍，3 秒冷卻。
- EXE、設定視窗與系統匣使用薯餅正面照片圖示，內含 16–256 px 多尺寸 Windows 圖示。
- 「所有螢幕」漫遊會先走到目前螢幕邊界，再從目的螢幕對應入口繼續行走，支援螢幕間隙與負座標排列。
- 設定（含 OBS 擷取與 Twitch 非敏感設定）與最後位置保存於 `%LOCALAPPDATA%\ShuBingDesktopPet\settings.json`，目前 schema 為 `7`，舊設定會自動升級。

## 使用可攜版

開啟 `dist/ShuBingDesktopPet-win-x64`，直接執行 `ShuBingDesktopPet.exe`。這是內含 .NET 8 執行環境與角色資產的單檔可攜版，不必另外安裝 .NET。滑鼠穿透開啟時，仍可由系統匣選單關閉穿透或退出程式。

## 開發與驗證

需求：.NET 8 SDK（Windows）。

```powershell
dotnet build .\ShuBingDesktopPet.sln -c Release
dotnet test .\ShuBingDesktopPet.sln -c Release
dotnet publish .\src\ShuBingDesktopPet\ShuBingDesktopPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

角色圖集為 v2 規格：8×11、單格 192×208、總尺寸 1536×2288。原照逐張分析在
`pet-art/references/reference_catalog.csv`，完整參考圖板、contact sheet、動畫預覽與方向 QA
留存在 `pet-art`。手動驗收項目見 `docs/manual-qa.md`。

## 文件索引

- [使用說明](docs/user-guide.md)：安裝、操作、設定、系統匣、資料位置與疑難排解。
- [維護手冊](docs/maintenance-guide.md)：架構、建置、測試、發佈、動畫資源與生命週期。
- [交接手冊](docs/handover-guide.md)：接手清單、驗收重點、已知限制與發版流程。
- [版本控制規範](docs/versioning.md)：SemVer、分支、提交與標籤規則。
- [手動驗收](docs/manual-qa.md) 與 [驗證報告](docs/verification-report.md)：目前的測試與人工檢查紀錄。

餵食功能目前以 `v1.1.0` 候選版本維護；`v1.0.0` 保留為加入餵食前可回復的穩定基準。

### Twitch Bits／頻道點數餵食

設定視窗可啟用 Twitch，填入 Twitch Developer Console 建立的 Public app Client ID，並設定最低 Bits。收到 `channel.cheer` 後會以種子棒餵食薯餅（佇列最多 20 次）。

若要使用 Twitch 原生頻道點數，先在 Creator Dashboard 建立自訂獎勵，再勾選「啟用頻道點數餵食」，將獎勵名稱填成完全相同的文字，並從系統匣選「連線／重新授權 Twitch」。程式會額外要求 `channel:read:redemptions`，收到 `channel.channel_points_custom_reward_redemption.add` 且獎勵名稱相符時餵食。頻道點數僅適用於 Twitch 已開放 Channel Points 的頻道。Token 使用 Windows CurrentUser DPAPI 保存於本機，不會顯示在一般設定檔或 UI。
