using Microsoft.Extensions.Configuration;
using System; // ArgumentNullException を使うために追加
using System.Collections.Generic; // List<T> を使うために追加
using System.Linq; // FirstOrDefault や FindIndex を使うために追加 (FindIndexはList<T>のメソッドだが、Linqもよく使われるため残す)

namespace StreamBrowser.Models
{
    /// <summary>
    /// URL設定データを表します。
    /// </summary>
    public class URLSettingData
    {
        /// <summary>
        /// デフォルトで表示するページのインデックスを取得または設定します。
        /// </summary>
        public int DefaultPage { get; set; } = 0; // C# naming convention: PascalCase for public properties

        /// <summary>
        /// URLのリストを取得または設定します。
        /// 設定ファイルでは "urls" キーにマッピングされます。
        /// </summary>
        [ConfigurationKeyName("urls")]
        public URLs Urls { get; set; } = new URLs(); // C# naming convention: PascalCase for public properties
    }

    /// <summary>
    /// 名前付きURLを表します。
    /// </summary>
    public class URL
    {
        /// <summary>
        /// URLの名前を取得または設定します。
        /// 設定ファイルでは "@name" 属性にマッピングされます。
        /// </summary>
        [ConfigurationKeyName("@name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// URL文字列を取得または設定します。
        /// </summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// URLオブジェクトのコレクションを表します。名前によるアクセスを提供します。
    /// </summary>
    public class URLs : List<URL>
    {
        /// <summary>
        /// 指定された名前を持つURLオブジェクトを取得または設定します。
        /// </summary>
        /// <param name="name">取得または設定するURLの名前。</param>
        /// <returns>指定された名前を持つURLオブジェクト。見つからない場合は <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> が <c>null</c> の場合にスローされます (set時)。</exception>
        /// <remarks>
        /// get: 指定された名前を持つ最初のURLオブジェクトを返します。見つからない場合は <c>null</c> を返します。
        /// set: 指定された名前を持つURLオブジェクトが存在すれば更新し、存在しなければ新しいURLオブジェクトを追加します。
        ///      設定する <paramref name="value"/> の Name プロパティは、インデクサの <paramref name="name"/> 引数と一致することが期待されますが、
        ///      現在の実装では強制されません。
        /// </remarks>
        public URL? this[string name] // Use nullable reference type (URL?) if project enables NRTs
        {
            get
            {
                // Use Find for potentially slightly better performance than FirstOrDefault
                return this.Find(url => url.Name == name);
            }
            set
            {
                // Ensure the value being set is not null
                ArgumentNullException.ThrowIfNull(value, nameof(value));

                // Use FindIndex to locate the item by name
                int index = this.FindIndex(url => url.Name == name);

                if (index == -1)
                {
                    // If not found, add the new item
                    // Consider adding an assertion: Debug.Assert(value.Name == name);
                    base.Add(value);
                }
                else
                {
                    // If found, replace the existing item
                    // Consider adding an assertion: Debug.Assert(value.Name == name);
                    base[index] = value;
                }
            }
        }

        // The private IndexOf method is no longer needed as FindIndex is used directly in the indexer.
        // private int IndexOf(string name)
        // {
        //     return this.FindIndex(url => url.Name == name);
        // }
    }
}
