# 薯餅實拍像素修復（rc.4）

## 目標

修正 rc.3 餵食動作不明顯、走路影格上下跳動與中間影格變暗的問題。角色與食物必須直接取自專案原始照片，不得重新設計臉、毛色、身形或四肢。

## 來源照片

- 真實餵食連拍：`圖/LINE_ALBUM_薯餅_260814_13.jpg`、`14.jpg`、`15.jpg`
- 側面移動：`圖/LINE_ALBUM_薯餅_260814_23.jpg`、`24.jpg`

餵食三張照片中的薯餅和種子棒相連，因此透明遮罩可以同時保留鼻口、前爪與食物的真實接觸關係。照片 `12.jpg`、`16.jpg`、`17.jpg` 的自動配準可信度不足，已從成品排除。

## 製作策略

1. ImageGen 只產生去背遮罩參考；輸出 RGB 永遠套回原始 JPEG，生成像素不會進入角色或食物。
2. 餵食採 `13 → 14 → 15 → 14 → 13 → 14` 的實拍咬合循環。三個端點先依照片方向轉正，統一為 170 px 高、`y=184` 下緣基準，再組成六格；不另外貼食物圖示。
3. 走路兩個實拍端點統一為 128 px 高、`y=170` 腳底基準。端點間以預乘 alpha 混合出 25%、50%、75% 過渡，避免透明邊緣黑邊、亮度突降與腳底跳位。
4. 原照朝左，直接作為左走列；右走列只由左走列水平鏡像，確保兩個方向是同一隻薯餅、同一尺寸。
5. WPF 顯示層使用 `RenderOptions.BitmapScalingMode="HighQuality"`，75%～150% 顯示比例仍是整張 192×208 影格等比例取樣，不裁切角色。

## 維護與重建

- 去背：`tools/extract_photo_subject.py`，依賴 Pillow、NumPy、`opencv-python-headless`。先執行 `--self-check`，再以來源 JPEG 和遮罩參考圖輸出 RGBA。
- 尺寸與基準：`tools/register_sprite_frames.py`。
  - 走路：`--baseline 170 --max-width 182 --max-height 128`
  - 餵食：`--baseline 184 --max-width 150 --max-height 170`
- 走路過渡：`tools/compose_real_walking_frames.py --frame-a ... --frame-b ... --output-dir ... --self-check`
- 餵食組列：`tools/compose_photo_feeding_sequence.py --input-dir ... --output-dir ... --self-check`
- 圖集列替換：`tools/replace_atlas_rows.py`；row 1 為右走鏡像，row 2 為原照左走。完成後使用 `hatch-pet/scripts/validate_atlas.py --require-v2` 驗證。
- `work/` 保存可重建中間檔且由 Git 忽略；`qa/` 保存 contact sheet、GIF、註冊 JSON、替換 manifest 與 atlas 驗證結果。

## 驗收標準

- 臉、眼睛、耳朵、棕白毛色分界與種子棒均能逐項對上來源原照。
- 走路八格的腳底與身體頂部不得逐格跳動；過渡影格不得忽暗或出現透明黑邊。
- 餵食必須看得出鼻口靠近種子棒的真實連拍變化；不得出現漂浮食物、假手、額外肢體或遊戲式圖示。
- 餵食六格寬度一致、腳底／下緣基準固定；動作切換不得突然放大或縮小。
- 192×208 影格完整可見，透明 RGB 殘留為 0；左右方向一致且完整鏡像。
