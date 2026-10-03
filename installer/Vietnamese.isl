; *** Inno Setup version 6.5.0+ Vietnamese messages ***
;
; Translated from Default.isl (English) for display-pilot.
;
; To download user-contributed translations of this file, go to:
;   https://jrsoftware.org/files/istrans/
;
; Note: When translating this text, do not add periods (.) to the end of
; messages that didn't have them already, because on those messages Inno
; Setup adds the periods automatically (appending a period would result in
; two periods being displayed).

[LangOptions]
; The following three entries are very important. Be sure to read and
; understand the '[LangOptions] section' topic in the help file.
LanguageName=Tiếng Việt
LanguageID=$042A
; LanguageCodePage should always be set if possible, even if this file is Unicode
; This file is Unicode (UTF-8 with BOM), so it is set to zero
LanguageCodePage=0
; If the language you are translating to requires special font faces or
; sizes, uncomment any of the following entries and change them accordingly.
;DialogFontName=
;DialogFontSize=9
;DialogFontBaseScaleWidth=7
;DialogFontBaseScaleHeight=15
;WelcomeFontName=Segoe UI
;WelcomeFontSize=14

[Messages]

; *** Application titles
SetupAppTitle=Cài đặt
SetupWindowTitle=Cài đặt - %1
UninstallAppTitle=Gỡ cài đặt
UninstallAppFullTitle=Gỡ cài đặt %1

; *** Misc. common
InformationTitle=Thông tin
ConfirmTitle=Xác nhận
ErrorTitle=Lỗi

; *** SetupLdr messages
SetupLdrStartupMessage=Trình cài đặt sẽ cài %1. Bạn có muốn tiếp tục không?
LdrCannotCreateTemp=Không thể tạo tệp tạm. Đã dừng cài đặt
LdrCannotExecTemp=Không thể chạy tệp trong thư mục tạm. Đã dừng cài đặt
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%nLỗi %2: %3
SetupFileMissing=Thiếu tệp %1 trong thư mục cài đặt. Vui lòng khắc phục sự cố hoặc tải bản mới của chương trình.
SetupFileCorrupt=Các tệp cài đặt bị hỏng. Vui lòng tải bản mới của chương trình.
SetupFileCorruptOrWrongVer=Các tệp cài đặt bị hỏng hoặc không tương thích với phiên bản trình cài đặt này. Vui lòng khắc phục sự cố hoặc tải bản mới của chương trình.
InvalidParameter=Dòng lệnh có một tham số không hợp lệ:%n%n%1
SetupAlreadyRunning=Trình cài đặt đang chạy.
WindowsVersionNotSupported=Chương trình này không hỗ trợ phiên bản Windows trên máy tính của bạn.
WindowsServicePackRequired=Chương trình này yêu cầu %1 Service Pack %2 trở lên.
NotOnThisPlatform=Chương trình này không chạy được trên %1.
OnlyOnThisPlatform=Chương trình này phải chạy trên %1.
OnlyOnTheseArchitectures=Chương trình này chỉ cài đặt được trên các phiên bản Windows dành cho những kiến trúc bộ xử lý sau:%n%n%1
WinVersionTooLowError=Chương trình này yêu cầu %1 phiên bản %2 trở lên.
WinVersionTooHighError=Không thể cài đặt chương trình này trên %1 phiên bản %2 trở lên.
AdminPrivilegesRequired=Bạn phải đăng nhập bằng tài khoản quản trị viên để cài đặt chương trình này.
PowerUserPrivilegesRequired=Bạn phải đăng nhập bằng tài khoản quản trị viên hoặc tài khoản thuộc nhóm Power Users để cài đặt chương trình này.
SetupAppRunningError=Trình cài đặt phát hiện %1 đang chạy.%n%nVui lòng đóng tất cả các cửa sổ của chương trình này, rồi bấm OK để tiếp tục hoặc Huỷ để thoát.
UninstallAppRunningError=Trình gỡ cài đặt phát hiện %1 đang chạy.%n%nVui lòng đóng tất cả các cửa sổ của chương trình này, rồi bấm OK để tiếp tục hoặc Huỷ để thoát.

; *** Startup questions
PrivilegesRequiredOverrideTitle=Chọn chế độ cài đặt
PrivilegesRequiredOverrideInstruction=Chọn chế độ cài đặt
PrivilegesRequiredOverrideText1=%1 có thể được cài cho mọi người dùng (cần quyền quản trị viên) hoặc chỉ cho riêng bạn.
PrivilegesRequiredOverrideText2=%1 có thể được cài chỉ cho riêng bạn hoặc cho mọi người dùng (cần quyền quản trị viên).
PrivilegesRequiredOverrideAllUsers=Cài đặt cho &mọi người dùng
PrivilegesRequiredOverrideAllUsersRecommended=Cài đặt cho &mọi người dùng (khuyến nghị)
PrivilegesRequiredOverrideCurrentUser=Chỉ cài đặt cho &riêng tôi
PrivilegesRequiredOverrideCurrentUserRecommended=Chỉ cài đặt cho &riêng tôi (khuyến nghị)

; *** Misc. errors
ErrorCreatingDir=Trình cài đặt không thể tạo thư mục "%1"
ErrorTooManyFilesInDir=Không thể tạo tệp trong thư mục "%1" vì thư mục này chứa quá nhiều tệp

; *** Setup common messages
ExitSetupTitle=Thoát trình cài đặt
ExitSetupMessage=Quá trình cài đặt chưa hoàn tất. Nếu thoát bây giờ, chương trình sẽ không được cài đặt.%n%nBạn có thể chạy lại trình cài đặt vào lúc khác để hoàn tất việc cài đặt.%n%nThoát trình cài đặt?
AboutSetupMenuItem=&Giới thiệu về trình cài đặt...
AboutSetupTitle=Giới thiệu về trình cài đặt
AboutSetupMessage=%1 phiên bản %2%n%3%n%nTrang chủ %1:%n%4
AboutSetupNote=
TranslatorNote=

; *** Buttons
ButtonBack=< &Quay lại
ButtonNext=&Tiếp >
ButtonInstall=&Cài đặt
ButtonOK=OK
ButtonCancel=Huỷ
ButtonYes=&Có
ButtonYesToAll=Có cho &tất cả
ButtonNo=&Không
ButtonNoToAll=Khô&ng cho tất cả
ButtonFinish=&Hoàn tất
ButtonBrowse=&Duyệt...
ButtonWizardBrowse=&Duyệt...
ButtonNewFolder=&Tạo thư mục mới

; *** "Select Language" dialog messages
SelectLanguageTitle=Chọn ngôn ngữ cài đặt
SelectLanguageLabel=Chọn ngôn ngữ dùng trong quá trình cài đặt.

; *** Common wizard text
ClickNext=Bấm Tiếp để tiếp tục hoặc Huỷ để thoát trình cài đặt.
BeveledLabel=
BrowseDialogTitle=Chọn thư mục
BrowseDialogLabel=Chọn một thư mục trong danh sách bên dưới, rồi bấm OK.
NewFolderName=Thư mục mới

; *** "Welcome" wizard page
WelcomeLabel1=Chào mừng bạn đến với Trình hướng dẫn cài đặt [name]
WelcomeLabel2=Trình cài đặt sẽ cài [name/ver] vào máy tính của bạn.%n%nBạn nên đóng tất cả các ứng dụng khác trước khi tiếp tục.

; *** "Password" wizard page
WizardPassword=Mật khẩu
PasswordLabel1=Bản cài đặt này được bảo vệ bằng mật khẩu.
PasswordLabel3=Vui lòng nhập mật khẩu, rồi bấm Tiếp để tiếp tục. Mật khẩu có phân biệt chữ hoa và chữ thường.
PasswordEditLabel=&Mật khẩu:
IncorrectPassword=Mật khẩu bạn nhập không đúng. Vui lòng thử lại.

; *** "License Agreement" wizard page
WizardLicense=Thoả thuận cấp phép
LicenseLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
LicenseLabel3=Vui lòng đọc Thoả thuận cấp phép sau. Bạn phải chấp nhận các điều khoản của thoả thuận này trước khi tiếp tục cài đặt.
LicenseAccepted=Tôi &chấp nhận thoả thuận
LicenseNotAccepted=Tôi &không chấp nhận thoả thuận

; *** "Information" wizard pages
WizardInfoBefore=Thông tin
InfoBeforeLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
InfoBeforeClickLabel=Khi bạn đã sẵn sàng tiếp tục cài đặt, hãy bấm Tiếp.
WizardInfoAfter=Thông tin
InfoAfterLabel=Vui lòng đọc thông tin quan trọng sau trước khi tiếp tục.
InfoAfterClickLabel=Khi bạn đã sẵn sàng tiếp tục cài đặt, hãy bấm Tiếp.

; *** "User Information" wizard page
WizardUserInfo=Thông tin người dùng
UserInfoDesc=Vui lòng nhập thông tin của bạn.
UserInfoName=Tên người &dùng:
UserInfoOrg=Tổ &chức:
UserInfoSerial=&Số sê-ri:
UserInfoNameRequired=Bạn phải nhập tên.

; *** "Select Destination Location" wizard page
WizardSelectDir=Chọn vị trí cài đặt
SelectDirDesc=Bạn muốn cài [name] vào đâu?
SelectDirLabel3=Trình cài đặt sẽ cài [name] vào thư mục sau.
SelectDirBrowseLabel=Để tiếp tục, hãy bấm Tiếp. Nếu muốn chọn thư mục khác, hãy bấm Duyệt.
DiskSpaceGBLabel=Cần ít nhất [gb] GB dung lượng ổ đĩa trống.
DiskSpaceMBLabel=Cần ít nhất [mb] MB dung lượng ổ đĩa trống.
CannotInstallToNetworkDrive=Trình cài đặt không thể cài vào ổ đĩa mạng.
CannotInstallToUNCPath=Trình cài đặt không thể cài vào đường dẫn UNC.
InvalidPath=Bạn phải nhập đường dẫn đầy đủ kèm ký tự ổ đĩa, ví dụ:%n%nC:\APP%n%nhoặc đường dẫn UNC có dạng:%n%n\\server\share
InvalidDrive=Ổ đĩa hoặc thư mục chia sẻ UNC bạn chọn không tồn tại hoặc không truy cập được. Vui lòng chọn vị trí khác.
DiskSpaceWarningTitle=Không đủ dung lượng ổ đĩa
DiskSpaceWarning=Trình cài đặt cần ít nhất %1 KB dung lượng trống để cài đặt, nhưng ổ đĩa đã chọn chỉ còn %2 KB.%n%nBạn vẫn muốn tiếp tục?
DirNameTooLong=Tên thư mục hoặc đường dẫn quá dài.
InvalidDirName=Tên thư mục không hợp lệ.
BadDirName32=Tên thư mục không được chứa bất kỳ ký tự nào sau đây:%n%n%1
DirExistsTitle=Thư mục đã tồn tại
DirExists=Thư mục:%n%n%1%n%nđã tồn tại. Bạn vẫn muốn cài vào thư mục đó?
DirDoesntExistTitle=Thư mục không tồn tại
DirDoesntExist=Thư mục:%n%n%1%n%nkhông tồn tại. Bạn có muốn tạo thư mục này không?

; *** "Select Components" wizard page
WizardSelectComponents=Chọn thành phần
SelectComponentsDesc=Bạn muốn cài những thành phần nào?
SelectComponentsLabel2=Chọn các thành phần bạn muốn cài; bỏ chọn các thành phần bạn không muốn cài. Bấm Tiếp khi bạn đã sẵn sàng tiếp tục.
FullInstallation=Cài đặt đầy đủ
; if possible don't translate 'Compact' as 'Minimal' (I mean 'Minimal' in your language)
CompactInstallation=Cài đặt gọn nhẹ
CustomInstallation=Cài đặt tuỳ chỉnh
NoUninstallWarningTitle=Thành phần đã có
NoUninstallWarning=Trình cài đặt phát hiện các thành phần sau đã được cài trên máy tính của bạn:%n%n%1%n%nViệc bỏ chọn các thành phần này sẽ không gỡ cài đặt chúng.%n%nBạn vẫn muốn tiếp tục?
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=Lựa chọn hiện tại cần ít nhất [gb] GB dung lượng ổ đĩa.
ComponentsDiskSpaceMBLabel=Lựa chọn hiện tại cần ít nhất [mb] MB dung lượng ổ đĩa.

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=Chọn tác vụ bổ sung
SelectTasksDesc=Bạn muốn thực hiện thêm những tác vụ nào?
SelectTasksLabel2=Chọn các tác vụ bổ sung bạn muốn trình cài đặt thực hiện trong khi cài [name], rồi bấm Tiếp.

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=Chọn thư mục trong menu Start
SelectStartMenuFolderDesc=Bạn muốn trình cài đặt đặt lối tắt của chương trình ở đâu?
SelectStartMenuFolderLabel3=Trình cài đặt sẽ tạo lối tắt của chương trình trong thư mục menu Start sau.
SelectStartMenuFolderBrowseLabel=Để tiếp tục, hãy bấm Tiếp. Nếu muốn chọn thư mục khác, hãy bấm Duyệt.
MustEnterGroupName=Bạn phải nhập tên thư mục.
GroupNameTooLong=Tên thư mục hoặc đường dẫn quá dài.
InvalidGroupName=Tên thư mục không hợp lệ.
BadGroupName=Tên thư mục không được chứa bất kỳ ký tự nào sau đây:%n%n%1
NoProgramGroupCheck2=&Không tạo thư mục trong menu Start

; *** "Ready to Install" wizard page
WizardReady=Sẵn sàng cài đặt
ReadyLabel1=Trình cài đặt đã sẵn sàng cài [name] vào máy tính của bạn.
ReadyLabel2a=Bấm Cài đặt để tiếp tục, hoặc bấm Quay lại nếu bạn muốn xem lại hay thay đổi thiết đặt.
ReadyLabel2b=Bấm Cài đặt để tiếp tục.
ReadyMemoUserInfo=Thông tin người dùng:
ReadyMemoDir=Vị trí cài đặt:
ReadyMemoType=Kiểu cài đặt:
ReadyMemoComponents=Các thành phần đã chọn:
ReadyMemoGroup=Thư mục trong menu Start:
ReadyMemoTasks=Tác vụ bổ sung:

; *** TDownloadWizardPage wizard page and DownloadTemporaryFile
DownloadingLabel2=Đang tải xuống các tệp...
ButtonStopDownload=&Dừng tải xuống
StopDownload=Bạn có chắc muốn dừng tải xuống không?
ErrorDownloadAborted=Đã huỷ tải xuống
ErrorDownloadFailed=Tải xuống thất bại: %1 %2
ErrorDownloadSizeFailed=Không lấy được kích thước: %1 %2
ErrorProgress=Tiến độ không hợp lệ: %1 trên %2
ErrorFileSize=Kích thước tệp không hợp lệ: cần %1, nhận được %2

; *** TExtractionWizardPage wizard page and ExtractArchive
ExtractingLabel=Đang giải nén các tệp...
ButtonStopExtraction=&Dừng giải nén
StopExtraction=Bạn có chắc muốn dừng giải nén không?
ErrorExtractionAborted=Đã huỷ giải nén
ErrorExtractionFailed=Giải nén thất bại: %1

; *** Archive extraction failure details
ArchiveIncorrectPassword=Mật khẩu không đúng
ArchiveIsCorrupted=Tệp nén bị hỏng
ArchiveUnsupportedFormat=Định dạng tệp nén không được hỗ trợ

; *** "Preparing to Install" wizard page
WizardPreparing=Đang chuẩn bị cài đặt
PreparingDesc=Trình cài đặt đang chuẩn bị cài [name] vào máy tính của bạn.
PreviousInstallNotCompleted=Việc cài đặt/gỡ bỏ một chương trình trước đó chưa hoàn tất. Bạn cần khởi động lại máy để hoàn tất việc đó.%n%nSau khi khởi động lại máy, hãy chạy lại trình cài đặt để hoàn tất việc cài [name].
CannotContinue=Trình cài đặt không thể tiếp tục. Vui lòng bấm Huỷ để thoát.
ApplicationsFound=Các ứng dụng sau đang dùng những tệp mà trình cài đặt cần cập nhật. Bạn nên cho phép trình cài đặt tự động đóng các ứng dụng này.
ApplicationsFound2=Các ứng dụng sau đang dùng những tệp mà trình cài đặt cần cập nhật. Bạn nên cho phép trình cài đặt tự động đóng các ứng dụng này. Sau khi cài đặt xong, trình cài đặt sẽ thử mở lại các ứng dụng đó.
CloseApplications=Tự động đóng &các ứng dụng
DontCloseApplications=&Không đóng các ứng dụng
ErrorCloseApplications=Trình cài đặt không thể tự động đóng tất cả các ứng dụng. Bạn nên đóng tất cả các ứng dụng đang dùng những tệp mà trình cài đặt cần cập nhật trước khi tiếp tục.
PrepareToInstallNeedsRestart=Trình cài đặt cần khởi động lại máy tính của bạn. Sau khi khởi động lại máy, hãy chạy lại trình cài đặt để hoàn tất việc cài [name].%n%nBạn có muốn khởi động lại ngay bây giờ không?

; *** "Installing" wizard page
WizardInstalling=Đang cài đặt
InstallingLabel=Vui lòng chờ trong khi trình cài đặt cài [name] vào máy tính của bạn.

; *** "Setup Completed" wizard page
FinishedHeadingLabel=Hoàn tất Trình hướng dẫn cài đặt [name]
FinishedLabelNoIcons=Trình cài đặt đã cài xong [name] vào máy tính của bạn.
FinishedLabel=Trình cài đặt đã cài xong [name] vào máy tính của bạn. Bạn có thể mở ứng dụng bằng các lối tắt đã được tạo.
ClickFinish=Bấm Hoàn tất để thoát trình cài đặt.
FinishedRestartLabel=Để hoàn tất việc cài [name], trình cài đặt cần khởi động lại máy tính của bạn. Bạn có muốn khởi động lại ngay bây giờ không?
FinishedRestartMessage=Để hoàn tất việc cài [name], trình cài đặt cần khởi động lại máy tính của bạn.%n%nBạn có muốn khởi động lại ngay bây giờ không?
ShowReadmeCheck=Có, tôi muốn xem tệp README
YesRadio=&Có, khởi động lại máy ngay bây giờ
NoRadio=&Không, tôi sẽ khởi động lại máy sau
; used for example as 'Run MyProg.exe'
RunEntryExec=Chạy %1
; used for example as 'View Readme.txt'
RunEntryShellExec=Xem %1

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=Trình cài đặt cần đĩa tiếp theo
SelectDiskLabel2=Vui lòng đưa Đĩa %1 vào rồi bấm OK.%n%nNếu các tệp trên đĩa này nằm trong một thư mục khác với thư mục hiển thị bên dưới, hãy nhập đường dẫn đúng hoặc bấm Duyệt.
PathLabel=Đường dẫ&n:
FileNotInDir2=Không tìm thấy tệp "%1" trong "%2". Vui lòng đưa đúng đĩa vào hoặc chọn thư mục khác.
SelectDirectoryLabel=Vui lòng chỉ định vị trí của đĩa tiếp theo.

; *** Installation phase messages
SetupAborted=Quá trình cài đặt chưa hoàn tất.%n%nVui lòng khắc phục sự cố rồi chạy lại trình cài đặt.
AbortRetryIgnoreSelectAction=Chọn thao tác
AbortRetryIgnoreRetry=&Thử lại
AbortRetryIgnoreIgnore=&Bỏ qua lỗi và tiếp tục
AbortRetryIgnoreCancel=Huỷ cài đặt
RetryCancelSelectAction=Chọn thao tác
RetryCancelRetry=&Thử lại
RetryCancelCancel=Huỷ

; *** Installation status messages
StatusClosingApplications=Đang đóng các ứng dụng...
StatusCreateDirs=Đang tạo thư mục...
StatusExtractFiles=Đang giải nén các tệp...
StatusDownloadFiles=Đang tải xuống các tệp...
StatusCreateIcons=Đang tạo lối tắt...
StatusCreateIniEntries=Đang tạo mục INI...
StatusCreateRegistryEntries=Đang tạo mục registry...
StatusRegisterFiles=Đang đăng ký các tệp...
StatusSavingUninstall=Đang lưu thông tin gỡ cài đặt...
StatusRunProgram=Đang hoàn tất cài đặt...
StatusRestartingApplications=Đang mở lại các ứng dụng...
StatusRollback=Đang hoàn tác các thay đổi...

; *** Misc. errors
ErrorInternal2=Lỗi nội bộ: %1
ErrorFunctionFailedNoCode=%1 thất bại
ErrorFunctionFailed=%1 thất bại; mã %2
ErrorFunctionFailedWithMessage=%1 thất bại; mã %2.%n%3
ErrorExecutingProgram=Không thể chạy tệp:%n%1

; *** Registry errors
ErrorRegOpenKey=Lỗi khi mở khoá registry:%n%1\%2
ErrorRegCreateKey=Lỗi khi tạo khoá registry:%n%1\%2
ErrorRegWriteKey=Lỗi khi ghi vào khoá registry:%n%1\%2

; *** INI errors
ErrorIniEntry=Lỗi khi tạo mục INI trong tệp "%1".

; *** File copying errors
FileAbortRetryIgnoreSkipNotRecommended=Bỏ &qua tệp này (không khuyến nghị)
FileAbortRetryIgnoreIgnoreNotRecommended=&Bỏ qua lỗi và tiếp tục (không khuyến nghị)
SourceIsCorrupted=Tệp nguồn bị hỏng
SourceDoesntExist=Tệp nguồn "%1" không tồn tại
SourceVerificationFailed=Xác minh tệp nguồn thất bại: %1
VerificationSignatureDoesntExist=Tệp chữ ký "%1" không tồn tại
VerificationSignatureInvalid=Tệp chữ ký "%1" không hợp lệ
VerificationKeyNotFound=Tệp chữ ký "%1" dùng một khoá không xác định
VerificationFileNameIncorrect=Tên tệp không đúng
VerificationFileTagIncorrect=Thẻ của tệp không đúng
VerificationFileSizeIncorrect=Kích thước tệp không đúng
VerificationFileHashIncorrect=Mã băm của tệp không đúng
ExistingFileReadOnly2=Không thể thay thế tệp hiện có vì tệp được đánh dấu chỉ đọc.
ExistingFileReadOnlyRetry=&Bỏ thuộc tính chỉ đọc rồi thử lại
ExistingFileReadOnlyKeepExisting=&Giữ tệp hiện có
ErrorReadingExistingDest=Đã xảy ra lỗi khi đọc tệp hiện có:
FileExistsSelectAction=Chọn thao tác
FileExists2=Tệp đã tồn tại.
FileExistsOverwriteExisting=&Thay thế tệp hiện có
FileExistsKeepExisting=&Giữ tệp hiện có
FileExistsOverwriteOrKeepAll=&Làm như vậy cho các xung đột tiếp theo
ExistingFileNewerSelectAction=Chọn thao tác
ExistingFileNewer2=Tệp hiện có mới hơn tệp mà trình cài đặt đang định cài.
ExistingFileNewerOverwriteExisting=&Thay thế tệp hiện có
ExistingFileNewerKeepExisting=&Giữ tệp hiện có (khuyến nghị)
ExistingFileNewerOverwriteOrKeepAll=&Làm như vậy cho các xung đột tiếp theo
ErrorChangingAttr=Đã xảy ra lỗi khi thay đổi thuộc tính của tệp hiện có:
ErrorCreatingTemp=Đã xảy ra lỗi khi tạo tệp trong thư mục đích:
ErrorReadingSource=Đã xảy ra lỗi khi đọc tệp nguồn:
ErrorCopying=Đã xảy ra lỗi khi sao chép tệp:
ErrorDownloading=Đã xảy ra lỗi khi tải xuống tệp:
ErrorExtracting=Đã xảy ra lỗi khi giải nén tệp nén:
ErrorReplacingExistingFile=Đã xảy ra lỗi khi thay thế tệp hiện có:
ErrorRestartReplace=RestartReplace thất bại:
ErrorRenamingTemp=Đã xảy ra lỗi khi đổi tên tệp trong thư mục đích:
ErrorRegisterServer=Không thể đăng ký DLL/OCX: %1
ErrorRegSvr32Failed=RegSvr32 thất bại với mã thoát %1
ErrorRegisterTypeLib=Không thể đăng ký thư viện kiểu: %1

; *** Uninstall display name markings
; used for example as 'My Program (32-bit)'
UninstallDisplayNameMark=%1 (%2)
; used for example as 'My Program (32-bit, All users)'
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarkAllUsers=Mọi người dùng
UninstallDisplayNameMarkCurrentUser=Người dùng hiện tại

; *** Post-installation errors
ErrorOpeningReadme=Đã xảy ra lỗi khi mở tệp README.
ErrorRestartingComputer=Trình cài đặt không thể khởi động lại máy tính. Vui lòng tự khởi động lại.

; *** Uninstaller messages
UninstallNotFound=Tệp "%1" không tồn tại. Không thể gỡ cài đặt.
UninstallOpenError=Không thể mở tệp "%1". Không thể gỡ cài đặt
UninstallUnsupportedVer=Tệp nhật ký gỡ cài đặt "%1" có định dạng mà phiên bản trình gỡ cài đặt này không nhận ra. Không thể gỡ cài đặt
UninstallUnknownEntry=Gặp một mục không xác định (%1) trong nhật ký gỡ cài đặt
ConfirmUninstall=Bạn có chắc muốn gỡ bỏ hoàn toàn %1 cùng tất cả các thành phần của chương trình không?
UninstallOnlyOnWin64=Bản cài đặt này chỉ có thể gỡ trên Windows 64-bit.
OnlyAdminCanUninstall=Chỉ người dùng có quyền quản trị viên mới gỡ được bản cài đặt này.
UninstallStatusLabel=Vui lòng chờ trong khi %1 được gỡ khỏi máy tính của bạn.
UninstalledAll=Đã gỡ thành công %1 khỏi máy tính của bạn.
UninstalledMost=Đã gỡ cài đặt %1 xong.%n%nMột số mục không gỡ được. Bạn có thể tự xoá các mục này.
UninstalledAndNeedsRestart=Để hoàn tất việc gỡ cài đặt %1, cần khởi động lại máy tính của bạn.%n%nBạn có muốn khởi động lại ngay bây giờ không?
UninstallDataCorrupted=Tệp "%1" bị hỏng. Không thể gỡ cài đặt

; *** Uninstallation phase messages
ConfirmDeleteSharedFileTitle=Xoá tệp dùng chung?
ConfirmDeleteSharedFile2=Hệ thống cho biết tệp dùng chung sau không còn được chương trình nào sử dụng. Bạn có muốn trình gỡ cài đặt xoá tệp dùng chung này không?%n%nNếu vẫn còn chương trình đang dùng tệp này mà tệp bị xoá, các chương trình đó có thể không hoạt động đúng. Nếu không chắc chắn, hãy chọn Không. Để lại tệp trên hệ thống sẽ không gây hại gì.
SharedFileNameLabel=Tên tệp:
SharedFileLocationLabel=Vị trí:
WizardUninstalling=Trạng thái gỡ cài đặt
StatusUninstalling=Đang gỡ cài đặt %1...

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=Đang cài đặt %1.
ShutdownBlockReasonUninstallingApp=Đang gỡ cài đặt %1.

; The custom messages below aren't used by Setup itself, but if you make
; use of them in your scripts, you'll want to translate them.

[CustomMessages]

NameAndVersion=%1 phiên bản %2
AdditionalIcons=Lối tắt bổ sung:
CreateDesktopIcon=Tạo lối tắt trên &màn hình nền
CreateQuickLaunchIcon=Tạo lối tắt trên thanh &Khởi động nhanh
ProgramOnTheWeb=%1 trên web
UninstallProgram=Gỡ cài đặt %1
LaunchProgram=Mở %1
AssocFileExtension=&Liên kết %1 với phần mở rộng tệp %2
AssocingFileExtension=Đang liên kết %1 với phần mở rộng tệp %2...
AutoStartProgramGroupDescription=Khởi động:
AutoStartProgram=Tự động khởi động %1
AddonHostProgramNotFound=Không tìm thấy %1 trong thư mục bạn đã chọn.%n%nBạn vẫn muốn tiếp tục?
