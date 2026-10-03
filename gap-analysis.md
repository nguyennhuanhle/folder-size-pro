# Gap Analysis — Folder Size Pro — 2026-10-03

> So code với `use-cases.md` v0.2. **Chỉ nêu sự thật** (đo được / chạy được); không đề xuất sửa — việc đó thuộc Phase 4.
> Nguồn bằng chứng: `tools/FspVerify` (số thật cạnh nguồn độc lập), `--selftest` (chạy thật ViewModel + lõi trong app), chạy CLI, chụp ảnh cửa sổ thật, `grep` mã nguồn.
> Quy ước chấm: nghi ngờ giữa DONE và PARTIAL thì chọn PARTIAL. **Phiên Claude này không có quyền Administrator** — phần cần Administrator (MFT, VSS qua WMI, đọc thô ổ) do người dùng chạy `tools/verify-mft.ps1` trên D: và C:, báo cáo nằm ở `_fixtures/mft-report-*.txt`.

## Bằng chứng đo được (rút gọn)

| Phép đo | Kết quả |
|---|---|
| Kích thước biên (0/1/100/700/4095/4096/4097 B, 1 MiB), hard link (có 1 link NGOÀI vùng quét, kích thước "cũ" trong danh sách thư mục), nén NTFS, sparse (100 MiB → 1 MiB), ADS, junction vòng, đường dẫn 359 ký tự, tên `trailing-space `, `CON`, Unicode | Mọi con số **khớp kỳ vọng tính tay / `FSCTL_GET_RETRIEVAL_POINTERS`** (`FspVerify fixture`) |
| `D:\GitHub\qr-studio` (2.624 file) so với nguồn độc lập (extent thật + `FindFirstStreamW` + id file) | Số file, Kích thước, Trên đĩa: **khớp từng byte** (`FspVerify truth`) |
| 100.000 file nhỏ so với PowerShell `Get-ChildItem -Recurse -Force` | Số file + tổng Length **khớp từng byte**; máy quét **0,6 s (165.000 file/s)** vs PowerShell 3,9 s |
| Cả ổ D: (1.660.470 file, 187.211 thư mục) | **28–38 s**, chế độ thường, không Administrator |
| Cả ổ C: (2.357.058 file, 430.541 thư mục, 568 thư mục bị chặn quyền) | **92–125 s**; Trên đĩa 420,92 GiB, Kích thước 1,14 TiB — chênh vì **676 GiB file OneDrive "chỉ trên đám mây"** (0 B trên đĩa, đúng Q4); chưa giải thích 9,6% (SVI + 568 thư mục chặn quyền) |
| Đối chiếu ổ D: (không Administrator) | Chưa giải thích **159,9 MiB = 0,1%** của Used (sau khi cộng `$MFT` 1,63 GiB đọc qua `FSCTL_GET_NTFS_VOLUME_DATA` không cần admin; phần còn lại chủ yếu System Volume Information bị chặn) |
| OneDrive thật: 105.172 mục RECALL; sau khi mở-thuộc-tính + hỏi Std/Stream cả 105.172 mục | **0 mục đổi cờ** (KT-05); 780 file "chỉ trên đám mây" = 5,98 GB kích thước, 0 B trên đĩa |
| Quét lại 2 nhánh rồi so với quét mới toàn bộ | **0 khác biệt** (cây, tổng, hard link, thống kê loại) |
| Xoá thật vào Thùng rác (xác nhận có mục `$R…` trong `D:\$RECYCLE.BIN`) | Tổng gốc giảm đúng 8.192 + 4.096; ước tính "giải phóng ngay 0 / sau dọn Thùng rác X" đúng; hard link còn ngoài vùng chọn → X = 0 |
| Chế độ gọn (giới hạn 1 MB, giữ 5 file/thư mục) | Tổng Trên đĩa / Kích thước / số file **y hệt** quét đầy đủ; 46 nút gộp, 848 nút vs 2.861 |
| Theo dõi trực tiếp: thêm file 300.000 B | App **tự cập nhật** +303.104 B; tạo 60.000 file nhanh → báo "mất theo dõi" |
| **MFT trên D: (Administrator)** — `_fixtures/mft-report-D.txt` | Lần 2 (sau bản sửa): **2,7 s** (thường 25,6 s). Dữ liệu người dùng: số file / thư mục / Kích thước / Trên đĩa khớp từng byte ngoài 4 file 24 KiB trong System Volume Information (quét thường không vào được). `$Bitmap` = tổng cluster mọi attribute = **211.527.888.896 B, chênh 0**. Chưa giải thích **21,7 MB = 0,010%**. 0 bản ghi hỏng / mồ côi |
| **MFT trên C: (Administrator)** — `_fixtures/mft-report-C.txt` | Lần 4: **7,8 s** (thường 80 s), 2,4 triệu file + 446 nghìn thư mục. Sau khi trừ vùng diff VSS và luồng nội bộ Cloud Files (chỉ MFT thấy được): lệch 4,8 MB Kích thước / 4,3 MB Trên đĩa, đều là file đổi trong lúc quét. Chưa giải thích: MFT 0,066%, thường 0,141% |

## Người dùng

| Use Case | Status | Ghi chú |
|---|---|---|
| UC-01 Danh sách ổ (tổng/đã dùng/trống, loại FS) | DONE | Chụp ảnh thật: C:, D:, G: (FAT32) với thanh dùng + SSD/HDD. Ổ mạng hiện nếu `DriveInfo` thấy; chưa có ổ mạng để thử |
| UC-02 Quét ổ / thư mục / nhiều đường dẫn (hộp thoại, gõ, kéo-thả) | DONE | Gõ + nhiều đường dẫn bằng `|` + ổ + thư mục chạy thật; hộp thoại chọn thư mục và kéo-thả chỉ review mã (không tự động hoá được) |
| UC-03 Tiến độ thật + cây hiện dần | DONE | Selftest: số file/thư mục/byte/tốc độ/thư mục đang quét; cây dựng khi quét |
| UC-04 Tạm dừng / tiếp tục / huỷ, giữ kết quả dở dang "chưa hoàn tất" | DONE | Selftest + CLI: huỷ → `Partial`, 24.515 thư mục gắn "chưa quét", banner. Tạm dừng: dừng sau khi các luồng xong ≤127 mục đang đo (~vài nghìn file) rồi đứng yên |
| UC-05 Cây (Kích thước, Trên đĩa, %, số file/thư mục, ngày sửa, sắp xếp, mở/đóng) | DONE | Selftest: mở/đóng `sizes`, sắp theo Tên/File/Trên đĩa đúng thứ tự; chụp ảnh |
| UC-06 Top N file / thư mục lớn nhất | DONE | Top file; top thư mục tính theo file trực tiếp (không lặp cha/con) |
| UC-07 Phân bố theo loại (bảng + biểu đồ) | DONE | Chụp ảnh vi/en, sáng/tối |
| UC-08 Treemap tương tác, đồng bộ với cây | PARTIAL | Vẽ squarified, nhãn, màu theo nhóm đã chụp ảnh thật. Bấm / bấm đúp / chuột phải / "Lên một cấp" mới review mã, chưa thao tác thật |
| UC-09 Tìm / lọc (tên, đuôi, cỡ, ngày, thuộc tính) | PARTIAL | Chạy thật: tên `*.bin`, cỡ ≥ 1 MiB, đuôi `txt`, regex sai → lỗi ngay ở ô lọc. Lọc theo ngày sửa và theo cờ thuộc tính (ẩn/hệ thống/đám mây…) chưa chạy |
| UC-10 Chi tiết một mục | DONE | Selftest: số byte chính xác, ADS, các đường dẫn hard link khác, chủ sở hữu, ngày; đích reparse chưa thử (không tạo được symlink không-admin; junction thì có) |
| UC-11 Bảng Đối chiếu ổ | DONE | CLI + tab (chụp ảnh) cho D:; bấm vào từng dòng để xem chi tiết **không có** — chi tiết hiện ngay trên dòng |
| UC-12 Mở Explorer / sao chép đường dẫn / Properties | PARTIAL | Mã xong, chưa chạy (mở cửa sổ ngoài) |
| UC-13 Đưa vào Thùng rác (hai con số, đã trừ hard link) | DONE | Xem bằng chứng ở trên |
| UC-14 Lưu / mở snapshot `.fsp` | DONE | Selftest + CLI; có mốc thời gian + banner "số liệu CŨ" |
| UC-15 So sánh hai snapshot | DONE | GUI + CLI: Δ khớp (+296 KiB, +492 KiB) |
| UC-16 Xuất CSV / JSON / HTML | DONE | HTML có mốc UTC, byte chính xác trong `title=`; JSON/CSV đọc lại được |
| UC-17 Quét lại một nhánh | DONE | 0 khác biệt so với quét mới |
| UC-18 Cài đặt (đơn vị, ngôn ngữ, giao diện, loại trừ, ADS, ẩn/hệ thống, timeout, ngưỡng RAM) | PARTIAL | Cửa sổ dựng + lưu/đọc JSON, cờ loại trừ / bỏ ADS / SI chạy thật qua CLI; cửa sổ Cài đặt chưa mở thật để thử |
| UC-19 Menu chuột phải Explorer | PARTIAL | Bộ cài + khoá registry đã viết và biên dịch; xem mục "Cài thử" cuối file |
| UC-20 Chạy với quyền Administrator | PARTIAL | Mã + nút + cờ `--scan/--select` xong; hộp UAC không tự động hoá được |

## Quản trị viên

| Use Case | Status | Ghi chú |
|---|---|---|
| UC-30 Quét MFT nhanh, khớp quét thường | DONE | Ổ D: khớp từng byte (ngoài SVI). **Ổ C: lần 4 (Administrator, 12:31):** MFT 7,8 s vs thường 80 s; số thư mục người dùng khớp tuyệt đối (446.026), 0 bản ghi mồ côi, `$Bitmap` = tổng attribute (chênh 0). Sau khi trừ hai phần chỉ MFT thấy được (vùng diff VSS 5,13 GB bị volsnap giấu; luồng nội bộ Cloud Files `${3D0CE612-FDEE-43f7-8ACA-957BEC0CCBA0}` 148,6 MB bị cldflt giấu): Kích thước lệch 4,8 MB, Trên đĩa lệch 4,3 MB trên 1,3 TB / 495 GB, số file lệch 10 — toàn bộ là file cache Chrome/Claude sửa ở 12:29–12:30, trong lúc quét. Mọi thư mục OneDrive Δ Kích thước = 0. Phần chưa giải thích của MFT (332 MB, cố định qua 3 lần chạy) = đúng $Bitmap − cây → nay thành dòng đối chiếu riêng "Attribute NTFS ngoài luồng dữ liệu" (chưa chạy admin lại để thấy dòng này). Lưu ý: `fsutil file queryallocranges` làm OneDrive tải file — không dùng để kiểm |
| UC-31 Đo vùng bị chặn (SVI, WindowsApps, hồ sơ người khác) | DONE | Chạy thật bằng Administrator: System Volume Information đo được (24 KiB), 0 thư mục không truy cập được |
| UC-32 VSS, pagefile, hiberfil, swapfile, metafile NTFS thành dòng riêng | DONE | Chạy thật: VSS qua WMI trả về 0 B (D: không có shadow storage — số thật, không phải mặc định); metafile `$MFT` 1,82 GB, `$LogFile` 67 MB, `$Extend` 65 MB, `$Bitmap` 12,8 MB thành dòng riêng |
| UC-33 Buộc quét thường để đối chứng | PARTIAL | Cài đặt "Chế độ quét" + menu "Đối chứng hai chế độ" viết xong; chưa chạy (cần admin để có MFT) |
| UC-34 WinSxS: kích thước thật (đã trừ hard link) cạnh số Explorer | DONE | `C:\Windows\WinSxS`: 18,94 GB trên đĩa, 14.615 liên kết không tính thêm 1,54 GB; Chi tiết thư mục hiện "đếm kiểu Explorer = X, thật = Y" |

## Dòng lệnh

| Use Case | Status | Ghi chú |
|---|---|---|
| UC-40 `fsp scan` (bảng / `--json` / `--csv`) | DONE | Cùng lõi với GUI |
| UC-41 `--top --depth --on-disk --exclude --no-ads --si` | DONE | Chạy thật `--exclude`, `--no-ads`, `--si`, `--depth`, `--top`; thêm `--fast`, `--exact`, `--threads`, `--partial` |
| UC-42 `fsp reconcile`, `fsp compare` | DONE | Cả hai chạy thật |
| UC-43 `--save` snapshot | DONE | Mở lại bằng `compare` |
| UC-44 Mã thoát 0/1/2/3/4 | DONE | 0, 1 (`C:\Windows\System32\config`), 2 (đường dẫn sai / `--out` lỗi / `.fsp` hỏng), 3 (Ctrl+C `--partial` thật), 4 (`--mft` không admin) đều đã chạy |

## Hệ thống

| Use Case | Status | Ghi chú |
|---|---|---|
| UC-50 Theo dõi trực tiếp | DONE | Dùng `ReadDirectoryChangesW` (đã sửa v0.2); tự cập nhật + phát hiện tràn |
| UC-51 Cắm/rút ổ → cập nhật danh sách | PARTIAL | Hook `WM_DEVICECHANGE` + debounce viết xong; chưa cắm/rút ổ thật |
| UC-52 Kiểm tra nhất quán sau mỗi lần quét | DONE | 5–6 phép kiểm in ở CLI/GUI; luôn ĐẠT trong mọi lần chạy |
| UC-53 Lưu / khôi phục phiên | PARTIAL | Marker + autosave + `LoadPartial` chạy thật (73.957 file khôi phục, 24.923 thư mục "chưa quét", nhất quán ĐẠT). Hộp thoại hỏi khôi phục khi mở app chưa thấy hiện thật |
| UC-54 Log chẩn đoán, xoay vòng | DONE | 10 file × 2 MB; "gom log" có nút |

## Anti-use cases (Không thể)

| Rule | Status | Ghi chú |
|---|---|---|
| KT-01 Không bỏ qua im lặng thư mục bị từ chối | ENFORCED | Cờ `AccessDenied`, banner, tab Vấn đề, kích thước "—" (không đoán); thử bằng `icacls /deny` thật |
| KT-02 Không xoá vĩnh viễn | ENFORCED | Một API xoá duy nhất (`RecycleBin.Delete`, không có tham số "permanent"); `grep` không còn `DeleteFile/RemoveDirectory`; thử thật → mục nằm trong Thùng rác; Thùng rác tắt / vượt dung lượng / đường dẫn > 259 → từ chối trước khi gọi |
| KT-03 Vị trí bảo vệ | ENFORCED | Ma trận 25 đường dẫn (gốc ổ, Windows, System32, Program Files, Users, hồ sơ, SVI, `$Recycle.Bin`, pagefile, `$MFT`, tên 8.3, `..`, `\\?\`, hoa/thường) — chặn đúng; `C:\Windows\Temp`, `Documents`, file thường: cho phép. Lớp kiểm tra thứ hai trong `RecycleBin.Delete` (thử `C:\Windows\System32` → `Refused`) |
| KT-04 Không sửa/đổi tên/di chuyển/nén/sao chép | ENFORCED | `grep` toàn `src`: chỉ còn ghi file của app (`%LOCALAPPDATA%`), file xuất do người dùng chọn (tmp+đổi tên), và `SHFileOperation` của Thùng rác |
| KT-05 Không làm file đám mây bị tải | ENFORCED | Chỉ mở `FILE_READ_ATTRIBUTES`; đo thật trên OneDrive: 0/105.172 mục đổi cờ |
| KT-06 Không tự nâng quyền | ENFORCED | Manifest `asInvoker` (App + CLI); `UacRelaunch` chỉ được gọi ở nút UC-20 (`grep`) |
| KT-07 Không hai phiên quét chồng nhau | ENFORCED | Quét khi đang có phiên → hộp hỏi (selftest: chọn "chờ" → vẫn phiên cũ) |
| KT-08 Mọi số đều có mốc thời gian | ENFORCED | `CompletedUtc` bắt buộc; hiện ở thanh trạng thái, CLI, HTML, JSON/CSV, banner snapshot |
| KT-09 Không xoá junction/mount point/symlink-thư mục | ENFORCED | Ma trận: junction `loop\real\back` bị chặn. Symlink thật chưa thử được (tạo symlink cần quyền) nhưng cùng nhánh mã (`ReparsePoint` + `Directory`) |
| KT-20 Admin vẫn bị chặn | ENFORCED | `ProtectionPolicy` không đọc cờ admin (`grep`) — chưa chạy ở terminal admin |
| KT-21 MFT chỉ cho NTFS | ENFORCED | `--mft` trên ổ FAT32 `G:` → quét thường kèm ghi chú KT-21, không lỗi |
| KT-22 Không đọc/lưu nội dung file | ENFORCED | Mọi handle `FILE_READ_ATTRIBUTES`; parser MFT chỉ giữ độ dài dữ liệu thường trú. (Chưa kiểm `.fsp` không chứa nội dung bằng chuỗi mẫu — dữ liệu không bao giờ vào cấu trúc) |
| KT-23 Không driver/dịch vụ | ENFORCED | Không có mã cài; installer không có Run/Services/Scheduled Task (`grep`) |
| KT-30 CLI chỉ đọc | ENFORCED | CLI không gọi `RecycleBin`/`ProtectionPolicy`/`UacRelaunch` (`grep` = 0) |
| KT-31 CLI không bật UAC | ENFORCED | `--mft` không admin → exit 4, không hộp UAC |
| KT-40 Không gửi dữ liệu ra mạng | ENFORCED | Mã nguồn + assembly `FolderSizePro.Core` không tham chiếu `System.Net.Http/Sockets` (đã kiểm bằng reflection); `HtmlReport` chỉ dùng `WebUtility.HtmlEncode` |
| KT-41 Không chạy khi app đóng | ENFORCED | Không service/task/Run key |
| KT-42 Không tự xoá/sửa file người dùng | ENFORCED | Như KT-02/04 |
| KT-43 Không ghi vào ổ đang đo | ENFORCED | Dữ liệu app ở `%LOCALAPPDATA%`; export/snapshot vào vùng đang đo → hộp cảnh báo (vừa thêm) — hộp cảnh báo này chưa chạy thật |

## Error cases (Khi lỗi)

| Case | Status | Ghi chú |
|---|---|---|
| ER-01 Đường dẫn rỗng / sai / là file | DONE | Selftest + CLI (mã 2, stdout trống) |
| ER-02 Bị từ chối quyền | DONE | Quét tiếp, banner + danh sách + gợi ý Administrator |
| ER-03 Ổ bị rút / mất kết nối | PARTIAL | Ánh xạ NTSTATUS → cờ `Lost` + `Partial` viết xong; chưa rút ổ thật |
| ER-04 Vòng lặp junction | DONE | `loop\real\back` không đi theo, không treo |
| ER-05 Thay đổi khi quét | DONE | Ghi/xoá file liên tục khi quét: không crash, 11 mục `Changed`, nhất quán ĐẠT |
| ER-06 Ổ mạng treo (timeout) | PARTIAL | Watchdog + `CancelIoEx` viết xong; chưa có ổ mạng để thử |
| ER-07 Vượt ngưỡng RAM → Chế độ gọn | DONE | Xem bằng chứng |
| ER-08 "Chưa giải thích" > 1% → cảnh báo nổi | DONE | Ổ C: (không admin): chưa giải thích 44,98 GiB = 9,6% → banner vàng "vượt 1%" + dòng đỏ trong tab Đối chiếu (chụp ảnh thật) + CLI in ⚠; ổ D: 0,1% → không cảnh báo |
| ER-09 Xoá mục đã bị xoá ngoài app | DONE | Selftest: "Không còn tồn tại", gỡ khỏi cây |
| ER-10 Mục đang bị khoá | DONE | Khoá file thật → báo tên tiến trình (Restart Manager) |
| ER-11 Ổ không có Thùng rác | PARTIAL | `CheckEligibility` từ chối ổ không phải Fixed; chưa có ổ rời để thử |
| ER-12 `.fsp` hỏng / sai bản | DONE | Lật byte, cắt cụt, sai magic, version mới hơn, rỗng, không tồn tại: mỗi trường hợp một thông báo đúng nguyên nhân |
| ER-13 Xuất lỗi (không ghi được) | DONE | Xuất ra `Z:\…` → báo lỗi, không sót `.tmp` |
| ER-14 Từ chối UAC | PARTIAL | Bắt `ERROR_CANCELLED` viết xong; chưa bấm "No" thật |
| ER-15 Thoát đột ngột → khôi phục | PARTIAL | Xem UC-53 |
| ER-20 Không mở được ổ thô → quét thường + lý do | DONE | Không admin: Win32 5 → ghi chú + quét thường (đã chạy). Có admin: mở được và quét MFT (đã chạy). BitLocker/AV chặn chưa gặp |
| ER-21 Bản ghi MFT hỏng/mồ côi | DONE | Bản ghi tổng hợp có fixup hỏng → bị bỏ; ổ D: thật: 1.772.800 bản ghi, 0 hỏng, 0 mồ côi, hiện thành dòng riêng trong bảng đối chiếu |
| ER-22 VSS/pagefile/hiberfil lỗi → "không xác định" (không phải 0) | DONE | WMI lỗi trong phiên này → dòng hiện "không xác định" kèm lý do |
| ER-23 MFT ≠ thường → bảng khác biệt | PARTIAL | Menu "Đối chứng hai chế độ" viết xong; chưa chạy |
| ER-30 CLI đối số/đường dẫn sai | DONE | exit 2, stderr, stdout rỗng |
| ER-31 Ctrl+C | DONE | `--partial` → ghi kết quả dở dang, exit 3 (thật, bằng `GenerateConsoleCtrlEvent`) |
| ER-32 Không ghi được đầu ra | DONE | exit 2 + thông báo |
| ER-40 Tràn bộ đệm theo dõi | DONE | 60.000 file tạo nhanh → "mất theo dõi", banner + nút Quét lại |
| ER-41 Cài đặt/phiên hỏng | DONE | File JSON hỏng → `.bad` + mặc định + cảnh báo |
| ER-42 `%LOCALAPPDATA%` không ghi được | DONE | `Writable=false`, `Save()`/`Log` không ném lỗi, banner chế độ chỉ-bộ-nhớ |

## Ghost

| Code thừa | Ghi chú |
|---|---|
| `tools/FspProbe`, `tools/FspVerify`, `tools/make-icon.ps1`, `tools/verify-mft.ps1` | Công cụ kiểm chứng / dựng — không phải tính năng; không đóng gói |
| `--selftest`, `--screenshot`, `--size`, `--tab`, `--expand` trong `FolderSizePro.exe` + `SelfTest.cs` | Cờ phát triển để chạy thật ViewModel và chụp cửa sổ; không có use case. Giữ vì là cách kiểm chứng GUI |
| Đuôi/nhóm loại tệp `TypeCatalog` ~200 đuôi | Bảng dữ liệu của UC-07, không phải ghost |
| `ReconcileRow` "reserved" (TotalReserved) | Mở rộng của Q9 (nằm trong "Used"), không có dòng riêng trong use case |

## Việc nằm ngoài use-cases v0.2 mà code đã thêm (cần chấp nhận hoặc gỡ)

- Cột "Chế độ quét" trong Cài đặt (UC-33 cần nhưng use-cases không nêu rõ nơi chọn).
- Hộp cảnh báo ghi file xuất vào vùng đang đo (KT-43 nêu "nếu đường dẫn xuất nằm trong cây thì cảnh báo" ở plan, không có trong use-cases).
- Mở file `.fsp` bằng double-click (đăng ký trong installer).

## Tổng kết

- **DONE: 45 · PARTIAL: 15 · MISSING: 0 · BROKEN: 0**
- **ENFORCED: 19 · VIOLATED: 0**
- GHOST: 4 nhóm (đều là công cụ kiểm chứng/phát triển)
- Rủi ro lớn nhất còn lại: `ER-23` / `UC-33` (đối chứng hai chế độ trong giao diện) chưa chạy; các mục PARTIAL còn lại là thao tác giao diện / phần cứng chưa thử tay (treemap, Explorer, UAC, cắm rút ổ, ổ mạng, bộ cài).
