# Quy trình chia sẻ HaiView lên GitHub

Tài liệu cho tác giả. Làm lần lượt từ trên xuống. Mỗi bước có ô đánh dấu để biết mình đang ở đâu.

## Bước 0. Kiểm tra trước khi công khai

Repo công khai thì ai cũng xem được, kể cả lịch sử commit. Kiểm tra kỹ một lần trước khi đẩy lên.

- [ ] Ảnh trong `docs/images/` chỉ dùng bộ slide mẫu `docs/demo/HaiView-demo.pdf`, không có slide thật của bệnh viện, không có tên người bệnh, không có đề thi thật.
- [ ] Không có file PPTX/PDF thật nào trong thư mục (lệnh `git status` chỉ liệt kê mã nguồn, tài liệu, ảnh mẫu).
- [ ] Chọn tên hiển thị: `LICENSE` đang ghi `Copyright (c) 2026 hoanghaiKSNK`. Muốn ghi tên thật thì sửa dòng này. Câu "made by a hospital infection control trainer" trong README cũng có thể sửa hoặc bỏ.
- [ ] Email trong commit: GitHub hiện email của người commit. Muốn giấu email thật, vào GitHub → Settings → Emails, bật "Keep my email addresses private" rồi dùng địa chỉ dạng `...@users.noreply.github.com` cho Git:
  ```bash
  git config user.email "<id>+hoanghaiKSNK@users.noreply.github.com"
  ```

## Bước 1. Tạo repo và đẩy mã lên

```bash
cd ~/Documents/Codex/haiview
git add -A
git commit -m "HaiView 0.3.0: first public release"
gh repo create hoanghaiKSNK/haiview --public --source . --push \
  --description "Show 3 slides at once, in any layout. Free PDF and PowerPoint presenter for Mac and Windows."
```

- [ ] Mở `https://github.com/hoanghaiKSNK/haiview`, kiểm tra README hiện đúng ảnh.
- [ ] Vào tab **Actions**: lần build đầu tiên phải xanh cả hai việc `macos` và `windows`.

## Bước 2. Tạo bản phát hành (Release)

Bản phát hành là nơi người dùng bấm tải file zip. Chỉ cần gắn tag phiên bản, GitHub Actions sẽ tự build cả bản Mac lẫn Windows và tạo trang Release.

```bash
git tag v0.3.0
git push origin v0.3.0
```

- [ ] Đợi khoảng 10 phút, vào tab **Releases**: có 3 file `HaiView-0.3.0-macOS.zip`, `...-Windows-x64.zip`, `...-Windows-arm64.zip`.
- [ ] Tải thử từng file về một máy khác, mở lần đầu theo hướng dẫn trong README.

Nếu Actions lỗi, vẫn có thể phát hành bằng file build sẵn trên máy:

```bash
gh release create v0.3.0 dist/*.zip --title "HaiView 0.3.0" --notes-file CHANGELOG.md
```

## Bước 3. Làm đẹp trang repo

Trên trang repo, bấm biểu tượng bánh răng cạnh "About":

- [ ] **Description:** `Show 3 slides at once, in any layout. Free PDF and PowerPoint presenter for Mac and Windows.`
- [ ] **Topics:** `presentation`, `pdf-viewer`, `powerpoint`, `teaching`, `education`, `macos`, `windows`, `swiftui`, `wpf`, `medical-education`
- [ ] **Social preview** (Settings → General → Social preview): tải ảnh `docs/images/hero-2top1.png` lên. Ảnh này hiện ra khi dán link vào Facebook, Zalo, Slack.
- [ ] Bật **Discussions** (Settings → Features) nếu muốn người dùng hỏi đáp mà không phải mở Issue.

## Bước 4. Giới thiệu cho người khác

Viết ngắn, nói vấn đề trước, kèm một ảnh và link tải. Hai mẫu bên dưới chỉnh lại theo giọng của bạn.

**Mẫu tiếng Việt (nhóm giảng viên, Facebook, Zalo):**

> Ai hay chiếu đề hoặc ca lâm sàng dài 3 slide chắc từng gặp cảnh học viên chỉ thấy được từng slide một. Mình viết một app nhỏ tên HaiView: chiếu 2, 3 hay 5 slide cùng lúc trên máy chiếu, tự kéo vùng nào to vùng nào nhỏ, bấm một phím là sang đề kế tiếp. Mở thẳng được file PowerPoint. Miễn phí, có bản Mac và Windows: https://github.com/hoanghaiKSNK/haiview

**Mẫu tiếng Anh (Reddit r/macapps, r/Teachers, r/MedicalEducation):**

> I teach with a lot of 3-slide quizzes (question, data, answer) and projectors only show one slide at a time. So I built HaiView, a free presenter that puts several PDF or PowerPoint pages on one screen in any layout: drag the dividers, split cells, flip the whole group with one key. Mac and Windows, open source: https://github.com/hoanghaiKSNK/haiview . Feedback welcome.

Nơi nên đăng: nhóm giảng viên y khoa và điều dưỡng, nhóm giáo viên, Reddit (`r/macapps`, `r/Teachers`, `r/MedicalEducation`), sau khi có vài người dùng thì thử Product Hunt.

## Bước 5. Nhận góp ý và ra bản mới

- Lỗi và đề xuất đến qua tab **Issues** (đã có mẫu sẵn). Trả lời trong vài ngày, gắn nhãn `bug` hoặc `enhancement`.
- Mỗi lần ra bản mới:
  1. Sửa số phiên bản ở `macos/Support/Info.plist` (`CFBundleShortVersionString`, tăng `CFBundleVersion`) và `windows/HaiView/HaiView.csproj` (`<Version>`).
  2. Thêm mục mới lên đầu `CHANGELOG.md`.
  3. Commit, rồi `git tag vX.Y.Z` và `git push origin vX.Y.Z`.
- Cách đánh số: sửa lỗi nhỏ tăng số cuối (0.3.0 → 0.3.1), thêm tính năng tăng số giữa (0.3 → 0.4), lên 1.0.0 khi đã ổn định và có người dùng thật.

## Bước 6. Nâng cấp sau (khi có người dùng)

| Việc | Lợi ích | Chi phí |
|---|---|---|
| Ký và notarize bản Mac (Apple Developer Program) | Người dùng mở thẳng, không còn cảnh báo | 99 USD/năm |
| Ký bản Windows (chứng chỉ ký mã, hoặc Azure Trusted Signing) | Hết cảnh báo SmartScreen | khoảng 10 USD/tháng trở lên |
| Homebrew cask (`brew install --cask haiview`) | Người dùng Mac cài bằng một lệnh | Miễn phí, cần app đã ký |
| winget (`winget install HaiView`) | Người dùng Windows cài bằng một lệnh | Miễn phí |
| Mac App Store / Microsoft Store | Dễ tìm, tự cập nhật | 99 USD/năm (Apple), 19 USD một lần (Microsoft); bản store khó giữ tính năng mở PPTX |

## Cấu trúc repo

```
haiview/
├── macos/            Bản Mac: SwiftUI + PDFKit, build bằng macos/build.sh
├── windows/          Bản Windows: C# WPF + Windows.Data.Pdf, build bằng windows/build.sh
├── docs/
│   ├── images/       Ảnh cho README (chỉ dùng slide mẫu)
│   ├── demo/         Bộ slide mẫu HaiView-demo.pdf
│   └── PHAT-HANH.vi.md   Tài liệu này
├── scripts/          Script tạo slide mẫu
├── .github/          GitHub Actions (build + release) và mẫu Issue
├── README.md         Giới thiệu tiếng Anh (trang chính)
├── README.vi.md      Giới thiệu tiếng Việt
├── CHANGELOG.md      Lịch sử phiên bản
├── CONTRIBUTING.md   Hướng dẫn góp ý, đóng góp
└── LICENSE           Giấy phép MIT
```
