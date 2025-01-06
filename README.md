# QvPen Exporter
QvPenにエクスポート機能を追加した拡張機能と、エクスポートデータをシーンへLineRendererとしてインポートするEditor拡張機能です。

# Installation
1. vpmリポジトリを追加します
   [com.dolphiiiin.vpm](https://dolphiiiin.github.io/vpm-repos/)
2. VCCまたはALCOMを開き、`QvPen Exporter`(`com.dolphiiiin.qvpen-exporter`)をインストールします
# Dependencies
- [QvPen](https://github.com/ureishi/QvPen) 3.3.3 or later

# Usage
## Export
1. ワールドにQvPenを設置します
   - `Packages/QvPen Exporter/Prefab`にQvPen Exporterを導入したバージョンのQvPenが保存されています
2. QvPenで描画を行います
3. QvPenのパネルにあるメニューに追加されているボタン`Export All`をインタラクトすることで、インタラクトしたパネルに属する全てのQvPenのデータをエクスポートします
    - エクスポートされたデータは、VRChatのログファイルに出力されます。
   > **⚠️Warning**<br>
   VRChatのLoggingが有効になっていることを確認してください。
   VRChatのSettingsから`Debug` > `Logging`を有効になっていることを確認します。
4. エクスポートしたデータをQvPen Export Formatterで変換します
   - [QvPen Export Formatter](https://dolphiiiin.github.io/qvpen-export-formatter/)
   1.  ページを開いてVRChatのログファイルをペーストするか、ファイルを参照して選択します
   2. `フォーマット`をクリックして、変換を実行します
   3. エクスポートするデータを選択して、変換されたjsonファイルをダウンロードします

## Import
1. Unityプロジェクトへエクスポートして変換したjsonファイルを、インポートします
2. `Tools` > `QvPenImporter`を選択します
3. `JSONファイル`にインポートしたjsonファイルを指定します
4. `インポート`をクリックして、インポートを実行します
