# Audio Effector デザインシステム仕様書

## 1. 理念と設計原則

本デザインシステムは、Audio Effectorにおけるオブジェクト指向UI（OOUI）への大規模刷新（#233）を支えるビジュアルデザイン・スタイル体系の標準仕様です。
「機能（動詞）を探すUI」から「オブジェクト（名詞）を直接認識し操作するUI」へ移行するにあたり、ユーザーが画面内の各要素を見た瞬間に「何であるか（オブジェクトの種別）」「何ができるか（アフォーダンス）」「現在の状態（再生中、選択中、無効等）」を直感的に把握できるよう設計されています。

### 1.1 コア設計原則

1. **オブジェクトの知覚性（High Affordance & Object-First）**:
   - 楽曲（Track）、アルバム（Album）、プレイリスト（Playlist）、機器（Device）などの主要オブジェクトは、形状、階層、アイコン、ホバーフィードバックにより明確に差別化され、直感的な直接操作（クリック、ドラッグ＆ドロップ、右クリック）を促します。
2. **サイバー・モダン・エレガンス（Fluent Design × Neon Accent）**:
   - Windows 11 のモダンUI思想（Mica/Acrylicエフェクト、滑らかな角丸、繊細な境界線）をベースに、音楽の躍動感と没入感を高める深みのあるチャコール/ダークスレート背景と、シグネチャーカラーである**ネオンシアン（`#00FFFF`）**を融合します。
3. **モードレス性と視覚的一貫性（Modeless Consistency）**:
   - 画面遷移やパネル開閉によって操作体系やスタイルルールが分断されないよう、タイポグラフィ、余白、カラーパレット、コントロール形状を一元的にトークン化（Design Tokens）し、全画面で共通適用します。
4. **パフォーマンスへの配慮（Lightweight & Performant Rendering）**:
   - ソフトウェアレンダリングによるCPU負荷急増を防ぐため、高負荷エフェクト（動的なDropShadowやBlur）の乱用を排し、GPUアクセラレーションに適したソリッド/リニアグラデーション描画、UI仮想化（Virtualization）、完全描画抑止（Visibility="Collapsed"連動）をデザイン仕様レベルで義務付けます。

---

## 2. ドキュメント構成

本デザインシステムは、以下の仕様書群で構成されます：

| 仕様書 | 内容と役割 |
| :--- | :--- |
| [**01. デザイントークン仕様書**](01_デザイントークン仕様書.md) | カラーパレット（背景・前景・アクセント・状態色）、タイポグラフィ、スペーシング（余白）、角丸、エレベーション、モーション（時間・イージング）の最小構成要素定義 |
| [**02. 共通コントロールスタイル仕様書**](02_共通コントロールスタイル仕様書.md) | ボタン、入力フィールド、スライダー、リスト・データグリッド、スクロールバー、コンテキストメニュー等のWPFカスタムStyle/ControlTemplate標準仕様 |

---

## 3. レイヤー構造（トークンからUIへの展開）

```mermaid
flowchart TD
    subgraph Layer1 [1. プリミティブトークン (Primitive Tokens)]
        C_Raw[Raw Colors: #00FFFF, #161920...]
        S_Raw[Raw Sizes: 4px, 8px, 16px...]
        F_Raw[Font Metrics: 11pt, 13pt, 20pt...]
    end

    subgraph Layer2 [2. セマンティックトークン (Semantic Tokens)]
        C_Sem[Surface / Foreground / Accent / Status]
        S_Sem[Component Padding / Gap / Margin]
        T_Sem[Header / Body / Caption / Badge]
    end

    subgraph Layer3 [3. 共通コントロールスタイル (Component Styles)]
        BtnStyle[Button / ToggleButton Style]
        InpStyle[TextBox / SearchBox Style]
        SldStyle[Slider: Seek & Volume Style]
        LstStyle[ListBox / DataGrid ItemStyle]
        MnuStyle[ContextMenu / MenuItem Style]
    end

    subgraph Layer4 [4. 各画面・ビュー (OOUI Views)]
        TitleBar[CustomTitleBar]
        Sidebar[Sidebar Navigation]
        Workspace[Center Workspace Views]
        SidePanel[Right Side Multi-Tab Panel]
        PlayerBar[Persistent Player Bar]
    end

    Layer1 --> Layer2
    Layer2 --> Layer3
    Layer3 --> Layer4
```

---

## 4. 関連仕様書

- [全体レイアウト・ペイン構成UI仕様書](../画面設計/全体レイアウト・ペイン構成/全体レイアウト・ペイン構成UI仕様書.md)
- [画面遷移・ナビゲーション設計書](../画面設計/画面遷移・ナビゲーション設計書.md)
- [インタラクション標準化ガイドライン](../主要オブジェクト/インタラクション標準化ガイドライン.md)
- [パフォーマンス最適化方針](../詳細設計/パフォーマンス最適化.md)
