using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq; // FirstOrDefault, FindIndex を使うために必要

namespace StreamBrowser.Models
{
    /// <summary>
    /// URL設定データを表します。JSON設定ファイルの "URLSetting" セクションに対応します。
    /// </summary>
    public class URLSettingData
    {
        /// <summary>
        /// デフォルトで表示するページのインデックスを取得または設定します。
        /// デフォルト値は 0 です。
        /// </summary>
        public int DefaultPage { get; set; } = 0;

        /// <summary>
        /// URLのリストを取得または設定します。
        /// 設定ファイルでは "urls" キーにマッピングされます。
        /// null になることはなく、常にインスタンスが保証されます。
        /// </summary>
        [ConfigurationKeyName("urls")]
        public URLs Urls { get; set; } = new URLs(); // Null 非許容とし、初期化子を設定
    }

    /// <summary>
    /// 名前付きURLを表します。
    /// </summary>
    public class URL
    {
        /// <summary>
        /// URLの名前を取得または設定します。コンテキストメニュー等で表示されます。
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
        /// <returns>指定された名前を持つ最初のURLオブジェクト。見つからない場合は <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> が <c>null</c> の場合にスローされます (set時)。</exception>
        /// <remarks>
        /// get: 指定された名前を持つ最初のURLオブジェクトを返します。大文字と小文字を区別します。見つからない場合は <c>null</c> を返します。
        /// set: 指定された名前を持つURLオブジェクトが存在すれば更新し、存在しなければ新しいURLオブジェクト (<paramref name="value"/>) をリストに追加します。
        ///      <paramref name="value"/> の Name プロパティが <paramref name="name"/> 引数と一致するかどうかは検証しません。
        /// </remarks>
        public URL? this[string name] // Nullable reference type (URL?) を使用
        {
            get
            {
                // FirstOrDefault の方が意図が明確な場合が多い
                return this.FirstOrDefault(url => url.Name == name);
            }
            set
            {
                // 設定する値が null でないことを保証
                ArgumentNullException.ThrowIfNull(value, nameof(value));

                // 名前で既存の要素のインデックスを検索
                int index = this.FindIndex(url => url.Name == name);

                if (index == -1)
                {
                    // 見つからない場合は新しい要素を追加
                    base.Add(value);
                }
                else
                {
                    // 見つかった場合は既存の要素を置き換え
                    base[index] = value;
                }
            }
        }
    }
}
