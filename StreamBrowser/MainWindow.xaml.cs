using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq; // Linq を使うために追加
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace StreamBrowser
{
    public partial class MainWindow : Window
    {
        // --- 定数 ---
        private const string SettingsFileName = "Settings/URL.json";
        private const string UrlSettingsSection = "URLSetting";

        // --- プロパティ ---
        public int DefaultPage { get; private set; } = 0;
        public URLs? URLs { get; private set; }

        // --- フィールド ---
        private bool _isWebViewInitialized = false; // WebView2 の初期化状態を追跡

        // --- コンストラクタ ---
        public MainWindow()
        {
            InitializeComponent();
            // DataContext を設定 (XAML での {Binding URLs} などで必要になる場合があるため)
            this.DataContext = this;
            Loaded += Window_Loaded;
        }

        // --- イベントハンドラ ---

        /// <summary>
        /// ウィンドウがロードされたときに呼び出されます。
        /// 設定の読み込みとWebView2の初期化を開始します。
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 設定ファイルを非同期で読み込む
            await LoadUrlSettingsAsync(SettingsFileName, UrlSettingsSection);

            // 2. WebView2 コントロールを非同期で初期化する
            //    初期化完了後の処理は CoreWebView2InitializationCompleted で行う
            await InitializeWebView2Async();
        }

        // --- WebView2 初期化完了イベントハンドラ ---
        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                Debug.WriteLine("CoreWebView2InitializationCompleted: Success.");
                _isWebViewInitialized = true;

                // --- ContextMenuRequested イベントハンドラを登録 ---
                // CoreWebView2 が null でないことを確認してから登録
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.ContextMenuRequested += WebView_ContextMenuRequested;
                }
                else
                {
                     Debug.WriteLine("CoreWebView2InitializationCompleted: CoreWebView2 is null after successful initialization. Cannot register ContextMenuRequested.");
                     // 必要に応じてエラー処理を追加
                }


                // --- WebView2 の設定を行う ---
                ConfigureWebView2Settings();

                // --- 初期ページへ移動 ---
                NavigateToDefaultPage();
            }
            else
            {
                _isWebViewInitialized = false;
                Debug.WriteLine($"CoreWebView2InitializationCompleted: Failed. HResult={e.InitializationException?.HResult}, Message={e.InitializationException?.Message}");
                MessageBox.Show($"WebView2 のコア初期化に失敗しました。\nエラー: {e.InitializationException?.Message}", "WebView2 初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- ContextMenuRequested イベントハンドラ ---
        private void WebView_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            // CoreWebView2 または Environment が null の場合は処理しない
            if (webView?.CoreWebView2?.Environment == null)
            {
                Debug.WriteLine("WebView_ContextMenuRequested: CoreWebView2 or Environment is null.");
                return;
            }

            if (URLs == null || URLs.Count == 0)
            {
                Debug.WriteLine("WebView_ContextMenuRequested: No URLs loaded, showing default menu.");
                // URLがない場合はデフォルトメニューをそのまま表示させる (何もしない)
                return;
            }

            // --- デフォルトのメニュー項目をクリア (オプション) ---
            e.MenuItems.Clear();

            // --- カスタムメニュー項目を追加 ---
            try
            {
                // メニュー項目を区切るセパレーターを追加 (デフォルト項目を残す場合など)
                if (e.MenuItems.Count > 0) // デフォルト項目がある場合のみセパレーターを追加
                {
                    CoreWebView2ContextMenuItem separator = webView.CoreWebView2.Environment.CreateContextMenuItem(
                        "", null, CoreWebView2ContextMenuItemKind.Separator);
                    e.MenuItems.Add(separator);
                }

                // JSONから読み込んだURLリストに基づいてカスタムメニュー項目を追加
                foreach (var urlEntry in URLs)
                {
                    if (!string.IsNullOrWhiteSpace(urlEntry.Name) && !string.IsNullOrWhiteSpace(urlEntry.Url))
                    {
                        // CoreWebView2ContextMenuItem を作成
                        CoreWebView2ContextMenuItem newItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                            urlEntry.Name, // メニューに表示するテキスト
                            null,          // アイコンストリーム (nullでアイコンなし)
                            CoreWebView2ContextMenuItemKind.Command // 通常のコマンド項目
                        );

                        // クリックイベントハンドラを登録
                        newItem.CustomItemSelected += ContextMenuItem_CustomItemSelected;

                        // メニュー項目リストに追加
                        e.MenuItems.Add(newItem);
                        Debug.WriteLine($"WebView_ContextMenuRequested: Added menu item '{urlEntry.Name}'");
                    }
                    else
                    {
                        Debug.WriteLine($"WebView_ContextMenuRequested: Skipping invalid URL entry: Name='{urlEntry.Name}', Url='{urlEntry.Url}'");
                    }
                }
            }
            catch (Exception ex)
            {
                // メニュー項目作成中のエラー処理
                Debug.WriteLine($"WebView_ContextMenuRequested: Error creating context menu items: {ex.Message}");
                // エラーが発生しても、部分的に作成されたメニューやデフォルトメニューが表示される可能性がある
            }

            // e.Handled = true;
        }

        // --- カスタムメニュー項目クリック時のイベントハンドラ ---
        private void ContextMenuItem_CustomItemSelected(object? sender, object e) // イベント引数の型は object
        {
            if (sender is CoreWebView2ContextMenuItem menuItem)
            {
                // Name プロパティ (メニュー表示名) を取得
                string menuItemName = menuItem.Name;
                string? targetUrl = null; // Nullable string

                // URLs リストから Name が一致するものを検索して URL を取得
                if (URLs != null)
                {
                    // FirstOrDefault を使って、Name が一致する最初の要素を探す
                    var foundUrlEntry = URLs.FirstOrDefault(u => u.Name == menuItemName);
                    if (foundUrlEntry != null)
                    {
                        targetUrl = foundUrlEntry.Url;
                        Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Found URL '{targetUrl}' for menu item '{menuItemName}'");
                    }
                    else
                    {
                         Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Could not find URL entry for menu item name '{menuItemName}'");
                    }
                }
                else
                {
                     Debug.WriteLine($"ContextMenuItem_CustomItemSelected: URLs list is null.");
                }


                // URL の検証とナビゲーション
                if (!string.IsNullOrWhiteSpace(targetUrl) &&
                    Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute) &&
                    _isWebViewInitialized && webView?.CoreWebView2 != null)
                {
                    Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Navigating to {targetUrl}");
                    webView.CoreWebView2.Navigate(targetUrl);
                }
                // エラーハンドリング
                else if (string.IsNullOrWhiteSpace(targetUrl))
                {
                     Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Target URL is null or empty for menu item '{menuItemName}'.");
                     MessageBox.Show($"メニュー項目 '{menuItemName}' に対応する有効なURLが見つかりませんでした。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (!_isWebViewInitialized || webView?.CoreWebView2 == null)
                {
                    Debug.WriteLine($"ContextMenuItem_CustomItemSelected: WebView not ready. Cannot navigate to {targetUrl}");
                    MessageBox.Show("WebView がまだ準備できていません。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else // targetUrl は存在するが、形式が無効な場合
                {
                    Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Invalid URL format: {targetUrl}");
                    MessageBox.Show($"無効な URL です: {targetUrl}", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                 Debug.WriteLine($"ContextMenuItem_CustomItemSelected: Sender is not CoreWebView2ContextMenuItem. Sender type: {sender?.GetType().FullName}");
            }
        }


        // --- WebView2 関連メソッド ---

        /// <summary>
        /// WebView2 コントロールを非同期で初期化します。
        /// ハードウェアアクセラレーションを無効にするオプションを設定します。
        /// </summary>
        private async Task InitializeWebView2Async()
        {
            // 既に初期化済み、または CoreWebView2 が存在する場合は何もしない
            // CoreWebView2InitializationCompleted イベントで後続処理が行われる
            if (_isWebViewInitialized || webView.CoreWebView2 != null)
            {
                Debug.WriteLine("InitializeWebView2Async: Already initialized or CoreWebView2 exists.");
                // CoreWebView2 が存在するが _isWebViewInitialized が false の場合、初期化処理が完了していない可能性がある
                // この場合でも EnsureCoreWebView2Async を呼ぶべきか、あるいは完了イベントを待つべきか検討が必要
                // 現状では、既に CoreWebView2 があれば何もしない方針
                return;
            }

            try
            {
                Debug.WriteLine("InitializeWebView2Async: Creating environment with options...");
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--disable-gpu"
                };
                // ユーザーデータフォルダやブラウザ実行可能フォルダを明示的に指定しない場合は null
                var env = await CoreWebView2Environment.CreateAsync(null, null, envOptions);

                Debug.WriteLine("InitializeWebView2Async: Calling EnsureCoreWebView2Async with environment...");
                // EnsureCoreWebView2Async を呼び出すと、成功時に CoreWebView2InitializationCompleted イベントが発生する
                await webView.EnsureCoreWebView2Async(env);
                Debug.WriteLine("InitializeWebView2Async: EnsureCoreWebView2Async call returned. Waiting for completion event.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeWebView2Async: Error during initialization setup: {ex}");
                MessageBox.Show($"WebView2 の初期化準備中にエラーが発生しました。\nエラー: {ex.Message}", "WebView2 初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- WebView2 設定メソッド ---
        private void ConfigureWebView2Settings()
        {
            if (webView.CoreWebView2 == null)
            {
                Debug.WriteLine("ConfigureWebView2Settings: CoreWebView2 is null. Cannot configure.");
                return;
            }
            try
            {
                Debug.WriteLine("ConfigureWebView2Settings: Applying settings...");
                // ContextMenuRequested を使う場合、デフォルトコンテキストメニューは有効 (true) にしておく
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                // ステータスバーは不要なら false
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                Debug.WriteLine($"ConfigureWebView2Settings: AreDefaultContextMenusEnabled = {webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled}");
                Debug.WriteLine("ConfigureWebView2Settings: Settings applied.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigureWebView2Settings: Error applying settings: {ex.Message}");
                MessageBox.Show($"WebView2 の設定適用中にエラーが発生しました。\n{ex.Message}", "WebView2 設定エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 設定ファイルで指定されたデフォルトページにナビゲートします。
        /// </summary>
        private void NavigateToDefaultPage()
        {
            // 初期化チェック
            if (!_isWebViewInitialized || webView?.CoreWebView2 == null)
            {
                Debug.WriteLine("NavigateToDefaultPage called before WebView2 initialization completed or CoreWebView2 is null.");
                return;
            }

            if (URLs == null || URLs.Count == 0)
            {
                Debug.WriteLine("NavigateToDefaultPage: No URLs loaded or list is empty.");
                MessageBox.Show("表示するURLが設定ファイルに定義されていないか、読み込みに失敗しました。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DefaultPage >= 0 && DefaultPage < URLs.Count)
            {
                string initialUrl = URLs[DefaultPage].Url;
                if (!string.IsNullOrWhiteSpace(initialUrl) && Uri.IsWellFormedUriString(initialUrl, UriKind.Absolute))
                {
                    Debug.WriteLine($"NavigateToDefaultPage: Navigating to default page (Index: {DefaultPage}): {initialUrl}");
                    webView.CoreWebView2.Navigate(initialUrl);
                }
                else
                {
                    Debug.WriteLine($"NavigateToDefaultPage: Invalid URL for default page (Index: {DefaultPage}, URL: '{initialUrl}'). Navigating to first valid URL.");
                    MessageBox.Show($"デフォルトページとして指定されたURLが無効です (Index: {DefaultPage}, URL: '{initialUrl}')。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    NavigateToFirstValidUrl();
                }
            }
            else
            {
                Debug.WriteLine($"NavigateToDefaultPage: Invalid DefaultPage index ({DefaultPage}). Navigating to first valid URL.");
                MessageBox.Show($"デフォルトページのインデックス ({DefaultPage}) が無効です。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateToFirstValidUrl();
            }
        }

        /// <summary>
        /// URLリスト内の最初の有効なURLにナビゲートします。
        /// </summary>
        private void NavigateToFirstValidUrl()
        {
            // 初期化チェック
            if (!_isWebViewInitialized || webView?.CoreWebView2 == null || URLs == null)
            {
                 Debug.WriteLine("NavigateToFirstValidUrl: WebView2 not ready or no URLs.");
                 return;
            }

            foreach (var urlEntry in URLs)
            {
                if (!string.IsNullOrWhiteSpace(urlEntry.Url) && Uri.IsWellFormedUriString(urlEntry.Url, UriKind.Absolute))
                {
                    Debug.WriteLine($"NavigateToFirstValidUrl: Navigating to first valid URL found: {urlEntry.Url}");
                    webView.CoreWebView2.Navigate(urlEntry.Url);
                    return; // 最初の有効なURLを見つけたら終了
                }
                else
                {
                    Debug.WriteLine($"NavigateToFirstValidUrl: Skipping invalid URL in list: Name='{urlEntry.Name}', Url='{urlEntry.Url}'");
                }
            }

            Debug.WriteLine("NavigateToFirstValidUrl: No valid URLs found.");
            MessageBox.Show("設定ファイルに有効なURLが見つかりませんでした。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }


        // --- 設定読み込みメソッド ---

        /// <summary>
        /// 指定されたパスとセクション名からURL設定を非同期で読み込みます。
        /// </summary>
        private async Task LoadUrlSettingsAsync(string path, string section)
        {
            try
            {
                string fullPath = Path.Combine(AppContext.BaseDirectory, path);
                Debug.WriteLine($"Loading settings from: {fullPath}");
                string jsonContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonStream(memoryStream)
                    .Build();

                var urlSettings = new URLSettingData();
                config.GetSection(section).Bind(urlSettings);
                Debug.WriteLine($"Settings bound. DefaultPage: {urlSettings.DefaultPage}, URL Count: {urlSettings.Urls?.Count ?? 0}");

                this.DefaultPage = urlSettings.DefaultPage;
                this.URLs = urlSettings.Urls ?? new URLs();
            }
            catch (FileNotFoundException)
            {
                Debug.WriteLine($"Settings file not found: {path}");
                MessageBox.Show($"設定ファイルが見つかりません: {path}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (IOException ex)
            {
                 Debug.WriteLine($"IO error reading settings file: {path}. Error: {ex.Message}");
                MessageBox.Show($"設定ファイルの読み込み中にエラーが発生しました。\nファイル: {path}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load or parse settings: {path}, Section: {section}. Error: {ex}");
                MessageBox.Show($"URL設定の読み込みまたは解析に失敗しました。\nファイル: {path}\nセクション: {section}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
             Debug.WriteLine($"LoadUrlSettingsAsync finished. URLs count: {URLs?.Count ?? 0}");
        }
    }
}
