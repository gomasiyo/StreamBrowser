using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Models;
using System;
using System.Collections.Generic; // List<T> を使うために必要
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace StreamBrowser
{
    /// <summary>
    /// アプリケーションのメインウィンドウを表します。WebView2コントロールを含み、URL設定に基づいてコンテンツを表示します。
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string SettingsFileName = "Settings/URL.json";
        private const string UrlSettingsSection = "URLSetting";

        public URLs URLs { get; private set; } = new URLs();
        public int DefaultPage { get; private set; } = 0;

        private bool _isWebViewInitialized = false;

        /// <summary>
        /// MainWindow クラスの新しいインスタンスを初期化します。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            // イベントハンドラの登録
            Loaded += Window_Loaded;
            Closed += Window_Closed;
            // WebView2 の初期化完了イベントハンドラを登録 (XAMLでも可)
            webView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
        }

        /// <summary>
        /// ウィンドウの Loaded イベントを非同期で処理します。設定の読み込みと WebView2 の初期化を開始します。
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadUrlSettingsAsync(SettingsFileName, UrlSettingsSection);
                await InitializeWebView2Async();
            }
            catch (Exception ex) // 初期化中の予期せぬエラーをキャッチ
            {
                ShowError($"アプリケーションの初期化中にエラーが発生しました。\n{ex.Message}");
            }
        }

        /// <summary>
        /// ウィンドウの Closed イベントを処理します。イベントハンドラを解除し、リソースを解放します。
        /// </summary>
        private void Window_Closed(object? sender, EventArgs e)
        {
            // イベントハンドラの解除
            Loaded -= Window_Loaded;
            Closed -= Window_Closed;

            if (webView != null)
            {
                webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted;
                if (webView.CoreWebView2 != null)
                {
                    try
                    {
                        // ContextMenuRequested イベントハンドラを解除
                        webView.CoreWebView2.ContextMenuRequested -= WebView_ContextMenuRequested;
                        // CustomItemSelected は MenuItem ごとに解除されるため、ここでは不要
                    }
                    catch (ObjectDisposedException) { /* WebView2 が既に破棄されている場合は無視 */ }
                    catch (InvalidOperationException) { /* その他の予期せぬ状態変化の場合も無視 */ }
                }
                // WebView2 コントロールのリソースを解放
                webView.Dispose();
            }
        }


        /// <summary>
        /// WebView2 の CoreWebView2 の初期化完了イベントを処理します。
        /// </summary>
        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _isWebViewInitialized = true;
                try
                {
                    if (webView?.CoreWebView2 == null)
                    {
                        ShowError("WebView2 の CoreWebView2 オブジェクトが予期せず null です。");
                        _isWebViewInitialized = false;
                        return;
                    }

                    // コンテキストメニュー表示要求時のイベントハンドラを登録
                    webView.CoreWebView2.ContextMenuRequested += WebView_ContextMenuRequested;
                    ConfigureWebView2Settings();
                    // WebView2 が準備できたので、初期ページに移動する
                    TryNavigateToInitialPage();
                }
                catch (Exception ex)
                {
                    ShowError($"WebView2 の設定または初期ナビゲーション中にエラーが発生しました。\n{ex.Message}");
                    _isWebViewInitialized = false;
                }
            }
            else
            {
                _isWebViewInitialized = false;
                ShowError($"WebView2 のコア初期化に失敗しました。\nエラー: {e.InitializationException?.ToString() ?? "不明なエラー"}");
            }
        }

        /// <summary>
        /// WebView2 のコンテキストメニュー表示要求イベントを処理します。
        /// 一時リストを使用してメニュー項目を作成し、成功した場合のみ既存メニューを置き換えます。
        /// </summary>
        private void WebView_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            // WebView2 またはその環境が利用できない場合はデフォルトメニューを表示
            if (webView?.CoreWebView2?.Environment == null) return;

            // 表示する URL が設定されていない場合はデフォルトメニューを表示
            if (URLs.Count == 0)
            {
                return;
            }

            var customMenuItems = new List<CoreWebView2ContextMenuItem>();
            bool creationSuccess = false;
            try
            {
                // 1. カスタムメニュー項目を一時リストに作成
                foreach (var urlEntry in URLs)
                {
                    if (!string.IsNullOrWhiteSpace(urlEntry.Name) && IsValidUrl(urlEntry.Url)) // URLの有効性もチェック
                    {
                        CoreWebView2ContextMenuItem newItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                            urlEntry.Name,
                            null, // アイコンなし
                            CoreWebView2ContextMenuItemKind.Command
                        );

                        // イベントハンドラを登録 (毎回新しいMenuItemなので事前の解除は不要)
                        newItem.CustomItemSelected += ContextMenuItem_CustomItemSelected;
                        customMenuItems.Add(newItem);
                    }
                    else if (!string.IsNullOrWhiteSpace(urlEntry.Name)) // 名前はあるがURLが無効な場合
                    {
                        // ログやデバッグ出力で警告を出すと良いかもしれません
                        // System.Diagnostics.Debug.WriteLine($"Warning: Invalid or empty URL for menu item '{urlEntry.Name}'.");
                    }
                }
                creationSuccess = true; // ここまで例外なく到達したら作成成功
            }
            catch (Exception ex)
            {
                ShowWarning($"コンテキストメニュー項目の作成中にエラーが発生しました。\n{ex.Message}");
                // creationSuccess は false のまま
            }

            // 2. メニュー項目の作成に成功し、かつ1つ以上の有効な項目がある場合のみ、
            //    既存のメニューをクリアしてカスタムメニューに置き換える。
            if (creationSuccess && customMenuItems.Any())
            {
                e.MenuItems.Clear(); // ここで初めてクリアする
                foreach (var item in customMenuItems)
                {
                    e.MenuItems.Add(item);
                }
            }
        }


        /// <summary>
        /// カスタムコンテキストメニュー項目のクリックイベントを処理します。
        /// </summary>
        private void ContextMenuItem_CustomItemSelected(object? sender, object e)
        {
            if (sender is not CoreWebView2ContextMenuItem menuItem) return;

            // イベントハンドラを解除 (クリック後に不要になるため、メモリリーク防止)
            menuItem.CustomItemSelected -= ContextMenuItem_CustomItemSelected;

            try
            {
                string menuItemName = menuItem.Name;
                // クリックされたメニュー名と一致する URL エントリを検索
                var foundUrlEntry = URLs.FirstOrDefault(u => u.Name == menuItemName);

                // URLが見つかり、かつ有効であることを確認してからナビゲート
                if (foundUrlEntry != null && IsValidUrl(foundUrlEntry.Url))
                {
                    NavigateToUrl(foundUrlEntry.Url);
                }
                else
                {
                    // この警告は通常、WebView_ContextMenuRequested で無効なURLが除外されていれば表示されないはず
                    ShowWarning($"メニュー項目 '{menuItemName}' に対応する有効なURLが見つかりませんでした。");
                }
            }
            catch (Exception ex)
            {
                ShowWarning($"メニュー項目の処理中にエラーが発生しました。\n{ex.Message}");
            }
        }

        /// <summary>
        /// WebView2 コントロールを非同期で初期化します。
        /// </summary>
        private async Task InitializeWebView2Async()
        {
            try
            {
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    // ハードウェアアクセラレーションを無効にする場合
                    AdditionalBrowserArguments = "--disable-gpu"
                };

                var env = await CoreWebView2Environment.CreateAsync(null, null, envOptions);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                ShowError($"WebView2 の初期化準備中にエラーが発生しました。\nエラー: {ex.Message}");
                _isWebViewInitialized = false; // 初期化失敗を示す
            }
        }

        /// <summary>
        /// WebView2 の設定を行います。
        /// </summary>
        private void ConfigureWebView2Settings()
        {
            if (webView?.CoreWebView2 == null) return;

            try
            {
                var settings = webView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = true; // カスタムメニュー表示のためにTrue
                settings.IsStatusBarEnabled = false;
                settings.IsScriptEnabled = true;
            }
            catch (Exception ex)
            {
                ShowWarning($"WebView2 の設定適用中にエラーが発生しました。\n{ex.Message}");
            }
        }

        /// <summary>
        /// 設定に基づいて初期ページへのナビゲーションを試みます。
        /// </summary>
        private void TryNavigateToInitialPage()
        {
            if (!_isWebViewInitialized)
            {
                // WebView_CoreWebView2InitializationCompleted で初期化失敗時にエラー表示されるため、
                // ここでは警告を表示しないか、より簡潔なメッセージにする。
                // ShowWarning("WebView が初期化されていないため、ナビゲーションできません。");
                return;
            }

            if (URLs.Count == 0)
            {
                ShowWarning("表示するURLが設定ファイルに定義されていないか、読み込みに失敗しました。");
                NavigateToUrl("about:blank"); // URLがない場合は空白ページへ
                return;
            }

            string? targetUrl = null;

            // 1. DefaultPage インデックスの URL を試す
            if (DefaultPage >= 0 && DefaultPage < URLs.Count)
            {
                string? defaultUrl = URLs[DefaultPage].Url;
                if (IsValidUrl(defaultUrl))
                {
                    targetUrl = defaultUrl;
                }
                else
                {
                    ShowWarning($"デフォルトページとして指定されたURLが無効です (Index: {DefaultPage}, URL: '{defaultUrl ?? "null"}')。");
                }
            }
            else
            {
                ShowWarning($"デフォルトページのインデックス ({DefaultPage}) が範囲外です。");
            }

            // 2. DefaultPage が無効または範囲外だった場合、リストの最初から有効な URL を探す
            if (targetUrl == null)
            {
                targetUrl = URLs.Select(u => u.Url).FirstOrDefault(IsValidUrl);
                if (targetUrl != null)
                {
                    ShowWarning("デフォルトページが無効または範囲外のため、リスト内の最初の有効なURLに移動します。");
                }
            }

            // 3. ナビゲーション実行
            if (targetUrl != null)
            {
                NavigateToUrl(targetUrl);
            }
            else
            {
                // 有効なURLが一つも見つからなかった場合
                ShowError("設定ファイルに有効なURLが見つかりませんでした。");
                NavigateToUrl("about:blank"); // 有効なURLがない場合は空白ページへ
            }
        }

        /// <summary>
        /// 指定されたURLにナビゲートします。
        /// </summary>
        /// <param name="url">ナビゲート先のURL。</param>
        private void NavigateToUrl(string url)
        {
            // WebViewが初期化済みでCoreWebView2が利用可能かチェック
            if (!_isWebViewInitialized || webView?.CoreWebView2 == null)
            {
                ShowWarning($"WebView が初期化されていないため、URL '{url}' にナビゲーションできません。");
                return;
            }

            try
            {
                webView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) // Navigate で例外が発生する可能性は低いが念のため
            {
                ShowError($"ナビゲーション中にエラーが発生しました (URL: {url})。\n{ex.Message}");
            }
        }

        /// <summary>
        /// 指定された文字列が有効な絶対URIかどうかを判定します。
        /// </summary>
        /// <param name="url">検証するURL文字列。</param>
        /// <returns>有効な絶対URIであれば <c>true</c>、そうでなければ <c>false</c>。</returns>
        private bool IsValidUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }


        /// <summary>
        /// 指定された JSON 設定ファイルから URL 設定を非同期で読み込みます。
        /// </summary>
        /// <param name="path">アプリケーションベースディレクトリからの相対パス。</param>
        /// <param name="section">JSON内の設定セクション名。</param>
        private async Task LoadUrlSettingsAsync(string path, string section)
        {
            // 読み込み前にプロパティをデフォルト値にリセット
            this.URLs = new URLs();
            this.DefaultPage = 0;

            try
            {
                string fullPath = Path.Combine(AppContext.BaseDirectory, path);
                if (!File.Exists(fullPath))
                {
                    ShowError($"設定ファイルが見つかりません: {fullPath}");
                    return; // 設定が読み込めないのでここで終了
                }

                // UTF8 でファイルを非同期に読み込む
                string jsonContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                // JSON 文字列から設定を構築
                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));
                var config = new ConfigurationBuilder()
                    .AddJsonStream(memoryStream)
                    .Build();

                // 設定データをバインドするオブジェクトを準備
                var urlSettings = new URLSettingData();
                // 指定されたセクションをオブジェクトにバインド
                config.GetSection(section).Bind(urlSettings);

                // 読み込んだ設定値をプロパティにセット
                this.DefaultPage = urlSettings.DefaultPage;
                // Urls プロパティは null 非許容なので null チェック不要
                this.URLs = urlSettings.Urls ?? new URLs(); // Bind が null を設定する可能性に備える (通常はないはず)
            }
            catch (JsonException ex) // JSON 解析エラー
            {
                 ShowError($"設定ファイルの JSON 解析に失敗しました。\nファイル: {path}\nエラー箇所 (推定): Path={ex.Path}, Line={ex.LineNumber}, Pos={ex.BytePositionInLine}\nメッセージ: {ex.Message}");
            }
            catch (IOException ex) // ファイル読み取りエラー
            {
                ShowError($"設定ファイルの読み込み中に I/O エラーが発生しました。\nファイル: {path}\nエラー: {ex.Message}");
            }
            catch (Exception ex) // その他の予期せぬエラー (Bind の失敗など)
            {
                ShowError($"URL設定の読み込み中に予期せぬエラーが発生しました。\nファイル: {path}\nセクション: {section}\nエラー: {ex.ToString()}");
            }
        }

        // MessageBox を表示するためのヘルパーメソッド (エラー用)
        private void ShowError(string message)
        {
            // UI スレッドから呼び出すことを保証 (非同期メソッドから呼ばれる可能性があるため)
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            });
            // TODO: ログ出力
        }

        // MessageBox を表示するためのヘルパーメソッド (警告用)
        private void ShowWarning(string message)
        {
            // UI スレッドから呼び出すことを保証
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            // TODO: ログ出力
        }
    }
}
