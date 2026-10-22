# 薯餅 1.2.0-rc.2：走路與小白菜

這是獨立候選版，保留既有 `v1.1.0` 正式版及 `1.2.0-rc.1` 本機輸出。

## 素材與尺度

新增的 `LINE_ALBUM_薯餅_261023_1.mp4` 約 55 秒，主要呈現吃葉菜與短距離挪步。它用於確認低肩膀、腹部接近地面、短腿及毛色；八格完整步態是參考重繪，不是從影片直接截出的連續走路。

一般模式左右各八格，修長模式左右各八格；維持主圖集 1536×2288、單格 192×208、修長格 256×208。各來源 sheet 全程只用一個固定倍率，逐格只校準位置，非跳躍腳底為 `y=170`。一般與修長角色的頭部、耳朵和腳掌尺度經固定畫布對照調整；修長模式以軀幹伸展呈現。左右是分別製作的側面素材，不強制鏡像。

素材經內建 imagegen 製作，最終來源、提示詞與裝配設定位於 `pet-art/walk-bokchoy-20261023/`。新來源使用 4×2 與 2×4 兩種排列；裝配器依設定分列排序，毛邊按最近角色歸屬，包含分離足尖的可見像素都納入腳底計算。厚實來源裁切會直接拒絕，不以裁切或逐格縮放掩蓋。

## 小白菜

新增 `PetFoodType.BokChoy = 5`，JSON 寫為 `bokChoy`，舊食物數值和設定 schema 7 保持相容。六格內容為抱食、靠近嘴部、啃咬、低頭、咀嚼與收尾。使用既有餵食時間、慶祝和 3 秒冷卻，桌面與 OBS 共用餵食幀編號；Twitch 仍排入種子棒。

五種食物資源（含 Apple、Carrot 舊別名）和修長資源在啟動時校色、凍結並快取。小白菜的菜葉與淡色菜梗區域排除毛色統計與修改，RGBA 保持來源值。毛色目標是參考照片的視覺校準參數，非照片的精密色度量測。

## 重建與驗證

在 Windows、.NET 8 SDK 環境執行。素材工具需要 Python、Pillow、NumPy、SciPy。

```powershell
python tools/assemble_refresh_sprites.py --config pet-art/walk-bokchoy-20261023/assembly-config.json --output-root pet-art/walk-bokchoy-20261023/staging --self-check
dotnet test ShuBingDesktopPet.sln -c Release
dotnet publish src/ShuBingDesktopPet/ShuBingDesktopPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/ShuBingDesktopPet-walk-bokchoy-20261023
```

素材裝配產生 staging，安裝其 PNG 至對應 `src/ShuBingDesktopPet/Assets` 後再建置。不要用 staging 的一般描述覆蓋來源 `pet.json`。設定 `SHUBING_QA_OUTPUT` 為絕對資料夾路徑後執行測試，可輸出實際快取影格；將它放在工作目錄的 `runtime-qa` 後執行 `python tools/build_refresh_preview.py --root pet-art/walk-bokchoy-20261023` 即可重建獨立預覽。

自動驗證包含透明度、快取重用、食物區域逐像素不變、46 個內嵌 PNG 雜湊、餵食設定與時間軸，以及 75%／100%／150%、一般／修長下的實際 WPF OBS RenderFrame 路徑。預覽同時提供固定舞台逐格、連播與走路前後對照。

實際結果、發佈檔雜湊與已知驗證限制記錄在候選版附帶的 `驗證紀錄.md`。透明桌面視窗的真實滑鼠拖曳、追視、OBS Studio 擷取及 Twitch 網路事件，與程式化 WPF 繪製測試是不同驗證層級；未操作的部分不列為已驗收。
