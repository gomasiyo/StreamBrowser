using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;

namespace StreamBrowser
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitailizeWebView2Async();
        }

        private async Task InitailizeWebView2Async()
        {
            try
            {
                // 環境オプションを作成
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    // 追加のブラウザ引数としてハードウェアアクセラレーション無効化フラグを指定
                    AdditionalBrowserArguments = "--disable-gpu"
                };

                // カスタム環境オプションを使用してCoreWebView2環境を作成
                // 通常、UserDataFolderを指定しますが、デフォルトでよければnullや省略も可
                // var env = await CoreWebView2Environment.CreateAsync(null, null, environmentOptions);
                // EnsureCoreWebView2Asyncに直接渡すことも可能
                // 注意: EnsureCoreWebView2Asyncを複数回呼び出す場合、
                //       最初に使われた環境(オプション含む)が以降も使われます。
                //       確実に適用するには、明示的にEnvironmentを作成・指定する方が確実です。
                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: null,
                    options: envOptions);

                // 作成した環境を指定して WebView2 を初期化
                await webView.EnsureCoreWebView2Async(env);

                // WebView2の初期化が完了したら、Webページを読み込む
                if (webView.CoreWebView2 != null)
                {
                    // Webページを読み込む
                    webView.CoreWebView2.Navigate("https://video.unext.jp/");
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

        }
    }
}