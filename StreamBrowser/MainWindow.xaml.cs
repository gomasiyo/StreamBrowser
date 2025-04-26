using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Models;
using System;
using System.IO; // For FileNotFoundException, File, MemoryStream, IOException
using System.Text; // For Encoding
using System.Threading.Tasks; // For Task, async, await
using System.Windows;

namespace StreamBrowser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // --- 定数 ---
        private const string SettingsFileName = "Settings/URL.json"; // 設定ファイル名
        private const string UrlSettingsSection = "URLSetting";     // 設定ファイル内のセクション名

        // --- プロパティ ---

        /// <summary>
        /// デフォルトで表示するページのインデックスを取得します。
        /// </summary>
        public int DefaultPage { get; private set; } = 0;

        /// <summary>
        /// URL設定のリストを取得します。
        /// </summary>
        public URLs? URLs { get; private set; } // Nullable に変更し、初期値は null

        // --- コンストラクタ ---

        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;
        }

        // --- イベントハンドラ ---

        /// <summary>
        /// ウィンドウがロードされたときに呼び出されます。
        /// 設定の読み込み、WebView2の初期化、初期ページへのナビゲーションを行います。
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 設定ファイルを非同期で読み込む
            await LoadUrlSettingsAsync(SettingsFileName, UrlSettingsSection); // await を使用して非同期メソッドを呼び出す

            // 2. WebView2 コントロールを非同期で初期化する
            await InitializeWebView2Async();

            // 3. 設定に基づいて初期ページにナビゲートする
            NavigateToDefaultPage(); // 設定読み込みとWebView初期化完了後に実行
        }

        // --- WebView2 関連メソッド ---

        /// <summary>
        /// WebView2 コントロールを非同期で初期化します。
        /// ハードウェアアクセラレーションを無効にするオプションを設定します。
        /// </summary>
        private async Task InitializeWebView2Async()
        {
            try
            {
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments = "--disable-gpu"
                };
                var env = await CoreWebView2Environment.CreateAsync(null, null, envOptions);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 の初期化に失敗しました。\n{ex.Message}", "WebView2 初期化エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定ファイルで指定されたデフォルトページにナビゲートします。
        /// </summary>
        private void NavigateToDefaultPage()
        {
            if (webView?.CoreWebView2 == null)
            {
                System.Diagnostics.Debug.WriteLine("NavigateToDefaultPage called before WebView2 initialization completed.");
                return;
            }

            if (URLs == null || URLs.Count == 0)
            {
                MessageBox.Show("表示するURLが設定ファイルに定義されていないか、読み込みに失敗しました。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DefaultPage >= 0 && DefaultPage < URLs.Count)
            {
                string initialUrl = URLs[DefaultPage].Url;
                if (!string.IsNullOrWhiteSpace(initialUrl) && Uri.IsWellFormedUriString(initialUrl, UriKind.Absolute))
                {
                    webView.CoreWebView2.Navigate(initialUrl);
                }
                else
                {
                    MessageBox.Show($"デフォルトページとして指定されたURLが無効です (Index: {DefaultPage}, URL: '{initialUrl}')。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    NavigateToFirstValidUrl();
                }
            }
            else
            {
                MessageBox.Show($"デフォルトページのインデックス ({DefaultPage}) が無効です。\n最初の有効なURLに移動します。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                NavigateToFirstValidUrl();
            }
        }

        /// <summary>
        /// URLリスト内の最初の有効なURLにナビゲートします。
        /// </summary>
        private void NavigateToFirstValidUrl()
        {
            if (webView?.CoreWebView2 == null || URLs == null) return;

            foreach (var urlEntry in URLs)
            {
                if (!string.IsNullOrWhiteSpace(urlEntry.Url) && Uri.IsWellFormedUriString(urlEntry.Url, UriKind.Absolute))
                {
                    webView.CoreWebView2.Navigate(urlEntry.Url);
                    return;
                }
            }

            MessageBox.Show("設定ファイルに有効なURLが見つかりませんでした。", "ナビゲーションエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }


        // --- 設定読み込みメソッド (非同期版) ---

        /// <summary>
        /// 指定されたパスとセクション名からURL設定を非同期で読み込みます。
        /// </summary>
        /// <param name="path">設定ファイルのパス (アプリケーションベースディレクトリからの相対パス)。</param>
        /// <param name="section">設定ファイル内のセクション名。</param>
        /// <returns>非同期操作を表す Task。</returns>
        private async Task LoadUrlSettingsAsync(string path, string section)
        {
            try
            {
                // 設定ファイルのフルパスを取得
                string fullPath = Path.Combine(AppContext.BaseDirectory, path);

                // ファイルの内容を非同期で読み込む (UTF-8を想定)
                string jsonContent = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                // JSONコンテンツをメモリストリームに変換
                // using ステートメントで MemoryStream を適切に破棄する
                using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));

                // ストリームから設定を構築
                var config = new ConfigurationBuilder()
                    // SetBasePath は AddJsonStream の場合は必須ではないが、
                    // 他の設定ソースとの一貫性のために残しても良い
                    .SetBasePath(AppContext.BaseDirectory)
                    // AddJsonFile の代わりに AddJsonStream を使用
                    // reloadOnChange オプションは AddJsonStream では利用できない点に注意
                    .AddJsonStream(memoryStream)
                    .Build();

                var urlSettings = new URLSettingData();
                config.GetSection(section).Bind(urlSettings); // バインド処理は同期

                // 読み込んだ設定をプロパティに反映
                // このコードはUIスレッドで実行されるため、プロパティへの直接代入で問題ない
                this.DefaultPage = urlSettings.DefaultPage;
                this.URLs = urlSettings.Urls ?? new URLs(); // null の場合は空のリストを割り当て
            }
            catch (FileNotFoundException)
            {
                // ファイルが見つからない場合のエラー処理
                MessageBox.Show($"設定ファイルが見つかりません: {path}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // フォールバックとして空のリストを設定
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (IOException ex) // ReadAllTextAsync がスローする可能性のある他のI/Oエラー
            {
                // ファイル読み込み中のその他のI/Oエラー
                MessageBox.Show($"設定ファイルの読み込み中にエラーが発生しました。\nファイル: {path}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
            catch (Exception ex) // JSON形式エラー、バインドエラーなど、その他の予期せぬエラー
            {
                // JSONの解析エラーやバインドエラーなど
                MessageBox.Show($"URL設定の読み込みまたは解析に失敗しました。\nファイル: {path}\nセクション: {section}\nエラー: {ex.Message}", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                // フォールバックとして空のリストを設定
                this.URLs = new URLs();
                this.DefaultPage = 0;
            }
        }
    }
}
