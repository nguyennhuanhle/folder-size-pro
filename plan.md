# Plan — Folder Size Pro (Phase 2.1)

> **Trạng thái 2026-10-03:** R0–R7 đã build xong. Kết quả kiểm chứng: xem `gap-analysis.md`. Chế độ MFT đã kiểm trên ổ D: và C: thật (người dùng chạy `tools/verify-mft.ps1` bằng Administrator, báo cáo trong `_fixtures/`).

> Dựa trên `use-cases.md` v0.1 (đã duyệt mặc định 2026-10-03). **Chờ bạn duyệt kế hoạch này trước khi viết code.**
> Mục 9 liệt kê 6 chỗ kế hoạch lệch / làm rõ so với use-cases v0.1 — cần bạn chấp nhận để mình sửa use-cases lên v0.2.

## 0. Khả thi?

**Làm được.** Mọi thứ chạy bằng API Windows công khai; không cần driver. Độ khó nằm ở **độ chính xác**, không ở giao diện. Các cạm bẫy thật của NTFS mà kế hoạch đã tính tới:

| Cạm bẫy | Hậu quả nếu bỏ qua | Cách xử lý trong kế hoạch |
|---|---|---|
| Kích thước trong *danh sách thư mục* (`FindFirstFile`) có thể **cũ** với file có hard link và file đang ghi | Sai số âm thầm | Chế độ quét thường mở từng file ở mức *chỉ đọc thuộc tính* và hỏi lại kích thước thật (mục 3.1) |
| Hard link (WinSxS, ổ có dedupe thủ công) | Tổng thư mục vượt Used của ổ | Quy tắc "chủ sở hữu = đường dẫn nhỏ nhất", làm **sau** khi quét → kết quả không phụ thuộc thứ tự luồng (mục 3.3) |
| File nén CompactOS (WOF) có `Allocated = 0` ở luồng chính; dữ liệu nằm trong ADS `WofCompressedData` | Thiếu hàng chục GB | Q5 (tính ADS) bật mặc định — đây là lý do kỹ thuật, không chỉ cho đủ |
| `$BadClus:$Bad`, `$UsnJrnl:$J` là sparse khổng lồ (logical ≈ cả ổ) | Tổng "Kích thước" ảo | Hai con số song đôi (Q1); metafile gom vào nút riêng `[NTFS metadata]` |
| Thư mục OneDrive chưa nạp (`RECALL_ON_OPEN`): chỉ cần liệt kê cũng kích hoạt tải | Vi phạm Q4 | **Không liệt kê** thư mục có cờ này; hiện "chưa tải danh sách (đám mây)" |
| Bản ghi MFT mở rộng (`$ATTRIBUTE_LIST`), ghi trễ của NTFS | Chế độ MFT lệch quét thường ở file phân mảnh / đang ghi | Parser xử lý đủ extension record; khác biệt do ghi trễ được nêu trong Q8 và bảng đối chiếu hai chế độ (ER-23) |
| Thùng rác cùng ổ **không giải phóng dung lượng** ngay | Hộp xác nhận nói dối | Hộp xác nhận tách 2 số: "giải phóng ngay" và "sau khi dọn Thùng rác" (mục 9, #1) |
| Xoá vào Thùng rác quá lớn / Thùng rác tắt → Windows **xoá vĩnh viễn** im lặng nếu tắt hộp thoại | Mất dữ liệu — vi phạm KT-02 | Kiểm tra điều kiện *trước khi gọi* (NukeOnDelete, dung lượng tối đa, loại ổ); không đủ điều kiện = từ chối (mục 4, KT-02 & ER-11) |

**Hạn chế của phiên làm việc này:** Claude Code đang chạy **không có quyền Administrator**. Mình tự kiểm thử được toàn bộ chế độ quét thường, GUI, CLI, snapshot, xoá vào Thùng rác. **Chế độ MFT (R5) cần bạn chạy script kiểm chứng trong terminal Administrator** — mình viết sẵn, bạn dán kết quả lại cho mình đối chiếu.

## 1. Stack

- **C# / .NET 10**, solution `FolderSizePro.slnx`, theo đúng khuôn Display Pilot (cùng SDK 10.0.101 có sẵn trên máy).
- **WPF** (theme Fluent, `ThemeMode=System`) cho GUI. Không dùng thư viện biểu đồ — treemap và biểu đồ loại file tự vẽ bằng `DrawingContext` (nhẹ, tuỳ biến được màu/hover, không thêm phụ thuộc).
- **CsWin32** sinh P/Invoke (như Display Pilot) + vài khai báo `Nt*` tự viết (`NtQueryDirectoryFile`, `NtQueryInformationFile`, `NtCreateFile` không có trong Win32 metadata).
- **CommunityToolkit.Mvvm** cho ViewModel. **Brotli** (có sẵn trong .NET) cho file `.fsp`. Không phụ thuộc mạng.
- Ba exe chung một lõi: `FolderSizePro.exe` (WinExe), `fsp.exe` (Console), lõi `FolderSizePro.Core.dll`. Đóng gói: zip portable trước, installer Inno Setup sau (đã có Inno Setup 6 trên máy).

```text
folder-size-pro/
  FolderSizePro.slnx
  use-cases.md  plan.md  gap-analysis.md  README.md
  src/
    FolderSizePro.Core/        ← toàn bộ logic đo, không có UI
      Model/        Arena, NodeFlags, ScanResult, Snapshot types
      Native/       NativeMethods.txt, NtApi.cs, VolumeInfo.cs
      Scan/         DirectoryScanner (thường), MftScanner (admin), ScanOptions, Progress
      Analysis/     Rollup, HardLinkResolver, Top-N, TypeStats, Reconcile, Compare
      Safety/       ProtectionPolicy, RecycleEligibility, RecycleBinDeleter
      Watch/        ChangeWatcher (ReadDirectoryChangesW)
      Storage/      FspFile (đọc/ghi .fsp), Exporters (CSV/JSON/HTML), SettingsStore, SessionStore
      Text/         ByteFormatter (Q7), Resources vi/en
    FolderSizePro.Cli/         ← fsp.exe
    FolderSizePro.App/         ← WPF
  tools/
    FspVerify/                 ← chạy THẬT, in số THẬT, không assert (xem mục 6)
    verify-mft.ps1             ← bạn chạy trong terminal Administrator
  installer/  FolderSizePro.iss  Vietnamese.isl
  _fixtures/                   ← bộ file mẫu do FspVerify tạo (gitignore)
```

## 2. Mô hình dữ liệu

### 2.1 Kho nút (arena) — để cả ổ hàng chục triệu file vẫn vừa RAM

Cây lưu thành **mảng cột** (struct-of-arrays), mỗi nút ≈ 56 byte, không có object con trỏ:

```text
parent        int32      chỉ số nút cha
firstChild    int32      con đầu tiên
nextSibling   int32      anh em kế
nameOffset    int32      vị trí trong kho chuỗi UTF-16 (nối liền, loại trùng tên thư mục thường gặp)
nameLen       int16
flags         uint32     Dir, ReparsePoint(+loại), Cloud(OnlyCloud/Pinned), Compressed, Sparse, Wof,
                          Hidden, System, HardLinkNonOwner, AccessDenied, Changed, TimedOut, Lost,
                          Truncated(chế độ gọn), HasAds
logical       int64      kích thước (Q1)  — với thư mục: tổng cây con
allocated     int64      trên đĩa (Q1)    — với thư mục: tổng cây con + phần index của chính thư mục
ownLogical    int64      riêng nút (không tính con) — cần để cộng/trừ khi quét lại nhánh
ownAllocated  int64
fileCount     int32      (thư mục) số file trong cây con
dirCount      int32
mtime         int64      FILETIME: ngày sửa gần nhất trong cây con (thư mục) / của file
linkCount     int16      từ NumberOfLinks (chỉ có ở quét thường "Exact" và MFT)
```

- Chi tiết hiếm (ngày tạo/truy cập, chủ sở hữu, đích reparse, các ADS, các đường dẫn hard link khác) **không nằm trong arena** — lấy theo yêu cầu khi người dùng mở UC-10 (`GetFileInformationByHandleEx`, `FindFirstFileNameW`, `FindFirstStreamW`, `GetNamedSecurityInfo`). Tiết kiệm RAM, và luôn là số mới.
- Đỉnh 10 triệu nút ≈ 560 MB + kho tên ≈ 300 MB. Ngưỡng bộ nhớ cài đặt mặc định 2 GB; vượt → **Chế độ gọn** (ER-07): chỉ giữ cây thư mục + mỗi thư mục K=200 file lớn nhất, phần còn lại gộp thành nút ảo `<N file khác>` — **tổng vẫn đúng từng byte**, chỉ mất khả năng xem từng file nhỏ.

### 2.2 Hai quy tắc quyết định kết quả (dùng chung cho mọi chế độ quét)

- **Chủ sở hữu hard link** = đường dẫn nhỏ nhất theo so sánh `OrdinalIgnoreCase` toàn đường dẫn. Mọi chế độ dùng đúng một hàm `HardLinkResolver.Pick`.
- **Khoá nhận diện file** = (số seri ổ, FileId 128-bit). Với FAT/exFAT/mạng, không có hard link nên không dedupe.

### 2.3 Lưu trên đĩa (`%LOCALAPPDATA%\FolderSizePro\`)

```text
settings.json   { language, theme, unit(Binary|Si), includeAds, showHiddenSystem, excludedPaths[],
                  networkTimeoutSec, memoryLimitMb, topN, liveWatch, lastPaths[] }
session/        phiên dở dang để khôi phục (UC-53): scan-state.fsp + marker "đang quét"
logs/           xoay vòng, tối đa 10 file × 2 MB (UC-54)
```

### 2.4 Định dạng `.fsp`

`magic "FSPS" · version(uint16) · createdUtc · scanRoots[] · volume{serial, fs, clusterSize, total, free, used} · options · completeness(Complete|Partial) · warnings[] · [Brotli: arena cột + kho tên + bảng top-N + thống kê loại] · SHA-256 toàn file`. Ghi vào file tạm rồi `Move` (ER-13). Đọc: sai magic / SHA / version mới hơn → ER-12 với đúng nguyên nhân.

## 3. Thuật toán

### 3.1 Quét thường ("Exact") — mọi quyền, mọi file system

1. Mở thư mục bằng `NtCreateFile` (đường dẫn `\??\` hoặc `\\?\` → xử lý ≥260 ký tự, Q6), `FILE_LIST_DIRECTORY`, `FILE_OPEN_REPARSE_POINT`.
2. Liệt kê bằng `NtQueryDirectoryFile(FileIdExtdDirectoryInformation)` bộ đệm 256 KB → mỗi mục có tên, thuộc tính, FileId 128-bit, `ReparsePointTag`, EndOfFile, AllocationSize (**chỉ là giá trị tham khảo**).
3. Với **mỗi file thường**: mở tương đối thư mục cha bằng `NtCreateFile(FILE_READ_ATTRIBUTES, share all, OPEN_REPARSE_POINT)` — thao tác này **không đọc dữ liệu, không kích hoạt tải đám mây** (Q4, Q10) — rồi `NtQueryInformationFile`: `FileStandardInformation` (EndOfFile, AllocationSize, NumberOfLinks) + `FileStreamInformation` (ADS, nếu bật Q5). Đây là giá trị **được dùng**.
   - Nếu mở bị từ chối (file hệ thống như `pagefile.sys`, `$MFT`): dùng giá trị từ danh sách thư mục, gắn cờ "ước lượng từ danh sách" (hiện ở chi tiết, không phải ER).
   - Nếu mở thất bại vì file biến mất giữa chừng → ER-05.
4. **Reparse point**: thư mục-reparse (junction/symlink/mount point) → ghi nhận một nút, không đi vào (Q3). File-reparse thuộc nhóm đám mây (`IO_REPARSE_TAG_CLOUD_*`) → file bình thường có cờ Cloud; `WOF`, `APPEXECLINK`, `DEDUP` → file bình thường có cờ riêng. Thư mục có `RECALL_ON_OPEN`/`RECALL_ON_DATA_ACCESS` chưa nạp → **không liệt kê** (Q4).
5. **Song song**: hàng đợi công việc theo *thư mục* (work-stealing), số luồng I/O = 2 cho HDD (phát hiện bằng `IOCTL_STORAGE_QUERY_PROPERTY` → SeekPenalty), `min(16, 2×lõi)` cho SSD/NVMe, 4 cho ổ mạng. Mỗi thư mục do đúng một luồng liệt kê → không khoá trên đường nóng.
6. **Tiến độ trực tuyến** (UC-03): mỗi luồng cộng tổng của thư mục *đã xong* vào nút cha bằng `Interlocked`; giao diện đọc mỗi 250 ms. Số hiển thị khi đang quét là *tạm thời*; sau khi quét xong chạy một lượt gộp từ lá lên gốc (3.3) cho số chính thức.
7. **Tạm dừng / huỷ** (UC-04): `CancellationToken` + cổng tạm dừng kiểm tra giữa các thư mục. Hủy → kết quả giữ nguyên, cờ `Partial`.
8. **Loại trừ** (UC-18): so khớp đường dẫn tiền tố đã chuẩn hoá trước khi mở thư mục; nhánh bị loại trừ vẫn hiện một nút "đã loại trừ" (không biến mất âm thầm).

### 3.2 Quét MFT ("Fast", admin, chỉ NTFS)

1. Mở `\\.\X:` (read, share read/write). `FSCTL_GET_NTFS_VOLUME_DATA` → kích thước bản ghi, cluster, LCN của `$MFT`, tổng/ trống cluster (chính xác từng cluster).
2. Đọc bản ghi 0 (`$MFT`), parse runlist của `$DATA` → đọc tuần tự toàn bộ `$MFT` bằng I/O lớn (1–4 MB, `FILE_FLAG_SEQUENTIAL_SCAN`, 2 luồng đọc + N luồng parse).
3. Mỗi bản ghi: áp dụng fixup (update sequence array); đọc `$STANDARD_INFORMATION`, tất cả `$FILE_NAME` (mỗi cái = một hard link; bỏ tên DOS 8.3), `$DATA` (không tên = file; có tên = ADS; `Allocated` lấy từ header phi cư trú, đã tính nén/sparse; dữ liệu thường trú **chỉ lấy độ dài, bỏ nội dung ngay** — Q10), `$REPARSE_POINT`, `$INDEX_ALLOCATION`/`$INDEX_ROOT` (kích thước index của thư mục), `$ATTRIBUTE_LIST` (gom bản ghi mở rộng về bản ghi gốc qua trường base-record).
4. Dựng cây bằng (record, sequence); bản ghi mồ côi / trái sequence → đếm vào "chưa giải thích" (ER-21), không đoán.
5. Metafile `$MFT`, `$LogFile`, `$Bitmap`, `$BadClus`, `$UsnJrnl`, … gom dưới nút gốc ảo `[NTFS metadata]` (UC-32, Q9).
6. Dùng chung `HardLinkResolver`, `Rollup`, `TypeStats`, `Reconcile` với 3.1 → **cùng một định nghĩa số liệu**. Đây là cách giữ "khớp từng byte" giữa hai chế độ.

### 3.3 Gộp số, hard link, Top-N

- Sau khi quét: (a) gom mọi file có `linkCount > 1` theo khoá file, chọn chủ bằng `Pick`, đặt `ownLogical = ownAllocated = 0` + cờ `HardLinkNonOwner` cho các link còn lại; (b) gộp từ lá lên gốc (duyệt ngược chỉ số nút); (c) tính Top-N file / Top-N thư mục bằng heap giới hạn; (d) thống kê theo loại (đuôi → nhóm).

### 3.4 Đối chiếu ổ đĩa (Q9) — `Reconcile`

`Used` = (TotalClusters − FreeClusters) × cluster (NTFS, chính xác) hoặc `GetDiskFreeSpaceEx` (ổ khác). Bảng gồm các dòng: **Đã đo trong cây** · **Không truy cập được** (liệt kê, kèm phân bổ cho thư mục đã biết là SVI/VSS) · **NTFS metadata** · **pagefile/hiberfil/swapfile** · **Thùng rác** · **Shadow copy (VSS)** (admin: `Win32_ShadowStorage`; thường: "không xác định", không phải 0) · **Chưa giải thích = Used − tổng các dòng trên** (có thể âm khi ổ đang đổi nhiều → hiện đúng dấu, kèm ghi chú). Quét chế độ MFT cộng thêm: đếm bit trong `$Bitmap` để cross-check `Used`.

### 3.5 Theo dõi trực tiếp (UC-50) — `ChangeWatcher`

`ReadDirectoryChangesW` (đệ quy, một handle trên gốc). Gom sự kiện 500 ms → quét lại **đúng thư mục bị đổi** (không đệ quy trừ khi thư mục mới) bằng chính máy quét 3.1 → cộng/trừ chênh lệch lên các nút tổ tiên (nhờ `ownLogical/ownAllocated`). Tràn bộ đệm → đánh dấu nhánh `Lost` (ER-40).

### 3.6 Xoá vào Thùng rác (UC-13)

1. `ProtectionPolicy.Check(path)` (KT-03). 2. `RecycleEligibility.Check(selection)` → đọc `HKCU\…\BitBucket\Volume\{GUID}` (`NukeOnDelete`, `MaxCapacity`), loại ổ (cố định mới có), tổng dung lượng chọn ≤ giới hạn. 3. Tính "giải phóng": mỗi file chỉ tính nếu **mọi link** của nó nằm trong vùng chọn (so `linkCount` với số link tìm thấy trong vùng chọn); còn lại = 0 và ghi chú. 4. Hộp xác nhận hai con số: *giải phóng ngay* (= 0 nếu cùng ổ, vì Thùng rác nằm cùng ổ) và *giải phóng sau khi dọn Thùng rác*. 5. `IFileOperation` với `FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE`, **không** có `FOF_NOCONFIRMATION` phía "xoá vĩnh viễn". 6. Kiểm tra kết quả; cập nhật cây (trừ chênh lệch) hoặc ER-09/ER-10.

## 4. Mỗi "Không thể" = một quy tắc trong code

| Mã | Quy tắc | Nằm ở đâu | Cách ép buộc |
|---|---|---|---|
| KT-01 | Thư mục bị từ chối luôn hiện "Không truy cập được" + lý do, không đoán số | `DirectoryScanner` (bắt `STATUS_ACCESS_DENIED`) → cờ `AccessDenied` + danh sách lỗi; `Reconcile` đưa vào dòng riêng | Không có đường code nào ghi số cho nút `AccessDenied`; FspVerify có ca thư mục bị `icacls /deny` |
| KT-02 | Không xoá vĩnh viễn từ app | `RecycleBinDeleter` là **API xoá duy nhất** của Core; không có tham số "permanent"; `RecycleEligibility` chặn trước khi gọi | Review: grep không còn `DeleteFile/RemoveDirectory/FOF_NOCONFIRMATION` ngoài Core\Safety; thử thật ở FspVerify |
| KT-03 | Vị trí được bảo vệ không xoá được (kể cả cha của chúng) | `ProtectionPolicy` (chuẩn hoá đường dẫn: `\\?\`, 8.3, hoa/thường, dấu `\` cuối) — dùng cả ở nút xoá (disable + lý do) lẫn trong `RecycleBinDeleter` (kiểm tra lần hai) | Hai lớp; FspVerify thử `C:\`, `C:\Windows`, `C:\Users`, hồ sơ hiện tại, `System Volume Information`, `$Recycle.Bin`, `pagefile.sys` |
| KT-04 | App không sửa/đổi tên/di chuyển/nén/sao chép | Core không có hàm nào làm vậy; GUI không có lệnh | Review + grep API ghi |
| KT-05 | Không làm file đám mây bị tải xuống | Mở handle chỉ `FILE_READ_ATTRIBUTES`; không bao giờ `FILE_READ_DATA`; không liệt kê thư mục `RECALL_ON_OPEN` (Q4) | Test với thư mục OneDrive thật của bạn: trạng thái file trước/sau quét không đổi |
| KT-06 | Không tự nâng quyền | Chỉ `UacRelaunch` (nút UC-20) dùng `ShellExecute "runas"`; manifest `asInvoker` | Review; không có đường nâng quyền nào khác |
| KT-07 | Không hai phiên quét gốc chồng nhau | `ScanCoordinator` kiểm tra tiền tố đường dẫn giữa các phiên đang chạy → hộp hỏi "huỷ phiên cũ / chờ" | FspVerify + thử tay |
| KT-08 | Mọi số đều có mốc thời gian | `ScanResult.CompletedUtc` bắt buộc; mọi view, export, CLI in mốc | Trường bắt buộc (không nullable) |
| KT-20 | Admin vẫn bị `ProtectionPolicy` | Cùng lớp như KT-03 (không đọc cờ admin) | FspVerify chạy trong terminal Administrator |
| KT-21 | MFT chỉ cho NTFS | `ScanPlanner.Choose(volume, options)` → ổ khác NTFS thì ép 3.1 + `ScanNote` | FspVerify (ổ mạng/exFAT nếu có, hoặc mô phỏng bằng `VolumeInfo` giả) |
| KT-22 | Không đọc/lưu nội dung file | Parser MFT bỏ dữ liệu thường trú ngay sau lấy độ dài (`Span` không giữ tham chiếu); không ghi nội dung vào log/snapshot | Review + kiểm tra `.fsp` không chứa chuỗi mẫu từ file thử |
| KT-23 | Không cài driver/dịch vụ | Không có code nào làm vậy; installer chỉ chép file + shortcut + (tuỳ chọn) menu chuột phải + PATH | Review |
| KT-30 | CLI chỉ đọc | `fsp` không tham chiếu `Safety\` | Tham chiếu dự án kiểm bằng `dotnet list` |
| KT-31 | CLI không bật UAC | Không gọi `ShellExecute runas`; thiếu quyền → exit 4 | FspVerify chạy `fsp` thường với `--mft` → phải exit 4 |
| KT-40 | Không gửi dữ liệu ra mạng | Không tham chiếu `System.Net.*`; không có endpoint | Script `tools/audit-network.ps1` quét assembly; + chạy app khi tường lửa chặn |
| KT-41 | Không chạy khi app đóng | Không có service/Scheduled Task/Run key (trừ khi người dùng chọn "khởi động cùng Windows" — **không có** trong bản này) | Review installer |
| KT-42 | Không tự xoá/sửa file người dùng | Như KT-02/KT-04 | Như trên |
| KT-43 | Không ghi vào ổ đang đo | Mọi ghi đi qua `AppPaths.Local` (`%LOCALAPPDATA%`) hoặc đường dẫn người dùng chọn; nếu đường dẫn xuất nằm trong cây đang quét thì cảnh báo | FspVerify theo dõi |

## 5. Mỗi "Khi lỗi" = một bộ xử lý cụ thể

| Mã | Tình huống | Xử lý / nơi đặt |
|---|---|---|
| ER-01 | Đường dẫn rỗng / sai cú pháp / không tồn tại | `PathValidator` trước khi quét → lỗi inline cạnh ô nhập, không tạo phiên |
| ER-02 | Bị từ chối quyền | `DirectoryScanner` ghi `ScanIssue{path, ntstatus, kind=AccessDenied}`; tiếp tục; cuối quét hiện số lượng + danh sách + gợi ý Administrator |
| ER-03 | Ổ bị rút/mất | `STATUS_NO_SUCH_DEVICE/VOLUME_DISMOUNTED/…` → nhánh gắn `Lost`, kết quả `Partial`, nút "Thử lại nhánh" |
| ER-04 | Vòng lặp junction | Không đi theo (3.1 #4); ghi nút reparse có đích; không có bước nào đi theo link nên không thể lặp |
| ER-05 | File/thư mục thay đổi trong lúc quét | `STATUS_OBJECT_NAME_NOT_FOUND/SHARING_VIOLATION` khi mở → bỏ qua mục, cờ `Changed`, `ScanIssue{Changed}`; gợi ý UC-17 |
| ER-06 | Ổ mạng treo | Mỗi thư mục mạng có timeout (`networkTimeoutSec`) bằng I/O hủy được (`CancelIoEx`) → cờ `TimedOut` + "Thử lại" |
| ER-07 | Vượt ngưỡng RAM | `Arena` kiểm tra ngưỡng → chuyển Chế độ gọn (2.1), bật `Truncated`, banner nêu rõ cái gì mất |
| ER-08 | "Chưa giải thích" > 1% | `Reconcile` đặt `Severity=Warn` → banner nổi + nguyên nhân gợi ý theo dữ liệu (thiếu quyền? có VSS? ổ đang đổi?) |
| ER-09 | Xoá mục đã bị xoá ngoài app | `RecycleBinDeleter` gặp `NOT_FOUND` → "Mục không còn tồn tại", gỡ khỏi cây, trừ chênh lệch |
| ER-10 | Mục bị khoá | Bắt `SHARING_VIOLATION` → Restart Manager (`RmGetList`) lấy tên tiến trình giữ; không ép |
| ER-11 | Ổ không có Thùng rác | `RecycleEligibility` → từ chối với lý do; không có nhánh dự phòng "xoá thẳng" |
| ER-12 | `.fsp` hỏng/sai bản | `FspFile.Open` ném `FspFormatException{Reason}`; UI hiển thị lý do |
| ER-13 | Xuất file lỗi | Ghi `*.tmp` cùng thư mục đích → `Move` + `Flush`; lỗi thì xoá tmp, báo nguyên nhân |
| ER-14 | UAC bị từ chối | `UacRelaunch` bắt `ERROR_CANCELLED` → thông báo, giữ phiên |
| ER-15 | App thoát đột ngột | `SessionStore`: marker "đang quét" + autosave 60 s; mở lại thấy marker → hỏi khôi phục |
| ER-20 | Không mở được ổ thô | `MftScanner` bắt lỗi mở → `ScanPlanner` chuyển 3.1, `ScanNote` nêu nguyên nhân (BitLocker khoá / AV / quyền) |
| ER-21 | Bản ghi MFT hỏng/mồ côi | Đếm vào `unexplainedRecords`, hiện ở `Reconcile`, không bỏ im lặng |
| ER-22 | VSS/pagefile/hiberfil truy vấn lỗi | Dòng trả `Unknown(reason)` (kiểu riêng, không phải 0) |
| ER-23 | MFT ≠ quét thường | `ModeComparer` (UC-33): quét hai chế độ → bảng khác biệt theo nhánh; không chọn bên nào |
| ER-30 | CLI đối số/đường dẫn sai | `fsp` in stderr + usage ngắn, exit 2, không in gì ra stdout |
| ER-31 | Ctrl+C | `Console.CancelKeyPress` → huỷ sạch; `--partial` thì ghi kết quả dở dang; exit 3 |
| ER-32 | Không ghi được file đầu ra | Như ER-13 |
| ER-40 | Tràn bộ đệm theo dõi | `ChangeWatcher` đánh `Lost` cả cây dưới gốc theo dõi + banner "mất theo dõi — quét lại" |
| ER-41 | Cache/log/phiên hỏng | `SettingsStore/SessionStore` bắt `JsonException/IOException` → đổi tên file hỏng `.bad`, tạo mới |
| ER-42 | `%LOCALAPPDATA%` không ghi được | `AppPaths` thăm dò khi khởi động → chạy chế độ "chỉ bộ nhớ", tắt autosave/log, banner |

## 6. Cách kiểm chứng THẬT (không assert)

`tools/FspVerify` tạo bộ fixture trong `_fixtures\` rồi chạy máy quét **thật** và in **bảng số thật**, đặt cạnh nguồn độc lập để mắt bạn so:

| Ca | Nguồn đối chứng độc lập |
|---|---|
| File 0 B, 1 B, 4095, 4096, 4097 B | Tính tay: allocated = bội của cluster (4096) |
| Hard link: 2 link khác thư mục + 1 link ngoài vùng quét | `fsutil hardlink list`; tổng không đếm đôi |
| Junction vòng, symlink thư mục | Không treo; nút reparse hiện ra |
| Đường dẫn > 260 ký tự, tên có dấu cách cuối, `CON` | Tạo bằng `\\?\`; Explorer không thấy nhưng ta phải thấy |
| File nén NTFS (`compact /c`), sparse (`fsutil sparse`), ADS (`:stream`) | `compact`, `fsutil file queryallocranges` cộng extent × cluster |
| Thư mục `icacls /deny` | Phải hiện `AccessDenied`, không đoán |
| Ghi file liên tục trong lúc quét | Không crash, cờ `Changed` |
| 100.000 file nhỏ | Thời gian + RAM thật; so tổng với `Get-ChildItem -Recurse -Force` |
| Thư mục OneDrive thật của bạn (`%USERPROFILE%\OneDrive`) | Trạng thái tải của file trước/sau quét **không đổi** (KT-05) |
| Toàn ổ D: | `Reconcile` in bảng; so "Used" với Explorer và `fsutil volume diskfree` |
| MFT vs quét thường (bạn chạy `verify-mft.ps1` Administrator) | Hai bộ số cạnh nhau + bảng khác biệt |

Cuối mỗi vòng build: chạy FspVerify và dán nguyên văn kết quả cho bạn xem.

## 7. Chia vòng build (theo vai trò / luồng) — tự rà soát cuối mỗi vòng

| Vòng | Nội dung | Use case |
|---|---|---|
| **R0** | **Phòng thí nghiệm độ chính xác** (spike, ~nửa buổi): thử API thật trên fixture để *chốt* các câu hỏi mở: (a) `AllocationSize` của `FileStandardInformation` vs `FileCompressionInformation` vs extent thật cho nén/sparse/WOF; (b) chi phí mở handle mỗi file + ADS (nếu >3× so với chỉ đọc danh sách thì đưa ra phương án, mục 9 #2); (c) hành vi `RECALL_ON_OPEN` trên OneDrive của bạn. Kết quả ghi thành mục "Quyết định kỹ thuật" trong file này | nền cho UC-01…10 |
| **R1** | **Lõi + CLI + FspVerify**: arena, DirectoryScanner (3.1), hard link/rollup/top-N/loại/Reconcile, ProtectionPolicy, ByteFormatter, `fsp scan/reconcile`, PathValidator | UC-01…06, 09-lọc, 10-đọc, 11, 40, 41, 42(reconcile), 44; KT-01,04,05,08,30,31; ER-01…07 |
| **R2** | **GUI Người dùng**: cửa sổ chính, danh sách ổ, quét + tiến độ + tạm dừng/huỷ, cây, top-N, chi tiết, bảng Đối chiếu, loại file, tìm/lọc, mở Explorer, cài đặt, vi/en, sáng/tối, kéo thả, UAC relaunch | UC-01…12, 18, 20; KT-06,07; ER-08,14 |
| **R3** | **Trực quan**: treemap (squarified, drill-down, đồng bộ với cây) + biểu đồ loại file | UC-07, 08 |
| **R4** | **Snapshot + so sánh + xuất + quét lại nhánh + xoá vào Thùng rác**; `fsp compare`, xuất từ CLI | UC-13…17, 42(compare), 43; KT-02,03; ER-09…13, 32 |
| **R5** | **Quản trị viên — MFT**: MftScanner, VSS/pagefile/hiberfil/metafile, WinSxS, đối chứng hai chế độ, exit 4 | UC-30…34; KT-20…23; ER-20…23 |
| **R6** | **Hệ thống**: ChangeWatcher, cắm/rút ổ, khôi phục phiên, log, kiểm tra nhất quán tự động, nhận biết `%LOCALAPPDATA%` lỗi | UC-50…54; KT-40…43; ER-15, 40…42 |
| **R7** | **Đóng gói**: zip portable + installer Inno (vi), menu chuột phải Explorer (tuỳ chọn), `fsp` vào PATH (tuỳ chọn), README | UC-19, KT-23, KT-41 |

Sau R1, R2, R4, R5 mình chạy Phase 3 (gap analysis) trước khi sang vòng tiếp theo.

## 8. Những gì KHÔNG có trong kế hoạch (đúng "Hệ thống KHÔNG làm")

Duplicate finder, cleaner, lập lịch/dịch vụ nền, telemetry/tự cập nhật, đọc nội dung file, nhiều tab, driver, xoá vĩnh viễn, di chuyển/nén file.

## 9. Chỗ kế hoạch lệch / làm rõ so với use-cases v0.1 — cần bạn chấp nhận

1. **UC-13 — nói thật về "giải phóng":** chuyển file vào Thùng rác trên **cùng ổ** không giải phóng dung lượng cho tới khi dọn Thùng rác. Mình sửa câu UC-13 thành: hộp xác nhận hiển thị *"giải phóng ngay: 0 B (nằm trong Thùng rác); sẽ giải phóng X sau khi dọn Thùng rác"*, X đã trừ hard link còn lại ở nơi khác.
2. **Q5 (tính ADS) vs tốc độ:** để chính xác, chế độ quét thường phải mở handle từng file (3.1 #3). Tốc độ chậm hơn chỉ đọc danh sách thư mục (ước tính 2–4×; sẽ đo ở R0). Mình đề xuất giữ **bật mặc định**, thêm công tắc "Quét nhanh (bỏ ADS, tin kích thước trong danh sách thư mục)" có nhãn **"kém chính xác"** và hiện nhãn đó cạnh mọi kết quả dùng nó. Nếu bạn muốn đổi mặc định, nói mình.
3. **UC-50 — cơ chế theo dõi:** use-cases v0.1 ghi "USN Journal trên NTFS". Mình đề xuất bản 1.0 dùng `ReadDirectoryChangesW` cho mọi ổ: không cần Administrator, không cần bản đồ FileId→nút trong RAM, đủ cho việc cập nhật theo thư mục. USN Journal để sau. → sửa câu UC-50 thành "dùng cơ chế theo dõi thay đổi của Windows".
4. **Thêm KT mới (đề xuất): không xoá junction / mount point / symlink-thư mục** (rủi ro Thùng rác đụng nội dung đích). Người dùng xoá trong Explorer. Một dòng thêm vào mục Không thể của Người dùng, mã `KT-09`.
5. **Thư mục placeholder OneDrive chưa nạp:** mình sẽ **không liệt kê** (để không kích hoạt tải danh sách). Hiện nút `"chưa tải danh sách (đám mây)"` với kích thước = không xác định. Đây là hệ quả trực tiếp của Q4 + KT-05 — chỉ báo để bạn biết có thể có những thư mục OneDrive "rỗng" trong cây.
6. **Đánh số mã** `KT-xx` / `ER-xx` cho mọi "Không thể" / "Khi lỗi" trong `use-cases.md` (như bảng ở mục 4–5) để gap analysis ở Phase 3 tham chiếu từng dòng. Chỉ là đánh nhãn, không đổi nghĩa.

## 10. Câu hỏi cho bạn trước khi code

- Mục 9 (6 điểm) — chấp nhận hết, hay sửa điểm nào?
- Tên file thực thi: `FolderSizePro.exe` + `fsp.exe` — ổn chứ?
- Thứ tự R0→R7 có hợp lý không? Nếu bạn muốn thấy **giao diện sớm hơn** (R2 trước R4/R5) thì không cần đổi; nếu muốn **MFT sớm hơn** vì đó là điểm hấp dẫn nhất, mình đẩy R5 lên trước R3/R4.

---

## 11. Quyết định kỹ thuật chốt ở R0 (số đo thật, 2026-10-03, máy: NTFS, cluster 4096)

Công cụ: `tools/FspProbe`. Fixture: `_fixtures/spike`. "EXTENT" = cộng cluster thật theo `FSCTL_GET_RETRIEVAL_POINTERS` (nguồn độc lập).

| Câu hỏi | Kết quả đo | Quyết định |
|---|---|---|
| (a1) Giá trị nào là "kích thước trên đĩa"? | `FileStandardInformation.AllocationSize` (qua handle) **khớp EXTENT** ở mọi ca có cluster: file 4095/4096/4097 B, 1 MiB, hard link, đường dẫn dài, **nén NTFS** (225.280), **sparse** (100 MiB logical → 1.048.576), ADS `:big` (53.248). `CompressedFileSize` chỉ bằng EOF với file không nén → **không dùng**. | Dùng `AllocationSize` của Std. |
| (a2) File thường trú trong MFT (≤ ~700 B) | Std báo 8…704 byte (làm tròn 8) nhưng EXTENT = 0 cluster — dữ liệu nằm trong `$MFT` | **Quy ước:** trên NTFS, `0 < alloc < cluster` ⇒ thường trú: tính 0 vào "trên đĩa", gắn cờ `Resident`, cộng riêng `residentBytes`. Tránh đếm đôi với dòng `$MFT` ở bảng Đối chiếu (Q9). |
| (a3) Số trong danh sách thư mục có đáng tin? | **Không.** (i) Hard link: sau khi thêm 50.000 B qua link A, danh sách thư mục của link B vẫn báo 100.000/102.400 trong khi Std báo 150.000/151.552 (**cũ — xác nhận**). (ii) File nén WOF/CompactOS: danh sách báo alloc = **0**, Std báo 8.192 (khớp `compact`: "stored in 8,192 bytes"). | Chế độ mặc định = Exact (mở từng file lấy Std). Danh sách thư mục chỉ là phương án dự phòng khi bị từ chối. |
| (a4) ADS | `FileStreamInformation` liệt kê đúng `:big` (alloc 53.248 = EXTENT) và `:Zone.Identifier` (alloc 32, thường trú → 0 cluster). Std chỉ tính luồng chính ⇒ **phải cộng ADS**. Luồng `WofCompressedData` bị bộ lọc WOF ẩn khỏi cả `FileStreamInformation` lẫn `fsutil`; ở chế độ thường Std đã ảo hoá về kích thước nén (8.192), nên **không cộng đôi**. | Tổng file = luồng chính + Σ ADS (đã trừ thường trú). Chế độ MFT thấy luồng thật nên phải cộng `WofCompressedData` và đặt luồng chính = 0 để ra cùng số (kiểm ở R5). |
| (b) Chi phí mở handle từng file | Trên `D:\GitHub` (1.409.563 file, 182.722 thư mục, 1 luồng, nguội): chỉ liệt kê **41 s**; liệt kê + mở + Std **124 s** (**3,0×**, khớp ước lượng 2–4×). Chênh lệch tổng: logical 128.675.524.612 B vs trên đĩa 130.875.158.360 B (cluster + nén…). Chạy nhiều luồng sẽ giảm mạnh phần mở handle (I/O độc lập). | Giữ Exact mặc định, song song hoá (plan 3.1 #5). Công tắc "Quét nhanh" ở mục 9 #2 vẫn có, gắn nhãn *kém chính xác*. ADS đo ở R1 trên tập lớn. |
| (c) Đám mây (OneDrive thật, `%USERPROFILE%\OneDrive`) | 105.172 mục mang cờ `RECALL_*`. Sau khi mở bằng `FILE_READ_ATTRIBUTES` + hỏi Std + Stream cho **cả 105.172 mục**: số mục đổi cờ = **0**. | **KT-05 đạt** bằng thiết kế (chỉ mở thuộc tính). |
| Junction vòng | `loop\real\back` là `R(tag A0000003)` (mount point) → không đi theo, không lặp. | OK (Q3, ER-04). |
| Đường dẫn > 260 + tên có dấu cách cuối | Thấy đủ nhờ `\?\` + `\??\`. | OK (Q6). |
