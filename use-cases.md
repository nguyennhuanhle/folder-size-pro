# Use Cases — Folder Size Pro

> Source of truth. Mọi code phải khớp file này. Cập nhật file này TRƯỚC khi thêm feature mới.
> Phiên bản **v0.2 (đã duyệt)** — 2026-10-03. v0.2 = v0.1 + đánh mã KT/ER + 6 làm rõ (UC-13, Q4, Q5, UC-50, KT-09, ghi chú OneDrive).

**Mục tiêu:** app Windows đo dung lượng từng ổ / thư mục / file **chính xác đến từng byte**, giải thích được mọi chênh lệch với Explorer, `chkdsk` và số "Used" của ổ đĩa. Chính xác quan trọng hơn đẹp, đẹp quan trọng hơn nhanh, nhưng nhanh (quét MFT) là điểm cộng lớn.

**Nền tảng:** Windows 10 1809+ / Windows 11, x64. Ổ NTFS là đối tượng chính; FAT32, exFAT, ReFS, ổ rời, ổ mạng (map / UNC) vẫn quét được ở chế độ thường.

---

## Quy ước đo (Q) — định nghĩa cố định, mọi use case bên dưới tuân theo

- **Q1 — Hai con số luôn đi song đôi:** *Kích thước* (logical — tổng độ dài dữ liệu của file) và *Kích thước trên đĩa* (allocated — dung lượng thật file chiếm: làm tròn cluster, trừ phần nén NTFS / sparse / CompactOS). Không bao giờ chỉ hiện một số rồi gọi chung là "size".
- **Q2 — Liên kết cứng (hard link):** một file thực (cùng volume serial + File ID) chỉ được cộng **một lần** vào tổng. Các đường dẫn khác trỏ cùng file được gắn nhãn "liên kết cứng" và không cộng thêm. Nhờ vậy `C:\Windows\WinSxS` và tổng cả ổ không bao giờ vượt quá số "Used" của ổ.
- **Q3 — Reparse point** (junction, symlink, mount point, và các loại khác): **không đi theo**. Hiện như một mục riêng, kích thước là của chính link, kèm đích trỏ tới. Nhờ vậy không đếm trùng và không bị vòng lặp.
- **Q4 — File đám mây (OneDrive Files On-Demand…):** file chưa tải về có kích thước trên đĩa = 0 và nhãn "chỉ trên đám mây". App chỉ đọc metadata, **không bao giờ** làm file bị tải xuống. Thư mục placeholder chưa nạp danh sách (`RECALL_ON_OPEN`) **không được liệt kê** (vì liệt kê sẽ kích hoạt tải): hiện là "chưa tải danh sách (đám mây)", kích thước không xác định.
- **Q5 — Alternate Data Stream (ADS):** mặc định được tính vào file chủ và liệt kê trong chi tiết. Bắt buộc để đúng với file CompactOS (dữ liệu nằm trong ADS `WofCompressedData`). Có công tắc "Quét nhanh (bỏ ADS, tin kích thước trong danh sách thư mục)" — mọi kết quả dùng công tắc này luôn mang nhãn **"kém chính xác"**.
- **Q6 — Đường dẫn:** hỗ trợ đường dẫn > 260 ký tự (`\\?\`), Unicode, tên đặc biệt (dấu cách/dấu chấm cuối, tên dành riêng như `CON`).
- **Q7 — Đơn vị:** mặc định nhị phân (KiB, MiB, GiB — 1024), đổi được sang SI (kB, MB, GB — 1000). **Số byte chính xác luôn xem được** (tooltip / chi tiết / xuất file).
- **Q8 — Ảnh chụp có mốc thời gian:** kết quả quét là bức ảnh tại thời điểm quét; luôn hiện giờ quét và không tự nhận là "số hiện tại".
- **Q9 — Đối chiếu ổ đĩa (reconcile):** sau mỗi lần quét cả ổ, app phải chia được số "Used" của ổ thành: *đã đo trong cây* + *không truy cập được* + *metadata/hệ thống đã biết* ($MFT, $LogFile, $Bitmap, System Volume Information / VSS, pagefile, hiberfil, swapfile, Thùng rác) + *chưa giải thích được*. Phần cuối cùng phải hiện rõ, không giấu, không làm tròn thành 0.
- **Q10 — Chỉ đọc:** app không ghi vào ổ đang đo (cache, snapshot, log đều lưu ở `%LOCALAPPDATA%\FolderSizePro` hoặc nơi người dùng chọn) và không đọc nội dung file — chỉ đọc metadata.

---

## Roles

- **Người dùng** — chạy app bình thường (không nâng quyền) bằng giao diện đồ hoạ.
- **Quản trị viên** — cùng người dùng nhưng đã chạy app với quyền Administrator (UAC); mở thêm quét MFT nhanh và vùng bị bảo vệ.
- **Dòng lệnh** — người / script gọi `fsp.exe` (CLI) không giao diện, dùng cùng lõi đo với GUI.
- **Hệ thống** — các tác vụ nền của chính app khi đang mở (theo dõi thay đổi, khôi phục phiên, kiểm tra nhất quán). Không có dịch vụ chạy khi app đã đóng.

---

## Người dùng

### Có thể
- **UC-01** Là người dùng, tôi có thể thấy danh sách ổ đĩa (cục bộ, rời, mạng) với tổng / đã dùng / trống và loại file system, để chọn nơi cần quét.
- **UC-02** Là người dùng, tôi có thể quét một ổ, một hoặc nhiều thư mục (chọn hộp thoại, gõ/dán đường dẫn, hoặc kéo thả vào cửa sổ), để biết dung lượng từng phần.
- **UC-03** Là người dùng, tôi có thể theo dõi tiến độ thật khi đang quét (số file, số thư mục, số byte đã đo, thư mục đang quét, tốc độ, thời gian đã chạy) và thấy cây kết quả hiện dần ngay, không phải đợi quét xong.
- **UC-04** Là người dùng, tôi có thể tạm dừng, tiếp tục, hoặc huỷ quét; kết quả dở dang được giữ lại và gắn nhãn "chưa hoàn tất".
- **UC-05** Là người dùng, tôi có thể xem cây thư mục, mỗi dòng có: Kích thước, Kích thước trên đĩa, % so với thư mục cha, số file, số thư mục con, ngày sửa gần nhất; sắp xếp theo cột và mở/thu gọn tuỳ ý.
- **UC-06** Là người dùng, tôi có thể xem Top N file lớn nhất và Top N thư mục lớn nhất trong toàn bộ vùng đã quét, để tìm "thủ phạm" nhanh.
- **UC-07** Là người dùng, tôi có thể xem phân bố theo loại file (đuôi / nhóm: video, ảnh, tài liệu, nén, mã nguồn, hệ thống…) bằng bảng và biểu đồ.
- **UC-08** Là người dùng, tôi có thể xem treemap tương tác (ô = file/thư mục, diện tích tỉ lệ kích thước), bấm vào để đi sâu, đồng bộ vị trí với cây.
- **UC-09** Là người dùng, tôi có thể tìm và lọc theo tên (có hỗ trợ ký tự đại diện / regex), đuôi, khoảng kích thước, ngày sửa/truy cập, thuộc tính (ẩn, hệ thống, nén, đám mây).
- **UC-10** Là người dùng, tôi có thể xem chi tiết một mục: đường dẫn đầy đủ, số byte chính xác (logical + allocated), kích thước cluster, thuộc tính, ngày tạo/sửa/truy cập, số liên kết cứng và các đường dẫn khác của file đó, các ADS, trạng thái nén/sparse/CompactOS/đám mây, đích của reparse point, chủ sở hữu.
- **UC-11** Là người dùng, tôi có thể xem bảng Đối chiếu ổ đĩa (Q9) và bấm vào từng phần để thấy chi tiết, để hiểu vì sao tổng không khớp Explorer.
- **UC-12** Là người dùng, tôi có thể mở mục trong Explorer (chọn sẵn file), sao chép đường dẫn, mở hộp thoại Properties của Windows.
- **UC-13** Là người dùng, tôi có thể đưa mục đã chọn vào Thùng rác, sau khi xem hộp xác nhận nêu hai con số: *giải phóng ngay* (= 0 nếu cùng ổ, vì Thùng rác nằm trên cùng ổ) và *sẽ giải phóng sau khi dọn Thùng rác* (đã trừ phần liên kết cứng còn tồn tại ở nơi khác), để dọn ổ mà không bị ảo số.
- **UC-14** Là người dùng, tôi có thể lưu kết quả quét thành file snapshot (`.fsp`) và mở lại offline bất cứ lúc nào.
- **UC-15** Là người dùng, tôi có thể so sánh hai snapshot (hoặc snapshot với lần quét mới) để thấy thư mục/file nào tăng, giảm, mới, mất — sắp theo mức thay đổi.
- **UC-16** Là người dùng, tôi có thể xuất kết quả ra CSV, JSON hoặc báo cáo HTML (kèm số byte chính xác, mốc thời gian quét, các cảnh báo).
- **UC-17** Là người dùng, tôi có thể quét lại riêng một nhánh (thư mục) mà không quét lại toàn bộ, tổng của các thư mục cha được cập nhật theo.
- **UC-18** Là người dùng, tôi có thể đặt cài đặt: đơn vị (Q7), ngôn ngữ (Tiếng Việt / English), giao diện sáng/tối/theo hệ thống, danh sách đường dẫn loại trừ, bật/tắt tính ADS, hiện/ẩn file hệ thống, thời gian chờ cho ổ mạng, ngưỡng bộ nhớ.
- **UC-19** Là người dùng, tôi có thể bấm chuột phải thư mục trong Explorer → "Quét bằng Folder Size Pro" (tuỳ chọn khi cài đặt).
- **UC-20** Là người dùng, tôi có thể bấm nút "Chạy với quyền Administrator" để app tự khởi động lại có nâng quyền (UAC) và mở lại đúng kết quả/đường dẫn đang xem.

### Không thể
- **KT-01** Là người dùng, tôi KHÔNG THỂ làm app bỏ qua im lặng một thư mục bị từ chối quyền — mục đó luôn hiện "Không truy cập được" kèm nguyên nhân; app không đoán số.
- **KT-02** Là người dùng, tôi KHÔNG THỂ xoá vĩnh viễn (bỏ qua Thùng rác) từ app — chỉ có "vào Thùng rác"; xoá vĩnh viễn làm ở Explorer. Lý do: công cụ đo không được là công cụ làm mất dữ liệu không cứu được.
- **KT-03** Là người dùng, tôi KHÔNG THỂ xoá các vị trí được bảo vệ: gốc ổ đĩa, `C:\Windows`, `Program Files`, `Program Files (x86)`, `ProgramData`, thư mục gốc `Users` và hồ sơ người dùng hiện tại, `System Volume Information`, `$Recycle.Bin`, các file hệ thống ($MFT, pagefile, hiberfil…) — nút xoá bị vô hiệu kèm lý do.
- **KT-04** Là người dùng, tôi KHÔNG THỂ sửa, đổi tên, di chuyển, nén hay sao chép file từ trong app — app chỉ đo (và đưa vào Thùng rác theo UC-13).
- **KT-05** Là người dùng, tôi KHÔNG THỂ làm app tải xuống file OneDrive/đám mây khi quét hay khi xem chi tiết — app không mở handle đọc dữ liệu (Q4, Q10).
- **KT-06** Là người dùng, tôi KHÔNG THỂ khiến app tự nâng quyền mà không hỏi — chỉ UC-20 và qua hộp UAC của Windows.
- **KT-07** Là người dùng, tôi KHÔNG THỂ chạy hai phiên quét có gốc chồng lên nhau cùng lúc — tránh đếm trùng và tranh I/O; app hỏi: huỷ phiên cũ hay chờ.
- **KT-08** Là người dùng, tôi KHÔNG THỂ thấy con số nào mà không biết nó đo lúc nào (Q8).
- **KT-09** Là người dùng, tôi KHÔNG THỂ xoá junction, mount point, symlink-thư mục từ app — rủi ro thao tác Thùng rác chạm nội dung của đích; xoá trong Explorer.

### Khi lỗi
- **ER-01** Là người dùng, khi tôi nhập đường dẫn không tồn tại / sai cú pháp / để trống, thì hệ thống báo lỗi ngay cạnh ô nhập ("Không tìm thấy đường dẫn…") và không bắt đầu quét.
- **ER-02** Là người dùng, khi quét gặp thư mục bị từ chối quyền, thì hệ thống ghi nhận, quét tiếp phần còn lại, cuối quét hiện "N mục không truy cập được" (bấm để xem danh sách + mã lỗi) và gợi ý chạy bằng quyền Administrator.
- **ER-03** Là người dùng, khi ổ/thư mục bị rút hoặc mất kết nối giữa chừng, thì hệ thống dừng nhánh đó, giữ kết quả đã có gắn nhãn "chưa hoàn tất", đánh dấu nhánh bị mất, và cho phép thử lại.
- **ER-04** Là người dùng, khi gặp vòng lặp junction/symlink, thì hệ thống không đi theo (Q3), không treo, hiển thị link như một mục riêng.
- **ER-05** Là người dùng, khi file/thư mục bị thay đổi hoặc xoá trong lúc đang quét, thì hệ thống không crash, ghi nhận "đã thay đổi khi quét", và gợi ý quét lại nhánh (UC-17).
- **ER-06** Là người dùng, khi quét ổ mạng bị treo quá thời gian chờ (UC-18), thì hệ thống đánh dấu nhánh lỗi "hết thời gian", không treo giao diện, cho thử lại.
- **ER-07** Là người dùng, khi bộ nhớ cho cây kết quả vượt ngưỡng cài đặt (ổ hàng chục triệu file), thì hệ thống cảnh báo rõ và chuyển sang chế độ gọn (chỉ giữ cây thư mục + top file lớn nhất), nêu rõ những gì không còn xem được; không crash, không tự cắt số liệu mà không báo.
- **ER-08** Là người dùng, khi phần "chưa giải thích được" ở bảng Đối chiếu vượt 1% số Used của ổ, thì hệ thống hiện cảnh báo nổi bật kèm các nguyên nhân thường gặp (chưa chạy Administrator, VSS, ổ đang thay đổi nhiều…), không ẩn đi.
- **ER-09** Là người dùng, khi tôi đưa vào Thùng rác một mục đã bị xoá ngoài app, thì hệ thống báo "Mục không còn tồn tại" và gỡ nó khỏi cây.
- **ER-10** Là người dùng, khi mục cần xoá đang bị khoá bởi tiến trình khác, thì hệ thống báo lỗi, nêu tên tiến trình đang giữ (nếu xác định được), không ép xoá.
- **ER-11** Là người dùng, khi ổ chứa mục không có Thùng rác (ổ mạng, một số ổ rời), thì hệ thống từ chối xoá và giải thích — không âm thầm xoá vĩnh viễn thay thế.
- **ER-12** Là người dùng, khi mở file `.fsp` hỏng / sai phiên bản / không đọc được, thì hệ thống báo nguyên nhân cụ thể (hỏng, phiên bản mới hơn…) và không crash.
- **ER-13** Là người dùng, khi xuất file mà đường dẫn không ghi được hoặc đĩa đầy, thì hệ thống báo lỗi, không để lại file dở dang (ghi file tạm rồi đổi tên khi xong).
- **ER-14** Là người dùng, khi từ chối hộp UAC ở UC-20, thì hệ thống giữ nguyên phiên hiện tại ở quyền thường và báo "Chưa nâng quyền".
- **ER-15** Là người dùng, khi app thoát đột ngột giữa lúc quét, thì lần mở sau hệ thống hỏi có khôi phục phiên / kết quả dở dang không.

---

## Quản trị viên

(Gồm mọi thứ của Người dùng, cộng thêm:)

### Có thể
- **UC-30** Là quản trị viên, tôi có thể quét ổ NTFS bằng chế độ MFT nhanh (đọc Master File Table trực tiếp) để quét cả ổ trong vài chục giây; kết quả phải khớp chế độ quét thường ở mọi con số trong Q1–Q5.
- **UC-31** Là quản trị viên, tôi có thể đo các vùng mà quyền thường bị chặn (System Volume Information, `WindowsApps`, hồ sơ của người dùng khác…) để bảng Đối chiếu gần về 0 "chưa giải thích".
- **UC-32** Là quản trị viên, tôi có thể xem dung lượng Shadow Copy (VSS) / điểm khôi phục, pagefile, hiberfil, swapfile, và kích thước các metafile NTFS ($MFT, $LogFile, $Bitmap…) như các dòng riêng trong bảng Đối chiếu.
- **UC-33** Là quản trị viên, tôi có thể chọn bắt buộc quét thường (không MFT) để đối chứng hai chế độ và thấy chênh lệch nếu có.
- **UC-34** Là quản trị viên, tôi có thể xem `WinSxS` với kích thước thật (đã trừ liên kết cứng theo Q2) bên cạnh con số Explorer thường báo, kèm giải thích.

### Không thể
- **KT-20** Là quản trị viên, tôi KHÔNG THỂ vượt các luật bảo vệ khi xoá — quyền cao không mở khoá danh sách vị trí được bảo vệ ở mục Người dùng.
- **KT-21** Là quản trị viên, tôi KHÔNG THỂ dùng chế độ MFT trên ổ không phải NTFS (FAT32, exFAT, ReFS, ổ mạng) — app tự dùng quét thường và nói rõ lý do.
- **KT-22** Là quản trị viên, tôi KHÔNG THỂ khiến app đọc hay lưu nội dung file qua đường truy cập thô — chỉ metadata (Q10).
- **KT-23** Là quản trị viên, tôi KHÔNG THỂ cài driver kernel hoặc dịch vụ nền qua app — app chỉ chạy như tiến trình người dùng.

### Khi lỗi
- **ER-20** Là quản trị viên, khi không mở được ổ ở chế độ thô (BitLocker đang khoá, phần mềm bảo mật chặn, ổ không hỗ trợ), thì hệ thống chuyển sang quét thường, báo lý do cụ thể.
- **ER-21** Là quản trị viên, khi một bản ghi MFT hỏng hoặc không dựng được đường dẫn, thì hệ thống tính nó vào "chưa giải thích được" kèm số lượng, không đoán, không bỏ qua im lặng.
- **ER-22** Là quản trị viên, khi truy vấn VSS/pagefile/hiberfil thất bại, thì dòng đó hiện "Không xác định" (không phải 0) kèm lỗi.
- **ER-23** Là quản trị viên, khi kết quả MFT và quét thường (UC-33) khác nhau, thì hệ thống hiện bảng chênh lệch theo từng nhánh để điều tra, không âm thầm chọn một bên.

---

## Dòng lệnh

### Có thể
- **UC-40** Là người dùng dòng lệnh, tôi có thể chạy `fsp scan <đường dẫn>` và nhận kết quả dạng bảng văn bản, `--json` hoặc `--csv` ra stdout/file, dùng chính lõi đo của GUI (cùng đường dẫn → cùng con số).
- **UC-41** Là người dùng dòng lệnh, tôi có thể dùng `--top N`, `--depth N`, `--on-disk`, `--exclude`, `--no-ads`, `--si` để điều chỉnh đầu ra.
- **UC-42** Là người dùng dòng lệnh, tôi có thể dùng `fsp reconcile <ổ>` để in bảng Đối chiếu (Q9) và `fsp compare a.fsp b.fsp` để so snapshot, phục vụ viết script / kiểm thử độ chính xác.
- **UC-43** Là người dùng dòng lệnh, tôi có thể lưu snapshot `.fsp` từ CLI để mở bằng GUI sau đó.
- **UC-44** Là người dùng dòng lệnh, tôi có thể dựa vào mã thoát: 0 = đầy đủ, 1 = hoàn tất nhưng có mục không truy cập được, 2 = đối số/đường dẫn sai, 3 = bị huỷ/dở dang, 4 = cần quyền Administrator.

### Không thể
- **KT-30** Là người dùng dòng lệnh, tôi KHÔNG THỂ xoá, sửa, di chuyển bằng CLI — CLI chỉ đọc.
- **KT-31** Là người dùng dòng lệnh, tôi KHÔNG THỂ làm CLI bật hộp UAC — CLI không tương tác; muốn quyền cao thì tự chạy shell Administrator, nếu thiếu quyền thì trả mã thoát 4.

### Khi lỗi
- **ER-30** Là người dùng dòng lệnh, khi đường dẫn sai hoặc đối số không hợp lệ, thì CLI in thông báo ra stderr, in cách dùng ngắn, thoát mã 2, không in kết quả một phần ra stdout.
- **ER-31** Là người dùng dòng lệnh, khi bấm Ctrl+C giữa quét, thì CLI dừng sạch, và nếu có `--partial` thì vẫn ghi kết quả dở dang (gắn nhãn) rồi thoát mã 3.
- **ER-32** Là người dùng dòng lệnh, khi không ghi được file đầu ra, thì CLI báo lỗi stderr, không để file dở dang.

---

## Hệ thống

### Có thể
- **UC-50** Là hệ thống, tôi có thể (khi người dùng bật "Theo dõi trực tiếp") nghe thay đổi bằng cơ chế theo dõi thay đổi của Windows (`ReadDirectoryChangesW`, mọi file system, không cần Administrator) để cập nhật cây từng phần mà không quét lại.
- **UC-51** Là hệ thống, tôi có thể phát hiện cắm/rút ổ rời, cập nhật danh sách ổ ở UC-01.
- **UC-52** Là hệ thống, tôi có thể sau mỗi lần quét cả ổ chạy kiểm tra nhất quán: tổng các con trong cây = tổng cha, không file nào bị đếm hai lần (Q2), bảng Đối chiếu cộng lại đúng số Used; kết quả kiểm tra ghi vào log và hiện ở UI nếu lệch.
- **UC-53** Là hệ thống, tôi có thể tự lưu phiên (cài đặt, cửa sổ, kết quả dở dang) để khôi phục sau khi thoát đột ngột.
- **UC-54** Là hệ thống, tôi có thể ghi log chẩn đoán cục bộ (xoay vòng, giới hạn dung lượng) để người dùng gửi khi báo lỗi — tuỳ người dùng quyết định gửi.

### Không thể
- **KT-40** Là hệ thống, tôi KHÔNG THỂ gửi bất kỳ dữ liệu nào ra internet (không telemetry, không kiểm tra cập nhật tự động, không tải gì về) — tên file/thư mục là thông tin riêng tư.
- **KT-41** Là hệ thống, tôi KHÔNG THỂ chạy khi app đã đóng — không dịch vụ nền, không tác vụ lập lịch.
- **KT-42** Là hệ thống, tôi KHÔNG THỂ tự xoá hay sửa file người dùng.
- **KT-43** Là hệ thống, tôi KHÔNG THỂ ghi vào ổ đang được đo (cache/log chỉ ở `%LOCALAPPDATA%\FolderSizePro` — nếu chính ổ đó là ổ hệ thống, app ghi chú rằng cache của app là một phần rất nhỏ trong số Used).

### Khi lỗi
- **ER-40** Là hệ thống, khi bộ đệm thay đổi bị tràn (buffer overflow) hoặc USN Journal bị tắt / bị cuộn qua (wrapped), thì tôi đánh dấu nhánh/ổ "mất theo dõi — cần quét lại", không im lặng giữ số cũ.
- **ER-41** Là hệ thống, khi cache/log/phiên bị hỏng, thì tôi bỏ file đó, tạo lại, báo ngắn gọn, không chặn khởi động app.
- **ER-42** Là hệ thống, khi thư mục `%LOCALAPPDATA%` không ghi được hoặc đầy, thì tôi vẫn cho quét và xem kết quả trong bộ nhớ, tắt các tính năng cần lưu và báo rõ.

---

## Hệ thống KHÔNG làm

- Không có tìm file trùng lặp bằng hash — cần đọc nội dung file (trái Q10), để phiên bản sau nếu cần.
- Không có "dọn dẹp tự động" (xoá temp, cache, registry, cookie…) — app đo, người dùng quyết định.
- Không xoá vĩnh viễn, không di chuyển/nén/sao chép file.
- Không đọc nội dung file, không quét virus, không phân tích SMART/sức khoẻ ổ đĩa.
- Không có lập lịch quét định kỳ, thông báo cảnh báo dung lượng, dịch vụ nền.
- Không có tài khoản, đồng bộ đám mây, telemetry, tự cập nhật.
- Không gọi API OneDrive/Google Drive — chỉ nhận diện file placeholder trên đĩa (Q4).
- Không quét máy từ xa bằng agent (chỉ ổ map / đường dẫn UNC).
- Không hỗ trợ macOS, Linux, Windows 8/7; không có bản 32-bit.
- Không hỗ trợ ổ Linux (ext4) hay ổ BitLocker đang khoá.
- Không có nhiều tab kết quả trong một cửa sổ (so sánh đã có qua snapshot, UC-15).
- Không có driver kernel / dịch vụ Windows riêng.
