using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
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

        public int DefaultPage { get; private set; } = 0;
        public URLs? URLs { get; private set; }

        private bool _isWebViewInitialized = false;

        /// <summary>
        /// MainWindow クラスの新しいインスタンスを初期化します。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this; // DataBinding のために設定
            Loaded += Window_Loaded;
        }

        /// <summary>
        /// ウィンドウの Loaded イベントを処理します。設定の読み込みと WebView2 の初期化を開始します。
        /// </summary>
        /// <param name="sender">イベントのソース。</param>
        /// <param name="e">イベントデータ。</param>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadUrlSettingsAsync(SettingsFileName, UrlSettingsSection);
            // WebView2 の初期化は非同期で行われ、完了後に CoreWebView2InitializationCompleted が呼ばれる
            await InitializeWebView2Async();
        }

        /// <summary>
        /// WebView2 の CoreWebView2 の初期化完了イベントを処理します。
        /// 初期化成功時に、コンテキストメニューイベントの購読、設定の適用、初期ページへのナビゲーションを行います。
        /// </summary>
        /// <param name="sender">イベントのソース。</param>
        /// <param name="e">初期化完了イベントのデータ。</param>
        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _isWebViewInitialized = true;

                // CoreWebView2 オブジェクトが利用可能になったらイベントハンドラを登録
                if (webView.CoreWebView2 != null)
                {
                    // コンテキストメニュー表示要求時のイベントハンドラを登録
                    webView.CoreWebView2.ContextMenuRequested += WebView_ContextMenuRequested;
                }

                ConfigureWebView2Settings();
                // WebView2 が準備できたので、最初のページに移動する
                NavigateToDefaultPage();
            }
            else
            {
                _isWebViewInitialized = false;
                MessageBox.Show($"WebView2 のコア初期化に失敗しました。\nエラー: {e.InitializationException?.Message}", "WebView2 初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// WebView2 のコンテキストメニュー表示要求イベントを処理します。
        /// デフォルトのメニュー項目をクリアし、設定ファイルから読み込んだ URL に基づいてカスタムメニュー項目を追加します。
        /// </summary>
        /// <param name="sender">イベントのソース。</param>
        /// <param name="e">コンテキストメニュー要求イベントのデータ。</param>
        private void WebView_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            // WebView2 の環境が利用できない場合は何もしない
            if (webView?.CoreWebView2?.Environment == null)
            {
                return;
            }

            // 表示する URL が設定されていない場合は、デフォルトのコンテキストメニューを表示させる (または何もしない)
            if (URLs == null || URLs.Count == 0)
            {
                return;
            } else {
                 // カスタムメニューのみを表示するため、既存の (デフォルトの) メニュー項目をすべて削除
                e.MenuItems.Clear();
            }


            try
            {
                // 設定ファイルから読み込んだ URL リストを反復処理
                foreach (var urlEntry in URLs)
                {
                    // Name と Url が両方とも有効な場合のみメニュー項目を作成
                    if (!string.IsNullOrWhiteSpace(urlEntry.Name) && !string.IsNullOrWhiteSpace(urlEntry.Url))
                    {
                        // WebView2 環境を使用して新しいメニュー項目を作成
                        CoreWebView2ContextMenuItem newItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                            urlEntry.Name, // メニューに表示されるテキスト
                            null,          // アイコン (今回はなし)
                            CoreWebView2ContextMenuItemKind.Command // 通常のクリック可能なコマンド
                        );

                        // メニュー項目がクリックされたときのイベントハンドラを登録
                        newItem.CustomItemSelected += ContextMenuItem_CustomItemSelected;
                        // 作成したメニュー項目をコンテキストメニューに追加
                        e.MenuItems.Add(newItem);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"コンテキストメニューの作成中にエラーが発生しました。\n{ex.Message}", "メニュー作成エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// カスタムコンテキストメニュー項目のクリックイベントを処理します。
        /// クリックされたメニュー項目に対応する URL へ WebView2 でナビゲートします。
        /// </summary>
        /// <param name="sender">イベントのソース (クリックされた CoreWebView2ContextMenuItem)。</param>
        /// <param name="e">イベントデータ (通常は null)。</param>
        private void ContextMenuItem_CustomItemSelected(object? sender, object e)
        {
            // sender が CoreWebView2ContextMenuItem であることを確認
            if (sender is CoreWebView2ContextMenuItem menuItem)
            {
                // メニュー項目の表示名 (CreateContextMenuItem で設定したもの) を取得
                string menuItemName = menuItem.Name;
                string? targetUrl = null;

                // URLs リストが null でないことを確認
                if (URLs != null)
                {
                    // Linq を使用して、クリックされたメニュー名と一致する Name を持つ URL エントリを検索
                    var foundUrlEntry = URLs.FirstOrDefault(u => u.Name == menuItemName);
                    // 一致するエントリが見つかった場合、その URL を取得
                    if (foundUrlEntry != null)
                    {
                        targetUrl = foundUrlEntry.Url;
                    }
                }

                // URL が有効で、WebView2 が初期化済みの場合にナビゲートを実行
                if (!string.IsNullOrWhiteSpace(targetUrl) &&
                    Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute) &&
                    _isWebViewInitialized && webView?.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Navigate(targetUrl);
                }
                // URL が見つからなかった場合のエラー表示
                else if (string.IsNullOrWhiteSpace(targetUrl))
                {
                     MessageBox.Show($"メニュー項目 '{menuItemName}' に対応する有効なURLが見つかりませんでした。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                // WebView2 が準備できていない場合のエラー表示
                else if (!_isWebViewInitialized || webView?.CoreWebView2 == null)
                {
                    MessageBox.Show("WebView がまだ準備できていません。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                // URL は存在するが形式が無効な場合のエラー表示
                else
                {
                    MessageBox.Show($"無効な URL です: {targetUrl}", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// WebView2 コントロールを非同期で初期化します。
        /// 必要な環境オプションを設定し、CoreWebView2 を準備します。
        /// </summary>
        private async Task InitializeWebView2Async()
        {
            // 既に初期化済み、または CoreWebView2 が存在する場合は処理をスキップ
            if (_isWebViewInitialized || webView.CoreWebView2 != null)
            {
                return;
            }

            try
            {
                // WebView2 環境のオプションを設定
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    // 特定の環境で問題が発生する場合があるため、GPU アクセラレーションを無効にする
                    AdditionalBrowserArguments = "--disable-gpu"
                };
                // WebView2 環境を作成 (ユーザーデータフォルダ等はデフォルトを使用)
                var env = await CoreWebView2Environment.CreateAsync(null, null, envOptions);
                // WebView2 コントロールの CoreWebView2 を初期化
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 の初期化準備中にエラーが発生しました。\nエラー: {ex.Message}", "WebView2 初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// WebView2 の設定を行います。デフォルトコンテキストメニューの有効化やステータスバーの非表示などを設定します。
        /// </summary>
        private void ConfigureWebView2Settings()
        {
            // CoreWebView2 が利用可能か確認
            if (webView.CoreWebView2 == null)
            {
                return;
            }
            try
            {
                // ContextMenuRequested イベントが発生するためには、デフォルトのコンテキストメニューを有効にする必要がある
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                // アプリケーションの UI に合わせてステータスバーを非表示にする
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 の設定適用中にエラーが発生しました。\n{ex.Message}", "WebView2 設定エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 設定ファイルで指定されたデフォルトページに WebView2 でナビゲートします。
        /// </summary>
        private void NavigateToDefaultPage()
        {
            // WebView2 が初期化されていない場合はナビゲーションしない
            if (!_isWebViewInitialized || webView?.CoreWebView2 == null)
            {
                return;
            }

            // URL リストが読み込まれていない場合はエラー表示
            if (URLs == null || URLs.Count == 0)
            {
                MessageBox.Show("表示するURLが設定ファイルに定義されていないか、読み込みに失敗しました。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // DefaultPage インデックスが有効範囲内か確認
            if (DefaultPage >= 0 && DefaultPage < URLs.Count)
            {
                string initialUrl = URLs[DefaultPage].Url;
                // URL が有効な形式か確認
                if (!string.IsNullOrWhiteSpace(initialUrl) && Uri.IsWellFormedUriString(initialUrl, UriKind.Absolute))
                {
                    webView.CoreWebView2.Navigate(initialUrl);
                }
                else
                {
                    // デフォルト URL が無効な場合は、最初の有効な URL へフォールバック
                    MessageBox.Show($"デフォルトページとして指定されたURLが無効です (Index: {DefaultPage}, URL: '{initialUrl}')。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    NavigateToFirstValidUrl();
                }
            }
            else
            {
                // DefaultPage インデックスが無効な場合は、最初の有効な URL へフォールバック
                MessageBox.Show($"デフォルトページのインデックス ({DefaultPage}) が無効です。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateToFirstValidUrl();
            }
        }

        /// <summary>
        /// 設定ファイル内の URL リストから、最初に見つかった有効な URL に WebView2 でナビゲートします。
        /// </summary>
        private void NavigateToFirstValidUrl()
        {
            // WebView2 が初期化されていない、または URL リストがない場合は処理しない
            if (!_isWebViewInitialized || webView?.CoreWebView2 == null || URLs == null)
            {
                 return;
            }

            // URL リストを順番にチェック
            foreach (var urlEntry in URLs)
            {
                // 有効な形式の URL が見つかったらナビゲートして終了
                if (!string.IsNullOrWhiteSpace(urlEntry.Url) && Uri.IsWellFormedUriString(urlEntry.Url, UriKind.Absolute))
                {
                    webView.CoreWebView2.Navigate(urlEntry.Url);
                    return; // 最初の有効な URL にナビゲートしたらループを抜ける
                }
            }

            // 有効な URL が一つも見つからなかった場合のエラー表示
            MessageBox.Show("設定ファイルに有効なURLが見つかりませんでした。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// 指定された JSON 設定ファイルから URL 設定（デフォルトページインデックスと URL リスト）を非同期で読み込みます。
        /// </summary>
        /// <param name="path">設定ファイルの相対パス。</param>
        /// <param name="section">設定ファイル内の読み込むセクション名。</param>
        private async Task LoadUrlSettingsAsync(string path, string section)
        {
            try
            {
                // アプリケーションのベースディレクトリからの相対パスを絶対パスに変換
                string fullPath = Path.Combine(AppContext.BaseDirectory, path);
                // ファイルの内容を非同期で読み込み
                string jsonContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
                // JSON 文字列をメモリストリームに変換 (ConfigurationBuilder で読み込むため)
                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

                // ConfigurationBuilder を使用して設定を構築
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory) // 設定ファイルの検索基点を設定
                    .AddJsonStream(memoryStream)           // メモリストリームから JSON 設定を読み込み
                    .Build();

                // URLSettingData オブジェクトを作成し、設定値をバインド
                var urlSettings = new URLSettingData();
                config.GetSection(section).Bind(urlSettings); // 指定されたセクションの値をオブジェクトにマップ

                // 読み込んだ設定値をプロパティにセット
                this.DefaultPage = urlSettings.DefaultPage;
                // URLs が null の場合は空のリストを生成
                this.URLs = urlSettings.Urls ?? new URLs();
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show($"設定ファイルが見つかりません: {path}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // エラー発生時は空のリストとデフォルト値で初期化
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (IOException ex)
            {
                MessageBox.Show($"設定ファイルの読み込み中にエラーが発生しました。\nファイル: {path}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (Exception ex) // JSON 解析エラーなども含む可能性のある一般的な例外
            {
                MessageBox.Show($"URL設定の読み込みまたは解析に失敗しました。\nファイル: {path}\nセクション: {section}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
        }
    }
}
