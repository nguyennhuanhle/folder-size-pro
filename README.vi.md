# Folder Size Pro

[English](README.md)

Ứng dụng Windows đo dung lượng **chính xác từng byte** của ổ đĩa, thư mục và file — và **giải thích được** mọi chênh lệch với Explorer, `chkdsk` và số "Used" của ổ.

- Giao diện: `FolderSizePro.exe` (WPF, giao diện sáng/tối theo Windows, tiếng Việt / English)
- Dòng lệnh: `fsp.exe` (cùng lõi đo với giao diện → cùng đường dẫn cho cùng con số)
- Không telemetry, không tự cập nhật, không dịch vụ nền, không gửi gì ra mạng. Chỉ **đọc** metadata, không đọc nội dung file.

## Vì sao con số này đáng tin

Mọi số đều theo các **quy ước đo** cố định (xem `use-cases.md`, mục Q1–Q10):

| | |
|---|---|
| **Hai con số song đôi** | *Kích thước* (logical) và *Trên đĩa* (cluster thật: làm tròn cluster, trừ phần nén NTFS / sparse / CompactOS) |
| **Liên kết cứng** | một file thực chỉ tính **một lần** (chủ = đường dẫn nhỏ nhất) → WinSxS và tổng cả ổ không bao giờ vượt "Used" |
| **Reparse point** | junction / symlink / mount point **không đi theo** → không đếm trùng, không lặp vô hạn |
| **File đám mây** (OneDrive…) | chỉ đọc thuộc tính, **không bao giờ** làm file bị tải xuống; chưa tải = 0 trên đĩa |
| **ADS** | được cộng vào file chủ (bắt buộc để đúng với file CompactOS) |
| **File nhỏ thường trú trong MFT** | 0 cluster, tính riêng — không đếm đôi với `$MFT` |
| **Đường dẫn dài** (> 260), tên lạ | đo được (`\\?\`) |
| **Đối chiếu ổ đĩa** | chia "Used" thành *đã đo* + *NTFS metadata* + *pagefile…* + *Thùng rác* + *System Volume Information* + **chưa giải thích** — phần cuối không bị giấu |
| **Mốc thời gian** | mọi kết quả ghi rõ quét lúc nào |

Kích thước trong *danh sách thư mục* của Windows có thể **cũ** với hard link và bằng **0** với file CompactOS — nên chế độ mặc định mở từng file (chỉ mức thuộc tính) để lấy số thật. Chế độ "Quét nhanh" bỏ bước này và luôn mang nhãn **KÉM CHÍNH XÁC**.

## Chạy

- **Giao diện:** mở `FolderSizePro.exe` → chọn ổ ở bên trái, "Chọn thư mục…", gõ đường dẫn rồi Enter, hoặc kéo-thả thư mục vào cửa sổ.
- **Quyền Administrator** (nút "Chạy với quyền Admin", qua hộp UAC của Windows): đo thêm vùng bị chặn (`System Volume Information`…) và dùng **quét MFT** (cả ổ trong vài giây) cho ổ NTFS.
- **Dòng lệnh:**

```text
fsp scan D:\Data --depth 2 --top 20
fsp scan D:\ --json --out d.json --save d.fsp
fsp reconcile D:
fsp compare cu.fsp moi.fsp
```

Mã thoát: `0` đầy đủ · `1` có mục không truy cập được · `2` đối số/đường dẫn sai · `3` huỷ/dở dang · `4` cần Administrator.

## Tính năng

Cây thư mục · Treemap · Loại file · Top lớn nhất · Đối chiếu ổ · Tìm/lọc (tên, đuôi, cỡ, ngày, thuộc tính, regex) · Chi tiết (số byte chính xác, liên kết cứng, ADS, chủ sở hữu, đích của liên kết) · Quét lại một nhánh · Snapshot `.fsp` + **so sánh** hai snapshot · Xuất HTML / CSV / JSON · Theo dõi trực tiếp thay đổi · Đưa vào Thùng rác (hộp xác nhận nói thật: *giải phóng ngay 0 B*, *sau khi dọn Thùng rác X*, đã trừ hard link còn ở nơi khác).

## An toàn

- App **không xoá vĩnh viễn**. Chỉ "vào Thùng rác", và từ chối trước khi gọi nếu Thùng rác bị tắt, đầy, ổ không có Thùng rác, hoặc đường dẫn quá dài (khi đó Windows sẽ xoá thẳng).
- Chặn xoá: gốc ổ, `Windows` (cả vùng lõi System32/WinSxS…), `Program Files`, `ProgramData`, `Users`, hồ sơ hiện tại, `System Volume Information`, `$Recycle.Bin`, file hệ thống, junction/mount point/symlink-thư mục — kể cả khi chạy Administrator.
- File của app (cài đặt, log, phiên) chỉ ghi vào `%LOCALAPPDATA%\FolderSizePro`, không bao giờ vào ổ đang đo.

## Kiểm chứng

`tools\FspVerify` chạy máy quét **thật** trên bộ file mẫu (hard link, junction vòng, đường dẫn dài, nén, sparse, ADS, thư mục bị chặn quyền, file đổi liên tục…) và in số thật cạnh nguồn độc lập (`FSCTL_GET_RETRIEVAL_POINTERS`, `FindFirstStream`, PowerShell). Kết quả nằm trong `gap-analysis.md`.
Chế độ MFT cần Administrator: chạy `tools\verify-mft.ps1` trong PowerShell Administrator.

## Giới hạn đã biết

- Chế độ MFT đọc ảnh chụp `$MFT` trên đĩa; thay đổi mới nhất chưa được NTFS ghi xuống có thể chưa thấy.
- Thư mục đám mây chưa tải danh sách (`RECALL_ON_OPEN`) **không được liệt kê** (liệt kê sẽ kích hoạt tải) — hiện là "chưa tải danh sách".
- Menu chuột phải trên Windows 11 nằm trong "Show more options".
- ADS trên **thư mục** không được tính (hiếm gặp).
- Ngoài phạm vi: tìm file trùng, dọn rác tự động, lập lịch, đọc nội dung file, nhiều tab, driver/dịch vụ.

## Build

```text
dotnet build FolderSizePro.slnx -c Release
powershell scripts\publish.ps1          # zip portable
powershell scripts\build-installer.ps1  # bộ cài (cần Inno Setup 6)
```

## Tải về

Bộ cài và bản portable ở trang [Releases](https://github.com/nguyennhuanhle/folder-size-pro/releases). Bản build chưa ký số nên Windows SmartScreen có thể cảnh báo lần đầu chạy ("More info" → "Run anyway").

## Giấy phép

[MIT](LICENSE)
