### 1. 概要
機器転送タブ（Device Sync）において、ユーザーが転送したい楽曲を「曲単位」および「アルバム単位」で選択できる（SELECT）機能を実装します。
現在、機器転送タブ内には楽曲選択のUIが存在しないため、タブの下部にPC上のアルバム一覧を表示し、そこから転送対象の曲を選択可能にします。

### 2. 実装内容
- **UIコンポーネントの追加** (`DeviceSyncView.xaml`)
  - 既存のデバイスフォルダリストの下部に、転送対象を選択するための `ListBox` または `ListView` を追加します。
  - アルバムのリスト表示と、アルバムを展開して曲単位で選択できるUI（例：`Expander` ＋ `ListBox` を組み合わせた構造、またはツリービュー）を実装します。
  - 各アルバムとトラックにチェックボックス（`CheckBox`）を配置し、それぞれの `IsSelected` プロパティへバインドします。
- **ViewModelの連携と拡張** (`MainViewModel.cs`)
  - `Albums` コレクションを `DeviceSyncView.xaml` 側でバインドできるように適切に公開・整理します。
  - 転送ロジック (`TransferSelected`) 自体は既に `album.IsSelected` と `track.IsSelected` を評価して動作するようになっているため、新たに追加するUIからこれらのプロパティを更新できるようにバインディングを構成します。
  - 選択状態のクリアや全選択など、選択操作を補助するコマンド（例：`SelectAllCommand`, `ClearSelectionCommand`）の追加を必要に応じて行います。
- **レイアウトの調整** (`DeviceSyncView.xaml`)
  - `Grid.RowDefinitions` を調整し、上部のデバイス上ディレクトリ表示エリアと、下部のPC側転送候補アルバム表示エリアが適切に分割表示されるようにデザインを修正します。可能であれば `GridSplitter` でリサイズ可能にします。

### 3. 関連Issue
- 親Issue/関連Issue: #92 (機器接続時に機器が検出されないバグの対応)
