using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static HaiView.Lang;

namespace HaiView
{
    public sealed class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var settings = Settings.Load();
            Lang.Init(settings.Language);
            var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new MainWindow(settings, args.Length > 0 ? args[0] : null);
            app.Run(window);
        }
    }

    public sealed class MainWindow : Window
    {
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xED));
        static readonly Brush StageBg = Brushes.Black;

        readonly Settings settings;
        LayoutNode tree;
        string layoutName;

        // Document state
        PdfSource? doc;
        string? sourcePath;
        int start;
        readonly Dictionary<int, int> deltas = new();
        int? selected;
        int? zoomed;
        bool editing;
        bool blackout;
        string typed = "";
        bool isFullScreen;
        WindowState prevState;
        Rect prevBounds;

        // UI
        readonly DockPanel root = new();
        readonly StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        readonly Border toolbarHost = new();
        readonly Canvas stage = new() { Background = StageBg, ClipToBounds = true };
        readonly Grid stageHost = new() { Background = StageBg };
        readonly ListBox thumbs = new() { Width = 190 };
        readonly Border thumbsHost = new() { Visibility = Visibility.Collapsed };
        readonly TextBlock rangeText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), MinWidth = 120, TextAlignment = TextAlignment.Center };
        readonly ToggleButton thumbsToggle = new();
        readonly ToggleButton editToggle = new();
        readonly Button layoutButton = new();
        readonly ComboBox flipBox = new() { Width = 130, Margin = new Thickness(4, 0, 4, 0), Focusable = false };
        readonly Popup layoutPopup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = true };
        readonly Border overlayHost = new() { IsHitTestVisible = false };
        readonly Grid overlays = new();
        readonly Border blackLayer = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
        readonly Border typedBadge = MakeBadge();
        readonly Border busyBadge = MakeBadge();
        readonly Border editBanner = new();
        readonly Grid emptyState = new();

        readonly List<CellView> cells = new();
        readonly Dictionary<string, Thumb> dividerThumbs = new();
        readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        int renderToken;

        public MainWindow(Settings s, string? openPath)
        {
            settings = s;
            tree = s.Tree ?? LayoutPreset.BuiltIn()[4].Tree;
            layoutName = s.LayoutName ?? LayoutPreset.BuiltIn()[4].Title;

            Title = "HaiView";
            var work = SystemParameters.WorkArea;
            Width = Math.Min(1280, work.Width * 0.92);
            Height = Math.Min(780, work.Height * 0.92);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = StageBg;
            AllowDrop = true;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/HaiView;component/HaiView.ico", UriKind.Absolute)); } catch { }

            BuildToolbar();
            BuildThumbs();
            BuildOverlays();

            DockPanel.SetDock(toolbarHost, Dock.Top);
            toolbarHost.Child = toolbar;
            toolbarHost.Background = SystemColors.ControlBrush;
            toolbarHost.BorderBrush = SystemColors.ActiveBorderBrush;
            toolbarHost.BorderThickness = new Thickness(0, 0, 0, 1);
            root.Children.Add(toolbarHost);
            DockPanel.SetDock(thumbsHost, Dock.Left);
            root.Children.Add(thumbsHost);
            stageHost.Children.Add(stage);
            stageHost.Children.Add(emptyState);
            stageHost.Children.Add(overlayHost);
            root.Children.Add(stageHost);
            Content = root;

            stage.SizeChanged += (_, _) => Relayout();
            renderTimer.Tick += (_, _) => { renderTimer.Stop(); RenderCells(); };
            PreviewKeyDown += OnKey;
            Drop += OnFileDrop;
            Closing += (_, _) => SaveSettings();

            Rebuild();
            if (openPath != null) Loaded += (_, _) => Open(openPath);
        }

        // ───────────────────────── Toolbar ─────────────────────────

        static Button ToolButton(string text, string tip, RoutedEventHandler onClick)
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(2, 0, 2, 0), Focusable = false };
            b.Click += onClick;
            return b;
        }

        void BuildToolbar()
        {
            toolbar.Children.Add(ToolButton(L("Open", "Mở"), L("Open a PDF or PowerPoint file (Ctrl+O)", "Mở file PDF hoặc PowerPoint (Ctrl+O)"), (_, _) => OpenDialog()));

            thumbsToggle.Content = L("Pages", "Trang");
            thumbsToggle.ToolTip = L("Page thumbnails - drag a page onto a cell (T)", "Danh sách trang thu nhỏ - kéo trang thả vào ô (T)");
            thumbsToggle.Padding = new Thickness(10, 4, 10, 4);
            thumbsToggle.Margin = new Thickness(2, 0, 12, 0);
            thumbsToggle.Focusable = false;
            thumbsToggle.Checked += (_, _) => SetThumbs(true);
            thumbsToggle.Unchecked += (_, _) => SetThumbs(false);
            toolbar.Children.Add(thumbsToggle);

            layoutButton.Padding = new Thickness(8, 4, 8, 4);
            layoutButton.Focusable = false;
            layoutButton.ToolTip = L("Choose a layout", "Chọn bố cục");
            layoutButton.Click += (_, _) => ShowLayoutPopup();
            layoutPopup.PlacementTarget = layoutButton;
            toolbar.Children.Add(layoutButton);

            editToggle.Content = L("Edit layout", "Sửa bố cục");
            editToggle.ToolTip = L("Edit layout: drag dividers, split or remove cells (E)", "Sửa bố cục: kéo đường chia, chia hoặc xoá ô (E)");
            editToggle.Padding = new Thickness(10, 4, 10, 4);
            editToggle.Margin = new Thickness(4, 0, 12, 0);
            editToggle.Focusable = false;
            editToggle.Checked += (_, _) => SetEditing(true);
            editToggle.Unchecked += (_, _) => SetEditing(false);
            toolbar.Children.Add(editToggle);

            toolbar.Children.Add(ToolButton("◀", L("Previous (←)", "Trang trước (←)"), (_, _) => Previous()));
            toolbar.Children.Add(rangeText);
            toolbar.Children.Add(ToolButton("▶", L("Next (→)", "Trang sau (→)"), (_, _) => Next()));

            flipBox.Items.Add(L("Flip whole group", "Lật cả nhóm"));
            flipBox.Items.Add(L("Flip 1 page", "Lật 1 trang"));
            flipBox.SelectedIndex = settings.StepByGroup ? 0 : 1;
            flipBox.ToolTip = L("Does → move the whole group or just one page?", "Mỗi lần bấm → thì nhảy cả nhóm hay chỉ 1 trang");
            flipBox.SelectionChanged += (_, _) => settings.StepByGroup = flipBox.SelectedIndex == 0;
            toolbar.Children.Add(flipBox);

            var present = ToolButton(L("Present ▾", "Chiếu ▾"), L("Choose the projector / TV screen", "Chọn màn hình máy chiếu / TV"), (s, _) => ShowPresentMenu((Button)s));
            toolbar.Children.Add(present);

            UpdateLayoutButton();
        }

        void UpdateLayoutButton()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(LayoutIcon(tree, 30, 18));
            sp.Children.Add(new TextBlock { Text = layoutName + "  ▾", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            layoutButton.Content = sp;
        }

        static FrameworkElement LayoutIcon(LayoutNode t, double w, double h)
        {
            var c = new Canvas { Width = w, Height = h };
            foreach (var leaf in t.Layout(new Rect(0, 0, w, h)).Leaves)
            {
                var r = leaf.Rect;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, r.Width - 2), Height = Math.Max(1, r.Height - 2),
                    RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0x30, 0x30, 0x30)),
                };
                Canvas.SetLeft(rect, r.X + 1); Canvas.SetTop(rect, r.Y + 1);
                c.Children.Add(rect);
            }
            return c;
        }

        void ShowPresentMenu(Button anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            var header = new MenuItem { Header = L("Present full screen on", "Chiếu toàn màn hình lên"), IsEnabled = false };
            menu.Items.Add(header);
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                string name = $"{screen.DeviceName.Replace(@"\\.\", "")} ({b.Width}×{b.Height})" + (screen.Primary ? L(" - this PC", " - màn hình máy") : "");
                var item = new MenuItem { Header = name };
                var sc = screen;
                item.Click += (_, _) => PresentOn(sc);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var labels = new MenuItem { Header = L("Page numbers (H)", "Hiện số trang (H)"), IsCheckable = true, IsChecked = settings.ShowLabels };
            labels.Click += (_, _) => { settings.ShowLabels = !settings.ShowLabels; RefreshCells(); };
            menu.Items.Add(labels);
            var black = new MenuItem { Header = L("Black screen (B)", "Màn hình đen (B)") };
            black.Click += (_, _) => SetBlackout(!blackout);
            menu.Items.Add(black);
            var full = new MenuItem { Header = L("Full screen (F / F5)", "Toàn màn hình (F / F5)") };
            full.Click += (_, _) => ToggleFullScreen();
            menu.Items.Add(full);
            if (sourcePath != null)
            {
                var reload = new MenuItem { Header = L("Reload file (Ctrl+R)", "Tải lại file (Ctrl+R)") };
                reload.Click += (_, _) => Reload();
                menu.Items.Add(reload);
            }
            menu.Items.Add(new Separator());
            var langMenu = new MenuItem { Header = "Language / Ngôn ngữ" };
            foreach (var (label, code) in new[] { ("Automatic / Tự động", (string?)null), ("English", "en"), ("Tiếng Việt", "vi") })
            {
                var li = new MenuItem { Header = label };
                li.Click += (_, _) => Relaunch(code);
                langMenu.Items.Add(li);
            }
            menu.Items.Add(langMenu);
            menu.IsOpen = true;
        }

        void Relaunch(string? lang)
        {
            settings.Language = lang;
            SaveSettings();
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        // ───────────────────────── Layout popup ─────────────────────────

        Button LayoutTile(LayoutPreset p)
        {
            bool isCurrent = p.Tree.Signature() == tree.Signature();
            var sp = new StackPanel { Width = 96 };
            var icon = LayoutIcon(p.Tree, 64, 36);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = p.Title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), Height = 30 });
            var b = new Button
            {
                Content = sp, Margin = new Thickness(4), Padding = new Thickness(6), Focusable = false,
                Background = isCurrent ? new SolidColorBrush(Color.FromArgb(0x40, 0x2F, 0x80, 0xED)) : Brushes.White,
                BorderBrush = isCurrent ? Accent : Brushes.LightGray,
                BorderThickness = new Thickness(isCurrent ? 2 : 1),
            };
            b.Click += (_, _) => { ApplyLayout(p.Tree.Clone(), p.Title); layoutPopup.IsOpen = false; };
            return b;
        }

        void ShowLayoutPopup()
        {
            var panel = new StackPanel { Margin = new Thickness(14), Width = 560 };
            panel.Children.Add(new TextBlock { Text = L("Built-in layouts", "Bố cục có sẵn"), FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(4, 0, 0, 6) });
            var wrap = new WrapPanel();
            foreach (var p in LayoutPreset.BuiltIn()) wrap.Children.Add(LayoutTile(p));
            panel.Children.Add(wrap);

            var gridRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 0, 0) };
            gridRow.Children.Add(new TextBlock { Text = L("Custom grid:", "Lưới tự chọn:"), VerticalAlignment = VerticalAlignment.Center });
            var rows = new ComboBox { Width = 50, Margin = new Thickness(8, 0, 4, 0) };
            var cols = new ComboBox { Width = 50, Margin = new Thickness(8, 0, 4, 0) };
            for (int i = 1; i <= 4; i++) rows.Items.Add(i);
            for (int i = 1; i <= 5; i++) cols.Items.Add(i);
            rows.SelectedItem = settings.GridRows; cols.SelectedItem = settings.GridCols;
            gridRow.Children.Add(rows);
            gridRow.Children.Add(new TextBlock { Text = L("rows", "hàng"), VerticalAlignment = VerticalAlignment.Center });
            gridRow.Children.Add(cols);
            gridRow.Children.Add(new TextBlock { Text = L("columns", "cột"), VerticalAlignment = VerticalAlignment.Center });
            var useGrid = new Button { Content = L("Use this grid", "Dùng lưới này"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            useGrid.Click += (_, _) =>
            {
                settings.GridRows = (int)(rows.SelectedItem ?? 2);
                settings.GridCols = (int)(cols.SelectedItem ?? 2);
                ApplyLayout(LayoutNode.Grid(settings.GridRows, settings.GridCols), L("Grid", "Lưới") + $" {settings.GridRows} × {settings.GridCols}");
                layoutPopup.IsOpen = false;
            };
            gridRow.Children.Add(useGrid);
            panel.Children.Add(gridRow);

            if (settings.Saved.Count > 0)
            {
                panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 6) });
                panel.Children.Add(new TextBlock { Text = L("Your layouts (right-click to delete)", "Bố cục của bạn (chuột phải để xoá)"), FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(4, 0, 0, 6) });
                var savedWrap = new WrapPanel();
                foreach (var p in settings.Saved.ToList())
                {
                    var tile = LayoutTile(p);
                    var cm = new ContextMenu();
                    var del = new MenuItem { Header = L("Delete this layout", "Xoá bố cục này") };
                    del.Click += (_, _) => { settings.Saved.Remove(p); SaveSettings(); layoutPopup.IsOpen = false; };
                    cm.Items.Add(del);
                    tile.ContextMenu = cm;
                    savedWrap.Children.Add(tile);
                }
                panel.Children.Add(savedWrap);
            }

            panel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 8) });
            var edit = new Button { Content = L("Edit current layout (E)", "Sửa bố cục hiện tại (E)"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(4, 0, 0, 8) };
            edit.Click += (_, _) => { layoutPopup.IsOpen = false; SetEditing(true); };
            panel.Children.Add(edit);

            var saveRow = new DockPanel { Margin = new Thickness(4, 0, 0, 8) };
            var saveBtn = new Button { Content = L("Save current layout", "Lưu bố cục hiện tại"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(saveBtn, Dock.Right);
            var nameBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = L("Layout name, e.g. Quiz 3 slides", "Tên bố cục, ví dụ: Đề 3 slide") };
            void DoSave()
            {
                var name = nameBox.Text.Trim();
                if (name.Length == 0) return;
                settings.Saved.RemoveAll(x => x.Title == name);
                settings.Saved.Add(new LayoutPreset { Title = name, Tree = tree.Clone() });
                layoutName = name;
                UpdateLayoutButton();
                SaveSettings();
                layoutPopup.IsOpen = false;
            }
            saveBtn.Click += (_, _) => DoSave();
            nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) DoSave(); };
            saveRow.Children.Add(saveBtn);
            saveRow.Children.Add(nameBox);
            panel.Children.Add(saveRow);

            var gapRow = new DockPanel { Margin = new Thickness(4, 0, 0, 0) };
            var gapText = new TextBlock { Text = ((int)settings.Gap).ToString(), Width = 28, TextAlignment = TextAlignment.Right };
            DockPanel.SetDock(gapText, Dock.Right);
            var gapLabel = new TextBlock { Text = L("Gap between pages", "Khoảng cách giữa các trang"), Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(gapLabel, Dock.Left);
            var slider = new Slider { Minimum = 0, Maximum = 30, Value = settings.Gap };
            slider.ValueChanged += (_, e) => { settings.Gap = e.NewValue; gapText.Text = ((int)e.NewValue).ToString(); Relayout(); };
            gapRow.Children.Add(gapText);
            gapRow.Children.Add(gapLabel);
            gapRow.Children.Add(slider);
            panel.Children.Add(gapRow);

            layoutPopup.Child = new Border
            {
                Child = panel, Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
            };
            layoutPopup.IsOpen = true;
        }

        // ───────────────────────── Thumbnails ─────────────────────────

        Point dragStartPoint;

        void BuildThumbs()
        {
            thumbsHost.Child = thumbs;
            thumbsHost.BorderBrush = SystemColors.ActiveBorderBrush;
            thumbsHost.BorderThickness = new Thickness(0, 0, 1, 0);
            thumbs.Focusable = false;
            ScrollViewer.SetHorizontalScrollBarVisibility(thumbs, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(thumbs, true);
            thumbs.PreviewMouseLeftButtonDown += (_, e) => dragStartPoint = e.GetPosition(null);
            thumbs.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var d = e.GetPosition(null) - dragStartPoint;
                if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
                if (e.OriginalSource is DependencyObject src && FindItem(src) is ListBoxItem item && item.Tag is int page)
                    DragDrop.DoDragDrop(item, "page:" + page, DragDropEffects.Copy);
            };
        }

        static ListBoxItem? FindItem(DependencyObject? d)
        {
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            return d as ListBoxItem;
        }

        void FillThumbs()
        {
            thumbs.Items.Clear();
            if (doc == null) return;
            for (int i = 0; i < doc.PageCount; i++)
            {
                int page = i;
                var img = new Image { Width = 160, Height = 90, Stretch = Stretch.Uniform };
                var label = new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 };
                var sp = new StackPanel { Margin = new Thickness(4) };
                sp.Children.Add(new Border { Child = img, BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray });
                sp.Children.Add(label);
                var item = new ListBoxItem
                {
                    Content = sp, Tag = page, Focusable = false,
                    ToolTip = L("Click: jump the group here (or change only the selected cell) · Drag onto a cell: show this page there",
                                "Bấm: đưa nhóm tới trang này (nếu đang chọn một ô thì chỉ ô đó đổi) · Kéo thả vào một ô: ô đó hiện trang này"),
                };
                item.MouseLeftButtonUp += (_, _) =>
                {
                    if (selected is int s) AssignPage(page, s); else GoTo(page + 1);
                };
                bool loaded = false;
                item.Loaded += async (_, _) =>
                {
                    if (loaded || doc == null) return;
                    loaded = true;
                    img.Source = await doc.RenderAsync(page, 240);
                };
                thumbs.Items.Add(item);
            }
            HighlightThumbs();
        }

        void HighlightThumbs()
        {
            var shown = ShownPages();
            foreach (ListBoxItem item in thumbs.Items)
            {
                bool on = item.Tag is int p && shown.Contains(p);
                if (item.Content is StackPanel sp && sp.Children[0] is Border b)
                {
                    b.BorderBrush = on ? Accent : Brushes.LightGray;
                    b.BorderThickness = new Thickness(on ? 3 : 1);
                }
            }
            if (thumbsHost.Visibility == Visibility.Visible && start < thumbs.Items.Count)
                thumbs.ScrollIntoView(thumbs.Items[start]);
        }

        void SetThumbs(bool on)
        {
            thumbsToggle.IsChecked = on;
            thumbsHost.Visibility = on && doc != null ? Visibility.Visible : Visibility.Collapsed;
            if (on) HighlightThumbs();
        }

        // ───────────────────────── Overlays ─────────────────────────

        static Border MakeBadge() => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        void BuildOverlays()
        {
            overlayHost.Child = overlays;
            overlays.Children.Add(blackLayer);
            typedBadge.Child = new TextBlock { Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.SemiBold };
            overlays.Children.Add(typedBadge);
            busyBadge.Child = new TextBlock { Foreground = Brushes.White, FontSize = 18, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, TextAlignment = TextAlignment.Center };
            overlays.Children.Add(busyBadge);

            editBanner.Background = new SolidColorBrush(Color.FromArgb(0xEE, 0xF4, 0xF4, 0xF4));
            editBanner.CornerRadius = new CornerRadius(18);
            editBanner.Padding = new Thickness(16, 8, 16, 8);
            editBanner.Margin = new Thickness(0, 0, 0, 18);
            editBanner.HorizontalAlignment = HorizontalAlignment.Center;
            editBanner.VerticalAlignment = VerticalAlignment.Bottom;
            editBanner.Visibility = Visibility.Collapsed;
            var eb = new StackPanel { Orientation = Orientation.Horizontal };
            eb.Children.Add(new TextBlock { Text = L("Editing layout: drag the blue dividers to resize · use the buttons on each cell to split or remove it",
                                                    "Đang sửa bố cục: kéo đường chia màu xanh để đổi rộng hẹp · dùng nút trên từng ô để chia hoặc xoá"), VerticalAlignment = VerticalAlignment.Center });
            var done = new Button { Content = L("Done", "Xong"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 2, 12, 2), Focusable = false };
            done.Click += (_, _) => SetEditing(false);
            eb.Children.Add(done);
            editBanner.Child = eb;
            // The banner needs clicks, so it lives on the stage host rather than the hit-test-free overlay.
            stageHost.Children.Add(editBanner);

            var es = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            es.Children.Add(new TextBlock { Text = "▦", FontSize = 56, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            es.Children.Add(new TextBlock { Text = L("Drop a PDF or PowerPoint file here", "Kéo thả file PDF hoặc PowerPoint vào đây"), FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 14) });
            var openBtn = new Button { Content = L("Open file…", "Mở file…"), Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Center, Focusable = false };
            openBtn.Click += (_, _) => OpenDialog();
            es.Children.Add(openBtn);
            es.Children.Add(new TextBlock
            {
                Text = L("→ / Space / PageDown: next   ·   ←: previous   ·   type a number + Enter: go to page\nClick a cell to flip it on its own   ·   double-click: zoom a cell   ·   B: black screen   ·   F / F5: full screen\nE: edit layout (resize, split, remove cells)   ·   T: page list, drag a page onto a cell",
                         "→ / Space / PageDown: trang sau   ·   ←: trang trước   ·   gõ số + Enter: tới trang\nBấm một ô để lật riêng ô đó   ·   bấm đúp: phóng to ô   ·   B: màn hình đen   ·   F / F5: toàn màn hình\nE: sửa bố cục (kéo rộng hẹp, chia, xoá ô)   ·   T: danh sách trang, kéo trang thả vào ô"),
                Foreground = Brushes.Gray, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 18, 0, 0), FontSize = 13,
            });
            emptyState.Children.Add(es);
        }

        void ShowTyped()
        {
            ((TextBlock)typedBadge.Child).Text = L("Go to page ", "Đi tới trang ") + typed + "  ↵";
            typedBadge.Visibility = typed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void ShowBusy(string? msg)
        {
            ((TextBlock)busyBadge.Child).Text = msg ?? "";
            busyBadge.Visibility = msg != null ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetBlackout(bool on)
        {
            blackout = on;
            blackLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetEditing(bool on)
        {
            if (editing == on && editToggle.IsChecked == on) return;
            editing = on;
            editToggle.IsChecked = on;
            editBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
        }

        // ───────────────────────── Stage: cells and dividers ─────────────────────────

        int CellCount => tree.LeafCount;
        int PageCount => doc?.PageCount ?? 0;
        int PageIndex(int cell) => start + cell + (deltas.TryGetValue(cell, out var d) ? d : 0);

        HashSet<int> ShownPages() =>
            Enumerable.Range(0, CellCount).Select(PageIndex).Where(p => p >= 0 && p < PageCount).ToHashSet();

        /// <summary>Recreate cell and divider controls (after the tree structure changes).</summary>
        void Rebuild()
        {
            stage.Children.Clear();
            cells.Clear();
            dividerThumbs.Clear();
            emptyState.Visibility = doc == null && !editing ? Visibility.Visible : Visibility.Collapsed;
            if (doc == null && !editing) { UpdateStatus(); return; }

            var (leaves, dividers) = tree.Layout(new Rect(0, 0, 1, 1));
            foreach (var leaf in leaves)
            {
                var cv = new CellView(this, leaf.Index, leaf.Path);
                cells.Add(cv);
                stage.Children.Add(cv);
            }
            if (zoomed == null)
            {
                foreach (var d in dividers)
                {
                    var thumb = MakeDivider(d);
                    dividerThumbs[d.Id] = thumb;
                    stage.Children.Add(thumb);
                }
            }
            Relayout();
            RefreshCells();
        }

        /// <summary>Position existing controls (window resize, divider drag, gap change).</summary>
        void Relayout()
        {
            double g = settings.Gap;
            var area = new Rect(g / 2, g / 2, Math.Max(1, stage.ActualWidth - g), Math.Max(1, stage.ActualHeight - g));
            var (leaves, dividers) = tree.Layout(area);
            foreach (var cv in cells)
            {
                Rect r;
                if (zoomed is int z)
                {
                    cv.Visibility = cv.Index == z ? Visibility.Visible : Visibility.Collapsed;
                    r = area;
                }
                else
                {
                    cv.Visibility = Visibility.Visible;
                    r = leaves.First(l => l.Index == cv.Index).Rect;
                }
                r.Inflate(-g / 2, -g / 2);
                Canvas.SetLeft(cv, r.X); Canvas.SetTop(cv, r.Y);
                cv.Width = Math.Max(1, r.Width); cv.Height = Math.Max(1, r.Height);
            }
            const double thick = 14;
            foreach (var d in dividers)
            {
                if (!dividerThumbs.TryGetValue(d.Id, out var t)) continue;
                t.Tag = d;
                if (d.Horizontal)
                {
                    Canvas.SetLeft(t, d.Position - thick / 2); Canvas.SetTop(t, d.Parent.Y);
                    t.Width = thick; t.Height = d.Parent.Height;
                }
                else
                {
                    Canvas.SetLeft(t, d.Parent.X); Canvas.SetTop(t, d.Position - thick / 2);
                    t.Width = d.Parent.Width; t.Height = thick;
                }
            }
            ScheduleRender();
        }

        Thumb MakeDivider(LayoutNode.DividerInfo info)
        {
            var template = new ControlTemplate(typeof(Thumb));
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            line.SetValue(FrameworkElement.WidthProperty, info.Horizontal ? 3.0 : double.NaN);
            line.SetValue(FrameworkElement.HeightProperty, info.Horizontal ? double.NaN : 3.0);
            grid.AppendChild(line);
            template.VisualTree = grid;

            var t = new Thumb
            {
                Template = template, Tag = info,
                Cursor = info.Horizontal ? Cursors.SizeWE : Cursors.SizeNS,
                Background = editing ? Accent : Brushes.Transparent,
            };
            t.MouseEnter += (_, _) => t.Background = Accent;
            t.MouseLeave += (_, _) => { if (!editing && !t.IsDragging) t.Background = Brushes.Transparent; };

            List<double>? startRatios = null;
            double total = 0;
            t.DragStarted += (_, _) =>
            {
                var d = (LayoutNode.DividerInfo)t.Tag;
                startRatios = tree.NodeAt(d.Path).Ratios?.ToList();
                total = 0;
            };
            t.DragDelta += (_, e) =>
            {
                if (startRatios == null) return;
                var d = (LayoutNode.DividerInfo)t.Tag;
                total += d.Horizontal ? e.HorizontalChange : e.VerticalChange;
                double extent = d.Horizontal ? d.Parent.Width : d.Parent.Height;
                if (extent <= 0) return;
                double pair = startRatios[d.Index] + startRatios[d.Index + 1];
                const double minR = 0.08;
                double a = Math.Clamp(startRatios[d.Index] + total / extent, minR, pair - minR);
                var node = tree.NodeAt(d.Path);
                if (node.Ratios == null) return;
                node.Ratios[d.Index] = a;
                node.Ratios[d.Index + 1] = pair - a;
                MarkEdited();
                Relayout();
            };
            t.DragCompleted += (_, _) => { startRatios = null; if (!editing) t.Background = Brushes.Transparent; SaveSettings(); };
            return t;
        }

        void RefreshCells()
        {
            foreach (var cv in cells) cv.Refresh();
            UpdateStatus();
            ScheduleRender();
        }

        void ScheduleRender()
        {
            renderTimer.Stop();
            renderTimer.Start();
        }

        async void RenderCells()
        {
            if (doc == null) return;
            int token = ++renderToken;
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            foreach (var cv in cells.ToList())
            {
                if (cv.Visibility != Visibility.Visible) continue;
                int idx = PageIndex(cv.Index);
                if (idx < 0 || idx >= PageCount) { cv.SetImage(null); continue; }
                var size = doc.PageSize(idx);
                double aspect = size.Width / Math.Max(1, size.Height);
                double w = Math.Min(cv.Width, cv.Height * aspect) * dpi;
                var bmp = await doc.RenderAsync(idx, (int)w);
                if (token != renderToken) return;
                cv.SetImage(bmp);
            }
        }

        void UpdateStatus()
        {
            if (PageCount == 0) { rangeText.Text = ""; return; }
            var shown = ShownPages();
            if (shown.Count == 0) { rangeText.Text = $"- / {PageCount}"; }
            else
            {
                int lo = shown.Min(), hi = shown.Max();
                rangeText.Text = lo == hi ? L("Page", "Trang") + $" {lo + 1} / {PageCount}" : L("Pages", "Trang") + $" {lo + 1}-{hi + 1} / {PageCount}";
            }
            HighlightThumbs();
        }

        // ───────────────────────── Layout changes ─────────────────────────

        void ApplyLayout(LayoutNode t, string name)
        {
            tree = t;
            layoutName = name;
            ResetPanes();
            UpdateLayoutButton();
            SaveSettings();
            Rebuild();
        }

        void MarkEdited()
        {
            string mark = L(" (edited)", " (đã chỉnh)");
            if (!layoutName.EndsWith(mark)) { layoutName += mark; UpdateLayoutButton(); }
        }

        internal void SplitCell(int[] path, string axis)
        {
            if (CellCount >= 16) return;
            tree.SplitLeaf(path, axis);
            MarkEdited(); ResetPanes(); SaveSettings(); Rebuild();
        }

        internal void RemoveCell(int[] path)
        {
            if (CellCount <= 1) return;
            tree.RemoveLeaf(path);
            MarkEdited(); ResetPanes(); SaveSettings(); Rebuild();
        }

        void ResetPanes()
        {
            deltas.Clear();
            selected = null;
            zoomed = null;
        }

        // ───────────────────────── Navigation ─────────────────────────

        void Next(bool onePage = false)
        {
            if (PageCount == 0) return;
            if (selected is int s)
            {
                if (PageIndex(s) < PageCount - 1) deltas[s] = (deltas.TryGetValue(s, out var d) ? d : 0) + 1;
            }
            else
            {
                int step = settings.StepByGroup && !onePage ? CellCount : 1;
                if (start + step <= PageCount - 1) start += step;
            }
            RefreshCells();
        }

        void Previous(bool onePage = false)
        {
            if (PageCount == 0) return;
            if (selected is int s)
            {
                if (PageIndex(s) > 0) deltas[s] = (deltas.TryGetValue(s, out var d) ? d : 0) - 1;
            }
            else
            {
                int step = settings.StepByGroup && !onePage ? CellCount : 1;
                start = Math.Max(0, start - step);
            }
            RefreshCells();
        }

        void GoTo(int pageNumber)
        {
            if (PageCount == 0) return;
            int target = Math.Clamp(pageNumber, 1, PageCount) - 1;
            if (selected is int s) deltas[s] = target - start - s;
            else { start = target; deltas.Clear(); }
            RefreshCells();
        }

        void AssignPage(int page, int cell)
        {
            if (page < 0 || page >= PageCount) return;
            deltas[cell] = page - start - cell;
            RefreshCells();
        }

        internal void ToggleSelect(int cell)
        {
            selected = selected == cell ? null : cell;
            RefreshCells();
        }

        internal void ToggleZoom(int cell)
        {
            zoomed = zoomed == null ? cell : null;
            selected = null;
            Rebuild();
        }

        internal void ResetCells()
        {
            deltas.Clear();
            selected = null;
            RefreshCells();
        }

        internal void DropOnCell(int cell, IDataObject data)
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                Open(files[0]);
                return;
            }
            if (data.GetData(DataFormats.StringFormat) is string s && s.StartsWith("page:") && int.TryParse(s[5..], out int p))
                AssignPage(p, cell);
        }

        // ───────────────────────── Keyboard ─────────────────────────

        void OnKey(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (ctrl)
            {
                if (key == Key.O) { OpenDialog(); e.Handled = true; }
                else if (key == Key.R) { Reload(); e.Handled = true; }
                else if (key >= Key.D1 && key <= Key.D9)
                {
                    var presets = LayoutPreset.BuiltIn();
                    int i = key - Key.D1;
                    if (i < presets.Count) ApplyLayout(presets[i].Tree, presets[i].Title);
                    e.Handled = true;
                }
                return;
            }

            e.Handled = true;
            switch (key)
            {
                case Key.Right: case Key.Down: case Key.PageDown: case Key.Space:
                    Next(shift && selected == null); break;
                case Key.Left: case Key.Up: case Key.PageUp: case Key.Back:
                    Previous(shift && selected == null); break;
                case Key.Home: start = 0; deltas.Clear(); RefreshCells(); break;
                case Key.End: if (PageCount > 0) { start = Math.Max(0, PageCount - CellCount); deltas.Clear(); RefreshCells(); } break;
                case Key.Enter:
                    if (int.TryParse(typed, out int n)) GoTo(n);
                    typed = ""; ShowTyped(); break;
                case Key.Escape:
                    if (typed.Length > 0) { typed = ""; ShowTyped(); }
                    else if (blackout) SetBlackout(false);
                    else if (editing) SetEditing(false);
                    else if (zoomed != null) ToggleZoom(zoomed.Value);
                    else if (selected != null) { selected = null; RefreshCells(); }
                    else if (isFullScreen) ToggleFullScreen();
                    break;
                case Key.B: case Key.OemPeriod: SetBlackout(!blackout); break;
                case Key.F: case Key.F5: ToggleFullScreen(); break;
                case Key.H: settings.ShowLabels = !settings.ShowLabels; RefreshCells(); break;
                case Key.E: SetEditing(!editing); break;
                case Key.T: SetThumbs(thumbsHost.Visibility != Visibility.Visible); break;
                case Key.Z:
                    if (selected is int s) ToggleZoom(s); else if (zoomed is int z) ToggleZoom(z);
                    break;
                case Key.R: ResetCells(); break;
                default:
                    int digit = key >= Key.D0 && key <= Key.D9 ? key - Key.D0
                              : key >= Key.NumPad0 && key <= Key.NumPad9 ? key - Key.NumPad0 : -1;
                    if (digit >= 0) { typed += digit; ShowTyped(); }
                    else e.Handled = false;
                    break;
            }
        }

        // ───────────────────────── Full screen and screens ─────────────────────────

        void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                prevState = WindowState;
                prevBounds = new Rect(Left, Top, Width, Height);
                toolbarHost.Visibility = Visibility.Collapsed;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                isFullScreen = true;
            }
            else
            {
                toolbarHost.Visibility = Visibility.Visible;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = prevState;
                if (prevState == WindowState.Normal)
                {
                    Left = prevBounds.X; Top = prevBounds.Y; Width = prevBounds.Width; Height = prevBounds.Height;
                }
                isFullScreen = false;
            }
        }

        void PresentOn(System.Windows.Forms.Screen screen)
        {
            if (isFullScreen) ToggleFullScreen();
            WindowState = WindowState.Normal;
            var dpi = VisualTreeHelper.GetDpi(this);
            var b = screen.WorkingArea;
            Left = b.X / dpi.DpiScaleX + 20;
            Top = b.Y / dpi.DpiScaleY + 20;
            Dispatcher.BeginInvoke(new Action(ToggleFullScreen), DispatcherPriority.ApplicationIdle);
        }

        // ───────────────────────── Files ─────────────────────────

        void OpenDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = L("PDF or PowerPoint", "PDF hoặc PowerPoint") + "|*.pdf;*.ppt;*.pptx;*.pps;*.ppsx;*.pptm|PDF|*.pdf|PowerPoint|*.ppt;*.pptx;*.pps;*.ppsx;*.pptm",
                Title = L("Choose a PDF or PowerPoint file", "Chọn file PDF hoặc PowerPoint"),
            };
            if (dlg.ShowDialog(this) == true) Open(dlg.FileName);
        }

        void OnFileDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) Open(files[0]);
        }

        void Reload()
        {
            if (sourcePath != null) Open(sourcePath, force: true, keepPosition: true);
        }

        async void Open(string path, bool force = false, bool keepPosition = false)
        {
            string ext = Path.GetExtension(path);
            try
            {
                string pdfPath;
                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) pdfPath = path;
                else if (PowerPointConverter.Extensions.Contains(ext))
                {
                    ShowBusy(L($"Asking PowerPoint to convert “{Path.GetFileName(path)}” to PDF…", $"Đang nhờ PowerPoint chuyển “{Path.GetFileName(path)}” sang PDF…"));
                    pdfPath = await PowerPointConverter.ConvertAsync(path, force);
                }
                else
                {
                    MessageBox.Show(this, L("HaiView opens PDF and PowerPoint files (.pdf, .pptx, .ppt).", "Chỉ mở được file PDF hoặc PowerPoint (.pdf, .pptx, .ppt)."), "HaiView");
                    return;
                }
                var loaded = await PdfSource.LoadAsync(pdfPath);
                ShowBusy(null);
                doc = loaded;
                sourcePath = path;
                Title = Path.GetFileName(path) + " - HaiView";
                if (keepPosition) start = Math.Min(start, Math.Max(0, PageCount - 1));
                else { start = 0; ResetPanes(); }
                FillThumbs();
                SetThumbs(thumbsToggle.IsChecked == true);
                Rebuild();
            }
            catch (Exception ex)
            {
                ShowBusy(null);
                MessageBox.Show(this, L("Couldn't open the file.", "Không mở được file.") + "\n\n" + ex.Message, "HaiView", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void SaveSettings()
        {
            settings.Tree = tree;
            settings.LayoutName = layoutName;
            settings.Save();
        }

        // ───────────────────────── Cell view ─────────────────────────

        sealed class CellView : Grid
        {
            readonly MainWindow w;
            public int Index { get; }
            readonly int[] path;
            readonly Image image = new() { Stretch = Stretch.Uniform };
            readonly Border pageLabel = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)), CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            };
            readonly Border frame = new() { BorderBrush = Accent };
            readonly Border editLayer = new();

            public CellView(MainWindow owner, int index, int[] path)
            {
                w = owner; Index = index; this.path = path;
                Background = Brushes.Transparent;
                AllowDrop = true;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                Children.Add(image);
                pageLabel.Child = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold };
                Children.Add(pageLabel);
                Children.Add(frame);

                var editPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                editPanel.Children.Add(new TextBlock { Text = L("Cell", "Ô") + $" {index + 1}", Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(EditButton("◫", L("Split left - right", "Chia đôi trái - phải"), () => w.SplitCell(path, "h")));
                row.Children.Add(EditButton("⊟", L("Split top - bottom", "Chia đôi trên - dưới"), () => w.SplitCell(path, "v")));
                row.Children.Add(EditButton("✕", L("Remove this cell", "Xoá ô này"), () => w.RemoveCell(path)));
                editPanel.Children.Add(row);
                editLayer.Child = new Border
                {
                    Child = editPanel, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x20)),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(14),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                editLayer.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
                editLayer.BorderThickness = new Thickness(1.5);
                editLayer.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
                Children.Add(editLayer);

                MouseLeftButtonDown += (_, e) =>
                {
                    if (w.editing) return;
                    if (e.ClickCount == 2) w.ToggleZoom(Index);
                    else if (e.ClickCount == 1) w.ToggleSelect(Index);
                };
                DragEnter += (_, _) => frame.BorderThickness = new Thickness(4);
                DragLeave += (_, _) => Refresh();
                Drop += (_, e) => { e.Handled = true; w.DropOnCell(Index, e.Data); Refresh(); };

                var cm = new ContextMenu();
                ContextMenuOpening += (_, _) =>
                {
                    cm.Items.Clear();
                    AddItem(cm, w.selected == Index ? L("Deselect cell", "Bỏ chọn ô") : L("Select to flip this cell only", "Chọn ô này để lật riêng"), () => w.ToggleSelect(Index));
                    AddItem(cm, w.zoomed == null ? L("Zoom this cell", "Phóng to ô này") : L("Zoom out", "Thu nhỏ lại"), () => w.ToggleZoom(Index));
                    cm.Items.Add(new Separator());
                    AddItem(cm, L("Split left - right", "Chia đôi trái - phải"), () => w.SplitCell(path, "h"));
                    AddItem(cm, L("Split top - bottom", "Chia đôi trên - dưới"), () => w.SplitCell(path, "v"));
                    AddItem(cm, L("Remove this cell", "Xoá ô này"), () => w.RemoveCell(path), w.CellCount > 1);
                    cm.Items.Add(new Separator());
                    AddItem(cm, L("Reset cells to consecutive pages", "Đưa mọi ô về trang liên tiếp"), () => w.ResetCells());
                };
                ContextMenu = cm;
            }

            static void AddItem(ContextMenu cm, string header, Action a, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (_, _) => a();
                cm.Items.Add(mi);
            }

            Button EditButton(string glyph, string tip, Action a)
            {
                var b = new Button { Content = glyph, ToolTip = tip, FontSize = 18, Width = 46, Height = 34, Margin = new Thickness(4, 0, 4, 0), Focusable = false };
                b.Click += (_, e) => { e.Handled = true; a(); };
                return b;
            }

            public void SetImage(BitmapSource? bmp) => image.Source = bmp;

            public void Refresh()
            {
                int idx = w.PageIndex(Index);
                bool hasPage = idx >= 0 && idx < w.PageCount;
                if (!hasPage) image.Source = null;
                image.Opacity = w.editing ? 0.55 : 1;
                ((TextBlock)pageLabel.Child).Text = (idx + 1).ToString();
                pageLabel.Visibility = w.settings.ShowLabels && hasPage && !w.editing ? Visibility.Visible : Visibility.Collapsed;
                frame.BorderThickness = new Thickness(w.selected == Index ? 4 : 0);
                editLayer.Visibility = w.editing ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
