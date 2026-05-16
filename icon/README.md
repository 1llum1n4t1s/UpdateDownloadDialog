# icon/

NuGet パッケージ / DemoApp 用のアイコンを置く場所。

## 期待されるファイル

| ファイル名 | 用途 | サイズ目安 |
|---|---|---|
| `app_icon.png` | NuGet パッケージ アイコン (`<PackageIcon>`) | 128x128 PNG (≤1MB) |
| `app.ico` | DemoApp / Windows 実行ファイル アイコン (`<ApplicationIcon>`) | 256x256 含むマルチサイズ ICO |

## 取り扱い

`Directory.Build.props` には条件付き Include が記述されているため、
ファイルが**存在する場合のみ** NuGet パッケージへ自動同梱される。

ファイル未配置のまま `dotnet pack` するとアイコンなしのパッケージが作られる
（警告: NU5048 が出るが pack 自体は成功）。
